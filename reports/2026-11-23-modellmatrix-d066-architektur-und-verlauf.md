# D-066 — Verbindliche Architektur-Nachträge und GEEF-Verlauf

**Stand:** 2026-11-23. Dauerhafte Kopie der Abschnitte §7–§9 aus `geef_architecture.md` und des Evaluations-Logs. Die `geef_*`-Arbeitsdateien werden nach Phase 4.3 entfernt; die Decisions-Log-Einträge D-066/10–12 und `scripts/cli-proxy-selfupdate.sh` verweisen auf diese Datei.

---

## 7. Anpassungen nach Phase 1.6 (verbindlich, gehen §2–§4 vor)

1. **`ModelBindingPolicy.ForSave(ManagedBinding incoming)`:** Der Parameter `existing` entfällt.
   - `Slot` null → manuell, Binding `CliRoute.Normalize`, Domain null.
   - `Slot` gesetzt → verwaltet; Binding bleibt, wenn es ein Kandidat ist, sonst `Primary(slot, domain ?? General)`.
2. **Zuweisung nur in `IModelAssignmentService.ApplyAsync`** (Snapshot-Bau). Validator und Materializer bekommen **keinen** `IModelAssignmentService`.
   - **Materializer:** setzt Slot (geparst oder Default) und Binding `Primary(slot, crewDomain)`. Abweichende Composer-Angabe → Warnung.
   - **Validator Step 11:** prüft Slot-Syntax und Zulässigkeit (kritisch) sowie fehlenden Slot und Nicht-CLI-Route (nicht kritisch).
   - **Validator Step 10:** Plurality nur zwischen Teilen ohne Slot (manuell oder reuse ohne Slot); verwaltete Teile werden übersprungen.
   - **Step 8:** nutzt bei verwalteten Teilen den Primary-Provider.
3. **Gebiet:**
   - `spec.Domain` bleibt ein freier String (Packs, Namen, Dedup).
   - Crew-Gebiet = `ModelDomainParser.Parse(spec.Domain)`.
   - Step46 setzt `CrewTemplates.Domain` direkt je Template (Liste) und **keine** Executor-`ModelDomain`.
4. **Step46:**
   - Kein Default-Slot für Restbestand (Schritt 4 entfällt).
   - Klassifikations-Array als `internal static` für Tests.
   - Toleranter Enum-Konverter in den Configurations: unbekannter String → null.
5. **Matrix-Änderungen:**
   - CriticalAdvisor = Fable → Astra.
   - Composer-Reviewer: completeness und tool-binding = FactReviewer; fit und reuse-correctness = DomainReviewer; prompt-quality = StyleReviewer.
6. **Modellvergleich:** Gleich, wenn kanonische IDs gleich sind oder beide Anthropic sind und dieselbe Familie (opus/sonnet/haiku/fable) tragen, wobei mindestens eine Seite ein `-latest`-Alias ist.
7. **Warnungen:**
   - `ModelAssignmentService` loggt, wenn ein Primärkandidat nicht verfügbar ist, und wenn auf den Fallback (Regel 3) ausgewichen wird.
   - Der cli-proxy-Client loggt eine Warnung bei `x-cli-proxy-failover` (Machbarkeit prüfen).
8. **Konsistenztests** für die Modell-ID-Orte (siehe Advisor-Log).

## 8. Nachtrag D-066/10 und Härtung (2026-11-23, nach Iteration Advisor 2, verbindlich)

**8.1 Alias-Semantik Codex ≠ Claude.**
- Die Codex-Abbildung `gpt-<linie>-latest → konkrete ID` leitet der Proxy deterministisch und vollständig aus der Modellliste ab, auch im Zustand `static` (gegen `_FALLBACK_MODELS`).
- Der Proxy kennzeichnet das in beiden Codex-Katalog-Endpunkten mit `x_aliases_complete: true`.
- Ist `x_aliases_complete` gesetzt, **ersetzt** das Atelier die Abbildung. Das gilt bei `live` und bei `static`; bei `static` wird nicht mehr `RecordNoAliasLayer` aufgerufen.
- Claude ohne das Flag behält das D-064-Merge. Bei `degraded` oder Transportfehler bleibt die alte Abbildung stehen.
- Leitsatz: Der Snapshot hält die ID, die der Proxy in diesem Moment aufrufen würde.

