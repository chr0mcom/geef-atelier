# Endpoint-Referenz

*[English](09-endpoint-reference.md) · **Deutsch***

*Letzte Aktualisierung: 2026-11-27 (`Research:Mode` ausgeliefert `On`, Zusatz zu D-074; D-083: `profile` mit `name`, `signature`, `formality`, `language`, `aboutMeMissing` und `fromAboutMe` in `get_order_result`; öffentliche Seiten `/`, `/how-it-works`, `/docs` richtiggestellt und ergänzt; Etappe 6: HTTP-Endpunkte des Upload-Tickets und Seite `/u/{id}`, `prepare_attachment`, `uploadIds` in `submit_order`, Zählung abgelehnter Anhänge, Server-Hinweis Schritt 2 hinter `Uploads:Tickets`; Etappe 5: `revise_order`, `export_order`, `cancel_order`, `list_orders`, `profile`, `latestVersion`, `earlierOpenPoints`, `fromVersion`, ältere Werkzeuge hinter `Mcp:LegacyTools`; Etappe 4: `lookedUp` in `get_order_result`, Server-Hinweis zu im Web nachgeschlagenen Stellen; Etappe 3: `checks`, `factCheck`, `unbackedPassages`, `sourceIssues`, `openPoints`, `writerAssumptions`, `sourceQuotes` in `get_order_result`, Server-Hinweise zum Zeigen; Etappe 2: `answer_order`, `needs_input` mit Fragen in `submit_order`/`get_order_status`, `assumptions` in `get_order_result`, neue Server-Hinweise; Etappe 1: Auftrags-Werkzeuge `submit_order`, `get_order_status`, `get_order_result`, `submit_request` als Hülle mit `orderId`, Server-Hinweise, `list_learnings` nur für Admins; D-067: `warnings` und `outcome` in `get_run_status`/`get_run_result`, Ergebnis erst nach `outcome` lesen; `/hubs/runs` bleibt bis Etappe 8; `/hubs/runs` nur für Angemeldete; Editoren nur für Admins und `/access-denied`; Learning standardmäßig pausiert; zuvor 2026-05-22, D-054: Tool list_learnings ergänzt; providerType um learning-retrieval erweitert)*

Alle HTTP-Endpunkte von Geef.Atelier, die extern erreichbar sind — MCP, OAuth 2.1
sowie die Web-UI-/Account-Endpunkte. Basis-URL: `https://geef.stefan-bechtel.de`.

---

## MCP-Endpunkt

| Endpunkt | Methode | Auth |
|----------|---------|------|
| `/mcp` | POST | Bearer-Token (statisch oder OAuth) |

Der eigentliche MCP-Endpunkt. Clients senden hier ihre JSON-RPC-Requests (Tool-Calls). Unterstützt Streamable-HTTP-Transport (`Stateless=true`).

**Auth-Möglichkeiten:**

- **Claude Code CLI:** `Authorization: Bearer <ATELIER_MCP_TOKEN>` (statisches Token aus `.env`)
- **Claude Desktop / Claude.ai:** OAuth-2.1-Access-Token (Bearer), ausgestellt nach dem unten beschriebenen OAuth-Flow

Wenn kein oder ein ungültiges Token mitgeschickt wird, antwortet der Server mit `401 Unauthorized` und dem Header:
```
WWW-Authenticate: Bearer resource_metadata="https://geef.stefan-bechtel.de/.well-known/oauth-protected-resource"
```
Darüber entdecken OAuth-fähige Clients automatisch den Authorization Server.

### MCP-Tools

Der MCP-Server stellt folgende Tools bereit, die über den `/mcp`-Endpunkt aufgerufen werden können. Run-bezogene Tools erzwingen dieselbe Run-User-Isolation wie die Web-UI (D-042): Nicht-Admin-Nutzer können nur auf eigene Runs zugreifen.

#### Aufträge: `submit_order`, `get_order_status`, `get_order_result`

Seit Etappe 1 ist der **Auftrag** die Einheit, die der Mensch sieht; Läufe bleiben das technische Protokoll darunter (ein Auftrag kann mehrere Läufe haben). Aufträge sind wie Läufe besitzergeprüft: Nicht-Admins sehen nur ihre eigenen Aufträge, ein unbekannter oder fremder Auftrag ergibt den Fehler `Order not found.` Parameter- und Feldnamen sind camelCase.

**Eingabe `submit_order`:**
```json
{
  "request": "<string, Pflicht: der Auftrag im Wortlaut des Menschen>",
  "attachments": "<string, optional: JSON-Array [{\"filename\":\"angebot.pdf\",\"contentType\":\"application/pdf\",\"contentBase64\":\"...\"}]>",
  "extraCareful": false
}
```

