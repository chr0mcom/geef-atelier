# Claude-Code-Prompt: Post-Skeleton Schritt 2 — Reviewer-Kalibrierung

*Diese Datei ist als Eingabe für Claude Code gedacht. Wird parallel zu Post-Skeleton Schritt 1 (Postgres-Backup) gebaut oder direkt im Anschluss.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Walking Skeleton komplett, App läuft produktiv. Erste Real-World-Test-Briefings haben ein konkretes Kalibrierungs-Problem an der Pipeline aufgedeckt, das die Vertrauenswürdigkeit des Systems unterminiert. Deine Aufgabe ist **Post-Skeleton Schritt 2**: Reviewer-Kalibrierung, in drei zusammengehörigen Ebenen.

Dieser Prompt beschreibt **das Problem und die Ziele**, nicht den Umsetzungsplan. Du entwickelst den konkreten Implementierungs-Plan selbst in Phase 1.1 (Task Comprehension) und Phase 1.4 (Architect), nachdem du den aktuellen Code-Stand in Phase 1.2 (Grounding) gelesen hast.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, alle Hard Rules. **Plan-Phase-Integration** ist Standard (seit Schritt 5).

## Das beobachtete Problem

Ein Test-Briefing wurde durch die Pipeline gegeben mit der Frage:

> *Wie viele Farben sind mindestens notwendig, um eine Ebene einzufärben, wenn je zwei Punkte mit Abstand 1 unterschiedlich gefärbt sein müssen?*

Das ist das bekannte **Hadwiger-Nelson-Problem** der kombinatorischen Geometrie. Der Executor hat eine substantiell korrekte, gut strukturierte Antwort produziert (Hadwiger-Nelson-Problem, chromatische Zahl der Ebene, untere Schranke 4 via Moser-Spindel, obere Schranke 7 via Sechseck-Parkettierung, de Greys Durchbruch 2018 mit 5 als neuer unterer Schranke, offenes Problem zwischen 5 und 7). Faktisch korrekt, Quellen sauber referenziert, präzise Mathematik.

Der `KlarheitReviewer` produzierte sechs Findings:
- **2 × Critical**
- **3 × Minor**
- **1 × Info**

Die Pipeline brach ab mit Status `Aborted` und Begründung *"Aborted due to critical reviewer finding"* — wegen der `DefaultConvergencePolicy.AbortOnCritical=true`-Einstellung aus dem Walking Skeleton (siehe D-012).

**Die zwei Critical-Findings im Wortlaut:**

> *"Die Beschreibung der Moser-Spindel ist faktisch falsch: Die Moser-Spindel besteht aus 7 Knoten und 11 Kanten, nicht aus 'sieben Punkten' allgemein — **das ist zwar zufällig korrekt**, aber die Aussage 'ein Graph aus sieben Punkten in der Ebene mit Einheitsabständen' ist unpräzise. Wichtiger: Die untere Schranke 4 wurde bereits früher durch den Golomb-Graphen bzw. die Moser-Spindel (1961) gezeigt..."*

> *"Die Knotenzahl in de Greys ursprünglicher Konstruktion ist nicht korrekt wiedergegeben: De Greys erster publizierter Graph hatte 1581 Knoten — **diese Zahl stimmt zwar**, jedoch wird in der Literatur häufig auch 1567 oder 1585 genannt, je nach Version."*

In beiden Fällen schreibt der Reviewer selbst, dass die Zahl/Beschreibung **stimmt** — und stuft trotzdem als Critical ein. Das ist eine fehlerhafte Severity-Kalibrierung: was der Reviewer kritisiert, sind Präzisierungs-Wünsche und Stil-Nuancen, nicht Faktenfehler.

**Diagnose:**
Die aktuellen Reviewer-Prompts haben keine klare Definition, was "Critical" bedeutet. Ohne diese Definition tendiert das Reviewer-Modell (vermutlich Claude Opus oder ein anderes über OpenRouter) dazu, jede gefundene Verbesserungsmöglichkeit als wichtig zu markieren. Das ist ein bekanntes Phänomen bei LLM-Reviewern ohne Severity-Anchoring.