**8.2 Eine Regel „ausgemustertes Codex-Modell“.**
- **Denylist in `CliRoute`:** `gpt-4*`, `gpt-5*`, `luna`, `-mini`, o-Series.
- **Ersatz:** Der Alias der eigenen Linie (Sol, Terra), sonst Astra. Die Aliase sind Konstanten in `ModelMatrix`.
- **Step46** nutzt für Profile und Finalizer dasselbe eingefrorene Regex-Literal. Eine bereits richtig geroutete, nicht ausgemusterte konkrete ID bleibt unverändert.
- **Paritätstest:** Eine gemeinsame Beispieltabelle läuft gegen `CliRoute.Normalize` und gegen das SQL.

**8.3 Verfügbarkeit: ein Prädikat für alle Pfade.**
- „Nachweislich unbenutzbar“ (gemeldet `[]` bzw. ID fehlt in gemeldeter Liste) gilt auch im Fallback.
- **Reihenfolge des Fallbacks:**
  1. ein nutzbarer Kandidat unter Einhaltung der Bezahlgrenze, notfalls ohne Familienpräferenz;
  2. ein nutzbarer, unbezahlter Kandidat, auch wenn er das Executor-Modell ist, mit Plurality-Warnung;
  3. erst zuletzt `candidates[0]`, mit der Warnung „kein nutzbarer Kandidat“.
- `IModelCatalog.ListReportedModelIdsAsync` hat keine Default-Implementierung. Unbekannt heißt `null`, nie `[]`.

**8.4 Host-Abgleich (Vertrag).**
- Der Abgleich läuft nach dem Lock in jedem Ausstiegszweig (gemeinsame Ausstiegsfunktion).
- Der Smoke-Test prüft den Exit-Code und nur die letzte Modellantwort.
- `config.toml` wird per TOML-Parser am Top-Level-Schlüssel gesetzt, atomar geschrieben und nachgelesen.
- Gescheiterte Backups, Schreibvorgänge und Rollbacks werden eigens gemeldet. Der Exit-Code ist ehrlich, auch bei `HOST_ONLY`.
- Der Schlüssel des Gateways steht nie in `argv`.
- Das Skript ist sourcebar, die Pfade sind per Umgebung überschreibbar, und es wird mit Fake-CLIs getestet.

**8.5 Deploy-Reihenfolge.**
1. Erst `cli-proxy`, dann `web`. Ohne Aliase im Proxy sind `gpt-sol-latest`/`gpt-astra-latest` nicht verfügbar und scheitern beim Aufruf.
2. Post-Deploy-Prüfung: `GET /v1/cli/codex-cli/models` liefert `x_source: live`, ein nicht leeres `x_aliases` mit `gpt-sol-latest` und `x_aliases_complete: true`.

## 9. Strukturänderungen nach Iteration 3 (2026-11-23, Iteration Advisor 3 und Fable-Berater, verbindlich)

**9.1 Matrix-Gebiet explizit (`model_domain`), Heuristik entfällt.**
- Das Composer-Schema bekommt das Pflichtfeld `model_domain`. Seine Enum-Werte werden aus `Enum.GetNames<ModelDomain>()` erzeugt; ein Drift-Test sichert das ab.
- Der freie `domain`-String bleibt für Packs, Namen und Dedup. Das hebt die letzte Zeile von D-066/4 auf.
- `CrewSpecArtifact.ModelDomain` speichert den Rohwert. Die einzige Auflösung ist `EffectiveModelDomain()`: zuerst `model_domain` exakt geparst, sonst ein exakter Parse von `domain`, sonst General.
- Fehlt `model_domain` oder ist es ungültig, gibt Validator Step 11 eine **Warnung**. Das Gebiet ist dann der freie `domain`-String, sofern er ein exakter Enum-Name ist (Kompatibilität mit alten Artefakten), sonst General.
- `ModelDomainParser` verliert die Synonymtabelle vollständig. Es bleibt nur `TryParseExact` über `EnumNames`.
- Bewusst akzeptiert: Eine Fehleinstufung durch den Composer kostet höchstens zwei bezahlte Reviewer (I2).

