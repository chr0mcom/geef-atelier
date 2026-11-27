# Tool-System

***Deutsch** · [English](10-tool-system.md)*

*Letzte Aktualisierung: 2026-11-27 (D-091: Stufe 8d, das Tool-System wurde entfernt; die Seite ist nur noch ein kurzer Hinweis; zuvor 2026-11-24, D-067: Ist-Zustand nach Etappe 0; 2026-06-10, D-060)*

---

Das Tool-System aus D-060 (zentraler Werkzeug-Katalog, Seiten unter `/tools`, Werkzeug-Definitionen, -Aufrufe und
-Bindungen an Profilen, MCP-Server-Konfigurationen für Werkzeuge, die agentische Werkzeug-Schleife und Grounding über
Werkzeuge) wurde **in Stufe 8d entfernt (D-091)**. Sein MCP-Client war schon in Stufe 8c entfallen (D-089). Der agentische
Pfad war nie in die Pipeline eingebaut (D-067).

Eine Werkzeugnutzung durch die Akteure wird durch nichts ersetzt. Der einzige Zugang zum Web ist die Recherche im
Grounding-Schritt des Direktwegs ([08-crew-system_de.md](08-crew-system_de.md#recherche-im-grounding-schritt-d-074)). Die
Datenbanktabellen und die alten Läufe wurden beim Aufräumen der Datenbank entfernt (D-092).

Die frühere Beschreibung steht in der Git-Historie dieser Datei.

## Secrets und die Allowlist für Umgebungsvariablen

Diese Regel kam mit D-067 und gilt weiter, jetzt für die Provider-Einstellungen: Wo ein Provider eine Umgebungsvariable
nennt (Variable für den API-Schlüssel, Variable zum Überschreiben des Endpunkts), werden nur Namen von der Allowlist
gespeichert oder gelesen (`IEnvReferencePolicy`). Ein nicht erlaubter Name wird beim Speichern abgelehnt, und zur Laufzeit
wird sein Wert nicht gelesen.

Die Liste besteht aus dem eingebauten Standard (`EnvReferenceOptions.DefaultAllowed`: die üblichen Namen der
Provider-Schlüssel, `OLLAMA_ENDPOINT`, `TAVILY_API_KEY` und das Präfix `ATELIER_SECRET_*`) und den Einträgen aus
`EnvReference:Allowed` (genaue Namen oder Präfixe mit `*` am Ende; der Vergleich beachtet Groß- und Kleinschreibung).
Geheimnisse der Infrastruktur wie das Datenbank-Passwort stehen absichtlich nicht darauf.
