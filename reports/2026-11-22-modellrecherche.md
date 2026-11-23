# Modellrecherche für Geef Atelier (Stand 2026-11-22)

Methodik und Lesehilfe: Recherche per Websuche und Seitenabruf am 2026-11-22. Kennzeichnung: **[belegt]** = Herstellerdokumentation oder unabhängige Quelle direkt gelesen; **[Hersteller]** = Eigenangabe, nicht unabhängig bestätigt; **[schwach]** = Aggregator, Blog, Einzeltest oder Forenbeitrag; **[alt]** = Quelle älter als 3 Monate (vor 2026-08-22) oder Modelle, die inzwischen überholt sind; **[Schluss]** = meine Schlussfolgerung, kein Quellenbefund. Direktabrufe von arena.ai und eqbench.com scheiterten (HTTP 403 bzw. leere Tabelle); Arena-Werte stammen daher aus Aggregatoren und sind als **[schwach]** zu lesen. Wichtigster Gesamtbefund: Es gibt **keinen belastbaren unabhängigen Benchmark für deutsche Textqualität mit den aktuellen 5.x/6.x/4-Modellen**; fast alle deutschsprachigen Messungen betreffen Modelle bis Opus 4.8 / GPT-5.5 / Gemini 3.1.

## 1. Stand der Frontier (November 2026)

### 1.1 Verifikation der genannten Modell-IDs

| ID | Status | Quelle |
|---|---|---|
| `claude-fable-5-2` | existiert, veröffentlicht 2026-11-09, 8 $/40 $ je MTok, 1M Kontext, 128K Output [belegt] | https://platform.claude.com/docs/en/models/fable-5-2/overview (2026-11) |
| `claude-opus-5-5` | existiert, 2026-09-22, 4 $/20 $ [belegt] | https://platform.claude.com/docs/en/models/opus-5-5/overview |
| `claude-sonnet-5-5` | existiert, Release 2026-09-28 laut Retirement-Tabelle, 2 $/10 $ [belegt] | https://platform.claude.com/docs/en/models/overview |
| `claude-haiku-5-5` | existiert, 2026-10-07, ab 0,10 $/0,50 $ [belegt] | ebd. |
| `claude-opus-latest`, `claude-fable-latest` | **nicht verifizierbar**: Die Anthropic-Doku nennt für 5.x nur gepinnte, datumslose IDs; "Alias"-Zeile wiederholt dieselben IDs. Vermutlich proxy-interne Aliase | ebd. |
| `gpt-6.2-astra`, `gpt-6.2-sol`, `gpt-6.1-terra`, `gpt-6-luna`, `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-5.6-sol/terra/luna` | existieren im lokalen Codex-Katalog (`~/.codex/models_cache.json`, abgerufen 2026-11-22). Beschreibungen dort: Astra "most capable for complex coding and professional work", Sol "fast, go-to model", Terra "efficient, visual understanding and computer use", Luna "fast and affordable model for easier tasks" | lokal; API-IDs `gpt-6.2-astra`/`gpt-6.2-sol` bestätigt in https://community.openai.com/t/gpt-6-2-astra-and-sol-are-now-available/1417531 (2026-11-17) |