**Konsequenz ohne Mitigation:**
Bei real-world-Briefings mit Substanz wird die Pipeline mit hoher Wahrscheinlichkeit abbrechen, weil mindestens ein Reviewer mindestens ein Critical-Finding produziert. Das System ist in der aktuellen Form für echte Use-Cases nicht brauchbar.

## Die drei Ziele dieses Schritts

### Ziel A — Reviewer-Prompts schärfen (Kern-Maßnahme)

Die Reviewer-System-Prompts müssen eine **explizite, mit Beispielen gestützte Severity-Taxonomie** enthalten, die das Reviewer-Modell daran hindert, Stil-Imprecision als Critical zu markieren.

**Verbindliche Severity-Definitionen (Atelier-Standard):**

- **Critical** — Substanzielle Fakten- oder Logik-Fehler, die die Antwort grundlegend falsch oder irreführend machen. Ein Critical-Finding bedeutet: ein Leser, der dem Text vertraut, wird durch ihn fehlinformiert. Beispiele: falscher Name einer Person, falsche Jahreszahl eines historischen Ereignisses, falsches mathematisches Theorem-Statement, Widerspruch zwischen zwei Abschnitten desselben Textes.

- **Major** — Wichtige Auslassungen oder klare Ungenauigkeiten, die zwar nicht direkt fehlinformieren, aber den Nutzen der Antwort erheblich schmälern. Beispiele: ein zentrales Gegenargument fehlt, eine wichtige Einschränkung ist nicht erwähnt, eine zentrale Quelle wird nicht zitiert.

- **Minor** — Stil-Verbesserungen, Präzisierungswünsche, Klarheits-Erhöhungen. Der Text ist substantiell korrekt, könnte aber besser formuliert sein. Beispiele: zwei Sätze würden zusammengefasst klarer, eine Formulierung ist umständlich, ein Begriff sollte präziser definiert werden.

- **Info** — Optionale Hinweise, weiterführende Anregungen, Beobachtungen ohne Handlungsdruck. Der Reviewer findet nichts, was geändert werden müsste, hat aber eine Beobachtung mitzuteilen.

**Anti-Pattern explizit benennen in der Prompt:**
Wenn der Reviewer in seiner Begründung Formulierungen wie "ist zwar korrekt", "stimmt zwar", "zufällig richtig", "im Prinzip okay" verwendet, ist das Finding per Definition **kein Critical**, sondern höchstens Minor oder Info. Das ist die wichtigste Anti-Halluzinations-Regel für Severity-Klassifikation.

**Konkretes Beispiel in den Prompt einbauen:**
Verwende den Hadwiger-Nelson-Run als Negativ-Beispiel direkt in der Reviewer-System-Prompt — das gibt dem Modell ein Referenz-Anker, um künftig nicht in dieselbe Falle zu laufen. Z.B.: *"Wenn du eine Beschreibung wie 'Graph aus sieben Punkten mit Einheitsabständen' findest und dabei feststellst, dass die Zahlen stimmen — das ist ein Minor-Finding (Stil-Präzisierung), nicht Critical."*

### Ziel B — Convergence-Policy weniger fragil machen

Die `DefaultConvergencePolicy.AbortOnCritical=true`-Einstellung aus D-012 ist sinnvoll als Schutz gegen wirklich kaputte Pipeline-Antworten, aber sie macht das System **fragil gegen einzelne überzogene Reviewer-Findings**. Ein einzelner Reviewer kann den ganzen Run kippen.

**Mögliche Lösungs-Ansätze (Architect wählt einen oder kombiniert):**

- **(B1) AbortOnCritical=false und auf Multi-Iteration vertrauen** — die Pipeline iteriert (max 3x laut D-012), Executor sieht die Findings, verbessert. Wenn nach 3 Iterations immer noch Critical-Findings vorliegen, dann Abort.

