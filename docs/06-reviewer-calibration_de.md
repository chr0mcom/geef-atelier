# Reviewer-Kalibrierung

*[English](06-reviewer-calibration.md) · **Deutsch***

*Letzte Aktualisierung: 2026-11-27 (D-091: Stufe 8d, die Prüfschleife, für die dieser Standard geschrieben wurde, ist entfernt; Abschnitte als historisch markiert, Abschnitt zur Convergence aktualisiert; zuvor 2026-05-24, D-058: Pflicht-Findings-Regel ergänzt; Tool-Call-Retry im cli-proxy)*

Dieses Dokument beschreibt den **Atelier-Standard für Reviewer-Severity** und die **Convergence-Policy-Strategie**.

> **In Stufe 8d entfernt (D-091).** Die alte Prüfschleife, in der KI-Reviewer mit eigenen Prompts den Schweregrad jedes
> Befunds wählten, gibt es nicht mehr. Severity-Taxonomie, Tool-Schema, Pflicht-Findings-Regel und der Weg, neue Reviewer
> hinzuzufügen, sind unten **historisch**. Die Prüfer des Direktwegs beschreibt
> [08-crew-system_de.md](08-crew-system_de.md#vorlage-direct-zwei-prüfer-d-073); dort legt der Code den Schweregrad eines
> Befunds fest, und nur `Error` oder höher blockiert.

## Severity-Taxonomie (Atelier-Standard)

*Historisch (in Stufe 8d entfernt, D-091).* Die Prüfschleife nutzte vier Severity-Stufen für Reviewer-Findings. Die Definitionen waren für ihre Reviewer-Prompts verbindlich.

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

Die Tests, die dieses Beispiel verankerten, wurden mit der Prüfschleife entfernt.

## Tool-Schema

*Historisch (in Stufe 8d entfernt, D-091).* Die Reviewer antworteten über das Tool `submit_review` mit
`"severity": { "enum": ["critical", "major", "minor", "info"] }`.

### Pflicht-Findings-Regel (D-058)

*Historisch (in Stufe 8d entfernt, D-091).* Jeder Reviewer der Prüfschleife musste mindestens ein Finding zurückgeben,
durchgesetzt vom System-Prompt und von einer Wiederholung im Code.

## Convergence-Policy

Die Policy wird via `ConvergenceOptions` (`src/Geef.Atelier.Infrastructure/Configuration/`) konfiguriert und aus dem Abschnitt `Convergence` der `appsettings.json` gelesen. Eine Aufstellung im Code darf die Werte überschreiben:

| Wert | `appsettings.json` | Override von `direct` und `direct-outline` |
|---|---|---|
| `MaxIterations` | 6 | 2 |
| `AbortOnCritical` | false | false |
| `DetectRegression` | true | false |
| `StagnationThreshold` | 3 | 2 |

Jeder Lauf schreibt also einmal und korrigiert höchstens einmal. Was ein Lauf ausliefert, der nicht konvergiert, beschreibt
[08-crew-system_de.md](08-crew-system_de.md#konvergenz-und-auslieferung).

### Begründung: AbortOnCritical=false als Default

*Für die Prüfschleife geschrieben; der Default gilt weiter.* Mit `AbortOnCritical=true` (alter Default aus D-012) bricht ein einziger überzogener Critical-Finding die gesamte Pipeline ab. Das macht das System fragil gegen Reviewer-Kalibrierungsfehler. Mit `AbortOnCritical=false` sieht die nächste Iteration die Findings und kann sie adressieren.

D-025 dokumentiert die Entscheidungspunkte hinter dieser Kalibrierung.

## Neue Reviewer hinzufügen

*In Stufe 8d entfernt (D-091).* Es gibt keine Reviewer-Profile in der Datenbank und keine Seiten, um welche anzulegen. Die
Prüfungen des Direktwegs sind Profile in `SystemCrew`, deren Implementierungen `DirectPipelineAssembler` einsetzt; eine neue
Prüfung braucht Code.

## Learning-Evaluation-Crew — strenge Kalibrierung (D-054)

*In Stufe 8d entfernt (D-091)*, zusammen mit dem Lernzyklus. Die frühere Beschreibung steht in der Git-Historie.
