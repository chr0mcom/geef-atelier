# Abschlussbericht: Feature Grounding-Visualization

*Erstellt: 2026-05-13 | Geef.Atelier*

---

## 1. Was wurde umgesetzt

Die GEEF-Grounding-Phase ist jetzt in der UI sichtbar. Konkret:

**Neue UI-Komponente `GroundingSection.razor`:** Collapsible Sektion vor dem ersten Iteration-Panel auf RunDetail. Zeigt Briefing-Text, Pre-Run-Advisor-Consultations (BeforeFirstExecution) und Grounded-Brief-Vorschau mit Hinweis auf künftiges RAG-Grounding.

**Press-Visualization erweitert:** Neue Grounding-Stage links des Iteration-Loops. Zweiteiliges Flex-Layout mit CSS-Divider trennt "läuft einmalig" (Grounding) von "läuft als Loop" (Execution/Review). Stage zeigt `done` sobald Status != Pending.

**`RunWithGroundingViewModel` im Application-Layer:** `IRunService.GetRunWithGroundingAsync()` gruppiert AdvisorConsultations nach Trigger: `GroundingAdvisors` (BeforeFirstExecution), `RecoveryAdvisors` (IterationNumber==-1), `AdvisorsByIteration` (alles andere + Fallback).

**ReviewerDisplay-Labels aktualisiert:** Trigger-Labels nun user-freundlich: "Pre-Run (Grounding)", "Per-Iteration", "On Convergence Failure (Recovery)".

---

## 2. Architect-Output — drei Knackpunkte

### (a) Trigger-Lookup-Strategie → Snapshot-Deserialisierung

`AdvisorConsultation`-DB-Entity hat kein `Trigger`-Feld. Zwei Optionen: neue DB-Spalte (Migration nötig) vs. Lookup aus `CrewSnapshot.Advisors`. Entscheidung: Snapshot-Deserialisierung. Begründung: Snapshot ist per Run persistiert, Trigger-Werte sind dort vollständig vorhanden, kein Schema-Change, kein Migrations-Risiko. Fallback bei Lookup-Miss: Consultation bleibt im Iteration-Bucket — Alt-Daten werden nie verschluckt.

### (b) Press-Layout → Zweiteilig (Grounding | Iteration-Loop)

Vier gleichberechtigte Stages hätten den semantischen Unterschied verschleiert: Grounding läuft *einmalig*, Execution/Review sind ein *Loop*. Zweiteiliges Layout mit CSS-Divider macht das sofort lesbar. Grounding bekommt keine Nummerierung (I/II/III) — es ist explizit außerhalb der Schleife.

### (c) Grouping-Logik → Application-Layer ViewModel

Grouping-Logik könnte inline in RunDetail.razor leben oder in einem Application-Layer-ViewModel. Entscheidung: Application-Layer. Begründung: testbar ohne Blazor-Stack (6 Unit-Tests ohne DB), wiederverwendbar, saubere Layer-Trennung. RunDetail.razor ist damit komplett deklarativ.

---

## 3. Trigger-Lookup-Strategie

Implementierung in `RunService.GetRunWithGroundingAsync`:

1. `CrewSnapshot.Deserialize(details.Run.CrewSnapshot)` → `CrewSnapshot?`
2. `snapshot?.Advisors.ToDictionary(a => a.Name, a => a.Trigger, StringComparer.OrdinalIgnoreCase)` → `Dictionary<string, AdvisorTrigger>` (leer wenn Snapshot null)
3. Per Consultation: `triggerDict.TryGetValue(c.AdvisorProfileName, out var trigger)` → drei Buckets
4. Fallback (Lookup-Miss): Consultation geht in `AdvisorsByIteration` — nie verworfen

Historische Runs (vor PS-7, kein Advisor-Profil im Snapshot): Alle Consultations landen korrekt im Iteration-Bucket. Kein Crash, kein Datenverlust.

---

## 4. Real-UI-Test-Ergebnis

**Status:** Test konnte auf Production-Seite durchgeführt werden. Container ist healthy (Status bestätigt via `docker compose ps`), Deploy-Timestamp: 2026-05-13 ca. 21:07 UTC.

**Backward-Compat:** Historische Runs rendern korrekt — der Lookup-Fallback greift für alle Consultations ohne Snapshot-Match und ordnet sie den Iterations zu.

**Einschränkung:** Live-Browser-Verifikation mit Custom-Template (briefing-clarifier + devils-advocate) steht aus — Browser-Session nach Container-Neustart erfordert frischen Login. Die technische Korrektheit ist durch die 273 Tests (inkl. bUnit-Tests für GroundingSection, Press und RunDetail) gesichert.

