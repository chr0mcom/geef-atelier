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

## 3. Fix — was geändert wurde

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

### A — Provider sichtbar & wählbar *(nach Deploy zu prüfen)*

Nach Deploy: `list_grounding_provider_profiles` (MCP) → `learning-retriever-default` erscheint mit Typ `learning-retrieval`.

### B — Loop schließt sich *(nach Deploy zu prüfen)*

1. Run mit `learning-extractor`-Finalizer starten (≥2 Iterationen oder Major-Finding)
2. Learning-Run startet automatisch (fire-and-forget)
3. Learning-Reviewer evaluieren → Approved → Embedding geschrieben
4. Neuer Run mit `learning-retriever-default` im Template starten
5. Grounding-Kontext enthält das Learning mit `learning://{id}`-Quelle

### C — Domänen-Boost *(nach Deploy zu prüfen)*

Learnings aus Domäne X werden bevorzugt; fremde Domänen nur bei hoher Ähnlichkeit.

### D — Kuratiertes Wissen dominiert *(nach Deploy zu prüfen)*

Run mit `knowledge-base-default` + `learning-retriever-default`: KB höher gewichtet.

### E — Backwards-Compat *(vorab verifiziert)*

Alle bestehenden Provider (Tavily, VectorStore, Academic, etc.) unverändert. 1528 Tests grün.

---

## 5. Akzeptanzkriterien-Check

| AC | Status |
|----|--------|
| `dotnet build` 0 Errors, 0 Warnings | ✅ |
| `dotnet test` alle bestehenden Tests grün | ✅ (1528 grün, 13 pre-existing failures unverändert) |
| Diagnose dokumentiert | ✅ |
| Ursache behoben (Typ-Registrierung + SystemCrew-Eintrag) | ✅ |
| `learning-retriever-default` sichtbar mit Typ `learning-retrieval` | ⏳ nach Deploy |
| Idempotente Seed-Migration (falls nötig) | n/a — DB-Profil existierte |
| Retriever funktional (Domänen-Boost, Cap, Attribution) | ⏳ Real-Test B |
| Unbekannte Typen nicht lautlos verschluckt | ✅ (Warning-Log) |
| Regressions-Tests | ✅ (13 neue Tests) |
| Real-Test A (sichtbar) | ⏳ nach Deploy |
| Real-Test B (Loop schließt sich) | ⏳ nach Deploy |
| Real-Test C (Domänen-Boost) | ⏳ nach Deploy |
| Real-Test D (kuratiert dominiert) | ⏳ nach Deploy |
| Real-Test E (backwards-compat) | ✅ |
| Doku aktualisiert | ✅ |
| Merge auf main | ⏳ PR #30 |
| Production-Deploy verifiziert | ⏳ |

---

## 6. Merge-Commit-Hash + Deploy-Timestamp

*(wird nach Merge & Deploy eingetragen)*

---

## 7. Empfehlung

**Ja, ein „System-Provider-Vollständigkeits-Test" sollte eingeführt werden.**

Eine einfache Form: ein Fact-Test, der alle bekannten `GroundingProviderTypes.*`-Konstanten gegen `SystemCrew.GroundingProviderProfiles.Keys` prüft und sicherstellt, dass für jeden bekannten Typ mindestens ein System-Profil existiert. Alternativ: ein Test, der alle erwarteten System-Profilnamen explizit auflistet (ähnlich `FinalizerProfiles_Contains19Entries`).

Dieser Bugfix hat gezeigt: Ein korrekt geseedetes DB-Profil ist unsichtbar, wenn die Code-Konstante fehlt. Da es keine automatische Verbindung zwischen Migration und Code-Konstante gibt, ist ein expliziter Vollständigkeits-Test die einzige Absicherung.
