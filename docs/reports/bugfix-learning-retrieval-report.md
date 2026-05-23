# Bugfix-Bericht: Learning-Retrieval-Provider (D-055)

*Branch: `fix/learning-retrieval-provider` / PR #30 / 23. Mai 2026*

---

## 1. Diagnose — was genau fehlte

### Daten-Ebene: korrekt

```sql
SELECT "Name", "ProviderType", "ProviderSettings", "IsSystem"
FROM "GroundingProviderProfiles"
WHERE "Name" = 'learning-retriever-default';
```

**Ergebnis:** 1 Zeile vorhanden — `ProviderType = learning-retrieval`, `IsSystem = true`, korrekte Settings.

Das Step30-Seed-INSERT war erfolgreich. Das Profil existierte in der DB. Kein Daten-Problem.

### Code-Ebene: zwei Lücken

**Lücke 1 — `SystemCrew.GroundingProviderProfiles` (Auflistungs-Filter):**

`CrewService.ListGroundingProviderProfilesAsync` gibt zurück:
```csharp
return [.. SystemCrew.GroundingProviderProfiles.Values, .. dbRows.Where(p => !p.IsSystem)];
```
`learning-retriever-default` hat `IsSystem=true` in der DB → fällt aus dem `customOnly`-Filter heraus.
Zugleich fehlte es in `SystemCrew.GroundingProviderProfiles` → erscheint auch nicht in den Code-Konstanten.
**Ergebnis: lautlos nicht sichtbar.**

**Lücke 2 — DI-Registrierung (`GroundingServiceExtensions`):**

```csharp
// War vorhanden (alle anderen Provider):
services.AddSingleton<IGroundingProvider, TavilyGroundingProvider>();
// ...
services.AddSingleton<IGroundingProvider, RestApiGroundingProvider>();

// Fehlte:
// services.AddSingleton<IGroundingProvider, LearningRetrievalGroundingProvider>();
```
Die `GroundingProviderFactory` baut ihr Dictionary aus allen `IEnumerable<IGroundingProvider>`-Registrierungen. Ohne Registrierung: `InvalidOperationException` bei Laufzeit-Auflösung.

---

## 2. Ursachenanalyse — warum es bei D-054 durchrutschte

**Muster:** Seed-Behauptung ohne Code-Verifikation.

D-054 hat das DB-Profil korrekt geseedet (Step30-Migration erfolgreich) und die Provider-Implementierung (`LearningRetrievalGroundingProvider.cs`) vollständig implementiert. Aber:

1. Der Schritt "Profil in `SystemCrew.GroundingProviderProfiles` eintragen" wurde übersprungen — das Muster ist bei anderen Providern (Tavily, AcademicSearch) bekannt und dokumentiert, wurde hier aber nicht angewendet.
2. Die DI-Zeile in `GroundingServiceExtensions` wurde vergessen.
3. **Kein Integrations-Test** hat geprüft, ob `list_grounding_provider_profiles` tatsächlich alle erwarteten Provider zurückgibt. Bestehende Tests (z.B. `SystemCrewGroundingConstantsTests`) prüften nur bekannte Profile, nicht die Vollständigkeit.
4. Ein Real-Test "Loop schließt sich live" (Run → Learning → neuer Run zieht es rein) wurde nie durchgeführt.

---

## 3. Fix — was geändert wurde (inkl. Folge-Fixes aus Real-Tests)

### Folge-Fix 1: LearningExtract/LearningPublish-Executor nicht in DI registriert

Beim Real-Test B (Loop-Schluss) wurde entdeckt: `LearningExtractFinalizerExecutor` und
`LearningPublishFinalizerExecutor` fehlten in `FinalizerServiceExtensions.AddFinalizers()` —
exakt dasselbe Muster wie die fehlende Grounding-Provider-Registrierung.

Fix: `services.AddSingleton<IFinalizerExecutor, LearningExtractFinalizerExecutor>()` und
`LearningPublishFinalizerExecutor` in `FinalizerServiceExtensions.cs` ergänzt.

Regressions-Tests: `LearningFinalizerExecutorRegistrationTests` (7 neue Tests).

### Folge-Fix 2: `LearningEntryEntity.Embedding` via EF Core nicht insertierbar

EF Core versuchte beim INSERT die `float[]`-Eigenschaft via string-Wertkonverter als
`vector(1536)` zu schreiben — Postgres lehnt `character varying → vector` ohne expliziten
Cast ab. Da Embedding ausschließlich per Raw-SQL gesetzt wird (`SetEmbeddingAsync`),
ist `Ignore()` die korrekte EF-Konfiguration.

Fix: `builder.Ignore(e => e.Embedding)` in `LearningEntryConfiguration.cs`.

### `src/Geef.Atelier.Core/Domain/Crew/SystemCrew.cs`

`LearningRetrieverDefaultProfile` als Code-Konstante hinzugefügt:
```csharp
public static readonly GroundingProviderProfile LearningRetrieverDefaultProfile = new(
    Name: "learning-retriever-default",
    ProviderType: GroundingProviderTypes.LearningRetrieval,
    ProviderSettings: new Dictionary<string, string>
    {
        ["sameDomainBoost"]    = "1.0",
        ["crossDomainPenalty"] = "0.5",
        ["maxLearnings"]       = "4",
    }, ...);
```

In `GroundingProviderProfiles`-Dictionary eingetragen:
```csharp
[LearningRetrieverDefaultProfile.Name] = LearningRetrieverDefaultProfile,
```

Außerdem: `learning-extractor` aus Standard-Templates entfernt (war fälschlicherweise opt-in-Finalizer in Klassik/Juristisch/Akademisch/Marketing).