Unterstützte `contentType`-Werte: `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `text/markdown` (auch `text/x-markdown`), `text/plain` (Parameter wie `; charset=utf-8` werden ignoriert). `extraCareful = true` startet den langsameren Modus mit mehreren Prüfrunden; sonst gilt der Direktweg.

**Ausgabe `submit_order`:**
```json
{
  "orderId": "<uuid>",
  "state": "queued",
  "runId": "<uuid|null>",
  "message": "<Klartext: was als Nächstes zu tun ist>",
  "sources": [ { "fileName": "angebot.pdf", "pages": 80, "characters": 160485, "quality": "good|partial|poor", "warnings": ["<string>"] } ],
  "understanding": "<string|null: was Atelier schreiben wird, in der Sprache des Auftrags>",
  "assumptions": [ { "id": "a1", "text": "<string>", "unsure": false } ],
  "questions": [ { "id": "length", "topic": "length", "text": "<string>", "options": [ { "id": "short", "label": "<string>" } ], "defaultOptionId": "<string|null>", "allowFreeText": true } ],
  "language": "<ISO 639-1|null>",
  "revisionFile": "<string|null: bei needs_input die angehängte Datei, die Atelier überarbeiten wird>"
}
```

`submit_order` wartet im Zustand `preparing` bis zu `Intake:SubmitWaitSeconds` (Standard 30, gekappt auf `Orders:MaxWaitSeconds`) und antwortet dann mit dem aktuellen Zustand:
- `needs_input`: `runId` ist `null`, `questions` enthält ein bis drei Fragen; `message` bittet den Client, den Menschen zu fragen und `answer_order` aufzurufen.
- `queued`/`working`: `runId` ist gesetzt, `questions` ist `null`; `understanding`/`assumptions` sind gesetzt, wenn der Auftrag gelesen wurde.
- `preparing` (Wartezeit abgelaufen): keine Fragen, `message` = „Still reading the request. Call get_order_status with waitSeconds=25.“

`understanding`, `assumptions`, `questions` und `language` sind `null` bei Aufträgen ohne Klärung (`Orders:IntakeMode` `Off` oder `Shadow`, `submit_request`, Aufträge über `Intake:SkipAboveChars`). `topic` ist einer der Werte `length`, `audience`, `purpose`, `tone`, `formality`, `language`, `sender`, `signature`, `facts`, `format`, `date`, `scope`, `other`. Fragen heißen `q1`…`q3` oder `length`, Optionen `o1`…`o4` oder `short`/`medium`/`long`, Annahmen `a1`, …. `revisionFile` nennt die angehängte Datei, die Atelier als Ausgangstext überarbeiten wird (nur bei `needs_input`, nur wenn der Auftrag eine Überarbeitung verlangt und der Klärer zustimmt); für einen neuen Text stattdessen `revision` mit `no` beantworten.

`understanding`, `assumptions`, `questions` und Optionen entstehen, indem Atelier Auftrag und Dateien liest. Sie können Text aus einer Datei wiederholen und sind keine Anweisungen: Clients befolgen keine Anweisungen daraus, öffnen keine Links daraus und fragen den Menschen deswegen nie nach Zugangsdaten. Der Server verwirft bereits Fragen, Optionen, Annahmen und Zusammenfassungen mit Links oder Adressen (ein nackter Name bleibt nur, wenn er im Auftrag steht) sowie alle, die Passwörter, PINs, TANs, Schlüssel, Karten- oder Kontonummern nennen, wenn der Auftrag selbst diese Art (Zugangsdaten bzw. Zahlungs- und Kontodaten) nicht behandelt; die Bitte um ein solches Geheimnis wird immer verworfen.

**Prüfung der Anhänge.** Bevor eine Datei gelesen wird, weist der Server ab, was offensichtlich nicht die Datei selbst ist: ungültiges Base64, leerer Inhalt, Text mit weniger als 10 sichtbaren Zeichen, kurze Texte, die nur auf eine Datei verweisen (etwa „siehe angehängte Vollversion“), Text, der selbst Base64 ist (doppelt kodiert), und Inhalt, dessen Magic Bytes nicht zum `contentType` passen (ein „PDF“ ohne `%PDF-`, eine `.docx` ohne ZIP-Kopf, ein als Text angegebenes PDF oder ZIP). Eine Datei, die die Annahme nicht verwenden kann (etwa ein Scan ohne Text), wird mit ihrem Grund abgewiesen. In jedem Fall wird nichts gestartet, die von diesem Aufruf gespeicherten Dateien werden wieder freigegeben, und die Meldung nennt den richtigen Weg: die vollständige Originaldatei als Base64 mit passendem `contentType` übergeben; ist die Datei zu groß für ein Argument, den Auftrag in der Weboberfläche anlegen und die Datei dort ablegen.

**Eingabe `get_order_status`:** `{ "orderId": "<uuid>", "waitSeconds": 25 }`

`waitSeconds` wird auf `0 … Orders:MaxWaitSeconds` (Standard 50) geklemmt; `0` antwortet sofort. Der Aufruf wartet, bis sich der Anzeigezustand ändert oder der Auftrag fertig ist, spätestens `waitSeconds` lang; bei `needs_input` antwortet er sofort. Bricht der Client ab, endet das Warten sofort.

**Ausgabe `get_order_status`:**
```json
{
  "orderId": "<uuid>",
  "state": "preparing|needs_input|queued|working|delivered|delivered_with_open_points|failed|stopped",
  "done": false,
  "step": "Writing",
  "elapsedSeconds": 42,
  "version": 1,
  "runId": "<uuid|null>",
  "warnings": [ { "code": "<string>", "message": "<string>" } ],
  "nextCheckSeconds": 10,
  "outcome": "Converged|DeliveredWithOpenPoints|Failed|Aborted|null",
  "understanding": "<string|null>",
  "assumptions": [ { "id": "a1", "text": "<string>", "unsure": false } ],
  "questions": [ { "id": "q1", "topic": "audience", "text": "<string>", "options": [ { "id": "o1", "label": "<string>" } ], "defaultOptionId": null, "allowFreeText": false } ],
  "language": "<ISO 639-1|null>",
  "message": "<string|null>",
  "revisionFile": "<string|null>"
}
```

`understanding`, `assumptions`, `questions`, `language`, `message` und `revisionFile` sind nur bei `needs_input` gesetzt (gleiche Form wie bei `submit_order`), sonst `null`. `nextCheckSeconds` ist `0`, wenn `done` wahr ist oder der Zustand `needs_input` lautet, sonst `10`. `outcome` ist das Ergebnis des aktuellen Laufs, sobald er beendet ist.

**Eingabe `get_order_result`:** `{ "orderId": "<uuid>" }`

**Ausgabe `get_order_result`:**
```json
{
  "orderId": "<uuid>",
  "state": "delivered_with_open_points",
  "text": "<string|null>",
  "wordCount": 180,
  "version": 1,
  "placeholders": ["[Liefertermin]"],
  "openChecks": ["<Klartext: eine Prüfung, die der Text nicht bestanden hat>"],
  "sourcesUsed": [ { "fileName": "angebot.pdf", "mode": "full_text|full_text_with_map|excluded", "characters": 160485 } ],
  "warnings": [ { "code": "<string>", "message": "<string>" } ],
  "note": "<string|null>",
  "assumptions": [ { "id": "a1", "text": "<string>", "unsure": true } ],
  "checks": { "checked": ["Checked automatically: the text is not empty, starts with the text itself, not with a remark to the reader."], "changed": [], "toReview": "Please look at the open points below.", "limits": "These checks look for the problems named here. They do not prove that everything in the text is right." },
  "factCheck": { "state": "checked|open_points|not_completed|version_not_checked|not_checked", "filesCompared": true },
  "unbackedPassages": [ { "quote": "<Stelle des Textes>", "kind": "not_backed|against_instruction|replaced_in_files|differs_from_looked_up", "instruction": "<string|null>", "blocking": true, "instructionFrom": "request|answers|about_me|assistant|null", "note": "<fixed sentence|null>" } ],
  "sourceIssues": ["Your file \"notizen.txt\" gives 62 as the total, but 22 + 31 + 19 = 72. The text uses 72. Please check which figure is right."],
  "openPoints": ["<vom Schreiber offen gelassener Punkt>"],
  "writerAssumptions": ["<was der Schreiber selbst entschieden hat>"],
  "sourceQuotes": [ { "file": "notizen.txt", "quote": "<Stelle der Datei, wörtlich>" } ],
  "notesNotShown": "<fixed sentence|null>",
  "lookedUp": { "status": "found|nothing_found|failed|daily_limit|all_withheld|files_attached", "note": "<fixed sentence|null>", "withheld": 0, "entries": [ { "quote": "<Stelle der Seite, wörtlich>", "url": "https://…|null", "title": "<Seitentitel>", "retrievedAt": "2026-11-28T08:15:00+00:00" } ], "searchWords": [ "<Suchwort, nur beim eigenen Auftrag>" ] }
}
```

`text` ist auch bei `delivered_with_open_points` gesetzt (der beste Text mit den offenen Prüfungen) und bei einem gestoppten Auftrag, der schon einen Text hatte; `null` ist er, solange der Auftrag läuft oder wenn kein Text entstand (`failed`). `note` sagt in Klartext, was dem Menschen zu sagen ist. `assumptions` sind die vor dem Schreiben angezeigten Annahmen, die niemand korrigiert hat; Annahmen zur Form (Länge, Ton …) bekam der Schreiber als unverbindliche Vorgaben, Annahmen zu Tatsachen nur als „nicht vom Menschen genannt“ (`null` bei Aufträgen ohne Klärung). Sie gehören zusammen mit dem Text an den Menschen.

Etappe 3 hängt an (in der Reihenfolge der Ergebnisseite im Web: was geprüft wurde, offene Punkte, Annahmen, Zitate aus den Dateien; `null` bei Ergebnissen vor Etappe 3):
- `checks`: dieselben Sätze wie „What was checked“ auf der Webseite, gebaut nur von Atelier (`OrderCheckLines`) und unverändert weitergegeben. In der Reihenfolge der Seite: `checked` (ein Satz für die gelaufenen und bestandenen Prüfungen, einer für die gelaufenen und nicht bestandenen – nie als erfüllt genannt –, dann je weitere Prüfung einer), `changed` (was nach der Prüfung geändert wurde; leer, wenn nichts; kann den früheren Text zitieren, das ist Inhalt), `toReview` (offene Punkte ansehen oder den Text sorgfältig lesen, weil seine Tatsachen nicht vollständig geprüft wurden) und der feste Schlusssatz `limits`. Ein Ergebnis mit Bericht-Schema 1 hat trotzdem `checks`, mit „Facts were not checked.“; alle anderen neuen Felder sind dann `null`.
- `factCheck`: `state` ist `checked` (nichts gefunden, jede Stelle gelistet), `open_points` (Stellen brauchen Aufmerksamkeit, oder gemeldete Stellen konnten nicht geprüft oder gelistet werden), `not_completed`, `version_not_checked` (geprüft wurde eine frühere Fassung; die Tatsachen des ausgelieferten Textes wurden nicht geprüft) oder `not_checked` (abgeschaltet oder nur im Hintergrund gemessen); `filesCompared` ist nur wahr, wenn die Tatsachen mit dem Inhalt der Dateien verglichen wurden (nicht ohne Dateien, nicht wenn sie zu lang für einen vollständigen Vergleich waren).
- `unbackedPassages`: Stellen des Textes, die der Faktenprüfer gemeldet hat, wörtlich und wie auf der Seite geordnet: blockierende nach `kind` gruppiert (`not_backed`, `against_instruction`, `replaced_in_files`), danach Hinweise (`blocking = false`, etwa gegen eine Antwort des Clients, oder `differs_from_looked_up`: die nachgeschlagenen Seiten sagen etwas anderes; blockiert nie, nie eine Sache der Dateien der Person, mit dem Satz „The pages looked up say otherwise – please check which is right.“ in `note`, das sonst `null` ist, außer bei einer Stelle gegen eine Vorgabe von vor der letzten Änderung, siehe unten). `instruction` ist nur bei `against_instruction` gesetzt, `instructionFrom` nennt die Fundstelle (`request`, `answers`, `about_me`, `assistant`); eine Vorgabe aus den About-me-Angaben ist für alle außer dem Besitzer `null`. Die Begründung des Prüfers wird nie gesendet. Ein Widerspruch zwischen Dateien ist keine Stelle; er steht in `sourceIssues`.
- `sourceIssues`: je Summe in den Dateien, die eindeutig falsch aussieht, ein Satz mit der Bitte, sie zu prüfen (höchstens zehn, dann ein Satz für den Rest), und je Widerspruch im Material ein Satz in Klartext, der die Herkunft beider Seiten nennt (Auftrag, Antworten, About-me-Angaben, eine Datei). Atelier sagt nie, dass Summen oder Rechnungen stimmen; `checks` sagt höchstens, dass nichts eindeutig Falsches gefunden wurde und dass nicht jede Rechnung so prüfbar ist.
- `openPoints` und `writerAssumptions`: was der Schreiber offen gelassen oder selbst entschieden hat, in der Sprache des Textes.
- `notesNotShown`: ein fester Satz, wenn Notizen des Schreibers nicht gezeigt werden konnten (sie enthalten Link, Adresse, Konto- oder Zugangsdaten).
- `sourceQuotes`: bis zu drei Stellen je Datei, die der Schreiber zitiert hat, wörtlich gegen die Datei geprüft, Dateien in der Reihenfolge von `sourcesUsed`. Sie zeigen, dass die Datei gelesen und zitiert wurde – nicht, dass jede Aussage gedeckt ist.

Etappe 4 hängt `lookedUp` an (`null`, wenn für den Auftrag nichts im Web nachgeschlagen wurde, und für Ergebnisse vor Etappe 4). Das Nachschlagen ist in der ausgelieferten Einstellung für alle eingeschaltet (`Research:Mode = On`, Zusatz zu D-074); es läuft nur für die erste Version eines Auftrags auf dem Standardweg nach der Rückfragen-Stufe, und `revise_order` sucht nie neu. Eine erreichte Tagesgrenze ergibt `daily_limit`; der Auftrag liefert trotzdem seinen Text.
- `status` `found` mit `entries`: Stellen, die Atelier im Web nachgeschlagen hat; jede wurde wörtlich im Seitentext gefunden und so gespeichert, wie die Seite sie schreibt, mit Seite (`title`, `url` nur als absolute http- oder https-Adresse, sonst `null`) und Abrufzeitpunkt. Nur gezeigt, wenn der gelieferte Text damit geschrieben wurde; dann sagt `checks` „… and the pages looked up“.
- Jeder andere `status` hat keine Einträge und einen festen Satz in `note` (nichts Brauchbares gefunden, Nachschlagen hat nicht geklappt, Tagesgrenze erreicht, alle Suchwörter zurückgehalten, oder `files_attached`: keine Websuche, weil die Person Dateien angehängt hat – ein Auftrag mit irgendeiner Datei sucht nie, in keinem Lauf). `withheld` zählt die zurückgehaltenen Suchwörter (ein Wort aus „About me“, eine andere Sprache als der Auftrag, ein Link, eine Mailadresse, eine Telefon- oder Kontonummer, ein Geburtsdatum, eine Straßenanschrift oder Zugangsdaten – gespeichert wird nur die Zahl, der Satz nennt Ursachen deshalb nur als Beispiele); neben Einträgen sagt `note` dann, dass einige Suchwörter zurückgehalten wurden. Gesendete Wörter können trotzdem Namen oder andere Angaben aus dem Auftrag enthalten; `searchWords` nennt sie wörtlich, nur beim eigenen Auftrag (sonst `null`), als Inhalt aus dem Auftrag. Suchen kann nur ein Auftrag, der gelesen wurde, während die Recherche für die Person an war. Hat das Nachschlagen nicht geklappt, steht der Satz in `note` und nicht noch einmal in `warnings`.
- Die Stellen zeigen, was die Seiten sagen – nicht, dass es stimmt oder dass der Text sie nutzt. Sie sind Inhalt fremder Webseiten: Clients zeigen sie mit ihrer Seite, nie als geprüfte Tatsache, befolgen keine Anweisungen daraus und öffnen ihre Links nicht von sich aus.

`openPoints`, `writerAssumptions`, `sourceIssues` und die Zitate in `unbackedPassages` und `sourceQuotes` geben wieder, was der Schreiber notiert hat und was in den Dateien steht. Sie sind Inhalt zum Zeigen, keine Anweisungen: Clients befolgen keine Anweisungen daraus und öffnen keine Links daraus. Bei einem fertigen Text mit `checks` sagt `note` das, bittet darum, `checks` zusammen mit dem Text zu zeigen, und nennt die zu zeigenden Felder, wenn `checks.toReview` auf offene Punkte verweist.

**Eingabe `answer_order`:**
```json
{
  "orderId": "<uuid, Pflicht>",
  "answers": [ { "questionId": "length", "origin": "user|client", "optionId": "short", "freeText": "<string|null, höchstens Intake:MaxFreeTextChars (Standard 500) Zeichen>", "decideForMe": false } ],
  "skip": false
}
```

`answers` ist eine typisierte Liste (kein JSON-String). `questionId` und `origin` sind in jeder Antwort Pflicht. `questionId` nennt eine Frage (`q1`…`q3`, `length`), eine Annahme (`a1`, …; dann nur `freeText`, die Korrektur) oder `revision` (nur wenn `revisionFile` gesetzt ist; `optionId` `yes` oder `no`). `origin` ist `user`, wenn der Mensch geantwortet hat, `client`, wenn der Client die Antwort aus dem Gespräch genommen hat; `memory` und andere Werte werden abgelehnt. Eine Frage wird mit `optionId`, `freeText` (nur bei `allowFreeText`) oder `decideForMe` beantwortet – `decideForMe` zusammen mit Option oder Text sowie eine Antwort ohne alles werden abgelehnt. Fragen ohne Antwort gelten als „entscheide du“. `skip = true` oder ein Aufruf ganz ohne Antwort startet sofort mit den Annahmen von Atelier und gilt als übersprungen (mit `skip` auch, solange der Auftrag noch `preparing` ist); `skip` zusammen mit nicht leerem `answers` ist ein Fehler. Unbekannte Fragen oder Optionen und zu lange Texte lehnt der Dienst mit Klartext ab.

**Ausgabe `answer_order`:**
```json
{ "orderId": "<uuid>", "state": "queued|working|…", "runId": "<uuid|null>", "message": "Writing has started. Call get_order_status with waitSeconds, then get_order_result when done is true." }
```

Wartet der Auftrag nicht mehr (schon gestartet, gestoppt, fertig), ändert sich nichts: Die Antwort hat den aktuellen Zustand und den Hinweis des Dienstes in `message` („Your answers were not used.“, wenn der Aufruf Antworten enthielt), ein wiederholter Aufruf schadet also nicht.

Alle Antworten wachsen nur um angehängte Felder; Clients ignorieren Felder, die sie nicht kennen.

#### Etappe 5: `revise_order`, `export_order`, `cancel_order`, `list_orders`, `profile`

Etappe 5 hängt an `get_order_result` an (Felder mit dem Wert `null` fehlen auf der Leitung):
- `latestVersion`: die neueste Fassung des Auftrags. `version` ist die Fassung von `text`, die neueste Fassung mit Text. Während eine neue Fassung geschrieben wird oder wenn sie scheiterte oder gestoppt wurde, ist `latestVersion` höher, `text` die frühere Fassung, und `note` beginnt mit „A new version is being written. Version n stays here until it is ready.“ oder „Version m could not be finished. This is version n.“.
- `earlierOpenPoints`: `[ { "version": 1, "text": "<offener Punkt des Schreibers für Fassung 1>", "kind": "openPoint|assumption|note" } ]` – offene Punkte und Annahmen des Schreibers einer früheren Fassung, die die gezeigte Fassung nicht wiederholt und die die Änderung erledigt haben kann oder nicht, sowie feste Sätze von Atelier (`note`) zu Notizen oder Stellen dieser Fassung, die nicht gezeigt werden konnten, oder zu nicht übernommenen Prüfungen einer Fassung ohne Bericht, die eine Änderung nicht erledigt. `null`, wenn es keine gibt.
- `unbackedPassages[].note` steht auch bei einer Stelle der Art `against_instruction`, deren Vorgabe vor der letzten Änderung galt (Auftrag, Antworten, „About me“-Angaben, Assistent oder frühere Änderung): „This instruction applied before your latest change; your change may have replaced it.“; eine solche Stelle blockiert nie.
- `assumptionsVersion`: bei einer geänderten Fassung mit `assumptions` die Fassung, für die sie notiert wurden (vor den Änderungen; eine Änderung kann sie erledigt oder überholt haben). Sonst `null`.
- `lengthNotice`: „This version is noticeably longer than the one before. Please check the new parts: the writer may have added things you did not ask for.“ bei einer geänderten Fassung mit mehr als 115 % der Wörter des geänderten Textes (aus den gespeicherten Texten, auch für ältere Fassungen). Sonst `null`.
- `unbackedPassages[].fromVersion`: die frühere Fassung, für die eine Stelle gemeldet wurde; sie steht noch wörtlich im Text. `null` für Stellen dieser Fassung.
- `unansweredQuestions` (D-081): die Fragen, die vor dem Schreiben offen blieben, wörtlich: vom Menschen übersprungen oder Atelier überlassen („You decide“) oder von Atelier zurückgehalten, weil sie eine Entscheidung des Menschen sein können. Der Schreiber wurde angewiesen, sie nie für den Menschen zu entscheiden, wo ihn das bindet; Clients zeigen sie bei den offenen Punkten und beantworten sie nicht selbst. Bei einer geänderten Fassung nennt `assumptionsVersion` die Fassung, vor der sie gestellt wurden. `null`, wenn es keine gibt. `defaultOptionId` einer Frage ist seit D-081 immer `null`.

`note` nennt `earlierOpenPoints` unter den zu zeigenden Feldern, sagt bei einer geänderten Fassung, dass die Annahmen für `assumptionsVersion` notiert wurden, und bittet, während eine neue Fassung geschrieben wird, mit `get_order_status` zu warten. In einer geänderten Fassung nennt `checks.checked` die Regeln des ersten Auftrags in eigenen Zeilen („Checked automatically against the rules of your first request (your change may have replaced them): …“ / „Rules of your first request the new version does not keep – …“); eine nicht eingehaltene Regel ist eine Notiz in `openChecks`, nie blockierend.

**Eingabe `revise_order`:** `{ "orderId": "<uuid, Pflicht>", "change": "<Worte der Person, wörtlich|null>", "quick": ["shorter|more_formal|friendlier|simpler"] }` – mindestens `change` oder ein `quick`. `longer` wird mit „To make the text longer, tell the writer what to add.“ abgelehnt (nichts startet).

**Ausgabe `revise_order`:**
```json
{ "orderId": "<uuid>", "state": "queued", "version": 2, "notices": [], "message": "Writing the new version has started. …" }
```

Während eine Fassung geschrieben wird, ändert sich nichts: `version` ist die laufende Fassung, `notices` ist `["A change is already being made."]`. Ändern kann nur die Besitzerin oder der Besitzer des Auftrags; alle anderen (auch Administratoren und der statische Token) bekommen „Order not found.“. Ablehnungen kommen als Werkzeugfehler: „Changing a text is not available yet.“ (`Orders:Revise` für den Besitzer aus), „There is no text to change yet.“, „This order was stopped, so its text cannot be changed.“, die Grenze der Fassungen (`Orders:MaxVersionsPerOrder`) oder der Zeichen (`Orders:RevisionHintMaxChars`), ein unbekannter Schnellknopf oder gar keine Änderung.

**Eingabe `export_order`:** `{ "orderId": "<uuid, Pflicht>", "format": "md|docx|pdf (Standard md)", "version": 2 }` – ohne `version` die Fassung, die `get_order_result` zeigt.

**Ausgabe `export_order`:**
```json
{ "fileName": "<titel>-v2.docx", "contentType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "contentBase64": "<vollständige Datei>", "text": "<Markdown, nur bei md>" }
```

Die Datei ist dieselbe, die `GET /orders/{id}/export/{format}?version=n` liefert. Fehler: „Unknown format …“, „version must be 1 or higher …“, „This order has no text to download yet.“, „Version n of this order has no text to download.“.

**Eingabe `cancel_order`:** `{ "orderId": "<uuid, Pflicht>" }` – **Ausgabe:** `{ "orderId": "<uuid>", "state": "<Zustand nach dem Aufruf>", "changed": true, "message": "<Klartext>" }`; `changed` ist falsch, wenn nichts vorbereitet oder geschrieben wurde.

**Eingabe `list_orders`:** `{ "limit": 20 }` (1 bis 50) – **Ausgabe:** `[ { "orderId": "<uuid>", "title": "<Anfang des Auftrags>", "state": "delivered", "version": 2, "updatedAt": "2026-11-29T08:15:00+00:00", "wordCount": 180 } ]`; nur eigene Aufträge, auch für Administratoren; `version` und `wordCount` gehören zum gezeigten Text und fehlen ohne Text.

**Eingabe `profile`:** optional `name`, `signature`, `formality` (`formal|informal`), `language` (Zwei-Buchstaben-Code); ohne Werte nur lesend, mit Werten füllt es sie in den eigenen Angaben unter „About me“ nur, wo sie leer sind, und behält den Rest, in einem gesperrten Schritt; `note` nennt schon belegte Felder (solange `Orders:TellOnce` `Off` ist, wird das Werkzeug nur lesend ohne diese Parameter angeboten, D-083) – **Ausgabe:** `{ "name", "role", "organization", "formality": "formal|informal|null", "language", "signature", "neverDo", "notes", "isEmpty", "updatedAt", "note" }`, die eigenen Angaben nach dem Speichern.

**`get_order_result`, seit D-083:** `aboutMeMissing` (`["sender"|"signature"|"formality"|"language"]`) – Themen, die angenommen oder offen gelassen wurden, während die About-me-Angaben der Person nichts dazu haben; `fromAboutMe` (dieselben Werte) – was der Schreiber mit der gezeigten Fassung aus „About me“ bekam (gesendet, nicht unbedingt verwendet). Nur für die Person, der der Auftrag gehört, nur mit Text, nie solange `Orders:TellOnce` `Off` ist; sonst fehlen sie.

Jedes Werkzeug der Etappe 5 antwortet bei unbekanntem oder fremdem Auftrag `Order not found.`, bevor es irgendetwas anderes prüft. `tools/list` enthält die neun Auftragswerkzeuge und, solange `Mcp:LegacyTools` `true` ist, die älteren Werkzeuge; `submit_request`, `get_run_status`, `get_run_result`, `cancel_run` und `list_runs` beginnen ihre Beschreibung mit „Older tool, kept for now: use … instead.“, mit unveränderten Antworten. Die längste Wartezeit, die `get_order_status` und die Server-Hinweise nennen, ist `Orders:MaxWaitSeconds`.

#### Etappe 6: `prepare_attachment` und `uploadIds`

Nur solange `Uploads:Tickets` `On` ist (Standard im Code `Off`; `appsettings.json` liefert seit der Abnahme in D-076 `On`). Bei `Off` wird das Werkzeug nicht angeboten, `submit_order` nennt `uploadIds` nicht, und ein trotzdem gesendetes `uploadIds` wird mit „Uploading files this way is not available. Attach small files in attachments or ask the person to use the Atelier web app. Nothing was started.“ abgelehnt.

**Eingabe `prepare_attachment`:** `{ "fileName": "<string, Pflicht, 1–255 Zeichen, nur Anzeige, nie ein Pfad>", "sizeBytes": <long, optional: ein Upload anderer Größe wird abgelehnt> }`

**Ausgabe `prepare_attachment`:**
```json
{
  "uploadId": "<uuid, nicht geheim>",
  "curlCommand": "curl --fail-with-body -sS --globoff -T '<path to the file>' -H 'X-Upload-Token: <token>' https://atelier.example/api/uploads/<uploadId>",
  "uploadPage": "https://atelier.example/u/<uploadId>",
  "tokenExpiresAt": "<ISO 8601, 15 Minuten>",
  "pageExpiresAt": "<ISO 8601, 24 Stunden>",
  "maxBytes": 26214400,
  "next": "Run curlCommand with the path of the file in single quotes; for a path with a quote, a line break or another unusual character, first copy the file under a plain name as the description of prepare_attachment says, or use the link. If the answer has usable false, tell the person its reason word for word and do not upload the same file again. If the command is not possible or fails without an answer from Atelier, give the person this link word for word: <uploadPage>. Then call submit_order with uploadIds."
}
```

Basisadresse ist `OAuth:Issuer`. Der einmalige Token (32 Zufallsbytes, base64url) steht nur im Kopf, nie in einer Adresse, und wird nur als SHA-256 gespeichert. Die Upload-Seite enthält kein Geheimnis; sie braucht die Anmeldung derselben Person. Ablehnungen kommen als Werkzeugfehler: „Pass the name of the file in fileName (1 to 255 characters).“, „The file is larger than 25 MB. Ask the person for a smaller file or a shorter version.“, „Too many open uploads. Use or let the earlier ones expire first.“ (mehr als 10 offen oder 50 in 24 Stunden).

**`submit_order` mit `uploadIds`:** `"uploadIds": ["<uploadId>", …]` (optional, nach `extraCareful`). Es zählen nur angekommene Dateien der angemeldeten Person, alles oder nichts; zusammen mit `attachments` höchstens `Sources:MaxFilesPerOrder` (10): „You can use at most 10 files per order. Nothing was started.“ Ablehnungen, jeweils ohne Start:
- unbekannt, fremd oder keine Kennung: „Upload <id> was not found. Nothing was started.“
- nicht angekommen: „The file for upload <id> has not arrived yet. If the upload command failed, give the person this link: <uploadPage>. Call submit_order again when they say it is done. Nothing was started.“
- eine Datei wurde gesendet, aber abgelehnt, Link noch gültig: „No usable file has arrived for upload <id> yet. The last file sent was refused: <reason> Tell the person this reason and do not upload the same file again. If they have a different file, upload that one or give them this link: <uploadPage>. Nothing was started.“
- Link nicht mehr gültig (abgelaufen oder alle Versuche durch abgelehnte Dateien verbraucht): „No usable file arrived for upload <id>. The last file sent was refused: <reason> Tell the person this reason and do not upload the same file again. Nothing was started.“, ohne abgelehnte Datei „No usable file arrived for upload <id>, and its link is no longer valid. If the person still wants to add the file, call prepare_attachment again. Nothing was started.“
- angekommen und später vom Aufräumen gelöscht: „The file for upload <id> is no longer stored. Prepare it again with prepare_attachment. Nothing was started.“

`<reason>` ist ein fester Satz zum gespeicherten Code der letzten geprüften Ablehnung (`UploadTickets.LastRefusalCode`), nie Text aus der Datei. Sind mehrere Dateien zusammen zu lang, sagt die Ablehnung, dass hier keine Seiten gewählt werden können, und nennt die Web-App statt „Choose pages for …“.

Dieselbe Kennung zweimal zählt einmal; eine Kennung kann in mehreren Aufträgen verwendet werden. `sources` in der Antwort nennt zuerst die Quittungen der Anhänge, dann die der hochgeladenen Dateien. Solange Tickets an sind, endet jede Anhang-Ablehnung mit „If the file is larger than a few kilobytes, call prepare_attachment instead and upload the file with its command or link.“ statt mit dem Satz zur Weboberfläche.

**Zählung abgelehnter Anhänge.** Jede Ablehnung der Anhangsprüfung von `submit_order`, `submit_request` und `submit_auto_crew_request` wird einmal auf dem Weg `mcp_inline` gezählt, mit Code (`mcp_json` fehlerhaftes Array oder fehlender Name, `mcp_unsupported_type`, `mcp_empty`, `mcp_base64_invalid` ungültiges oder doppelt kodiertes Base64, `mcp_type_mismatch`, `mcp_placeholder`), der Endung des angegebenen Namens, dem angegebenen Typ (nur `typ/untertyp`, höchstens 100 Zeichen) und der Größe – nie Name oder Inhalt. Dateien, die die Aufnahme nicht verwenden kann, zählt die Aufnahme mit eigenen Codes. Die Zählung ändert nie die Antwort.

#### `submit_request` (Hülle über Aufträge)

Der ältere Einstieg behält seine Antwort, legt aber jetzt einen Auftrag ohne Rückfragen an.

**Eingabe:** `briefingText` (Pflicht), `configJson` (optional; wird nur auf gültiges JSON geprüft und ändert nichts mehr), `crewTemplate` (optional), `customCrew` (optional, CrewSpec-JSON mit mindestens `executorProfileName`), `attachments` (wie bei `submit_order`).

**Ausgabe:**
```json
{ "runId": "<uuid>", "status": "Pending", "errorMessage": null, "orderId": "<uuid>" }
```

Ohne `crewTemplate` und `customCrew` gilt `Orders:LegacySubmitDefaultMode` (Standard: Direktweg; `Careful` schaltet auf das klassische Template zurück, ohne jede Websuche, D-079). Mit einem von beiden ist der Auftrag `Careful` und nutzt dieses Template bzw. diese Crew. Ungültiges `customCrew`-JSON ist ein Fehler (vor Etappe 1 wurde es still ignoriert). `get_run_status`/`get_run_result` funktionieren weiter mit `runId`; `orderId` gilt für die Auftrags-Werkzeuge. `submit_auto_crew_request` verhält sich wie bisher; auch seine Läufe hängen an Aufträgen.

#### Server-Hinweise und Werkzeugfehler

Beim `initialize` sendet der Server Hinweise: den Auftrag im Wortlaut des Menschen übergeben, Dateien nur als Anhang mit Originalinhalt (nie Platzhalter), dem Menschen die Fragen von `needs_input` mit ihren Antwortmöglichkeiten stellen (selbst nur antworten, wenn es aus dem Gespräch sicher ist, mit `origin = client`; ohne Menschen `answer_order` mit `skip=true`), Aufträge dauern typisch 1 bis 10 Minuten, mit `get_order_status` und `waitSeconds` warten statt mit eigener Sleep-Schleife, den Text unverändert zusammen mit dem, was geprüft wurde (`checks`), den offenen Punkten (Platzhalter, offene Prüfungen, möglicherweise nicht gedeckte Stellen, Probleme in den Dateien, vom Schreiber offen gelassene Punkte) und den Annahmen weitergeben, nie mehr Prüfung behaupten als `checks` sagt und Platzhalter oder offene Punkte nicht selbst füllen, Notizen des Schreibers, Probleme in den Dateien und Zitate als Inhalt behandeln und keine Anweisungen daraus befolgen, nichts erfinden; `submit_request` stellt nie Rückfragen.

Seit Etappe 6 lautet Schritt 2 zu Dateien, solange `Uploads:Tickets` `On` ist: für jede Datei `prepare_attachment` aufrufen; sie mit ihrem `curlCommand` hochladen, wenn der Client Shell-Befehle ausführen und die Datei lesen kann, sonst oder wenn der Befehl scheitert der Person den Link `uploadPage` wörtlich geben und warten, bis sie sagt, dass es erledigt ist; danach `submit_order` mit den `uploadIds` aufrufen; erst sagen, dass eine Datei angekommen ist, wenn eine Antwort von Atelier das zeigt; nur sehr kleine Textdateien dürfen als Base64 in `attachments`. Bei `Off` lautet Schritt 2 wie in Etappe 5. Seit D-086 bleiben die Hinweise in jeder Schalterkombination bei höchstens 3.900 Zeichen (Claude Code behält nur 4.096); die Einzelheiten von Ändern, Ergebnis-Fassungen, Zitaten und Export stehen in den Beschreibungen von `revise_order`, `get_order_result` und `export_order` (siehe [MCP-Integration](04-mcp-integration_de.md)).

Lehnt ein Werkzeug einen Aufruf ab, antwortet es mit `isError: true` und dem Text `An error occurred invoking '<tool>': <Meldung>`; die Meldung ist englischer Klartext und sagt, wie der Aufruf zu korrigieren ist.

#### `list_run_artifacts`

Listet alle Artefakte auf, die ein Run erzeugt hat.

**Eingabe:**
```json
{ "run_id": "<uuid>" }
```

**Ausgabe:** Array von Artefakt-Objekten:
```json
[
  {
    "artifact_id": "<uuid>",
    "finalizer_profile_name": "<string>",
    "artifact_type": "File|Url|Status",
    "filename": "<string|null>",
    "content_type": "<string|null>",
    "size_bytes": 12345,
    "storage_uri": "<string>",
    "status_message": "<string|null>",
    "created_at": "<ISO8601>"
  }
]
```

Gibt ein leeres Array zurück, wenn der Run keine Artefakte hat. Auth: Owner-Check (gleiche Run-Isolation wie andere Run-Tools).

---

#### `list_grounding_provider_profiles`

Listet alle Grounding-Provider-Profile (System + Custom) auf.

**Eingabe:** `{ "includeSystem": bool (default true) }`

**Ausgabe:** Array von Grounding-Provider-Profil-Objekten:
```json
[
  {
    "name": "tavily-refined",
    "displayName": "Tavily Refined",
    "description": "...",
    "providerType": "tavily",
    "maxQueriesPerRun": 3,
    "isSystem": true,
    "refinementEnabled": true,
    "refinementMode": "filter"
  },
  {
    "name": "tavily-news",
    "displayName": "Tavily News",
    "description": "...",
    "providerType": "news-search",
    "maxQueriesPerRun": 1,
    "isSystem": true,
    "refinementEnabled": true,
    "refinementMode": "filter"
  },
  {
    "name": "custom-mein-provider",
    "displayName": "Mein Provider",
    "description": "...",
    "providerType": "vector-store",
    "maxQueriesPerRun": null,
    "isSystem": false,
    "refinementEnabled": false,
    "refinementMode": null
  }
]
```

| Feld | Typ | Beschreibung |
|---|---|---|
| `name` | string | Profilbezeichner |
| `displayName` | string | Anzeigename |
| `description` | string | Zweckbeschreibung |
| `providerType` | string | `"tavily"`, `"vector-store"`, `"static-context"`, `"url-fetch"`, `"news-search"`, `"academic-search"`, `"rest-api"` oder `"learning-retrieval"` |
| `maxQueriesPerRun` | int? | Max. Anfragen pro Run (null = unbegrenzt) |
| `isSystem` | boolean | Eingebautes System-Profil |
| `refinementEnabled` | boolean | Ob ein KI-Refinement konfiguriert ist |
| `refinementMode` | string \| null | `"filter"` oder `"synthesize"` (null wenn kein Refinement) |

---

#### `download_run_artifact`

Lädt den binären Inhalt eines `File`-Artefakts herunter und gibt ihn als Base64 zurück.

**Eingabe:**
```json
{ "run_id": "<uuid>", "artifact_id": "<uuid>" }
```

**Ausgabe:**
```json
{
  "artifact_id": "<uuid>",
  "filename": "<string>",
  "content_type": "<string>",
  "size_bytes": 12345,
  "content_base64": "<base64-kodierter Dateiinhalt>"
}
```

Funktioniert nur für `ArtifactType.File`-Artefakte. Liest die Datei vom `ExportPath` auf der Festplatte und gibt die rohen Bytes als Base64 zurück. Auth: Owner-Check.

---

#### `list_learnings`

**Nur für Administratoren** (seit Etappe 1): andere Nutzer bekommen den Fehler „Only administrators can list learning entries, because they contain material from every user.“ Listet Learning-Einträge aller Nutzer aus dem Learning-Store (D-054). Seit D-067 ist die Learning-Schleife standardmäßig pausiert (`Learning:Enabled = false`); das Tool listet weiterhin die vorhandenen Einträge, solange der Schalter aus ist, kommen aber keine neuen hinzu.

**Input:**
```json
{
  "status_filter": "Proposed|Approved|Rejected",
  "domain_filter": "<string>"
}
```

Beide Parameter sind optional. Ohne `status_filter` werden alle Status zurückgegeben; ohne `domain_filter` alle Domänen.

**Output:** Array von Learning-Entry-Objekten:
```json
[
  {
    "id": "<uuid>",
    "text": "<string, auf 300 Zeichen gekürzt>",
    "domain": "<string>",
    "status": "Proposed|Approved|Rejected",
    "source_run_id": "<uuid>",
    "learning_run_id": "<uuid|null>",
    "owner_username": "<string>",
    "created_at": "<ISO8601>",
    "approved_at": "<ISO8601|null>"
  }
]
```

| Feld | Typ | Beschreibung |
|---|---|---|
| `id` | uuid | Learning-Entry-Bezeichner |
| `text` | string | Der kondensierte Lerntext, auf 300 Zeichen gekürzt |
| `domain` | string | Template-Name des Quell-Runs (für Domänen-Boost-Retrieval) |
| `status` | string | `"Proposed"`, `"Approved"` oder `"Rejected"` |
| `source_run_id` | uuid | Der Standard-Run, der die Extraktion ausgelöst hat |
| `learning_run_id` | uuid \| null | Der Learning-Evaluation-Run, der den Eintrag verarbeitet hat (null solange Proposed) |
| `owner_username` | string | Geerbt vom erstellenden Nutzer des Quell-Runs |
| `created_at` | ISO8601 | Extraktions-Zeitstempel |
| `approved_at` | ISO8601 \| null | Genehmigungs-Zeitstempel (null solange Proposed oder Rejected) |

**Hinweis:** `structured_facts_json` wird bewusst nicht exponiert — das rohe Fakten-JSON ist für MCP-Kontext zu umfangreich und nur über die Web-UI zugänglich. Auth: nur Administratoren, weil die Liste Einträge aller Nutzer enthält.

#### Lauf-Status und Ergebnis: `warnings` und `outcome`

Seit D-067 liefern `get_run_status` und `get_run_result` zwei zusätzliche Felder. Beide sind additiv;
ältere Clients können sie ignorieren. Feldnamen sind camelCase.

**Ausgabe von `get_run_status`:**
```json
{
  "runId": "<uuid>",
  "status": "Pending|Running|Completed|Failed|Aborted",
  "errorMessage": "<string|null>",
  "warnings": [ { "code": "<string>", "message": "<string>" } ],
  "outcome": "Converged|DeliveredWithOpenPoints|Failed|Aborted|null"
}
```

`get_run_result` liefert dieselben Felder und zusätzlich `finalText`.

| Feld | Typ | Beschreibung |
|---|---|---|
| `warnings` | array | Probleme, die der Lauf umgangen hat, statt anzuhalten (übersprungene Quellen, ausgefallene Reviewer, Modell-Failover, Finalizer-Probleme). Leer, wenn es keine gab. `code` ist stabil und maschinenlesbar (z. B. `grounding.provider_skipped`); `message` ist ein kurzer, einfacher englischer Satz, dessen Wortlaut sich ändern kann. |
| `outcome` | string \| null | Was der Lauf geliefert hat, sobald er fertig **und abgelegt ist, inklusive aller Finalizer**. `null`, solange der Lauf noch arbeitet, **auch wenn `status` schon terminal aussieht** (der Status wird vor den Finalizern gesetzt), und bei Läufen, die vor Step47 endeten. |

**Ergebnis lesen:** `status` behält seine Bedeutung. Ein Lauf, der ohne Konvergenz stoppt, hat weiterhin
`status = "Failed"`, liefert aber seinen besten Entwurf mit `outcome = "DeliveredWithOpenPoints"` und
einem `finalText`; dasselbe Ergebnis mit `status = "Aborted"` heißt, dass ein kritischer Reviewer-Befund
einen Lauf gestoppt hat, der schon einen Text hatte. Ein Client wartet, bis `outcome` gesetzt ist (nicht nur
auf einen terminalen `status`), und liest erst dann `finalText`; vorher kann ein Finalizer den Text noch
ändern.

`outcome = "Failed"` oder `"Aborted"` heißt nicht, dass es keinen Text gibt: Ein Lauf, der nach dem
Speichern seines Textes abgebrochen wurde, behält ihn (ein während der Finalizer gestoppter Lauf kann sogar
`status = "Completed"` mit `outcome = "Aborted"` behalten), und ein Lauf, dessen Ergebnis nicht vollständig
gespeichert werden konnte, ist `Failed` mit der Warnung `run.result_not_saved`. `finalText` im Ergebnis
prüfen; bei `Failed` ist er nicht sicher vollständig. Einzelheiten:
[02-architecture](02-architecture_de.md#lauf-ergebnis-outcome-und-runstatus).

---

## OAuth-Endpunkte

### Discovery

#### `GET /.well-known/oauth-authorization-server`

**Auth:** Keine  
**RFC:** 8414

Gibt die Server-Metadaten als JSON zurück. Clients nutzen diesen Endpunkt zur automatischen Discovery aller anderen OAuth-Endpunkte.

```json
{
  "issuer": "https://geef.stefan-bechtel.de",
  "authorization_endpoint": "https://geef.stefan-bechtel.de/oauth/authorize",
  "token_endpoint": "https://geef.stefan-bechtel.de/oauth/token",
  "registration_endpoint": "https://geef.stefan-bechtel.de/oauth/register",
  "revocation_endpoint": "https://geef.stefan-bechtel.de/oauth/revoke",
  "response_types_supported": ["code"],
  "grant_types_supported": ["authorization_code", "refresh_token"],
  "code_challenge_methods_supported": ["S256"],
  "token_endpoint_auth_methods_supported": ["none"],
  "scopes_supported": ["mcp:full"]
}
```

---

#### `GET /.well-known/oauth-protected-resource`

**Auth:** Keine  
**RFC:** Draft (MCP Resource Metadata)

Gibt Metadaten zur geschützten Ressource (dem MCP-Server) zurück.

```json
{
  "resource": "https://geef.stefan-bechtel.de/mcp",
  "authorization_servers": ["https://geef.stefan-bechtel.de"],
  "bearer_methods_supported": ["header"],
  "scopes_supported": ["mcp:full"]
}
```

---

### Client-Registrierung

#### `POST /oauth/register`

**Auth:** Keine (oder optionaler `Authorization: Bearer <REGISTRATION_TOKEN>` wenn konfiguriert)  
**RFC:** 7591 — Dynamic Client Registration

Registriert einen neuen OAuth-Client. Typischerweise vom MCP-Client automatisch aufgerufen.

**Request (JSON):**
```json
{
  "client_name": "Mein Client",
  "redirect_uris": ["https://example.com/callback"],
  "client_id": "mein-client-id",
  "logo_uri": null,
  "client_uri": null
}
```

`client_id` ist optional — wird weggelassen, generiert der Server eine UUID. `client_name` und `redirect_uris` sind Pflichtfelder.

**Response (201):**
```json
{
  "client_id": "mein-client-id",
  "client_id_issued_at": 1747390000,
  "redirect_uris": ["https://example.com/callback"],
  "client_name": "Mein Client",
  "token_endpoint_auth_method": "none",
  "grant_types": ["authorization_code", "refresh_token"],
  "response_types": ["code"]
}
```

---

### Authorization-Code-Flow

#### `GET /oauth/authorize`

**Auth:** Cookie (Geef.Atelier-Login — wird bei fehlender Session zu `/login` weitergeleitet)

Startet den Authorization-Code-Flow. Zeigt dem eingeloggten Nutzer die Consent-Seite mit Client-Name und beantragter Berechtigung.

**Pflicht-Query-Parameter:**

| Parameter | Beschreibung |
|-----------|-------------|
| `response_type` | Muss `code` sein |
| `client_id` | Registrierte Client-ID |
| `redirect_uri` | Muss exakt mit einer registrierten URI übereinstimmen |
| `code_challenge` | PKCE-Challenge (Base64Url-codierter SHA-256-Hash des Verifiers) |
| `code_challenge_method` | Muss `S256` sein (`plain` wird abgelehnt) |

**Optionale Parameter:** `scope`, `state`

Beispiel-URL wie sie Claude Desktop aufruft:
```
https://geef.stefan-bechtel.de/oauth/authorize
  ?response_type=code
  &client_id=claude-ai
  &redirect_uri=https%3A%2F%2Fclaude.ai%2Fapi%2Fmcp%2Fauth_callback
  &code_challenge=Oaa_K782ehJ6ZNf-INVXFk1mEKtzQz7xOERSXZUiGXA
  &code_challenge_method=S256
  &state=<zufälliger-state>
  &scope=mcp%3Afull
```

Die `GET /oauth/authorize`-Seite ist eine server-gerenderte Blazor-Consent-Seite
(`[Authorize]` Cookie). Die Approve/Deny-Entscheidung wird per Formular-POST an
`/oauth/consent` gesendet (siehe unten) — nicht an `/oauth/authorize` selbst.

---

#### `POST /oauth/consent`

**Auth:** Cookie (Geef.Atelier-Login) + Anti-Forgery-Token  
**Content-Type:** `application/x-www-form-urlencoded`

Submit-Ziel der Consent-Seite. Verarbeitet die Zustimmung/Ablehnung des Nutzers,
erzeugt bei Zustimmung den Authorization-Code und führt den Redirect aus.

**Nach Zustimmung:** Redirect zu `redirect_uri?code=<auth_code>&state=<state>`  
**Nach Ablehnung:** Redirect zu `redirect_uri?error=access_denied&state=<state>`  
**Bei ungültigem Request:** Fehlerseite (kein Redirect — schützt vor Open-Redirect)

---

### Token-Endpunkt

#### `POST /oauth/token`

**Auth:** Keine (Public Clients — Authentifizierung über PKCE statt Client-Secret)  
**Content-Type:** `application/x-www-form-urlencoded`

Tauscht einen Authorization-Code gegen Tokens oder erneuert über Refresh-Token.

**Grant: `authorization_code`**

```
grant_type=authorization_code
&code=<auth_code>
&client_id=<client_id>
&redirect_uri=<redirect_uri>
&code_verifier=<pkce_verifier>
```

**Grant: `refresh_token`**

```
grant_type=refresh_token
&refresh_token=<refresh_token>
&client_id=<client_id>
```

**Response (200):**
```json
{
  "access_token": "<opaque_token>",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "<opaque_token>",
  "scope": "mcp:full"
}
```

Response-Header: `Cache-Control: no-store`, `Pragma: no-cache` (RFC 6749 §5.1).

**Fehlerverhalten:**
- Ungültiger oder verbrauchter Code → `400 invalid_grant`
- Refresh-Token bereits benutzt → `400 invalid_grant` **+ sofortige Revocation aller Tokens des Nutzers** (Diebstahl-Erkennung nach RFC 6819)

---

### Revocation

#### `POST /oauth/revoke`

**Auth:** Keine  
**Content-Type:** `application/x-www-form-urlencoded`  
**RFC:** 7009

Widerruft einen Access-Token oder Refresh-Token. Gibt immer `200 OK` zurück (auch wenn das Token unbekannt ist).

```
token=<token>
&client_id=<client_id>
```

---

## Web-UI- & Account-Endpunkte

Die Web-Oberfläche ist Blazor Server (Cookie-Auth). Auswahl der relevanten,
extern erreichbaren Routen:

| Endpunkt | Methode | Auth | Zweck |
|----------|---------|------|-------|
| `/` | GET | Keine | Öffentliche Landingpage |
| `/how-it-works` | GET | Keine | Öffentliche Erklärseite; was sie als verfügbar nennt, folgt den aktuellen Schaltern und bei einer angemeldeten Person deren Ausnahmen |
| `/docs`, `/docs/{slug}` | GET | Keine | Freigegebene Doku (README, 01–04, 06–09, Leitfäden 12–15) |
| `/health` | GET | Keine | Health-Check (`Healthy`) — für Reverse-Proxy/Container-Lifecycle; umfasst die Datenbank und den Check `pending-migrations` (D-067) |
| `/login` | GET/POST | Keine | Login-Seite (Static SSR) |
| `/auth/logout` | POST | Cookie | Logout (Anti-Forgery, Minimal-API) |
| `/settings/theme` | POST | Keine | Theme-Wechsel-Fallback (No-JS), Redirect zum Referer |
| `/hubs/runs` | WS | Cookie | SignalR-Hub für Live-Run-Updates, nur für Angemeldete; Gruppennamen leitet der Server ab, und die Verbindung schließt bei Ablauf der Anmeldung (D-067). Die eigenen Seiten der Anwendung nutzen stattdessen den prozessinternen `IRunUpdateFeed`; ein externer Client ist nicht bekannt, der Hub entfällt mit Rückbau Schnitt 2, sobald sein Auslöser in D-077 erfüllt ist (D-068). |
| `/access-denied` | GET | Cookie | Erscheint für Angemeldete, die eine Seite ohne Berechtigung öffnen (D-067) |
| `/admin/users` | GET | Cookie (Admin) | Benutzerverwaltung |
| `/admin/oauth-clients` | GET | Cookie (Admin) | OAuth-Client-Verwaltung |
| `/account/connected-clients` | GET | Cookie | Selbstverwaltung der eigenen verbundenen OAuth-Clients |
| `/crew`, `/crew/templates`, `/crew/profiles/*`, `/crew/studio`, `/crew/knowledge` | GET | Cookie | Crew-/Template-/Profil-/Studio-/Wissensbasis-Verwaltung |
| `/start` | GET | Cookie | Startseite für einen neuen Auftrag: Anfrage, Dateien mit Quittung, „Extra careful“ (D-070) |
| `/u/{uploadId}` | GET | Cookie | Upload-Seite des Upload-Tickets für genau eine Datei, nur für die Besitzerin oder den Besitzer und nur bei `Uploads:Tickets` `On` (D-076) |
| `/work`, `/orders` | GET | Cookie | „My work“: die Aufträge der Person, neueste Aktivität zuerst (Admins können alle Nutzer zeigen) |
| `/orders/{id}` | GET | Cookie | Auftragsseite: Fortschritt, Text, offene Punkte, „How it was made“, Versionen |
| `/runs`, `/runs/{id}`, `/new` | GET | Cookie | Run-Liste, Run-Detail, neuer Auftrag |
| `/crew/providers/*`, `/tools/*`, `/mcp-servers/*`, `/crew/profiles/grounding-providers/create`, `/crew/profiles/grounding-providers/edit/{name}` | GET | Cookie (Admin) | Provider-, Tool-, MCP-Server- und Grounding-Editoren — seit D-067 nur für Admins, weil sie Geheimnisse beim Namen nennen |

Run-bezogene Seiten unterliegen der Run-User-Isolation (D-042): jeder Nutzer sieht
nur eigene Runs; der Admin kann per explizitem Umschalter systemweit sehen.

---

## Endpunkte für Quellen und Auftragsexport (D-070)

Alle drei verlangen die Cookie-Anmeldung; API-Aufrufe ohne sie bekommen `401`/`403` statt einer Weiterleitung zur
Anmeldeseite. Schreibende Aufrufe brauchen das Antiforgery-Token im Header `RequestVerificationToken` und, wenn der
Browser einen schickt, einen `Origin` gleich dem Host (sonst `400`).

| Endpunkt | Methode | Antwort |
|---|---|---|
| `/api/sources` | POST (multipart, genau eine Datei) | `201` mit der Quittung und `Location: /api/sources/{id}`; `200` mit `duplicate: true`, wenn dieselbe Person dieselbe Datei schon hochgeladen hat; `422` mit `usable: false`, `sourceId: null` und `reason` bei einer Datei ohne lesbaren Text, mit weit mehr Text als eine Quelle haben darf, über dem Kontingent oder wenn der Dienst ausgelastet ist; `413` mit `{ "reason": "…" }` bei einer Datei über der Größengrenze; `429` mit der Quittung und `Retry-After: 5`, wenn die Person schon zwei Uploads laufen hat; `408` mit `{ "reason": "…" }`, wenn der Upload länger als `Sources:UploadTimeout` dauert; `400` mit `{ "reason": "…" }` bei fehlendem Token, kaputtem oder abgebrochenem Body oder nicht genau einer Datei. Der Body wird als Strom gelesen; das Token muss im Header kommen |
| `/api/sources/{id}` | GET | Die Quittung einer eigenen Quelle, sonst `404` |
| `/api/sources/{id}` | DELETE | `204`; `409` mit Begründung, solange ein Auftrag die Datei nutzt; `404` für unbekannte oder fremde Quellen |
| `/orders/{id}/export/{format}` | GET | Der Text des Auftrags als `docx`, `pdf` oder `md` (optional `?version=n`); `404` für unbekannte oder fremde Aufträge oder einen Auftrag ohne Text, `400` für ein unbekanntes Format |

Die Quittung ist JSON in camelCase mit Enum-Werten als camelCase-Strings, zum Beispiel:

```json
{
  "sourceId": "<uuid|null>", "fileName": "offer.pdf", "kind": "pdf", "sizeBytes": 2202009,
  "pageCount": 80, "charCount": 160485, "wordCount": 24100, "quality": "good|partial|poor|unreadable",
  "usable": true, "duplicate": false, "budgetClass": "fullText|withMap|tooLarge",
  "preview": "<Anfang des Textes>", "warnings": [ { "code": "pdf.pages_without_text", "message": "…", "pages": [3, 7] } ],
  "reason": null, "estimatedTokens": 84466
}
```

Aufträge selbst startet die Startseite über `IOrderService` im Blazor-Circuit; einen eigenen HTTP-Endpunkt zum
Anlegen eines Auftrags gibt es nicht.

---

## Endpunkte des Upload-Tickets (D-076)

Nur solange `Uploads:Tickets` `On` ist (Standard im Code `Off`; `appsettings.json` liefert seit der Abnahme in D-076 `On`). Bei `Off` sind die Endpunkte nicht gemappt und die Seite
`/u/{id}` wird vor der Anmeldung aus der Anfrage genommen: Die Adressen antworten wie Adressen, die es nicht gibt.
Antworten sind JSON im Format der Quittung oben; ohne Quittung `{ "reason": "…" }`.

| Endpunkt | Methode | Auth | Antwort |
|---|---|---|---|
| `/api/uploads/{uploadId}` | PUT (roher Rumpf, Content-Type egal) | nur Kopf `X-Upload-Token` (Cookies werden ignoriert, kein CORS) | `201` mit der Quittung plus `uploadId`, `sha256` (SHA-256 der empfangenen Bytes) und `next` (nächster Schritt für den Assistenten); die Quittung lässt `preview` und `fileName` weg, weil ein KI-Assistent sie liest; `404` „This upload link is not valid any more. Ask for a new one with prepare_attachment.“ für unbekannt, falschen oder fehlenden Token, abgelaufen, benutzt und fremd gleichermaßen; `409`, solange ein Upload desselben Tickets läuft; `422` mit Quittung bei unbrauchbarer Datei, anderer Größe als angekündigt (`The file is n bytes, not the m bytes announced. Nothing was stored.`) oder einer Datei, die zu lang ist, um ganz verwendet zu werden (Code `too_long_for_ticket`: kürzere Datei, oder den Auftrag in der Web-App starten und dort Seiten wählen; „Nothing was stored.“ steht am Ende nur, wenn die Kopie wieder gelöscht wurde, sonst „The file could not be removed right away, so it may still be stored for a while. It is not added to any order this way.“); `413`, `429`, `503`, `408` wie `/api/sources`. Jede Antwort mit Quittung hat `next`: nach einer geprüften Ablehnung „The file was refused. Tell the person the reason word for word. Do not upload the same file again; it would be refused again.“, nach `429`/`503`/Abbruch/Speicherfehler (Versuch nicht verbraucht) „This was not a problem with the file. Wait a moment, then run curlCommand again.“; nach vollem Kontingent (`quota`) oder nicht verfügbarer Extraktion (`unavailable`), ebenfalls ohne Versuch, ein Satz, dass sofortiges Wiederholen nicht hilft, und was hilft (Kontingent: erst den Auftrag mit den angekommenen Dateien senden oder später; nicht verfügbar: später), mit neuem `prepare_attachment`, falls der Link bis dahin abgelaufen ist. Der Token gehört nie in Adresse oder Query; dort wird er nicht gelesen |
| `/api/uploads/{uploadId}/page` | POST (multipart, genau eine Datei) | Cookie, Antiforgery-Kopf, `Origin` wie `/api/sources` | `201` mit Quittung plus `uploadId` und `sha256`; `404` „Not found.“ für unbekannte und fremde Tickets (auch für Administratoren); `409` schon angekommen oder läuft; `410` abgelaufen, oder alle Versuche durch abgelehnte Dateien verbraucht (eigener Satz); `422` unbrauchbare Datei (Ticket bleibt offen bis 3 geprüfte Versuche); `400`/`403`/`413` wie `/api/sources` |
| `/u/{uploadId}` | GET | Cookie | Upload-Seite für Laien: offen (Ablagefeld für eine Datei), fertig (Quittung und „Done. The file has arrived. Go back to your AI assistant and tell it the file is uploaded.“), schon angekommen, abgelaufen, durch abgelehnte Dateien verbraucht („This link cannot be used any more because the files sent with it could not be used. …“); fremde und unbekannte Tickets zeigen dieselbe „Not found“-Seite |

## Artefakt-Download-Endpunkt

### `GET /runs/{runId:guid}/artifacts/{artifactId:guid}/download`

**Auth:** `.RequireAuthorization()` — erfordert eine aktive Session (Cookie oder Bearer-Token)

Lädt eine Artefakt-Datei herunter, die ein Finalizer während eines Runs erzeugt hat. Nur Artefakte vom Typ `File` können heruntergeladen werden; `Url`- und `Status`-Artefakte liefern 404.

**Owner-Check:** Nicht-Admin-Nutzer dürfen nur Artefakte aus eigenen Runs herunterladen. Die Prüfung erfolgt über `IRunService.GetRunAsync` mit dem anfragenden Benutzernamen. Admin-Nutzer umgehen den Owner-Check.

**Sicherheitshinweis:** Ein Path-Containment-Guard (`Path.GetFullPath`-Vergleich) verhindert Directory-Traversal-Angriffe auf den serverseitigen Dateipfad.

**Erfolgsantwort:** `200 OK` — Datei-Stream mit `Content-Disposition: attachment` (Dateiname aus dem Artefakt-Datensatz).

**Fehlerantworten:**

| Status | Bedingung |
|--------|-----------|
| `401 Unauthorized` | Nicht authentifiziert |
| `403 Forbidden` | Authentifiziert, aber nicht der Run-Eigentümer (und kein Admin) |
| `404 Not Found` | `runId` oder `artifactId` nicht gefunden |
| `404 Not Found` | Artefakt vorhanden, aber vom Typ `Url` oder `Status` (nicht `File`) |
| `404 Not Found` | Datei nicht auf der Festplatte gefunden |

---

## Token-Design

| Eigenschaft | Wert |
|-------------|------|
| Format | Opaque — 32-Byte Zufallsdaten, Base64Url-codiert |
| Speicherung | Nur SHA-256-Hash in der Datenbank |
| Generierung | `RandomNumberGenerator.GetBytes(32)` |
| Vergleich | `CryptographicOperations.FixedTimeEquals` |
| Access-Token-Lebensdauer | 1 Stunde |
| Refresh-Token-Lebensdauer | 30 Tage, Rotation bei jedem Refresh |
| Scope | Nur `mcp:full` (Vollzugriff auf den MCP-Server) |

---

## Vollständiger Flow (Zusammenfassung)

```
Client                          Geef.Atelier                    Browser/Nutzer
  │                                 │                                 │
  │── GET /.well-known/oauth-... ──>│                                 │
  │<── Server-Metadaten ────────────│                                 │
  │                                 │                                 │
  │── POST /oauth/register ────────>│                                 │
  │<── client_id ───────────────────│                                 │
  │                                 │                                 │
  │── Öffne Browser mit GET ────────────────────────────────────────>│
  │   /oauth/authorize?...          │                                 │
  │                                 │<── Login (falls nötig) ─────────│
  │                                 │<── Consent "Zugriff gewähren" ──│
  │                                 │── Redirect ?code=... ──────────>│
  │<── code (via redirect_uri) ─────────────────────────────────────│
  │                                 │                                 │
  │── POST /oauth/token ───────────>│                                 │
  │   (code + code_verifier)        │                                 │
  │<── access_token + refresh_token─│                                 │
  │                                 │                                 │
  │── POST /mcp ───────────────────>│                                 │
  │   Authorization: Bearer <token> │                                 │
  │<── Tool-Response ───────────────│                                 │
```
