# Reviewer-Kalibrierung

*[English](06-reviewer-calibration.md) · **Deutsch***

*Letzte Aktualisierung: 2026-05-17 (Code-Referenzen auf Crew-Profil-System aktualisiert; Severity-Taxonomie inhaltlich unverändert)*

Dieses Dokument beschreibt den **Atelier-Standard für Reviewer-Severity** und die **Convergence-Policy-Strategie**. Es ist normatives Referenzdokument für alle, die Reviewer-Prompts anpassen oder neue Reviewer hinzufügen.

## Severity-Taxonomie (Atelier-Standard)

Die Atelier-Pipeline nutzt vier Severity-Stufen für Reviewer-Findings. Die Definitionen sind verbindlich — abweichende Interpretationen in Reviewer-Prompts sind ein Bug.

| Severity | Bedeutung | Beispiele |
|---|---|---|
| **critical** | Substanzieller Fakten- oder Logikfehler. Ein Leser, der dem Text vertraut, wird aktiv fehlinformiert. | Falscher Name einer Person; falsche Jahreszahl; falsches Theorem; Widerspruch zwischen zwei Abschnitten desselben Textes. |
| **major** | Wichtige Auslassung oder klare Ungenauigkeit, die den Nutzen erheblich schmälert, aber nicht direkt fehlinformiert. | Zentrales Gegenargument fehlt; wichtige Einschränkung nicht erwähnt; zentrale Quelle fehlt. |
| **minor** | Stil-Verbesserung, Präzisierungswunsch oder Klarheits-Erhöhung. Text ist substanziell korrekt. | Zwei Sätze wären zusammengefasst klarer; Begriff sollte präziser definiert werden; Formulierung ist umständlich. |
| **info** | Optionaler Hinweis ohne Handlungsdruck. Der Reviewer beobachtet etwas, ohne eine Änderung zu verlangen. | Hinweis auf weiterführende Quellen; Beobachtung über Tonalität ohne Kritik. |

### Anti-Pattern: "stimmt zwar" ≠ Critical

Die häufigste Fehlklassifikation: Ein Reviewer findet, dass etwas technisch korrekt ist, aber "hätte präziser formuliert werden können" — und stuft es als **critical** ein.

**Regel:** Wenn die Reviewer-Begründung Formulierungen enthält wie:
- "ist zwar korrekt, aber..."
- "stimmt zwar"
- "zufällig richtig"
- "ist im Prinzip okay, jedoch..."
- "die Zahl ist korrekt, allerdings..."

...dann ist das Finding **per Definition kein Critical**. Höchstens **minor**.

**Critical bedeutet: der Text ist falsch.** Nicht: "könnte präziser sein."

### Negativ-Beispiel (Hadwiger-Nelson)

Das Hadwiger-Nelson-Problem hat diese Fehlklassifikation ausgelöst:

> *"Die Beschreibung der Moser-Spindel ist faktisch falsch: Die Moser-Spindel besteht aus 7 Knoten und 11 Kanten, nicht aus 'sieben Punkten' allgemein — das ist zwar zufällig korrekt, aber die Aussage ist unpräzise."*

**Analyse:** Der Reviewer schreibt selbst "zufällig korrekt". Die Zahl 7 stimmt. Die Kritik ist eine Präzisierungs-Anfrage (graph-theoretische Terminologie "Knoten/Kanten" vs. "Punkte"). Das ist **minor**, nicht critical.

Die Hadwiger-Nelson-Taxonomie ist als `[InlineData]` in `SeverityClassificationTests` verankert.

## Tool-Schema

Das `submit_review`-Tool akzeptiert:
```json
"severity": { "enum": ["critical", "major", "minor", "info"] }
```

**Backwards-Kompat:** `ProfileBasedReviewer.MapSeverity()` (in `src/Geef.Atelier.Infrastructure/Pipeline/ProfileBasedReviewer.cs`) akzeptiert weiterhin `"error"` (→ `SdkSeverity.Error`) und `"warning"` (→ `SdkSeverity.Warning`) als Fallback für den Fall, dass das LLM vom Schema abweicht.

## Convergence-Policy

Die Policy wird via `ConvergenceOptions` (`src/Geef.Atelier.Infrastructure/Configuration/`) konfiguriert und aus `appsettings.json` gelesen:

```json
{
  "Convergence": {
    "MaxIterations": 3,
    "AbortOnCritical": false,
    "DetectRegression": true,
    "StagnationThreshold": 3
  }
}
```

### Begründung: AbortOnCritical=false als Default

Mit `AbortOnCritical=true` (alter Default aus D-012) bricht ein einziger überzogener Critical-Finding die gesamte Pipeline ab. Das macht das System fragil gegen Reviewer-Kalibrierungsfehler.

Mit `AbortOnCritical=false`:
- Die Pipeline iteriert bis zu `MaxIterations=3` Mal.
- Jede Iteration sieht die Findings der vorherigen und kann sie adressieren.
- Erst bei Stagnation (identische Findings über `StagnationThreshold=3` Iterationen) bricht die Pipeline ab — was dann ein legitimer Abort wäre.

### Wann AbortOnCritical=true sinnvoll ist

Wenn ein Deployment absolute Qualitätssicherheit erfordert und Reviewer-Kalibrierung als verlässlich gilt — z.B. domänen-spezialisierte Reviewer mit geprüften Prompts (Roadmap-Schritt 8: Domänen-Spezialisierung).

## Neue Reviewer hinzufügen

Seit dem Crew-System (D-028) sind Reviewer **datengetriebene Profile**, keine Code-Klassen
mehr (`LlmReviewer`/`AtelierSystemPrompts` wurden entfernt). Ein neuer System-Reviewer:

1. System-Prompt als `public const string` in `src/Geef.Atelier.Core/Domain/Crew/SystemPrompts.cs` anlegen.
2. Den **vollständigen Severity-Taxonomie-Block** aus einem bestehenden System-Reviewer (z.B. `briefing-fidelity` oder `clarity`) übernehmen — kein eigenes Schema erfinden.
3. Den Anti-Pattern-Abschnitt und das Hadwiger-Nelson-Beispiel mitkopieren.
4. Den Reviewer als `ReviewerProfile`-Konstante in `SystemCrew` (`src/Geef.Atelier.Core/Domain/Crew/SystemCrew.cs`) registrieren — mit Provider/Modell gemäß Modell-Pluralismus-Konvention (Fremd-Modell relativ zum Executor).
5. Bei Bedarf einem System-`CrewTemplate` in `SystemCrew` zur Reviewer-Liste hinzufügen. Custom-Reviewer entstehen stattdessen über `ICrewService` / die `/crew/profiles/reviewers`-UI — kein Code nötig.
6. `SeverityClassificationTests` um den neuen Reviewer-Namen erweitern (falls reviewer-spezifisch getestet).

D-025 dokumentiert die Entscheidungspunkte hinter dieser Kalibrierung.