**9.2 Ein Zuweisungsprädikat.**
- **Fakten je Kandidat:** Verfügbarkeit ∈ {Confirmed, Unknown, Missing, Unusable}.
- **Harte Regeln, nie gelockert:**
  - H1: nicht Unusable.
  - H2: Ein bezahlter Kandidat ist nur für Reviewer-Slots erlaubt, nur wenn `paidUsed < 2` und nur, wenn das Gebiet Kerngebiet ist oder der Slot ≥ 3 verwaltete Reviewer hat (je Slot, gebietsunabhängig).
  - Nicht-Reviewer-Slots haben nie bezahlte Kandidaten; ein Matrix-Test sichert das ab.
- **Weiche Regeln, lexikografisch, kleiner ist besser:**
  1. Missing (vor Confirmed/Unknown);
  2. gleiches Modell wie der Executor;
  3. nur Reviewer: außerhalb des Rings;
  4. nur Reviewer: wie oft das Modell im Slot schon vergeben ist;
  5. nur Reviewer: gleiche Familie wie der Executor;
  6. Position in der Matrix.
- **Ring:** wird je Wahl gebildet, aus den ersten 3 Kandidaten, die H1 und H2 (inkl. aktuellem Kontingent) sowie die weichen Regeln 1 und 2 erfüllen, in familiensortierter Reihenfolge (Fremdfamilie zuerst). Korrektur nach Iteration 4; vorher fehlte das Kontingent im Ring.
- **Ablauf:** Reviewer werden in Index-Reihenfolge vergeben (deterministisches Bezahlkontingent).
- **Warnungen:** jede verletzte weiche Regel 1–2, außerdem 4 bei einer vermeidbaren Wiederholung.
- **Letzte Instanz** (nichts erfüllt H1 und H2): der erste unbezahlte Kandidat, Warnung „no usable candidate“. Jede Matrixliste enthält einen unbezahlten Kandidaten; ein Test sichert das ab.
- **Absicherung:** ein erschöpfender Invarianten-Test I1–I6, je Invariante ein `[Fact]` mit Schleife. Er nutzt echte Matrixkandidaten und gemischte Profil-Gebiete und gibt die fehlschlagende Kombination aus.
- **Katalog:** Ein leeres HTTP-200 ergibt `reported = []`, ein Transportfehler `null`.

**9.3 Composer-`urls`.**
- `urls` ist nicht in der Whitelist.
- Ein Inline-`url-fetch` wird mit Warnung verworfen, nicht leer materialisiert. Der Composer-Prompt nennt den Typ „nur per Wiederverwendung“.
- DNS-Pinning ist eine eigene, priorisierte Härtung: vorbestehend, betrifft auch das Studio per MCP; Voraussetzung für ein agentisches `url-fetch`.

**9.4 Stoppschwelle für Iteration 4.** Angehalten wird, wenn
- ein Reviewer bei grünem Invarianten-Test ein Gegenbeispiel zu I1–I6 zeigt, oder
- ein Befund nur durch eine neue Regel, Ausnahme oder Wortliste in Parser oder Zuweisung zu beheben wäre.

Reine Verdrahtungsfehler der neuen Struktur bekommen genau eine weitere Runde.


---

# Evaluations-Log (GEEF Phase 3)

## Iteration 1 (2026-11-22 22:3x, Reviewer parallel: claude -p functional/tests/architecture, codex exec quality)

- **Strategie:** alle vier Reviewer parallel; Reviewer 5 (Live-UI) erst nach Deploy.
- **Codex-Reviewer:** Er scheiterte zuerst an der veralteten Host-CLI (0.151 lehnt `gpt-6.2-sol` ab). Nach dem CLI-Update auf 0.171.0 (2026-11-23) lief er erneut.
- **Befunde je Reviewer:**
  - functional: 1 MAJOR (Verfügbarkeit gegen gemergte Liste), 7 MINOR.
  - quality: 3 CRITICAL (Composer-`settings` ermöglichen Webhook- und REST-Geheimnis-Abfluss; FieldHelp), 5 MAJOR, 3 MINOR.
  - architecture: 6 MINOR.
  - tests: 0.
- **Aktionen:**
  - Alle Befunde behoben (D-066/11), dazu neue Tests.
  - Nebenbei kam die Betreiberanforderung „immer aktuelle Modelle“ dazu: D-066/10 mit Codex-Aliasen, Host-Abgleich im Selfupdate und Failover auf `gpt-sol-latest`.
  - Kein Iteration Advisor, laut Workflow erst ab Iteration 2.