- **(B2) Cross-Reviewer-Voting** — Critical-Abort nur, wenn **beide** Reviewer (BriefingTreueReviewer + KlarheitReviewer) in derselben Iteration Critical-Findings produzieren. Einzelne Critical-Findings werden zu Major degradiert oder schlicht ignoriert für den Abort-Entscheid.

- **(B3) Critical-Counting über Iterations** — Critical-Findings werden gezählt; nur wenn dieselbe Severity-Klasse über mehrere Iterations persistiert (Stagnation), bricht die Pipeline ab.

- **(B4) Konfigurierbare Policy** — `AtelierConvergencePolicy` mit Optionen aus `OrchestratorOptions` oder einer neuen `ConvergenceOptions`-Section in `appsettings.json`. Default-Werte konservativ, aber überschreibbar pro Deployment.

Empfehlung von hier: **B1 + B4 kombiniert.** AbortOnCritical=false als Default, MaxIterations=3 wie bisher, alles aus Config konfigurierbar. Damit hat die Pipeline drei Iterations Raum für Verbesserung, bevor sie aufgibt — und Maintainer können das später strenger einstellen, wenn sie es wollen.

Architect prüft, ob diese Empfehlung kompatibel mit dem Geef-SDK ist. Falls das SDK eine eigene `IConvergencePolicy`-Implementierung erlaubt (was D-012 nahelegt), dann ist B4 trivial; falls nicht, müssen wir an die Konfigurations-Hooks der `DefaultConvergencePolicy` herangehen.

### Ziel C — Multi-Iteration-Verbesserungsschleife tatsächlich nutzen

Das Geef-SDK hat das Iteration-Konzept bereits implementiert (D-012, D-015), mit `IterationHistory.Records` und `PreviousFindings`-Pattern. In der aktuellen Pipeline wird der Executor in Iteration 2+ vermutlich bereits mit den Findings der Vorherigen Iteration konfrontiert. Die Frage ist: **wird diese Information richtig genutzt?**

**Architect-Aufgabe in Phase 1.2:**
- Verifizieren, dass der Executor in Iteration 2+ tatsächlich die Findings aus Iteration 1 als Input bekommt.
- Falls ja: Wird das im Executor-Prompt explizit angesprochen ("hier sind die Findings der letzten Iteration, verbessere entsprechend") oder nur als unstrukturierter Kontext geliefert?
- Falls die Übergabe unstrukturiert ist: das ist die Verbesserungs-Stelle. Der Executor muss klar instruiert werden, dass er die Findings als Verbesserungsaufträge versteht und beim nächsten Versuch konkret darauf eingeht.

**Konkrete Verbesserungs-Charakteristik:**
Nach Iteration 1 mit Findings → Iteration 2 muss eine **erkennbare Verbesserung** liefern, nicht nur einen anderen Aufguss desselben Textes. Wenn der Reviewer in Iteration 1 sagt "Moser-Spindel-Beschreibung ist unpräzise (Minor)", soll Iteration 2 die präzise Beschreibung enthalten ("7 Knoten, 11 Kanten, von Leo und William Moser 1961"). Ist das nicht der Fall, hat die Pipeline ein **Stagnations-Problem**, das die Convergence-Policy als Abort-Signal nutzt — was dann auch ein legitimer Abort wäre.

## Was du tust — frei zu strukturieren

Du entwickelst in Phase 1 und 2 deinen eigenen Implementierungs-Plan. Plausible Bestandteile (nicht zwingend):