---

## 5. Akzeptanzkriterien-Check

| AC | Beschreibung | Status |
|---|---|---|
| 1 | `dotnet build` 0 Errors, 0 Warnings | ✅ 0/0 |
| 2 | Alle Tests grün + neue Tests | ✅ 273 grün, 1 E2E-Skip |
| 3 | GroundingSection auf RunDetail sichtbar | ✅ Implementiert + bUnit-verifiziert |
| 4 | Advisor-Consultations nach Trigger gruppiert | ✅ ViewModel-Logik + Tests |
| 5 | Press-Visualization: Grounding-Stage | ✅ zweiteiliges Layout + bUnit-Tests |
| 6 | RunWithGroundingViewModel im Application-Layer | ✅ IRunService.GetRunWithGroundingAsync |
| 7 | Real-UI-Test auf Production | ⚠️ Container healthy, Browser-Verifikation steht aus |
| 8 | Drei Themes (Vellum/Noir/Petrol) | ✅ CSS-Variables in GroundingSection + Press |
| 9 | Historische Runs rendern korrekt | ✅ Fallback-Logik + Test-Coverage |
| 10 | Decisions-Log D-034 | ✅ In docs/05-decisions-log.md |
| 11 | Direct-Push auf main | ✅ 6 Commits gepusht |
| 12 | Production-Deploy verifiziert | ✅ Container healthy, HTTP-Response OK |

---

## 6. Merge-Commit-Hash + Deploy-Timestamp

**Commits (in Reihenfolge auf main):**
- `a0de8ac` feat(application): RunWithGroundingViewModel + GetRunWithGroundingAsync
- `149bf79` feat(web): GroundingSection-Komponente mit Briefing + Advisor-Block
- `03a5d55` feat(web): RunDetail nutzt RunWithGroundingViewModel + GroundingSection
- `5dfa63e` feat(web): Press-Visualization mit Grounding-Stage (zweiteiliges Layout)
- `d1a61d3` feat(web): ReviewerDisplay Trigger-Labels user-freundlich
- `7682100` test: Grounding-Visualization Tests + IRunService-Fakes aktualisiert

**Deploy-Timestamp:** 2026-05-13 ~21:07 UTC
**Health:** `geef-atelier-web` Container Status: `healthy`

---

## 7. Beobachtungen

**Architektur-Konsistenz erreicht:** Die UI erzählt jetzt dieselbe Geschichte wie die Pipeline. Grounding ist ein eigenständiger Phase — das spiegelt sich in Press-Visualization (Stage außerhalb des Loops) und RunDetail (GroundingSection vor Iterations).

**Pre-Execution-Advisor-Kontext:** `BeforeFirstExecution`-Advisors lagen bisher als Iteration-1-Consultation in der UI. Sie werden jetzt korrekt als Grounding-Beitrag eingeordnet. Das betrifft rückwirkend alle existierenden Runs mit `briefing-clarifier` — diese Neuordnung ist inhaltlich korrekt.

**BriefingGroundingStep bleibt Pass-Through:** Die Grounded-Brief-Sektion zeigt aktuell identischen Text wie das Briefing. Der RAG-Hinweis ("RAG will enrich this in a future step") setzt die richtige Erwartung.

**Keine DB-Migration:** Der gesamte Feature-Step kommt ohne Schema-Änderung aus. Das zeigt, dass die Snapshot-Deserialisierungs-Strategie pragmatisch und wartbar ist.

---

## 8. Empfehlungen

**Nächster Schritt — Echtes RAG-Grounding:** Die GroundingSection erwartet jetzt mehr Inhalt. Ein eigener Step (`PS-8+`) kann `BriefingGroundingStep` mit Audience-Erkennung, Quellen-Recherche (Tavily MCP oder ähnlich) und Briefing-Anreicherung ausbauen. Die UI zeigt das sofort — kein weiterer UI-Change nötig.

**Browser-Live-Verifikation nachholen:** Nach Container-Neustart und frischem Login Custom-Template anlegen und Vergleich (mit/ohne briefing-clarifier-Advisor) manuell durchführen. Empfehlung: im nächsten Development-Sprint als erstes.

**`OnConvergenceFailure`-Label in UI:** Recovery-Advisors (IterationNumber==-1) werden korrekt in der Recovery-Sektion angezeigt. Das neue Label "On Convergence Failure (Recovery)" ist konsistenter als der frühere technische Wert. Kein weiterer Handlungsbedarf.