- **Bewusst nicht geändert:**
  - Die Netzgrenze im Webhook-Executor für betreiberkonfigurierte Senken; der Inline-Weg ist geschlossen.
  - Python-Kommentare (Konvention gilt für .NET).

## Iteration 2 (2026-11-23 12:36–12:42, gleiche Strategie)

- **Befunde je Reviewer:**
  - functional: 1 MAJOR (Host-Smoke findet „pong“ im ausgegebenen Prompt), 5 MINOR.
  - quality: 7 MAJOR, 2 MINOR.
  - architecture: 4 MINOR.
  - tests: 0.
- **Zählkorrektur laut Iteration Advisor:** quality hat 8 MAJOR, 2 MINOR.
- **Iteration Advisor** (`geef_advisor_iteration_2.md`):
  - **Grundursachen:** (A) D-066/10 lief ohne eigenes Design-Gate; (B) Fixes nur am Fundort statt an allen Konsumenten der Regel; (C) doppelte Regeln ohne Paritätstest. Der Trend ist konvergent.
  - **Empfehlungen:** Architektur-Nachtrag §8 und testgetriebenes Vorgehen für die drei Brennpunkte Parser, Verfügbarkeit und Step46.
- **Aktionen:**
  - `geef_architecture.md` §8 ergänzt.
  - Cluster R (eine Retired-Regel, Konstanten, Paritätstest C# ↔ SQL).
  - Cluster V (Fallback mit Nutzbarkeit, kein Interface-Default).
  - Cluster C (geparster Typ, unbekannter Typ kritisch, tote Zweige weg, Prompts).
  - F3 (Liste der Rechtsgebiete plus Negativkorpus).
  - Cluster K: `x_aliases_complete`, Ersetzen statt Mergen, Adapter-Default, Familien-Warnung, Python-Fallback-Paritätstest.
  - Cluster S: Skript-Umbau und Skript-Tests, per Subagent.
  - Doku: Deploy-Reihenfolge, D-066/11 erweitert.

## Iteration 3 (2026-11-23 12:58–13:03, gleiche Strategie, Briefing mit der Liste „bewusst belassen“)

- **Befunde je Reviewer:**
  - functional: 3 MINOR (Parser `rechts*`, Ring je (Slot, Gebiet), Container-Rollback Exit 1 statt 2).
  - quality: 5 MAJOR (Fallback umgeht die Mindestgröße für bezahlte Modelle; leerer HTTP-Katalog gilt als unbekannt; Container-Smoke zu weich; Container-Rollback-Exit; Randfälle Trim und Provider-Vorrang Step46 ↔ CliRoute), 2 MINOR (mehrzeiliges TOML-`model`; `Enum.TryParse` mit `+2` bzw. Komma).
  - architecture: 0.
  - tests: 0.
- **Stagnation:** Gebiets-Parser (it1 `weg`/`paper`, it2 `*recht`, it3 `rechts*`) und Fallback/Verfügbarkeit (it1, it2, it3) treten zum dritten Mal auf. Der Workflow sieht Anhalten vor; der Betreiber hat die Entscheidung an einen Fable-Berater delegiert („nutze ein Fable Subagent als Berater, unterbrich nicht mehr“).
- **Zählkorrektur:** quality hatte 1 CRITICAL (Composer-`urls` für `url-fetch`), 5 MAJOR, 3 MINOR (inkl. Q9 HTTP-Default in der Doku).
- **Iteration Advisor 3** (`geef_advisor_iteration_3.md`): Strukturfix statt Patch für Parser und Zuweisung.
- **Fable-Berater** (vom Betreiber delegiert): weitermachen.
  - **Präzisierungen:** fehlendes `model_domain` nur als Warnung; Ring über die Kandidaten, die die weichen Regeln 1 und 2 erfüllen; keine bezahlten Kandidaten für Nicht-Reviewer; Vergabe in Index-Reihenfolge.
  - **Tests und Ziele:** erschöpfender Invarianten-Test; `urls` raus, Inline-`url-fetch` mit Warnung verwerfen; DNS-Pinning als eigene priorisierte Härtung; Q7 bleibt bewusst so.
- **Aktionen:**
  - `geef_architecture.md` §9.
  - `ModelChooser` (ein Prädikat) mit `ModelAssignmentInvariantTests` (≈ 97.000 Kombinationen, per Mutation geprüft).
  - `model_domain` (Schema, Artefakt, Parser, Validator-Warnung, Prompts, Heuristik gelöscht).
  - `EnumNames.ParseExact`.
  - Leeres HTTP-200 meldet `[]`.
  - CliRoute: Modell-ID vor Provider; Step46 mit `btrim`.
  - `url-fetch`/`urls`.
  - Container-Smoke und Rollback-Status 2 (Subagent).
  - Doku D-066/12, Q9.
- **Testläufe:** .NET 2351 grün (nur Step10 rot, vorbestehend), cli-proxy 579 grün.

## Iteration 4 (2026-11-23, gleiche Strategie)

- **Stoppschwelle:** laut §9.4.
- **Befunde je Reviewer:**
  - architecture: 1 MAJOR (Ring ohne Kontingent, Abweichung von §9.2, Gegenbeispiel I5).
  - functional: 2 MINOR (Ring/Kontingent; Template-Studio ohne Matrix-Gebiet).
  - quality: 3 MAJOR (I5-Gegenbeispiel; Dedup verwirft `model_domain`; Rollback-Ziel `latest` statt laufender ID), 4 MINOR (I4 in der letzten Instanz; Testraum/I3 zu schwach; Vertrag bei fehlendem `model_domain`; `crew-design-rules` veraltet).
  - tests: 2 MINOR (Override-Richtungen; Vertrag `model_domain`).
- **Stoppschwelle §9.4:** wörtlich getroffen (Gegenbeispiele I4/I5 bei grünem Test).
- **Fable-Berater** (delegiert): kein Stopp; (1) und (2) sind Verdrahtungsfehler gegen §9.2 und bekommen die eine erlaubte weitere Runde. Iteration 5 ist die letzte.
- **Aktionen:**
  - Test zuerst verschärft (I3/I4/I5 zum Zeitpunkt jeder Wahl; Raum ≈ 645.000 Fälle inkl. Helper, Overrides, eingeschobene Rolle, manuelle unbezahlte, Teil-Fehlen); er war rot am alten Code.
  - Ring je Wahl aus den erlaubten Kandidaten inkl. Kontingent.
  - Plurality- und Wiederholungswarnung auch in der letzten Instanz.
  - `AtMostTwoReviewersPerCrewArePaid` → fünfter Reviewer Sonnet (dokumentiert).
  - Dedup nur bei gleichem Gebiet (Test).
  - Rollback-Ziel = laufende Image-ID (Subagent, 5 Tests).
  - Vertrag `model_domain` in §9.1, D-066/12 und Validator-Doku angeglichen.
  - `crew-design-rules` aktualisiert.
  - Doku „mit Warnung“.
  - Template-Studio-Gebiet und die Step-11-Warnung bei `existing-template` vertagt (D-066/11).
- **Tests:** .NET 2353 grün (Step10 rot, vorbestehend), cli-proxy 584 grün.

## Iteration 5 (2026-11-23, letzte Runde laut Fable-Berater)
- **Befunde je Reviewer:**
  - tests: 0.
  - architecture: 2 MINOR (englischer D-066/12-Text, Formatierung in `crew-design-rules`).
  - functional: 1 MINOR (Crew-Doku zu fehlendem `model_domain`).
  - quality: 2 MAJOR (Katalog meldet fehlenden OpenRouter-Schlüssel bei Budget-Timeout als „unbekannt“ → I3-Gegenbeispiel auf Integrationsebene; cli-proxy liefert nach Verlust von `models_cache.json` die alte Live-Liste statt der kuratierten, sodass der Failover-Alias unaufgelöst bleiben kann), 4 MINOR (Tab-Trim in Step46, defekte Codex-Cachedatei im Host-Abgleich als Status 0, Doku-Rückfallstufe `model_domain`, Editor-Hinweis „never share“).
- **Stoppschwelle:** §9.4 bzw. Fable-Entscheidung nach Iteration 4 („letzte Runde, Stopp ohne weitere Auslegung“) ist erreicht: I3-Gegenbeispiel bei grünem Invarianten-Test.
- **Einordnung:**
  - Ursache ist ein falscher Fakt im Katalog (Reihenfolge Schlüsselprüfung vs. HTTP-Timeout), nicht die Zuweisungsregel.
  - Keiner der Befunde braucht eine neue Regel oder Wortliste.
- **Status:** angehalten, Entscheidung beim Betreiber. Nichts deployt, nichts committet.
- **Betreiberentscheidung (AskUserQuestion, 2026-11-23):** „Fixen + 1 Prüfrunde“.
- **Aktionen:**
  - Schlüsselprüfung unabhängig vom HTTP-Ergebnis (Budget-Timeout-Test).
  - cli-proxy liefert bei fehlendem Cache die kuratierte Liste (Übergangstest live → static).
  - Step46 trimmt Tab/CR/LF wie C# (Paritätsfälle).
  - Host-Abgleich meldet unlesbare `models_cache.json` als Fehler (3 Tests) und unterscheidet den gültigen Fall ohne Sol.
  - Doku/Prompts/Editor-Hinweis/CLAUDE.md/EN-Log angeglichen.
  - `crew-design-rules`-Formatierung korrigiert.
- **Tests:** .NET 2360 grün (Step10 rot, vorbestehend), cli-proxy 589 grün.

## Iteration 6 (2026-11-23, Bestätigungsrunde nach Betreiberentscheidung)
- **Befunde je Reviewer:**
  - functional: 0 (alle it5-Befunde bestätigt behoben).
  - architecture: 2 MINOR (Doku-Restposten).
  - tests: 1 MINOR (Invarianten-Test stört Budget-Test parallel).
  - quality: 3 MAJOR (Studio blockiert Transform ohne Provider; Failover-Alias ohne Sol-Linie; Host-Abgleich CLI+Modell blockieren sich), 2 MINOR (gedrosselter Refresh nach Cacheverlust; `\v` in Postgres).
- **Aktionen:** alle behoben. Studio-Validierung und -Materialisierung, `_model_arg` ohne `-m` bei unauflösbarem Failover-Default, statische Liste bei Throttle nach Cacheverlust, `\x0B`, Zielkombinations-Smoke im Host-Abgleich (Subagent, 4 Tests), Doku, nicht parallele Collection.
- **Tests:** .NET 2365 grün (Step10 rot, vorbestehend), cli-proxy grün.
- **Weiteres Vorgehen:** Laut Betreiberentscheidung keine weitere volle Runde, sondern eine gezielte Gegenprüfung nur dieser Fixes, danach Phase 4.
- **Gezielte Gegenprüfung** (codex, `geef_review_verify6.md`): 3 MAJOR, 2 MINOR in den Fixes.
  - Studio: Prompt-Bearbeitung und sichtbare Modellwahl gingen verloren.
  - Explizit angefragter Sol-Alias fiel still auf den CLI-Standard.
  - Host behielt nach gescheitertem Modellwechsel die neue CLI mit dem alten Modell.
  - Race zwischen Cache-Datei und Zeitstempel.
  - Formulierung zu manuellen Reviewern.
- **Aktionen:**
  - Studio übernimmt den bearbeiteten Prompt und macht eine sichtbare Provider-/Modellwahl manuell; der Hinweis „Provider is required“ entfällt bei Transforms.
  - Interne Failover-Markierung `codex-failover:gpt-sol-latest`; nur sie fällt bei fehlender Sol-Linie auf den CLI-Standard.
  - `cached_fresh` setzt eine vorhandene Datei voraus.
  - Host-Abgleich rollt die CLI zurück, wenn der Modellwechsel nach Zielmodell-Smoke scheitert (5 Tests; bei nicht wiederherstellbarer Config Status 2 mit Sofort-Alarm, ohne CLI-Rollback).
  - Doku angepasst.
- **Tests:** .NET 2366 grün (Step10 vorbestehend rot), cli-proxy grün.
- **Abschluss Phase 3:**
  - Der Betreiber hatte „Fixen + 1 Prüfrunde“ entschieden. Die Prüfrunde (6) und die gezielte Gegenprüfung sind gelaufen, alle Befunde sind behoben.
  - Es gab keine weitere Runde, das ist bewusst und im Bericht offengelegt.
  - Weiter mit Phase 4.
