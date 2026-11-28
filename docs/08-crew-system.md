# 08 — Crew system (PS-5)

*[Deutsch](08-crew-system_de.md) · **English***

Last updated: 2026-11-27 (second addendum to D-087: "Extra careful" removed, one setup; D-091: stage 8d, rewritten to what exists now: the setup in code, crew snapshot, model matrix, reviewers of the direct path, the removed parts are listed at the end; before that 2026-11-26 note on where the crew system fits, D-076: files through the upload ticket; 2026-11-25, D-075 part B: change run, D-074: research in the grounding step; 2026-11-24, D-067: strict snapshot reading; older notes are in the git history)

## Overview

A "crew" is the setup of a run: one writer and its checks. Since stage 8d (D-091) there is **one way of working, the direct
path** (D-068 to D-077, [15-developer-guide.md](15-developer-guide.md)), and its setup exists **only in code**. Nobody
composes, edits or stores crews any more. What remains of the crew system is:

- one setup in `Geef.Atelier.Core.Domain.Crew.SystemCrew`,
- the **crew snapshot** that every run stores, so that it records what it was run with,
- the **model matrix**, which assigns the models when the snapshot is built.

The earlier system (templates and profiles in the database, editors and further actors) is listed under
[Removed parts](#removed-parts-stage-8d-d-091). Its full description is in the git history of this file.

## Core concepts

| Term | Meaning |
|---|---|
| **ExecutorProfile** | The writer: name, display name, system prompt, provider, model, `MaxTokens`, optional `ModelSlot`. |
| **ReviewerProfile** | A check of the draft, with the same fields. The implementation is injected when the pipeline is built. |
| **CrewTemplate** | A setup in code: writer, checks, evaluation strategy, convergence override, domain. |
| **CrewSnapshot** | The fully embedded copy of the setup, stored as JSONB in `Runs.CrewSnapshot`. |

## The setup

`SystemCrew` has exactly one template, `direct`. `ResolveOrderSnapshotAsync` uses it for every new run, and
`CrewService.ResolveCodeTemplateSnapshotAsync` turns it into the snapshot (build, assign models, resolve model aliases).
There is one production pipeline assembler, `DirectPipelineAssembler`; it accepts this name only. The former variant
"Extra careful" was removed after its measurement (second addendum to D-087).

| Template | Used for | Writer | Checks |
|---|---|---|---|
| `direct` | every order and every change run | `direct-writer` | `deterministic-checks`, `source-fact-check` |

| Profile | Shown as | Model |
|---|---|---|
| `direct-writer` | Writer | slot `Executor` |
| `deterministic-checks` | Automatic checks | none |
| `source-fact-check` | Fact check | slot `FactReviewer` |

It has no advisors, tools or steps after the checks. The **only access to the web** is the research in the grounding
step ([below](#research-in-the-grounding-step-d-074)).

## EvaluationStrategies

The enum `EvaluationStrategy` keeps `Parallel` (0), `Sequential` (1), `FailFast` (2) and `Priority` (3), because stored
snapshots name them. The setup uses `Parallel`: all checks run and all findings are collected.

## CrewSnapshot format (SchemaVersion 4)

The example shows the **structure**. Prompts are cut and model ids are placeholders. Enums are stored as numbers.

```json
{
  "schemaVersion": 4,
  "templateName": "direct",
  "executor": {
    "name": "direct-writer", "displayName": "Writer", "description": "...", "systemPrompt": "...",
    "provider": "claude-cli", "model": "<concrete model id>", "maxTokens": 256000, "isSystem": true,
    "modelSlot": 0, "modelDomain": null
  },
  "reviewers": [
    { "name": "deterministic-checks", "provider": "", "model": "", "...": "..." },
    { "name": "source-fact-check", "provider": "codex-cli", "model": "<concrete model id>", "modelSlot": 1, "...": "..." }
  ],
  "evaluationStrategy": 0,
  "convergenceOverride": { "maxIterations": 2, "abortOnCritical": false, "detectRegression": false, "stagnationThreshold": 2 },
  "crewDomain": 0
}
```

- It is serialized with `JsonNamingPolicy.CamelCase`.
- Only a snapshot of the current schema version runs; any other version ends the run with a plain sentence.
- Aliases are resolved before the snapshot is stored (`CrewSnapshotModelResolver`): the snapshot holds the concrete model
  ids, and a resumed run uses the same ones. If the catalog cannot be reached, the alias stays.

**Reading a snapshot for a run (D-067).** The orchestrator reads it strictly with `CrewSnapshot.DeserializeForRun`. A missing,
unreadable or empty snapshot, a schema version other than the current one (4, since D-092), or a snapshot without writer or
reviewer list raises `InvalidCrewSnapshotException`, and the run ends `Failed` with a clear message. There is no fallback setup. The
tolerant `CrewSnapshot.Deserialize` is for display only. A snapshot without any reviewer, or with a reviewer this version does
not know, is refused as well when the pipeline is built: there is no text without checks.

## Model matrix (D-066)

A profile states its **role** (`ModelSlot`), the setup states its **domain** (`ModelDomain`; `direct` is `General`), and
`Core/Domain/Crew/Models/ModelMatrix` holds the ordered candidates. `SystemCrew` has no model literals: the binding shown on a
profile is the primary of its slot. Claude runs through `claude-cli`, OpenAI through `codex-cli`.

| Slot | Used by | Candidates in order (General) |
|---|---|---|
| `Executor` | writer | Claude Opus, Codex Sol |
| `FactReviewer` | fact check | Codex Astra, Claude Opus, Gemini 4 Neon (paid), Claude Sonnet |
| `Intake` | questions before writing (D-071) | Claude Sonnet, Codex Sol |
| `GroundingRefiner` | picking passages in the research | Claude Sonnet, Codex Sol |

The enum and the matrix keep further slots and domains that no setup uses now; the numbers are stored in old snapshots, so
members are only ever appended.

**Model assignment** (`IModelAssignmentService.ApplyAsync`, once per run, when the snapshot is built):

- A candidate must be reported by the live catalog. An unreachable catalog counts as available, an HTTP provider without its
  API key as unavailable.
- All rules live in one function (`ModelChooser`), guarded by `ModelAssignmentInvariantTests`. Hard rules are never relaxed:
  the provider is usable, and a paid candidate goes only to a reviewer, only in the Legal or Academic domain or from 3
  reviewers of the same slot on, and to at most 2 reviewers per setup. With the setup of today the fact check therefore never
  gets a paid model.
- Reviewers with a slot do not share the writer's model while a permitted alternative exists, and prefer another vendor
  family.
- A profile without a slot keeps its binding. If it is the writer's model, a warning says so.
- A fallback or a missing plurality becomes the run warning `model.assignment`.
- The matrix names family aliases (`claude-opus-latest`, `gpt-sol-latest`, `gpt-astra-latest`). The cli-proxy resolves them
  to the newest model of the line, so a new release needs no code change (D-066/10).

The rationale for the matrix is in D-066 in the decisions log.

## Convergence and delivery

The setup runs with `MaxIterations: 2` (the writer writes once and corrects at most once), `AbortOnCritical: false`,
`DetectRegression: false` and `StagnationThreshold: 2`. Values a setup does not override come from the section `Convergence`.
Only findings of severity `Error` or higher block. A reviewer whose call fails is isolated and does not block; the run gets
the warning `review.reviewer_failed`.

- A converged run delivers its last version.
- A run that does not converge delivers the version with the fewest rejecting reviews (the later one on a tie) with the
  outcome "delivered with open points". Without any text it ends as failed.
- `Runs.Outcome` is set after the report (`Runs.ReportJson`) has been written
  ([02-architecture](02-architecture.md#run-outcome-and-runstatus)). If the result cannot be stored completely, the run gets
  `run.result_not_saved` and fails. If the service stopped while a run was finishing, the next start records an outcome and
  adds `run.restarted_while_finishing`.
- The switches `Convergence:BestDraft`, `WriterBlockingOnly` and `StopWithoutProgress` (D-085) were removed with the old
  review loop.

## Runs from before the database cleanup (D-092)

The orders and runs of the removed ways of working were deleted with the database cleanup, together with the code that only read
them. A stored setup that names a check this version does not know fails with "The saved setup of this task names a check that no
longer exists, so it cannot run. Please start it again."

## Uploaded files: safe reading (D-069 addendum)

Files from `POST /api/sources` and from MCP attachments go through `SourceIntakeService`. PDF and Word files are read
by the same application in a **separate process**, so a hostile file can never take the web process down.

### Intake steps

1. **Upload place and slot.** Each person runs at most `Sources:MaxConcurrentUploadsPerUser` (2) uploads at the same
   time; a further one is refused at once with HTTP 429, `Retry-After: 5` and "You are already adding 2 files at the
   same time. Please wait until one is done, then try again." Across all people at most `Sources:MaxConcurrentIntakes`
   uploads run at the same time; a caller waits up to `Sources:ExtractionQueueTimeout`, otherwise the answer is HTTP 503
   with `Retry-After: 30` and the receipt "The service is busy with other uploads. Please try again in a minute." (the
   card offers "Try again"). Both are taken **before** the request body is
   read: `POST /api/sources` reads the multipart body as a stream (no form buffer) and takes the antiforgery token only
   from the header. Pasted text needs neither, because it is already in memory.
2. **Streaming and time.** The upload is written to `<StorageRoot>/.incoming/<id>/upload.part` (directory 0700, file
   0600), hashed on the way and capped at `Sources:MaxFileBytes`. Only the first 8 KiB stay in memory for type
   detection. The request ends after `Sources:UploadTimeout` (5 minutes, HTTP 408 "The upload took too long. Check your
   connection and try again.") and, behind Kestrel, when it sends slower than `Sources:MinUploadBytesPerSecond`
   (4 KiB/s after a grace period of 10 s); both free the place and the slot. A broken or interrupted body ends as 400
   "The upload could not be read."; only a file above the size limit gets 413.
3. **Reuse and quota.** An identical file of the same owner is reused (the unique index
   `UX_Sources_OwnerUsername_Sha256` makes parallel uploads of the same file end in one source). Files that no order
   uses count against `Sources:MaxUnattachedSourcesPerUser` (50), `Sources:MaxUnattachedBytesPerUser` (500 MB) and
   `Sources:MaxUnattachedCharsPerUser` (20 million characters of text). The quota is checked before reading and again
   inside the inserting transaction under a lock per person (`pg_advisory_xact_lock(hashtext(owner))`), so parallel
   uploads cannot pass it together.
4. **Type check.** The kind comes from the content. Before a ZIP archive is opened in the web process, its end record
   is read: more entries than `Sources:MaxArchiveEntries`, a central directory larger than 1 KiB per allowed entry or a
   ZIP64 directory (no Word file needs one) refuse the file without listing the archive. `%PDF-` within the first
   1,024 bytes counts as a PDF on purpose (readers accept the header anywhere there).
5. **Extraction.** Text and Markdown are read in the web process. PDF and Word take an extraction slot
   (`Sources:ExtractionMaxConcurrency`, default 2) and run as
   `dotnet <application assembly> --extract-source <pdf|docx> <file>`. `Program.cs` branches into this mode before any
   host is built: no DI, database, configuration or sign-in. The answer of the process is parsed while it streams in;
   it is never held as a whole byte array.
6. **Text limit.** One source holds at most `Sources:MaxExtractedChars` (3 million characters; a book of 1,000 pages has
   about 2 million). Word files are counted while their XML parts are scanned, before the document model is built, and
   again while the body is walked; PDF reading stops at the page that passes the limit; text files are counted before
   they are decoded; the web process checks the length again before it copies the text. The receipt says "This file
   contains far more text than we can use. Please upload a shorter version."
7. **Preparation in one pass.** Line ends become `\n` first (so a lone `\r` starts a line before anything looks at
   the lines), lines that look like `[[page N]]` are neutralised in text, Markdown and Word files, long lines are broken
   (every broken piece is neutralised as well) and characters and words are counted without page markers. Text that
   needs none of this is not copied. Only the PDF extractor writes real markers.
8. **Store.** The received file is moved into `<StorageRoot>/<owner hash>/<id>.<ext>`; the temporary directory is
   removed in every case.

### Limits of the extraction process

| Limit | Option | Default | Effect when hit |
|---|---|---|---|
| Total time | `ExtractionTimeout` | 60 s | whole process tree killed, "Reading this file took too long." |
| Managed memory | `ExtractionMemoryLimitBytes` | 1 GiB | `DOTNET_GCHeapHardLimit` in the child; a watchdog also kills it above the limit plus 256 MiB |
| Text | `MaxExtractedChars` | 3,000,000 characters | reading stops, "This file contains far more text than we can use. Please upload a shorter version." |
| Answer size | `ExtractionMaxOutputBytes` | 20 MiB | process killed, file unreadable; sized for the text limit at up to six bytes per UTF-16 unit (emoji and format characters are written as `\uXXXX`, letters of the basic plane are not escaped) plus room for the JSON |
| Environment | — | — | cleared; only `PATH`, `HOME`/`TMPDIR` (the private upload directory), GC settings and the extraction limits as JSON |
| Working directory | — | — | the private upload directory |

A crash (for example a stack overflow, exit code 128 and above), an out-of-memory exit or a kill ends as a receipt
with "This file is too complex to read. Please save it again or split it into smaller files." The web process only
logs kind, outcome, duration, exit code and peak memory, never file content or names.
`Sources:ExtractionMode = InProcess` exists only for tests of the extractors.

### Defences inside the extractors

- **Word:** archive check (entries, total size `MaxUncompressedBytes` = 64 MiB, compression ratio from 64 KiB entry
  size on), then every XML part is read once as a stream without DTD and with a nesting limit (`MaxXmlDepth` = 100)
  before the document model is built; the body is walked without recursion.
- **PDF:** before opening, the Flate streams that are not pictures are unpacked into nothing and added up; above
  `MaxPdfInflatedBytes` (256 MiB) the file is refused. Nested filters are left to the process limits.

### Cleanup

The hourly cleanup also deletes, after `Sources:OrphanFileAge` (24 h), left-over upload directories, `*.part` files
and stored files that no source refers to. It looks only at the root and the owner folders below it, skips links and
resolves every path inside the root. When at least 10 stored files are unknown and they are more than half of all old
stored files, the database most likely does not match the volume (restored from an older backup or started empty);
then it deletes no stored file and logs an error.

### Operations note

The compose file limits the memory of the web container to 6 GiB (`mem_limit: 6g`) as a second layer behind the
limits above: the web process usually stays below 1 GB, each extraction process may use 1 GiB of managed heap (plus
the watchdog allowance) and at most two run at the same time; the rest is headroom. The limit protects the other
services of a shared host. If the host has less memory to spare, lower `ExtractionMaxConcurrency` or
`ExtractionMemoryLimitBytes` together with the limit.

## Automatic length check of the direct path (D-070 addendum)

The length check without a model is deliberately conservative: it blocks only when the request is clear and the text
is far off; otherwise it leaves a note. `RequestConstraintExtractor` reads lengths only outside quotes, code and text
attached after a lead-in ("Answer this e-mail:"). `LengthRuleGate` then lets a length become a blocking check only
when the whole request has exactly one length in words or characters (or a clear pair such as "between 100 and 150
words"), no other count (pieces, "each/per/je", variants, pages, minutes), no word that refers to an earlier text or a
revision ("draft", "previous", "too long", "shorten", "summarize", "translate" …), a limit word bound to the number
("at most", "höchstens", "about" … or "with/of/in N words" right after the text to write), no foreign preposition
between the instruction verb and the length in the same clause ("a title for a book with at least 80,000 words") and
no compound such as "300-word post". Every other length gives the note "Your request mentions a length (…); it was
not checked automatically." Even a blocking length leads to a correction only when the text misses the limit by more
than 25 % ("at most 100 words" from 126 words, "at least 200 words" below 150); a smaller miss is an open point. When
the length is the only blocking problem, the instruction to the writer ends with "Keep everything else unchanged."
The rate of false blocks is measured on synthetic, independently labelled requests
(`tests/Geef.Atelier.Tests/Domain/Checks/Corpus/`, `LengthCorpusTests`).

## Template `direct`: two reviewers (D-073)

The template `direct` (`SystemCrew.DirectTemplate`) runs the writer once and corrects at most once (`MaxIterations: 2`).
It has two reviewers, both injected at pipeline build time by `DirectPipelineAssembler`.

| Reviewer | Profile | Model | What it does |
|---|---|---|---|
| Automatic checks | `deterministic-checks` | none | `DraftCheckEvaluator`: not empty, no remark before the text, length, language, placeholders (in square brackets, up to 300 characters on one line; the fact check drops a flagged passage inside a placeholder only up to 200 characters, so a claim in a longer bracket is kept), forbidden patterns and words, required words, written calculations. Blocking failures → `Error`, the rest `Info`. |
| Fact check | `source-fact-check` | slot `FactReviewer` (today Codex Astra; not the writer's model, another vendor family preferred) | `SourceFactCheckReviewer`: compares the facts of the text with the request, the answers, the profile and the files (`FactCheckPrompts`, version `fact-check-1`). |

The fact check runs for every order of the direct path; only `Direct:FactCheckMode = Off`, `Shadow` or a failure of the
model and its one fallback leave the facts unchecked, and "What was checked" says so.

- **Material** comes from the grounding step (`AtelierContextKeys.FactMaterial`): request, answers of the person and of
  the client, profile and files, for a revision the text to revise as `S0`. Assumptions and the writer's notes are never
  material. Above `Direct:FactCheckMaxMaterialChars` (150,000 characters of material and text) the check gets the file map
  instead of the content.
- **Filter of the code** (`FactFindingFilter`): a finding stays only with a word-for-word quote; whether it blocks follows from
  where its quotes stand (request, the person's answers and profile block; the client's answers are hints; contradictions
  in the files never block). A reason that names a link, an address or account or access details never drops the finding:
  the reason is replaced by a fixed sentence, the quote of the text stays (an invented IBAN is exactly what the person must
  see); unsafe passages of the material are replaced the same way. "Covered word for word" needs whole words.
- **Every wording is checked in full.** A text changed for any reason (fact finding, length, language …) gets a full
  `check_facts` again, with the same fallback rules; only a wording an earlier round already checked word for word takes over
  that round's findings without a call.
  A model that ran into its time limit is not called again in the same run. The report knows which wording was checked
  (`FactCheckRound.TextHash`); a delivered text no check saw is `VersionNotChecked` ("The facts of this version of the text
  were not checked …"), and earlier findings are shown only when their passage still stands in it.
- **No finding is lost between rounds** (`FactCarryOver`): after every full check, the findings of all earlier answered rounds
  whose passage still stands in the text word for word (whole words) stay open with their blocking effect, and every source
  conflict stays (a correction does not change the files). Only repetitions are dropped: a passage that lies wholly inside a
  kept passage of the same kind (for an instruction, against the same instruction), or a conflict whose two passages contain
  each other as whole words and that names the same files; a passage that only overlaps another is shown as well. No limit
  applies, ids are given anew. When an earlier version is delivered as the best one, the findings of the later answered rounds
  are added the same way (conflicts always, passages only when they stand in the delivered text, unlisted ones counted). So in `On` a blocking
  passage that the second check does not flag again still rejects. Flagged passages an earlier check could not list stay
  counted. Quotes are widened to whole words, so an unchanged passage never counts as "removed"; without a check of the
  delivered wording, "Removed or replaced" adds that the statement may still be in the text in other words.
- **Modes:** `Report` (shipped) → findings `Warning`, decision `Approved`, the text is never changed; `On` → blocking findings
  `Error` and `Rejected`, the writer gets fixed sentences; `Shadow` → `Info` with metadata `shadow`, nothing shown.
- **Fallback:** when the model cannot be reached, one more attempt with the next candidate of `FactReviewer` that is neither
  the writer's model nor from the provider that failed nor paid per token (`FactCheckModelFallback`, today Sonnet). The
  gateway's own failover is limited to the same model (D-090). Calls, model and fallback are in the report (`factCheck`),
  never shown as a warning.
- **Persistence:** findings keep `Category = fact-check`, `Code` (`fact.unsupported`, `fact.instruction`, `fact.superseded`,
  `fact.source_conflict`) and `Quote`; `Runs.FactCheckMode` holds the mode of the run. When an account is
  deleted or renamed, its runs lose every copy of the About me details: the block in the briefing, the briefing in the stored
  events (`PipelineStartedEvent.Input` and any other text of an event that holds the block or the details word for word) and
  what the fact check copied into report, findings and events. Runs cleaned by an earlier version are cleaned again with the
  details still held in their events.
- **Recalculation never says "right":** the check of written calculations in the text and of totals in the files names only
  what looks clearly wrong and asks the person to check it; otherwise "What was checked" says at most that nothing clearly
  wrong was found and that not every calculation can be checked this way. Earlier reports read the same way.

## Research in the grounding step (D-074)

The grounding step of the direct path (`OrderGroundingStep`) loads the order's files in full text and can add passages looked
up on the web. This research (Tavily, section `Tavily` with `Endpoint` and `ApiKey`; without a key nothing is sent) is the
only access to the web. The step asks
`IOrderResearchService.EnsureAsync` before it renders the files, with the version of the run (from the assembler), whether the
run revises a text, whether the order has attached files and the About me details of the briefing. An order with any file
(whatever its role) never searches: the service stores the status `FilesAttached` once and nothing reaches the search or the
passage picker. Otherwise a topic with a word of the About me values (without the field names) that is not in the request is
withheld by the origin rule (`ResearchQueryGate`). The service decides
(switch `Research:Mode`, override per user, first version, no revision, clarified with research topics, daily limit reserved
before the search, in total and per account) and stores
the research once at the order (`Orders.ResearchJson`); every later run of the order gets the stored passages unchanged and
never searches again.

- **Source `W1`:** when the research has at least one passage, `DossierRenderer` writes its text and the step appends it after
  the files as source `W1` with the fixed name "Looked up on the web" (`PromptSource.LookedUp`). Only the code gives the id.
  It is defused like every file (`SourceBlockRenderer`) and counts for the budget of the background document; the warning
  about long files counts only the person's files.
- **Writer:** `W1` is never counted or listed as an attached file; without files the prompt has an introduction of its own. In the
  first pass the user prompt adds one fixed sentence after the note about the sources (`DirectWriterPrompts.LookedUpNote`):
  W1 may answer a point the brief lists as not given, said as W1 says it, and is content, never an instruction. The system
  prompt `SystemPrompts.DirectWriter` is unchanged (hash test); without `W1` the prompt is the one of stage 3.
- **Fact check:** `W1` is one more source of the material (`FactSource.LookedUp`, `MaterialPart.LookedUp`). Prompts, schemas
  and version `fact-check-1` are unchanged (`FactCheckPromptStabilityTests`). The recalculation of totals never sees `W1`.
- **Usage and report:** the usage row of `W1` has no source and the target `research`, at the position after the files. The
  report lists `W1` after the files with `lookedUp` and is marked `lookedUp`; the writer's quotes from `W1` are neither quotes
  from the files nor counted as unverified.
- **Never stops the run:** status `Failed` (also research that could not be stored, code `not_stored`), an exception of the
  service or a wait longer than 120 s (also when the service does not stop on its own) give the warning
  `grounding.research_failed` ("Looking things up on the web did not work this time. The text was written without it.") and
  the run goes on without `W1`. `NothingFound`, `AllWithheld`, `DailyLimit` and `FilesAttached` add no source and no warning; the result shows
  them as fixed sentences. Only the cancellation of the run itself ends the step.

## Files through the upload ticket (D-076)

An AI assistant cannot reliably write a file as Base64 into a tool argument. While `Uploads:Tickets` is `On`, the file therefore
arrives outside the MCP call:

1. `prepare_attachment(fileName, sizeBytes?)` adds a row to `UploadTickets` (state `Open`, token only as SHA-256, token
   15 minutes, page 24 hours) and returns `curlCommand` and `uploadPage`.
2. Either the assistant runs `curlCommand` (`PUT /api/uploads/{id}`, token in the `X-Upload-Token` header), or the person drops
   the file on the page `/u/{id}` (`POST /api/uploads/{id}/page`, signed in). Both ways claim the ticket in one statement
   (`Open → Receiving`, `Attempts + 1`, `ReceivingSince = now`) and hand the file to the same intake as the web interface
   (`ISourceIntakeService.IntakeAsync`: detection by content, extraction in the child process, limits). Origin of the source:
   token way `McpAttachment`, page `WebUpload`.
3. Usable → `Used` with `SourceId`. Checked and refused → back to `Open`, the attempt stays counted, the code is kept in
   `LastRefusalCode`; after 3 such attempts `Failed`. Not checked (too many uploads at once, overload, broken off, failed save,
   extraction not available, full quota, time limit, server error) → back to `Open`, the attempt is given back (`Attempts - 1`). A
   file too long to be used completely is refused on this way (`too_long_for_ticket`; pages can be chosen only in the web app) and
   not stored. Completing and releasing only apply to the own claim (same `ReceivingSince`).
4. `submit_order(…, uploadIds)` links the sources of the arrived tickets of the signed-in person to the order (all or nothing);
   from here on the file is an ordinary source of the order.

**Cleanup** (hourly cleanup service of the sources): `Receiving` for longer than `Sources:UploadTimeout` + 1 minute → `Open` or
`Failed` (after a crash; a running upload ends earlier at the time limit of its request). The ticket service checks the same
deadline at every access to the ticket (page, token or page upload) and releases a hanging ticket right away instead of waiting
for the next pass; the attempt stays counted. Tickets are deleted 7 days
(`Uploads:TicketRetention`) after the page expired; rows of `SourceRefusals` after 180 days (`Uploads:RefusalRetention`). When a
source is deleted, `UploadTickets.SourceId` becomes `NULL`.

**Counting refusals.** `SourceRefusals` holds per refused file the time, person, way (`web`, `mcp_inline`, `mcp_ticket`,
`upload_page`), code, detected kind, extension, declared type and size, never name or content. Not counted are a token upload
above the request limit (`413` before the claim), an invalid token, a file the upload page refuses in the browser, an upload that
ends at the time limit or is cancelled, and technical `400`; `too_large` is therefore under-counted. It is the basis for deciding about
further file kinds (trigger in D-076: at least 3 refusals from at least 2 accounts outside the measurement accounts in 60 days).
Evaluation, read-only; the list of measurement accounts comes from the operations configuration
(`-v messkonten='{account1,account2}'`):

```sql
SELECT "Code", coalesce("Extension", '-') AS "Endung", "Path" AS "Weg",
       count(*) AS "Ablehnungen", count(DISTINCT "OwnerUsername") AS "Konten"
FROM "SourceRefusals"
WHERE "CreatedAt" > now() - interval '60 days'
  AND NOT ("OwnerUsername" = ANY (:'messkonten'::text[]))
GROUP BY 1, 2, 3
ORDER BY 4 DESC, 1, 2;
```

## Change run on the direct path (D-075, part B)

A run that changes an earlier version of the order ("Change something", `revise_order`) always uses the direct path. It carries
`RunEntity.RevisionHint` (the person's change including the sentences of the quick buttons), `ParentRunId` (the run of the shown
version) and `SeedDraftText` (its delivered text). The order service creates the run; "Resume" on a failed or stopped change run
creates one the same way (`RunService.ResumeRunAsync`: the same `RevisionHint`, the version shown now as `ParentRunId` and seed, its
brief composed again, stored through `TryAddRevisionRunAsync`). It refuses with `RunNotResumableException` and creates no run when
the change run already has its own text (`RevisionLines.ResumeChangedText`; the run page shows the sentence instead of the button) or
when a later version of the order has a text (`RevisionLines.ResumeLaterVersion`). The direct path does the following:

- **Grounding:** the assembler passes `RevisionHint` to `OrderGroundingStep`, which sets the context key
  `AtelierContextKeys.RevisionHint`. The earlier text is only the seed, never material of the fact check. As for any revision, the
  research only returns what is stored.
- **Brief:** `RevisionBrief.Compose` adds the change as the line marked "the change to make now" and marks every older line of the
  block as "earlier change: already made in the text – do not make it again, keep it"; the heading says that the newest change wins
  and that the person's words take precedence over the blocks "Assumptions …" and "Not given by the person". `BriefingSections.Parse`
  gives the changes back apart from the answers (`BriefParts.Changes`, the last one as the newest), and the material of the fact check
  keeps them that way (`FactMaterial.Changes`). Only the checker's prompt writes them under the answers of the person, with a short
  mark ("newest change, version n" / "earlier change, version n; a newer change wins where they differ", `FactChange.Marked`) before
  the unchanged words. Quotes are looked up in the words only; in a change run (`FactMaterial.IsRevision`) the filter first takes the
  mark out of every quote of the checker (`FactChange.WithoutMarks`), so a quote that is only a mark is no instruction and no stored
  quote holds the mark. In the first run of an order the filter leaves the quotes as they are.
- **Writer:** only in the user prompt of the first pass, `DirectWriterPrompts.RevisionInstruction` replaces `SeedInstruction` (for
  providers without document mode through the same replacement "the text in draft.md" → "this text"). Wording since gate N′
  (D-075): connecting fixes as small as possible, changes of tone, style or length without a new statement and without changing who
  writes to whom, a longer text expands on what it already says. The system prompt stays bit-identical (hash test); the correction
  pass is unchanged.
- **Checks:** in a change run every rule of the first request (`DraftCheckResult.IsFirstRequestRule`: length, language, no
  placeholders, forbidden forms, ruled-out and required words) is only a note, never blocking (`DeterministicChecksReviewer.Evaluate`,
  sentence `FirstRequestNoteFormat`); empty text, remark before the text, the placeholders to fill in, the calculations and the fact
  check run as always. The report is marked `RunReport.Revision`, and "What was checked" names kept rules as rules of the first
  request.
- **Instructions in the fact check:** in a change run (`FactMaterial.IsRevision`) a finding of kind `Instruction` blocks only when
  its instruction quote stands in the newest change (also when it stands elsewhere as well). Any other origin that is found (request,
  answers, About me, assistant, earlier change) gives a hint with `FactFinding.BeforeLastChange`: never blocking, so `Info` in mode
  `On` and never in the correction. Instruction findings carried over from an earlier version are judged the same way
  (`FactFindingFilter.ForRevision` in `DirectReportBuilder.EarlierFindings`), and `FactCarryOver.Merge` never makes such a hint
  blocking again. Other kinds of findings and the first run stay unchanged.
- **Never lose across versions (`DirectReportBuilder`):** after the run the assembler reads the report of `ParentRunId`. Its open
  findings (passages and source conflicts) go into the new report through `FactCarryOver.Merge`: passages only when their quote still
  stands as whole words, conflicts always, each with `FactFinding.FromVersion` (a finding carried over before keeps the version that
  found it); when the new check reports the same passage, it appears once as its own finding – unless the new check is `Off` or in
  shadow mode, then the carried finding comes first. Own findings of an earlier check in `Off` or shadow mode are not carried over;
  findings it had carried over (with `FromVersion`) always are. The writer's open points and assumptions of the earlier version that
  the new one does not repeat word for word in the same kind (`SameAssumptionKey`) go to `RunReport.EarlierOpenPoints` with their
  version and `EarlierPointKind` (including those the earlier version had carried over), together with a fixed sentence
  (`EarlierPointKind.Note`) for its notes and flagged passages that could not be shown. When the earlier version has no report, the fixed sentence `ChecksNotTakenOverFormat` says that its checks were not taken over and not checked
  again. When the earlier report cannot be read, the report says so in one sentence and the run goes on. When an earlier version
  without a readable report was itself shown as "Ready – please check" (`DirectRevision.NeedsCheck`, resolved with
  `OrderStateResolver`), the report sets `RunReport.EarlierNeedsCheck`; a later version inherits it from the report of its base.
  `OrderStateResolver.ToRunState` reads it in every fact-check mode as a carried blocking point. Reports of all other runs stay
  bit-identical (`null` is not written).

## Removed parts (stage 8d, D-091)

The code of the following parts is gone; their database tables and columns were dropped with the database cleanup (D-092).

- The old review loop with the templates `klassik`, `juristisch`, `akademisch`, `marketing` and every own team; the workshop
  pages and editors for teams and profiles (the provider pages stay) and the workshop dashboard.
- Own profiles and templates in the database, and the API to create or submit own crews.
- Advisors, their profiles, consultations and costs.
- The grounding source profiles and their provider types, including URL fetching with its address check, vector search, run
  attachments as a source and static context.
- All finishing steps (rewrites, file export, metadata) and the run files with their download endpoint. Export of an order's
  result (`export_order`) is a separate function and stays.
- Composer and Auto-Crew, including composition runs and materialising, and what was left of Template Studio.
- The learning loop, the vector knowledge base and embeddings, the agentic tool path
  ([10-tool-system.md](10-tool-system.md)) and specialization packs ([11-specialization-packs.md](11-specialization-packs.md)).
- The older MCP tools of these parts. The current tools are described in
  [13-ai-assistant-guide.md](13-ai-assistant-guide.md); five old names answer for one more version with an error that names
  the successor.

Parts removed earlier, in stage 8c, are recorded in D-089.
