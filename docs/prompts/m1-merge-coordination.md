# M1-Merge-Coordination

*Konkreter Plan für den Merge von Branch `feature/openai-compatible-providers` zurück in `main`.*
*Erstellt: 10. Mai 2026.*

---

## Voraussetzung: Schritt 6 in main fertig

**Vor dem Merge:** Schritt 6 (`IRunService` + Cancellation-Mechanismus) muss in `main` abgeschlossen und gepusht sein. Der Merge fügt mehrere Dateien zusammen, die in beiden Branches geändert wurden — hauptsächlich `RunOrchestratorService.cs`. Solange Schritt 6 in main noch in Bearbeitung ist, würde ein Merge nur ein Zwischenkonflikt-Knäuel produzieren.

Wenn Schritt 6 noch nicht angefangen wurde, ist der Merge trivial — direkter Fast-Forward möglich, weil M1-Änderungen alleinstehen.

## Bekannte Konfliktstellen (sortiert nach Schwere)

### Kritisch: `src/Geef.Atelier.Web/Services/RunOrchestratorService.cs`

**M1-Änderung:** Konstruktor-Typen umbenannt (`anthropicClient`→`llmClient`, `IAnthropicClient`→`ILlmClient`, `AnthropicOptions`→`LlmOptions`); `AtelierPipelineFactory.Build`-Aufruf mit neuen Typen.

**Schritt-6-Änderung:** Erweitert um Cancellation-Watcher-Logik (DB-Flag-Polling + per-Run-CTS signalisieren). Voraussichtlich neue Felder, neue Methoden, möglicherweise neue Konstruktor-Parameter.

**Strategie:** Rebase M1 auf aktualisierten main, dann Konflikt manuell lösen. Substanz: ~10 Zeilen Typ-Renames in der Schritt-6-erweiterten Klasse anwenden. Kein struktureller Konflikt — die zwei Änderungssätze adressieren orthogonale Aspekte (M1 = Provider-Schicht-Adapter; Schritt 6 = Cancellation-Mechanismus).

### Mittel: `src/Geef.Atelier.Web/Program.cs`

**M1-Änderung:** Eine Zeile — `AddAnthropicClient(...).AddStandardResilienceHandler()` → `AddLlmClient(...).AddStandardResilienceHandler()`.

**Schritt-6-Änderung:** Voraussichtlich `AddScoped<IRunService, RunService>()` und ggf. weitere DI-Registrierungen für die Application-Schicht.

**Strategie:** M1-Zeile beibehalten; Schritt-6-Zeilen zusätzlich übernehmen. Beide Änderungssätze sind disjunkt — sollte ein dreizeiliges Diff produzieren statt eines echten Konflikts.

### Mittel: `src/Geef.Atelier.Web/appsettings.json`

**M1-Änderung:** Sektion `"Anthropic"` (3 Felder) ersetzt durch `"Llm"` (mit Endpoint, ApiKey, DefaultModel, DefaultMaxTokens, Actors-Dictionary).

**Schritt-6-Änderung:** Möglicherweise neue Sektionen (z.B. für Cancellation-Polling-Intervall, falls Schritt 6 das via Konfiguration steuert).

**Strategie:** `"Llm"`-Sektion aus M1 übernehmen; alle Schritt-6-Sections additiv ergänzen. Wenn Schritt 6 keine `"Anthropic"`-Sektion berührt hat, kein echter Konflikt.

### Niedrig: `tests/Geef.Atelier.Tests/Orchestrator/OrchestratorTestHost.cs`

**M1-Änderung:** Type-Renames (`IAnthropicClient`→`ILlmClient`, `AnthropicOptions`→`LlmOptions`). Tests-Setup mit `LlmOptions.Actors`-Dictionary statt einzelnen Modell-Properties.

**Schritt-6-Änderung:** Möglicherweise erweitert um Setup-Helper für Cancellation-Watcher-Tests.

**Strategie:** Renames mechanisch nach Merge anwenden. Tests durchlaufen lassen.

### Niedrig: Weitere Test-Dateien

Alle Test-Dateien, die `AnthropicOptions`/`AnthropicTokenUsage`/`FakeAnthropicClient` referenzierten. Schritt 6 hat hier vermutlich nur neue Tests hinzugefügt, die `IRunService` testen — orthogonal.

## Empfohlene Schrittfolge

```bash
# 1. Sicherstellen, dass main aktuell und Schritt 6 abgeschlossen ist
git checkout main
git pull origin main

# 2. M1-Branch checkout, dann auf main rebasen
git checkout feature/openai-compatible-providers
git pull origin feature/openai-compatible-providers
git rebase main

# 3. Konflikte lösen (Reihenfolge: kritisch → niedrig)
#    - Schwerpunkt: RunOrchestratorService.cs
#    - Dann: Program.cs, appsettings.json
#    - Dann: Test-Dateien

# 4. Nach jedem Konflikt: dotnet build für sofortige Verifikation
dotnet build

# 5. Wenn alle Konflikte gelöst und Build grün: rebase fortsetzen
git rebase --continue

# 6. Volltest mit Docker
dotnet test

# 7. Wenn 31/31 grün (oder mehr — Schritt 6 hatte ggf. neue Tests):
#    Force-Push auf den Feature-Branch (Rebase ändert History)
git push --force-with-lease origin feature/openai-compatible-providers

# 8. Merge in main
git checkout main
git merge --ff-only feature/openai-compatible-providers
git push origin main

# 9. Optional: Feature-Branch löschen
git branch -d feature/openai-compatible-providers
git push origin --delete feature/openai-compatible-providers
```