- **Phase 1.2 Grounding:** Aktueller Stand der Reviewer-Prompts lokalisieren (vermutlich in `Geef.Atelier.Infrastructure/Pipeline/` oder einer Konfiguration). Aktuelle Konvergenz-Policy-Konfiguration verstehen. Geef-SDK-Hooks für eigene Convergence-Policy untersuchen.
- **Phase 1.4 Architect:** Die drei Ziele konkret entscheiden — welcher Lösungsansatz für Ziel B, welche Form der Prompt-Schärfung für Ziel A, welche Eingriffe für Ziel C.
- **Phase 2 Execution:** Reviewer-Prompts schärfen (Ziel A), Convergence-Policy anpassen (Ziel B), Executor-Prompt für Iteration 2+ optimieren (Ziel C). Wahrscheinlich in dieser Reihenfolge, weil Ziel A ohne B und C bereits einen großen Effekt hat.
- **Phase 2 Verifikation:** Hadwiger-Nelson-Briefing replay als Real-Pipeline-Run. Erwartung: Pipeline konvergiert in ≤ 2 Iterationen mit Status `Completed`, Findings sind mehrheitlich Minor/Info, ein etwaiges Critical wäre ein substanzieller Faktenfehler (in diesem Text gibt es keinen).

## Akzeptanzkriterien

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün. Falls neue Tests für Reviewer-Kalibrierung sinnvoll sind (z.B. ein Severity-Klassifizierungs-Test mit synthetischen Findings), kommen sie dazu.
3. **Reviewer-Prompts enthalten klare Severity-Taxonomie** mit Beispielen und der Anti-Pattern-Regel ("stimmt zwar" ≠ Critical).
4. **Convergence-Policy ist nicht mehr fragil gegen einzelne überzogene Critical-Findings** — durch B1, B2, B3 oder B4 (oder eine begründete Kombination).
5. **Hadwiger-Nelson-Replay (Real-Pipeline-Test):** Derselbe Briefing-Text läuft erneut durch die Pipeline. Erwartetes Ergebnis: Status `Completed`, nicht `Aborted`. Mindestens eine Iteration mit Verbesserung gegenüber dem Original-Text (z.B. präzisere Moser-Spindel-Beschreibung). Token-Verbrauch und Dauer im Bericht festhalten.
6. **Mindestens ein weiterer Real-Pipeline-Test mit anderem Briefing-Typ** — eine technische Erklärung oder eine stilistische Aufgabe nach Wahl. Demonstriert, dass die Kalibrierung domänen-übergreifend trägt.
7. **Vorher/Nachher-Vergleich im Bericht:** Welche Findings hatte der Hadwiger-Nelson-Run vorher, welche jetzt? Wie viele Iterationen brauchte er? Welche Verbesserungen wurden zwischen den Iterationen sichtbar?
8. **Reviewer-Kalibrierungs-Dokumentation:** Die Severity-Taxonomie und die Convergence-Strategie sind in `docs/02-architecture.md` oder einer neuen `docs/06-reviewer-calibration.md` festgehalten, damit künftige Maintainer den Atelier-Standard kennen.
9. **Decisions-Log-Eintrag** (D-024 oder nächste freie Nummer) mit Architect-Entscheidungen und Realfakten.

## Was du in diesem Schritt NICHT tust

- **Keine neuen Reviewer-Rollen einführen** — die bestehenden zwei (`BriefingTreueReviewer`, `KlarheitReviewer`) bleiben. Domänen-spezifische Reviewer kommen in einem späteren Schritt (Roadmap-Punkt 8: Domänen-Spezialisierung).
- **Keine Provider-Schicht-Änderungen** — `ILlmClient`, `OpenAiCompatibleClient`, `LlmOptions` bleiben unverändert. Wir verbessern, was wir an die LLMs schicken, nicht wie wir mit ihnen reden.
- **Keine UI-Änderungen** — Findings werden weiter wie in Schritt 7 angezeigt. Die UI muss höchstens reflektieren, dass mehrere Iterationen normaler sind als bisher.
- **Keine Cost-Tracking-Aktivierung** — Roadmap-Punkt 3, separater Schritt.
- **Keine MCP-Änderungen** — die Tool-Antworten bleiben strukturell wie in Schritt 9.
- **Keine Audit-Trail-Erweiterung** — `CreatedByUser` bleibt, keine zusätzlichen Audit-Felder.
- **Keine Domain-Modell-Änderungen außer ggf. konfigurations-bezogene** — wenn `ConvergenceOptions` als neue Klasse kommt, ist das eine Konfigurations-Erweiterung, kein Domain-Modell-Eingriff.