Lokaler Befund: Codex-Kontextfenster laut Katalog: Astra 272k, Sol 500k, Terra 500k, Luna 272k. Vercel nennt für die API 1,05 Mio. Token (https://vercel.com/i/which-openai-model-should-you-build-with, 2026-11-21) — Widerspruch zwischen CLI-Katalog und API-Angabe; für Langkontext-Arbeit über die Codex-CLI gilt der lokale Wert.

### 1.2 Anthropic

- **Fable 5.2** (2026-11-09): "most capable generally available model for technical fields" (Coding, Wissenschaft, Finanzen); Anthropic empfiehlt Opus 5.5 für den Alltag. Fable ist **nicht** auf Schreiben oder Kreativität getrimmt: Auf der Produktseite findet sich keine Aussage zu Schreibqualität (https://www.anthropic.com/claude/fable, 2026-11-09). Mythos 5.2 teilt dieselbe Basis, ist aber nur für verifizierte Organisationen verfügbar (https://platform.claude.com/docs/en/models/overview). Fable 5.2 ist ein "Fable"-Linienmodell, kein Kreativmodell. [belegt]
- Einschränkungen Fable 5.2: erzwungene Tool-Nutzung (`tool_choice` any/tool) liefert 400; `temperature`/`top_p`/`top_k` auslassen; 30 Tage Datenspeicherung verpflichtend, kein Zero-Data-Retention ohne Sondergenehmigung; Cyber-/Bio-Anfragen werden ggf. auf schwächere Modelle umgeleitet (https://platform.claude.com/docs/en/models/fable-5-2/overview). Relevanz: Bei sensiblen Rechts-/Versicherungstexten die Retention-Regel prüfen (Geltung bei CLI-Abo unklar, [Schluss]).
- Anthropics eigene Systemkarte (laut Sekundärquelle) bewertet das Fable-5.2-Basismodell bei "Wärme" niedriger als Opus 5.5 und bei "creative mastery" niedriger als das letzte Mythos; Fable folge Stilregeln sehr strikt (harte Satzlängenlimits machen den Text abgehackt). [schwach, aus Sekundärquelle] https://moelueker.com/blog/claude-fable-5-2-vs-opus-5-5 (2026-11). Zwei Beobachter bemerkten weiterhin "Claude-Speak" in Fables Prosa (ebd.).
- **Opus 5.5** (2026-09-22): "long-running agentic coding and knowledge work"; Anthropic verspricht klarere Texte, weniger Jargon, bessere Befolgung von Schreibvorgaben, weniger falsche Zahlen/Quellen ("much less likely to state an incorrect figure or cite the wrong source") [Hersteller] (https://the-decoder.com/claude-opus-5-5-matches-fable-5-1-at-40-percent-lower-cost-as-anthropic-promises-to-fix-claudish-writing/, 2026-09-22; https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/prompting-claude-opus-5-5). Unabhängig: Every-Lesbarkeitstest (Flesch 68,4 vs. 61,35 bei Opus 5), aber als Editor hinter Opus 5, GPT-5.6 Sol und GPT-6 Astra; Methodik dünn [schwach] (https://www.orcarouter.ai/de/blog/claude-opus-5-5-creative-writing, 2026-09-23; Betreiber verkauft Modellzugang).
- **Sonnet 5.5** (2026-09-28): "best combination of speed and intelligence", 2 $/10 $; **Haiku 5.5** (2026-10-07): "high-volume, latency-sensitive tasks such as classification, extraction, and routing" [belegt].
- Fable vs. Opus laut Praxisvergleichen: Opus 5.5 für wohlspezifizierte Aufgaben, Fable bei offenen/schweren Problemen; bei Schreiben/Analyse/Synthese sei Opus "nicht nur billiger, sondern eventuell leicht besser" (AA-Briefcase Elo 1800 vs. 1785, Rubrik-Variante dreht um: 61,2 % vs. 66,1 % für Fable) [schwach] https://dev.to/max_quimby/fable-52-vs-opus-55-when-to-use-which-4nn5 (2026-11-12). Ein Medium-Einzeltest sah Fable in 4 von 5 Blindschreibtests vorn [schwach] (https://medium.com/the-ai-studio/i-tested-fable-5-2-vs-opus-5-5-cb1e5b321816).

### 1.3 OpenAI

- Staffel laut Preis/Positionierung (Vercel, 2026-11-21): **Astra** 8 $/40 $ ("anspruchsvollste Projekte, Analyse, Untersuchungen mit konkurrierenden Erklärungen"); **Sol** 2 $/10 $ ("Startpunkt für die meisten API-Workflows"; für Texte: "drafting copy that reconciles several sources"); **Terra** 1 $/5 $ (visuelles Verständnis, Computerbedienung, "revising prose"); **Luna** 0,10 $/0,50 $ (Klassifikation, Feldextraktion, enge Edits). Die Seite bewertet **keine** Schreibqualität, sie beschreibt nur Eignung. [Hersteller/Beschreibung]
- Release GPT-6.2 Astra/Sol: 2026-11-17; Astra "für die härtesten Projekte", Sol "effizient, vielseitig, für Alltag und mehrstufige Agenten" (OpenAI-Forum, 2026-11-17). Dort wird der Release ausdrücklich nicht als Kreativ-Release beschrieben; ein Moderator empfiehlt für Prosa/Fiktion Terra [schwach, ein Forenbeitrag]. DataCamp (2026-11-17): kein Schreib- oder Halluzinationstest; Sol sei Standard, Astra nur bei Bedarf [Hersteller-Zahlen].
- Älter und für Prosa womöglich relevant: Surge/Hemingway-Bench lobte die GPT-5.2-API für professionelle E-Mails und Marketingtexte, schwach bei Kreativem [alt] (https://surgehq.ai/blog/hemingway-bench-ai-writing-leaderboard; Datum der Seite unklar: 2026-02-04 oder 2026-10-21).

### 1.4 Google

- Gemini 4 ist am Markt: **Argon** (öffentlich seit 2026-10-13), **Carbon** (Topmodell für lange, schwere Vorhaben; API-Verfügbarkeit Ende Oktober noch nicht öffentlich), **Hydrogen** (2026-11-12, schnell, ersetzt 3.8 Flash, ca. 1,50 $/7,50 $), **Neon** (2026-11-18, "Carbon-Qualität bei Flash-Tempo", `gemini-4-neon`, 3 $/15 $, 1M Kontext) [Hersteller+Sekundär]. Quellen: https://blog.google/innovation-and-ai/models-and-research/gemini-models/introducing-gemini-4-neon/ (2026-11-18); https://9to5google.com/2026/11/18/gemini-4-neon-announcement/; https://9to5google.com/2026/11/12/gemini-4-hydrogen/. IDs für Carbon/Hydrogen nicht bestätigt; `gemini-4-argon` laut Preisseite. Argon/Carbon aktuell Aktionspreis 2 $/10 $ bis 2026-11-30, danach 4 $/20 $ [schwach/Sekundär].
- Google nennt für Neon "writes clearly", direkter Ton [Hersteller]. "Gemini 3.5 Pro" erschien laut Quellen von Juli nie [alt]; irrelevant durch Gemini 4.

### 1.5 xAI, DeepSeek, andere

- **xAI/SpaceXAI**: Grok 4.5 (2026-07-08), 4.6 (2026-08-12, 2 $/6 $), **4.7 (2026-09-21)** neuestes gefundenes Modell. Keine Schreibbenchmarks; AA bescheinigt 4.7 Zugewinne nur bei analytischer Qualität, "presentation quality" leicht schwächer (https://artificialanalysis.ai/articles/benchmarking-grok-4-7, 2026-09-21). Vectara-Halluzination neuestes Grok nur Grok 4.1 Fast mit 19,2 % [schwach] (Suprmind-Hub, 2026-11-07). Fazit: als Zusatzfamilie nur nachrangig.
- **DeepSeek**: V4-Pro (April-Vorschau, Update 0813), **V4.1-Flash (2026-09-10)**; deepseek-v4-pro-Anfragen werden seit 2026-09-14 auf V4.1-Flash umgeleitet (https://deepseek.com/en/news/deepseek-v4-1-flash/). V4.1-Pro noch ausstehend (Stand Quelle September). Auf AA-Omniscience V4 Pro 0813 mit 94,1 % Halluzinationsrate, V4.1 Flash 64,1 % [schwach] (Suprmind, 2026-11-07, Primärquelle AA 2026-10-31).
- **Moonshot Kimi**: K3 (2026-07-16), **K3.1 Pro** (2026-10-23, 2 $/10 $, 1M Kontext; offene Gewichte "bis 4.11." angekündigt, nicht verifiziert) [schwach]. **Z.ai GLM-5.3** (August, ~1,40 $/4,40 $ auf OpenRouter). **Alibaba Qwen4-Max** (2026-11-16), Qwen3.8-Max (2026-08-03). **Mistral Large 4** (Public Preview 2026-10-06, Preise/Kontext widersprüchlich) (https://docs.mistral.ai/resources/changelogs). Weitere Namen aus AA/Vals ("Muse …", "Wonder Full", "Inkling 2 Small") konnte ich nicht herstellerseitig zuordnen.

### 1.6 Gesamtindizes

- Artificial Analysis Intelligence Index (Seite ohne Datum, enthält Neon vom 2026-11-18, also frühestens dort): Fable 5.2 Max 65; Astra Max 62; Gemini 4 Neon xhigh 61; Carbon high 60; Opus 5.5 Max 59; Sol Max 57; Sonnet 5.5 Max 55; Hydrogen xhigh 54; Terra Max 51 (https://artificialanalysis.ai/leaderboards/models). Achtung: Indexversionen wechseln (Opus 5.5 am 2026-09-22 noch 58, Grok 4.6 im August 61, Grok 4.7 im September 46) — Zahlen verschiedener Zeitpunkte nicht mischen.
- Vals Index (2026-11-19): Fable 5.2 71,31 %; Gemini 4 Neon 70,87 %; **Sonnet 5.5 70,85 %** (https://www.vals.ai/benchmarks). Sonnet 5.5 liegt damit praktisch gleichauf mit dem Spitzenfeld, bei einem Viertel des Fable-Preises.
- LMArena Text (Snapshot 2026-11-18 laut Suchtreffer, 9,19 Mio. Stimmen): Gemini 4 Carbon 1526, Argon 1521, Neon 1518, Opus 5.5 1507 (Platz 5), Fable 5 1504 [schwach, nicht direkt abrufbar]. Fable 5.2 war in meinem Treffer nicht gelistet.

## 2. Empfehlungen nach Aufgabentyp

**Kreatives Schreiben / Fiktion.** Es gibt keinen unabhängigen, aktuellen, verlässlichen Kreativ-Benchmark.
- Arena Creative Writing (aus Aggregatoren): BenchLM-Beitrag, Stand 2026-11-20: Fable 5.2 1529 ±19 und Gemini 4 Argon 1520 ±9, statistisch gleichauf (https://benchlm.ai/best/for/fiction, 2026-11-21); BenchLeader 2026-11-04: Opus 5.5 1520, Argon 1517, Carbon 1515; pirouter 2026-11-09: Argon 1525 auf Platz 1. Fazit: Spitzenfeld = Fable 5.2, Opus 5.5, Gemini 4 Argon/Carbon/Neon; Reihenfolge nicht belastbar. [schwach]
- BenchLM-Rangliste "Fiction" ist ausdrücklich ein **Instruction-Following-Proxy**, kein Kreativmaß (Fable 5.2 87,7; Neon 86,9; Astra 83,3). Nicht als Qualitätsbeleg verwenden.
- EQ-Bench Creative Writing v3 (Richter sind Claude-Modelle, Judge-Wechsel 2026-03-01; Stand unklar): Spitzenfeld laut digitalapplied (2026-08-31, [alt]): Claude Opus 5, Kimi K3, GLM-5.3, GPT-5.6 Sol; arena.ai im selben Beitrag fast gegenläufig (https://www.digitalapplied.com/blog/which-ai-model-writes-best-leaderboards-disagree). Lehre: Boards widersprechen sich, Claude-Richter bevorzugen Claude-Stil [Schluss].
- Surge Hemingway-Bench (menschliche Profis): Gemini 3 Flash, Gemini 3 Pro, Opus 4.5 vorn; Opus "am natürlichsten, menschlichsten"; Gemini mit "literarischem Flair" [alt, Modelle überholt].
- Kimi K3 mit "Writing Elo" 2840 vs. Fable 5 2760 laut Decrypt (2026-07-16) [alt, schwach, Einzelquelle].
- Empfehlung [Schluss]: Executor für Fiktion/Rede aus {Opus 5.5, Gemini 4 Neon/Argon}, Fable 5.2 nur testweise; Entscheidend ist ein eigener A/B-Test.

**Deutsche Prosaqualität.**
- AA-Mehrsprachigkeit Deutsch (ohne Datum, nur Top 5, alte Modelle): Gemini 3.1 Pro 95, Opus 4.6 93, Gemini 3 Pro 93 (https://artificialanalysis.ai/models/multilingual/german) [alt].
- i6eal "German AI Benchmark" (Lauf 2026-06-22, 24 Aufgaben, Rauschen mehrere Punkte; Anbieter ist ein deutsches KI-Dienstleister; Richterpanel aus GPT/Gemini/Claude, name-blind): Opus 4.8 98, GPT-5.5 95, Qwen3.7 Max 93; Sprachqualität Claude 100; Recht/Steuern GPT-5.5 100; Geschäftskommunikation Opus 4.8 97, Amtsdeutsch 95 (https://i6eal.de/en/ki-benchmark-deutsch/) [alt, aber das beste deutschsprachige Geschäftsdeutsch-Signal].
- MÖVE (Bundesdruckerei, arXiv 2606.13111, 2026-06-11): 39 Modelle, "kein Modell dominiert"; Verwaltungsdeutsch; Rangliste nicht abgerufen [alt].
- Für Opus/Fable/Astra/Gemini 4: **keine belastbare deutsche Messung gefunden**. Opus 5.5 schreibt laut Arena-Auswertung kürzere Sätze und fast ohne Gedankenstriche (Sekundärquelle) [schwach]. Empfehlung [Schluss]: Claude (Opus 5.5) und Gemini 4 als Deutsch-Executor, GPT-6.2 Sol als Gegenprobe; deutschen A/B-Test mit eigenen Briefen.

**Rechtstexte (deutsches Recht).**
- BenGER (arXiv 2605.28183, EMNLP 2026, v4 2026-08-31): 12 Systeme, Spitze laut Zusammenfassung Gemini 3.1 Pro und Opus 4.7 mit ca. 83 Punkten; DeepSeek-V4-Pro 73,5 als bestes offenes Modell; LLM-Richter korreliert mit Menschen (r=0,76) [alt, 2.x-Generation].
- LEXam (ICLR 2026): alle Modelle verlieren bei deutschen Kursen und Schweizer Recht Genauigkeit (https://arxiv.org/abs/2505.12864) [alt].
- Vals (US-Recht!): LegalBench Stand 2026-10-13: Fable 5 88,56 %, Fable 5.1 88,51 %, Gemini 4 Argon 88,30 % (gesättigt, Fable 5.2/Opus 5.5/Astra nicht gelistet). **Vals Legal Index 2026-11-19**: Gemini 4 Neon 78,36 %, Wonder Full 77,86 %, Argon 76,78 %, Carbon 75,93 %, GLM 5.3 74,62 %, Qwen 3.8 Max 73,90 %, Hydrogen 73,63 % (https://www.vals.ai/benchmarks/legal_index). Claude-Modelle fehlen in den Top 10; Immigration-Bench: Fable 5.1/5.2, Opus 5.5 vorn.
- Empfehlung [Schluss]: Rechtsentwurf: Opus 5.5 oder Fable 5.2 als Executor (Sorgfalt, Quellentreue), Reviewer aus Gemini 4 Neon und GPT-6.2 Astra. Kein Benchmark deckt deutsche Schriftsatz-Qualität mit den aktuellen Modellen ab; Zitate (Normen, BGH-Aktenzeichen) immer gegen Quelle prüfen.

**Wissenschaftliches Schreiben / Zitiergenauigkeit.**
- GhostCite (arXiv 2602.06718, 2026): 13 Modelle, 375k Zitate, Fehlerrate je Modell ca. 14 % bis 79 %; Claude 4 ca. 22 %, GPT-5 ca. 51 % [alt, Vorgängermodelle]. Kernbefund aller Studien: Zitate nur mit Retrieval/Datenbank-Grundierung verlässlich (https://arxiv.org/html/2602.06718v2).
- Aktuelle Modelle: keine direkte Zitationsmessung gefunden. Proxy-Zahlen siehe Faktentreue. Anthropic nennt für Opus 5.5 interne Faktencheck-Tests (16/18 bestanden vs. 0/18) [Hersteller].
- Empfehlung [Schluss]: Executor Opus 5.5 plus Grounding-Rolle mit Websuche; Reviewer Gemini 4 Neon/Argon (niedrigste Halluzinationsrate) mit Auftrag "jede Quelle verifizieren".

**Marketing/Copywriting.** Kein Konversions-Benchmark. BenchLM-Beitrag (Arena Instruction Following 2026-11-06): Argon 1525, Opus 5.5 1522, GPT-6.1 Sol 1486; Empfehlung dort: Sol oder Argon für Stapel, Opus 5.5 für Markenstimme-Review (https://benchlm.ai/blog/posts/best-llm-writing) [schwach]. Surge (alt): GPT-API stark bei Marketing/Business-Mails. Empfehlung [Schluss]: Sol oder Gemini 4 Neon als Executor (Varianz zu Claude), Opus 5.5 als Stilreviewer.

**Rhetorik/Reden.** Nicht separat gemessen. Hemingway-Bench: Opus 4.5 "am natürlichsten bei herzlichen Reden" [alt]. Empfehlung [Schluss]: Opus 5.5 Executor, Gemini 4 Neon als Alternativ-Fassung; Fable nur bei argumentativ schwierigen Reden. Dünne Quellenlage.

**Geschäftskorrespondenz.** i6eal: Opus 4.8 97 (Geschäftskommunikation), 95 (Amtsdeutsch) [alt]. Bei hohem Volumen: Sonnet 5.5 für Routine-Edits, Gemini 3.8-Flash-Nachfolger Hydrogen bei bezahlter Variante (BenchLM nennt Gemini 3.8 Flash Arena Creative 1487 ±7, auf Niveau Fable 5.1; Stand 2026-11-06) [schwach].

**Faktentreue / Halluzinationsresistenz.** Zahlen stammen aus Suprmind-Hub (2026-11-07), der AA-Omniscience v1.1 (2026-10-31), Vectara (Snapshot 2026-10-22), SimpleQA Verified (2026-10-20), FACTS (2026-11-05) wiedergibt; nicht gegen Primärquellen geprüft, Quelle teils widersprüchlich (https://suprmind.ai/hub/ai-hallucination-rates-and-benchmarks/, https://metarix.io/aa-omniscience-hallucination-benchmark):
- AA-Omniscience (Halluzinationsrate bei Nichtwissen, niedriger besser): Gemini 4 Argon 4,3 %, Gemini 4 Carbon 11,4 % (Index 53,5, bester Wert), Haiku 5.5 34,2 %, Terra 34,5 %, Sonnet 5.5 43,3 %, Astra 42-50 %, Opus 5.5 50,3 %, Fable 5.1 70,0 %, Luna 79,3 %, DeepSeek V4 Pro 94,1 %. Fable 5.2 nicht gelistet. Muster: Gemini 4 verweigert eher, wenn es nicht weiß; Fable 5.1 beantwortet viel, rät aber häufiger.
- Vectara Zusammenfassungs-Halluzination (niedriger besser): GPT-6 Sol 6,5 %, Astra 8,7 %, Opus 5.5 9,9 %, Gemini 4 Argon 12,1 %; DeepSeek V4.1 Flash 4,2 %.
- SimpleQA Verified (höher besser): GPT-6 Astra 75,8 %, GPT-6.1 Sol 73,8 %, Opus 5 63,3 %, Sonnet 5.5 44,7 %, Haiku 5.5 23,9 %. FACTS: Astra 78,1; Sol 76,8; Argon 72,4; Opus 5.5 66,5.
- BullshitBench v2 (Widerspruch bei falschen Prämissen): Opus 4.8 95 %, Sonnet 5 80 %, Fable 5 56 % [schwach, 5.5/5.2 fehlen].
- Fazit: Weltwissen/Fakten: OpenAI (Astra/Sol); kalibrierte Ablehnung: Gemini 4; Opus 5.5 mittelgut. Die Benchmarks messen Verschiedenes; ein Gesamtsieger existiert nicht.

**Kritische Prüfung / Fehlerfinden (LLM-as-Judge).**
- Keine aktuelle Rangliste "Reviewer-Qualität" für diese Modelle gefunden. Literatur 2026 (arXiv 2604.22891, 2604.23178, 2609.17857): Selbstpräferenz ist real, aber teils Messartefakt; Ensembles über Familien beheben geteilte Verzerrungen nicht; Empfehlung: Richter aus anderer Familie als der Generator, gegen menschliche Urteile validieren (https://arxiv.org/html/2604.22891v2) [belegt als Preprints].
- i6eal nutzt bewusst ein Richterpanel aus drei Herstellern; BenGER: Ranglisten stabil über Richterfamilien [belegt].
- Opus 5.5: laut Early-Testern mehr Bugs gefunden bei weniger Fehlalarmen (Codereview) [Hersteller, Code, nicht Text]. Fable 5 zeigt nur 56 % bei Prämissenwiderspruch (BullshitBench) — mögliches Sykophantie-Signal [schwach, Schluss: Fable nicht als alleinigen Reviewer einsetzen].

**Langkontext-Synthese.** AA-LCR v1.1 (Stand ca. 2026-11-04): Kimi K3 88,7 %, Fable 5.1 85,3 %, Fable 5.1 Medium 84,7 % (https://artificialanalysis.ai/evaluations/artificial-analysis-long-context-reasoning; BenchLM 2026-11-04); Fable 5.2, Gemini 4, Astra nicht gelistet. Alle Claude-Modelle 1M Kontext; Gemini 4 Neon 1M; Codex-CLI siehe 1.1. Für Langdokumente: Claude Opus/Fable, Gemini 4 Neon.

**Strukturiertes JSON / Tool-Calling.** Dünn. Stand der Technik: Schemagültigkeit bei Claude 4.6/4.7 laut Entwicklerbenchmark 100 % auf 7 von 8 Schemata [alt, schwach] (https://dev.to/david_moores_cbc0233b7447/benchmarking-llm-structured-outputs-1ijc); "schemagültig ≠ inhaltlich richtig". Für die Crew-Komposition: Fable 5.2 lehnt erzwungenes `tool_choice` ab (400) [belegt], Sonnet 5.5 und Opus 5.5 erlauben kein Abschalten des Denkens. Auf Gateway-Ebene gilt, was cli-proxy tatsächlich umsetzt (nicht geprüft). Immer serverseitig gegen Schema validieren und Retry einbauen.

**Zusammenfassung (Texte kürzen/verdichten).** Kein Benchmark gefunden, der Fable/Astra/Gemini 4 für Zusammenfassungen direkt vergleicht. Nächstliegende Datenpunkte: Vectara (siehe oben): GPT-6 Sol 6,5 %. Hands-on-Tests zu Haiku 5.5 vs. Luna uneinig (Einzelblog: Luna schneller und billiger, Haiku scheiterte zweimal an Output-Limit) [schwach] (https://chaseai.io/blog/haiku-5-5-vs-gpt-6-luna). Empfehlung [Schluss]: Sonnet 5.5 oder GPT-6.2 Sol für Qualitätszusammenfassungen, Haiku 5.5 nur bei einfachen Kürzungen.

**Übersetzung.** Keine aktuelle, abrufbare Rangliste gefunden. WMT26 (Findings-Paper: https://aclanthology.org/2026.wmt-main.48/) bewertet 46 Systeme, die DE-EN-Ergebnisse habe ich nicht lesen können. [Schluss] Claude, Gemini 4 und GPT-6.x gelten alle als stark; Gegenprüfung durch andere Familie.

**Schnelle, günstige Helfer.** Haiku 5.5 (ab 0,10/0,50 $, "fastest"), GPT-6 Luna (0,10/0,50 $, "enge Edits"), Hydrogen (1,50/7,50 $, deutlich stärker laut Index: Hydrogen 53,9 vs. Haiku 36,5 vs. Luna 27,9 auf einer Aggregatorseite, [schwach]). Halluzination: Luna 79,3 % (AA-Omniscience) — nur für regelgebundene Aufgaben ohne Faktenwissen. Haiku 5.5 34,2 %.

## 3. Empfehlungen je Rolle (Schlussfolgerung, gestützt auf Abschnitt 2)

Leitgedanke: Reviewer immer aus anderer Familie als der Executor (Literatur siehe oben). Abo-Modelle zuerst; bezahlte nur dort, wo ein Familienwechsel nötig ist oder ein Befund klar für sie spricht.

| Rolle | Empfehlung (Abo) | Bezahlte Ergänzung | Begründung |
|---|---|---|---|
| **Executor deutsch/Prosa/Reden/Business** | Opus 5.5 (Standard); GPT-6.2 Sol als Familienalternative | Gemini 4 Neon | Opus: Hersteller- und Lesbarkeitssignale, beste Geschäftsdeutsch-Werte (alt: Opus 4.8). Gemini 4 liegt in Arena Creative auf Augenhöhe |
| **Executor schwere Analyse/Rechtsargumentation** | Fable 5.2 (bei Opus-Versagen), GPT-6.2 Astra | – | Fable/Astra führen Gesamtindizes; Fable nicht für Stil überlegen, 2x Preis, Datenretention beachten |
| **Executor Marketing, hohe Stückzahl** | GPT-6.2 Sol, Sonnet 5.5 | Gemini 4 Hydrogen | Kosten/Qualität; Instruction-Following-Werte (schwach) |
| **Reviewer (Executor = Claude)** | GPT-6.2 Astra (Fakten), GPT-6.2 Sol | Gemini 4 Neon/Argon (niedrige Halluzination, Vals Legal Index Platz 1 US-Recht) | Familienwechsel; OpenAI vorn bei SimpleQA/FACTS, Gemini bei Kalibrierung |
| **Reviewer (Executor = GPT/Gemini)** | Opus 5.5 (Stil, Quellen), Sonnet 5.5 | – | Claude-Familie als Gegenpol; Fable nicht als alleiniger Reviewer (Sykophantie-Hinweis, schwach) |
| **Advisor: Pre-Mortem, Teufels Anwalt** | Fable 5.2 oder Astra, Familien mischen | Neon | Tiefes Denken zählt, Stil nicht; Mehrkosten nur für wenige Aufrufe |
| **Advisor: Fachexperte Recht/Finanzen** | Opus 5.5 / Astra (Recht/Steuer laut i6eal GPT-5.5 100, alt) | Neon (Vals Legal Index) | Deutsches Recht nicht aktuell gemessen; Reviewer-Gegenprüfung |
| **Grounding-Refiner (Suchtreffer filtern/synthetisieren)** | GPT-6.2 Sol oder Sonnet 5.5; Haiku 5.5 nur zum reinen Filtern | Hydrogen | Quellentreue wichtiger als Stil: Vectara Sol 6,5 %, Astra 8,7 %, Opus 9,9 %; Luna wegen 79,3 % Halluzinationsrate meiden |
| **Transform-Finalizer (Zusammenfassung, Tonwechsel, Glossar)** | Sonnet 5.5, GPT-6.1 Terra ("revising prose", Vercel-Beschreibung), Sol | – | Günstig und treu; für Tonwechsel eine andere Familie als der Executor vermeiden Selbstglättung |
| **Crew-Composer (strukturiertes JSON)** | Sonnet 5.5 (Vals Index 70,85 % bei 2 $/10 $) oder Opus 5.5; Fallback Sol | Hydrogen | Fable lehnt erzwungenes Tool-Calling ab; immer Schema-Validierung + Retry |

Hinweise: (1) Kosten: Opus 5.5 verbraucht bei max Effort sehr viele Output-Token (AA: ~119k pro Aufgabe, Kosten pro Aufgabe nicht niedriger als Opus 5) — Effort bewusst auf medium/high setzen (https://the-decoder.com/claude-opus-5-5-matches-fable-5-1-at-40-percent-lower-cost-as-anthropic-promises-to-fix-claudish-writing/, 2026-09-22). (2) Opus 5.5 und Fable 5.2 haben Adaptive Thinking immer an; bei Fable 5.2 `temperature`/`top_p`/`top_k` weglassen (sonst 400). (3) Cyber-/Bio-Klassifikatoren sind für unsere Textarten praktisch irrelevant, aber bei Fehlalarmen als `stop_reason: refusal` prüfen.

## 4. Quellenübersicht (Benchmarks, Stand, Bewertung)

| Quelle | Stand | Aussage | Einordnung |
|---|---|---|---|
| Artificial Analysis Leaderboard https://artificialanalysis.ai/leaderboards/models | undatiert, nach 2026-11-18 | Fable 5.2 65, Astra 62, Neon 61, Opus 5.5 59 | unabhängig, Indexversion wechselt |
| AA Multilingual Deutsch https://artificialanalysis.ai/models/multilingual/german | undatiert | Gemini 3.1 Pro 95 vorn | [alt] |
| AA-Omniscience v1.1 (via Metarix, Suprmind) | 2026-10-31 | Gemini 4 Argon/Carbon halluzinieren am wenigsten | Zahlen aus Sekundärquellen |
| AA-LCR https://artificialanalysis.ai/evaluations/artificial-analysis-long-context-reasoning | ~2026-11-04 | Kimi K3 88,7 %, Fable 5.1 85,3 % | neue Modelle fehlen |
| AA GDPval-AA v2.1 (Suche; Japanischer Bericht 2026-11-08) | 2026-11-08 | Opus 5.5 1866, Kimi K3.1 Pro 1858, Muse Wonder Full 1857, Konfidenzintervalle überlappen | [schwach] |
| LMArena Text / Creative (Aggregatoren) | 2026-11-04 bis 11-20 | Gemini 4 vorn; Creative: Fable 5.2 ≈ Argon ≈ Opus 5.5 | arena.ai selbst nicht abrufbar |
| EQ-Bench Creative Writing v3 https://eqbench.com/creative_writing.html | Tabelle nicht ladbar | Claude-Richter | Spitze aus digitalapplied 2026-08-31 [alt] |
| Surge Hemingway-Bench | Februar 2026 | Menschliche Profi-Richter, Gemini 3 / Opus 4.5 vorn | [alt] |
| Vals.ai Index / Legal Index / LegalBench https://www.vals.ai/benchmarks | 2026-11-19 / 10-13 | Neon führt Legal Index; Fable-Linie führt LegalBench | US-Recht, kein deutsches Recht |
| BenGER (arXiv 2605.28183) | 2026-08-31 | Deutsches Recht, ~83 Punkte Spitze | [alt] |
| i6eal Deutsch https://i6eal.de/en/ki-benchmark-deutsch/ | 2026-06-22 | Opus 4.8 98, GPT-5.5 95 | Anbieter, 24 Aufgaben, [alt] |
| MÖVE (arXiv 2606.13111) | 2026-06-11 | Verwaltungsdeutsch, kein Gesamtsieger | [alt] |
| Vectara Halluzinations-Leaderboard (via Suprmind) | 2026-10-22 | Zusammenfassungstreue | Snapshot, Fable fehlt |
| SimpleQA Verified, FACTS (Kaggle, via Suprmind) | 2026-10-20 / 11-05 | OpenAI Astra vorn | Sekundär |
| BullshitBench v2 | undatiert in Quelle | Opus 4.8 95 %, Fable 5 56 % | [schwach] |
| Aggregator BenchLM https://benchlm.ai/best/for/fiction | 2026-11-21 | Proxy-Rangliste Instruction Following | nicht als Qualitätsmaß |

## 5. Lücken, Widersprüche, offene Punkte

- Keine unabhängige Messung deutscher Textqualität für Opus 5.5, Fable 5.2, Astra/Sol 6.2, Gemini 4. Empfohlen: kleiner eigener A/B-Test mit 6 bis 10 echten Texten (Rechtsbrief, Rede, Schadenmeldung, Marketing), geblindet, von menschlichem Urteil plus Richter aus Drittfamilie bewertet.
- Widersprüche: Arena Creative (Fable 5.2 vs. Argon vs. Opus 5.5, je nach Aggregator); EQ-Bench vs. Arena (nahezu gegenläufig); Astra-Kontext 272k (Codex-Katalog) vs. 1,05M (Vercel); Fable 5.2 Gesamtindex-Spitze vs. schwächere Wärme-/Kreativwerte; AA-Indexwerte über Versionen hinweg.
- Fable 5.2 fehlt bei Vectara, AA-Omniscience, LegalBench und AA-LCR (jeweils nur 5.1/5). Alle Faktentreue-Daten zu Fable betreffen Vorgängerversionen.
- Hersteller-Benchmarks (Anthropic, OpenAI, Google) sind Eigenangaben; Anthropic selbst weist darauf hin, dass Benchmark-Abstände auf diesem Niveau weniger aussagekräftig sind (the-decoder, 2026-09-22).
- Nicht verifiziert: `*-latest`-Aliase, Carbon-/Hydrogen-API-IDs, Mistral-Large-4-Preise, Kimi-K3.1-Gewichte, Hersteller der "Muse"-Modelle, Haiku-5.5-Preisstaffel über 100K Token (Sekundärquelle 0,50 $/2,50 $).