## Nach dem Merge — drei Folgeaufgaben

### 1. R2-Nachholpass (empfohlen, nicht Pflicht)

M1 wurde ohne formalen Geef-Workflow-Reviewer-Pass durchgeführt (siehe D-018). Insbesondere R2 (codex+gpt-5.4) hatte in Schritten 4 und 5 jeweils 1–4 MAJOR-Findings gefunden, die R1 (Claude) übersehen hatte — Drain-Race-Conditions, defensive JSON-Deserialisierung, Threading-Issues.

Auf den finalen post-Merge-Stand der LLM-Schicht (`OpenAiCompatibleClient`, `OpenAiMessageFormat`, `LlmReviewer`, `LlmExecutionStep`) einen R2-Pass laufen lassen. Dauert ~10 Minuten.

```bash
# Beispiel-Aufruf (anpassen an euer codex-Setup)
codex run --reviewer code-quality \
  --files src/Geef.Atelier.Infrastructure/Llm/ \
  --files src/Geef.Atelier.Infrastructure/Pipeline/Llm*.cs \
  --files tests/Geef.Atelier.Tests/Llm/ \
  --output docs/reports/m1-r2-followup.md
```

Findings ergänzend in D-018 dokumentieren (oder als D-019 wenn substantiell).

### 2. Real-OpenRouter-Integration-Test

`AtelierPipelineRunsAgainstOpenRouter` ist M1-AC9-Skip geblieben (kein Bearer-Key in der Ausführungsumgebung). **Vor Schritt-7-Beginn** einmal manuell ausführen:

```bash
# OpenRouter-Bearer-Key bereitstellen
export Llm__ApiKey="sk-or-v1-..."

# Test ausführen
dotnet test tests/Geef.Atelier.Tests/Geef.Atelier.Tests.csproj \
  --filter "FullyQualifiedName~AtelierPipelineRunsAgainstOpenRouter" -v n
```

Das verifiziert:
- `anthropic/claude-opus-4.7` ist der stabile Modellname auf OpenRouter (sonst `DefaultModel` auf `anthropic/claude-opus-4-5` oder ähnlich anpassen).
- `finish_reason: "tool_calls"` wird konsistent geliefert.
- Tool-Use-Schema-Kompatibilität.
- Cold-Start-Latenz und Token-Verbrauch im realistischen Bereich.

Beobachtungen kurz im Schritt-7-Bericht festhalten — das ist der erste echte End-to-End-Lauf der Pipeline gegen einen externen LLM-Provider.

### 3. Branch-Cleanup

Wenn alles grün ist und stabil läuft, den `feature/openai-compatible-providers`-Branch löschen (siehe Schritt 9 oben). Der Merge-Commit + Decisions-Log + Bericht halten alle relevanten Informationen fest.

## Zeitschätzung

| Phase | Aufwand |
|---|---|
| Rebase + Konflikt-Auflösung (`RunOrchestratorService.cs`) | 15–30 min |
| `Program.cs` + `appsettings.json` Konflikte | 5–10 min |
| Test-File-Renames | 5 min |
| `dotnet build` + `dotnet test`-Verifikation | 5–15 min (Testcontainer-Hochfahrzeit) |
| Force-Push + Merge + Push main | 2 min |
| **Summe Merge** | **~30–60 min** |
| R2-Nachholpass (optional) | 10 min |
| Real-OpenRouter-Test | 5 min Setup + 1–3 min Test-Lauf |

Insgesamt eine Stunde bis 90 Minuten konzentrierte Arbeit für den vollständigen M1-Abschluss inklusive Empfehlungen.

## Wenn etwas schief geht

**Falls Build nach Rebase rot:** Häufigste Ursache ist eine vergessene Type-Rename-Stelle. Suchen mit `git grep "Anthropic" -- "*.cs"` — nach M1 sollten keine Treffer mehr in `src/` oder `tests/` sein.

**Falls Tests rot, aber Build grün:** Vermutlich ein Setup-Pfad in `OrchestratorTestHost` oder anderen Test-Helpern, der die alte `AnthropicOptions`-Form referenziert. Setup-Code schrittweise auf die `LlmOptions.Actors`-Dictionary-Form umstellen.

**Falls Merge sehr aufwändig wird:** Zurück zu main, M1-Branch nicht mergen, stattdessen **Cherry-Pick einzelner Dateien** auf einen frischen Branch und dort den Provider-Wechsel atomar nachbauen. Aber das ist Notfall-Plan — der reguläre Rebase sollte ausreichen.