## Architect-Konsultation (Phase 1.4) — fünf Schwerpunkte

Die Architect-Phase ist diesmal **substantieller als üblich**, weil du den Implementierungs-Plan selbst entwickelst. Mindestens diese fünf Punkte gehören in den Plan:

1. **Wo leben die Reviewer-Prompts aktuell?** System-Prompt-Strings in Code, separate `.md`-Dateien, `appsettings.json`, in der Pipeline-Konfiguration? Welche Form ist für die Schärfung am besten geeignet — Inline-Strings sind einfach, separate Dateien sind besser editierbar.
2. **Geef-SDK-Convergence-Hooks:** Erlaubt das SDK eine eigene `IConvergencePolicy`-Implementierung, oder ist nur die `DefaultConvergencePolicy` mit ihren Properties konfigurierbar? Davon hängt ab, wie viel Eingriff für Ziel B nötig ist.
3. **Welche Lösung für Ziel B?** B1 (AbortOnCritical=false + Multi-Iteration vertrauen) ist einfach und pragmatisch. B2 (Cross-Reviewer-Voting) ist eleganter, aber komplexer. B4 (konfigurierbar) ist Production-flexibel. Empfehlung war B1+B4, aber Architect entscheidet nach Code-Realität.
4. **Wie wird die Iterations-Verbesserung (Ziel C) verifiziert?** Reicht ein manueller Vorher/Nachher-Vergleich im Bericht, oder soll ein automatischer Test (z.B. "Pipeline mit synthetischem Schwach-Briefing muss in Iteration 2 verbessern") dazukommen?
5. **Test-Strategie:** Welche neuen Tests sind sinnvoll? Mindestens: Severity-Klassifizierungs-Test (synthetisches Finding-Beispiel → erwartete Severity). Optional: ein Integration-Test mit Mock-LLM, der gezielt überzogene Critical-Findings produziert, und die Pipeline soll trotzdem konvergieren.

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/post-skeleton-02-reviewer-calibration-report.md`, gleicher Aufbau wie bisher. Diesmal **besonders wichtig**:

1. **Was wurde umgesetzt** — Die drei Ziele konkret, mit Code-/Konfigurations-Änderungen.
2. **Architect-Plan** — der von dir entwickelte Umsetzungsplan, mit Begründung der gewählten Lösungs-Ansätze für die drei Ziele.
3. **Vorher/Nachher-Verifikation** — Hadwiger-Nelson-Replay-Daten:
   - Vorher: 6 Findings (2 Critical, 3 Minor, 1 Info), Status Aborted nach Iteration 1, 1160 Tokens, 24s Dauer
   - Nachher: <Findings>, <Status>, <Iterationen>, <Tokens>, <Dauer>
4. **Zweiter Real-Test mit anderem Briefing** — Daten analog.
5. **Reviewer-Iterationen** — der übliche fünf-Reviewer-Pass.
6. **Beobachtungen zur Kalibrierungs-Wirkung** — Hat die Severity-Definition allein gereicht? Wie viel Effekt hatte die Convergence-Policy-Anpassung? War die Iterations-Verbesserung (Ziel C) tatsächlich sichtbar?
7. **Empfehlungen für nächsten Schritt** — typische Kandidaten: LiveUpdateFlowTests-Stabilisierung, Cost-Tracking, RAG-Vorbereitung, Domänen-Spezialisierung (jetzt naheliegender, weil Reviewer-Mechanik solider).

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- Reviewer-Prompts dürfen sehr lang werden (Severity-Taxonomie mit Beispielen kann 500+ Tokens kosten) — das ist akzeptabel. Pro-Iteration-Reviewer-Cost steigt um geschätzt 5-10%, was bei 1-2 Cent Run-Kosten irrelevant ist.
- Keine Secrets im Bericht. Keine Token-Werte im Bericht. Real-Test-Daten (Briefing-Text, Final-Text-Sample, Token-Verbrauch) sind okay zu loggen.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.