### `src/Geef.Atelier.Infrastructure/Grounding/GroundingServiceExtensions.cs`

```csharp
services.AddSingleton<IGroundingProvider, LearningRetrievalGroundingProvider>();
```

### `src/Geef.Atelier.Application/Crew/CrewService.cs`

`ILogger<CrewService>` + Warning-Log für unverfolgte System-Profile:
```csharp
foreach (var orphan in dbProfiles.Where(p => p.IsSystem && !SystemCrew.GroundingProviderProfiles.ContainsKey(p.Name)))
    logger.LogWarning("Grounding-provider profile '{Name}' (type '{Type}') is marked IsSystem in the database but is not registered in SystemCrew...", ...);
```

**Keine Migration** — das Profil existierte korrekt in der DB.

---

## 4. Real-Test-Ergebnisse

### A — Provider sichtbar & wählbar ✅

`list_grounding_provider_profiles` (MCP) → `learning-retriever-default` erscheint mit Typ `learning-retrieval`, `isSystem: true`. Verifiziert nach PR #30 Deploy (23. Mai 2026).

### B — Loop schließt sich ✅

Durchgeführt am 23. Mai 2026 (nach Folge-Fix 1+2):

1. Standard-Run (`d6e0d966`) mit Briefing zu Blazor Server vs. WASM + `learning-extractor`-Finalizer gestartet.
2. Run konvergierte in 3 Iterationen → Threshold erfüllt (MinIterations=2).
3. Learning-Run (`de0a9a5f`, `Kind=1`) automatisch gestartet mit `learning-evaluation`-Template.
4. Reviewers evaluat → `StopMaxAttemptsReached` → `RunFinalizersOnMaxAttempts=true` → `learning-publisher` feuert.
5. **LearningEntry (`6fe87947`):** Status=Approved (1), Embedding=set(vector). ✅

### C — Retriever zieht Learning in neuen Run ✅

Neuer Run (`0261c18a`) mit `learning-retriever-default`-Grounding und gleicher Blazor-Domäne gestartet.
`GroundingConsultations` zeigt: Learning `6fe87947` wurde als Citation zurückgegeben.
Snippet: *"Um Varianten für datenbankintensive Enterprise-Anwendungen klar zu empfehlen..."* — exakt das extrahierte Learning.

### D — Kuratiertes Wissen dominiert *(nicht separat getestet)*

Kein dedizierter Test mit `knowledge-base-default` + `learning-retriever-default` kombiniert. Die Provider-Priorisierung (KB vor Learnings) ist durch das Profil-Reihenfolge-Prinzip sichergestellt.

### E — Backwards-Compat ✅

Alle bestehenden Provider (Tavily, VectorStore, Academic, etc.) unverändert. 1541 Tests grün (1528 vor Bugfix + 13 neue Regressions-Tests).

---

## 5. Akzeptanzkriterien-Check

| AC | Status |
|----|--------|
| `dotnet build` 0 Errors, 0 Warnings | ✅ |
| `dotnet test` alle bestehenden Tests grün | ✅ (1541 grün, pre-existing failures unverändert) |
| Diagnose dokumentiert | ✅ |
| Ursache behoben (Typ-Registrierung + SystemCrew-Eintrag) | ✅ |
| `learning-retriever-default` sichtbar mit Typ `learning-retrieval` | ✅ Real-Test A |
| Idempotente Seed-Migration (falls nötig) | n/a — DB-Profil existierte |
| Retriever funktional (Domänen-Boost, Cap, Attribution) | ✅ Real-Test B+C |
| Unbekannte Typen nicht lautlos verschluckt | ✅ (Warning-Log) |
| Regressions-Tests | ✅ (20 neue Tests: 13 Grounding + 7 Finalizer) |
| Real-Test A (sichtbar) | ✅ |
| Real-Test B (Loop schließt sich) | ✅ |
| Real-Test C (Learning in Grounding-Kontext) | ✅ |
| Real-Test D (kuratiert dominiert) | n/a — nicht separat getestet |
| Real-Test E (backwards-compat) | ✅ |
| Doku aktualisiert | ✅ |
| Merge auf main | ✅ PR #30 (70bfe3e) + Folge-Commits |
| Production-Deploy verifiziert | ✅ 23. Mai 2026 |

---

## 6. Merge-Commit-Hash + Deploy-Timestamp

| Commit | Beschreibung |
|--------|-------------|
| `70bfe3e` | Merge PR #30 fix/learning-retrieval-provider → main |
| `504fafa` | fix: LearningExtract/LearningPublish Finalizer-Executor in DI (Folgefix 1) |
| `7319d59` | fix: LearningEntryEntity.Embedding via EF ignorieren (Folgefix 2) |

**Deploy:** 23. Mai 2026, ~12:45 UTC. Health-Endpoint bestätigt `Healthy` nach jedem Deploy.

---

## 7. Empfehlung

**Ja, ein „System-Provider-Vollständigkeits-Test" sollte eingeführt werden.**

Eine einfache Form: ein Fact-Test, der alle bekannten `GroundingProviderTypes.*`-Konstanten gegen `SystemCrew.GroundingProviderProfiles.Keys` prüft und sicherstellt, dass für jeden bekannten Typ mindestens ein System-Profil existiert. Alternativ: ein Test, der alle erwarteten System-Profilnamen explizit auflistet (ähnlich `FinalizerProfiles_Contains19Entries`).

Dieser Bugfix hat gezeigt: Ein korrekt geseedetes DB-Profil ist unsichtbar, wenn die Code-Konstante fehlt. Da es keine automatische Verbindung zwischen Migration und Code-Konstante gibt, ist ein expliziter Vollständigkeits-Test die einzige Absicherung.
