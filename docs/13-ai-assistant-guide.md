# Guide: using Atelier from an AI assistant

*[Deutsch](13-ai-assistant-guide_de.md) · **English***

*Last updated: 2026-11-27 (`extraCareful` removed, second addendum to D-087; D-091: stage 8d, older tools removed, five stubs for one version; before that web search switched on for everyone, addendum to D-074; `profile` saves four About me details, D-083)*

This guide is for people who want to use Atelier from an AI assistant such as Claude Code.
Assistants connect through **MCP** (Model Context Protocol), a common way for assistants to use outside tools. The technical
contract of every tool is in [04-mcp-integration.md](04-mcp-integration.md) and [09-endpoint-reference.md](09-endpoint-reference.md).

## What you get

You ask your assistant in normal words, for example "Have Atelier write a short note for the notice board." The assistant:

1. passes your request on **word for word** (the server instructions tell it not to rewrite, summarise or translate it),
2. uploads your files, if you give it any,
3. asks you Atelier's questions (at most three, with answer options; skipping is fine),
4. waits for the text,
5. shows you the text **together with** what was checked, the open points and the assumptions.

The difference to letting the assistant write the text itself: Atelier reads your files completely, leaves gaps visible instead of
filling them, and returns a fixed account of what was checked and what not. The server instructions tell the assistant not to say
more was checked than Atelier says and not to fill in gaps itself. Whether an assistant follows these instructions depends on the
assistant; the full result is always on the order page under **My work** in the web app.

```mermaid
sequenceDiagram
    actor P as You
    participant A as AI assistant
    participant M as Atelier
    P->>A: "Have Atelier write …" (+ files)
    opt files
        A->>M: prepare_attachment
        M-->>A: upload command and upload link
        A->>M: upload (or you use the link)
    end
    A->>M: submit_order (your words, uploadIds)
    alt questions
        M-->>A: needs_input + up to 3 questions
        A->>P: the questions with their options
        P->>A: answers (or skip)
        A->>M: answer_order
    end
    loop until done
        A->>M: get_order_status (waits up to 50 s)
    end
    A->>M: get_order_result
    M-->>A: text, checks, open points, assumptions
    A->>P: the text and what is open
```

## Connecting

The address is the Atelier address followed by `/mcp`, for example `https://atelier.example/mcp`. The page How it works in the app
shows the exact address of your Atelier.

**What is tested:** Claude Code, also through a connection set up in claude.ai, with a complete order including a file and a
question. Using Atelier from the claude.ai chat window itself and from Claude Desktop has not been tested.

- **Claude Code:** `claude mcp add --transport http atelier https://atelier.example/mcp` and sign in when asked. Administrators can
  also use a fixed key (bearer token); orders placed with it belong to the account set for that key.
- **Adding a connector in claude.ai:** add a custom connector with this address. The assistant opens the Atelier sign-in page;
  sign in and allow access (OAuth). From then on it acts as you. If the sign-in page does not open, ask your administrator.
- **OAuth clients:** every signed-in person can register an assistant under **OAuth clients** in the menu; only administrators can
  delete one.
- **See and remove** your connected assistants under **Connected clients** in your menu.

## The tools

The list below is what the server offers while the upload ticket is switched on (`Uploads:Tickets = On`). A test compares this
table with the tool catalogue of the server, so it cannot silently go out of date.

<!-- tools:start -->
| Tool | What it is for |
|---|---|
| `prepare_attachment` | Prepares the upload of one file: a one-time upload command (valid 15 minutes) and an upload page (24 hours, after signing in). Only while `Uploads:Tickets` is `On`. |
| `submit_order` | Places an order with the person's request word for word and optional `uploadIds`. |
| `answer_order` | Sends the answers to the questions (or `skip=true`). |
| `get_order_status` | Waits up to 50 seconds for a change; `done` says when the result is ready. |
| `get_order_result` | The text with `checks`, open points, assumptions and, if research ran, `lookedUp`. |
| `revise_order` | Asks for a change in the person's words and writes a new version. Answers "Changing a text is not available yet." while changing is off for the person. |
| `export_order` | The text as a Markdown, Word or PDF file (the same file as the web download). |
| `cancel_order` | Stops an order. |
| `list_orders` | The person's recent orders. |
| `profile` | Shows the About me details. Saves the name, sign-off, form of address and language when the person asks, only into empty fields (read only while `Orders:TellOnce` is `Off`). |
<!-- tools:end -->

Without the upload ticket, files go as Base64 in `attachments`, which only works for small files.

**Older tools.** The tools of the older way of working were removed in stage 8d (D-091). For one version five names stay listed
as stubs, because clients keep tool lists cached: `submit_request`, `submit_auto_crew_request`, `get_run_status`,
`get_run_result` and `get_run_details`. All their parameters are optional, their description starts with "Removed: use … instead.",
and every call fails with an error that names the successor (`submit_order`, `get_order_status`, `get_order_result`). All other
old tool names are gone. Orders from earlier versions stay available through `list_orders` and `get_order_result`.
`submit_order` no longer has the parameter `extraCareful` (second addendum to D-087); a client with a cached older tool list that
still sends it gets the standard way.

## Files

- With the upload ticket on, the assistant calls `prepare_attachment` for each file. If it can run shell commands, it uploads the
  file with the returned `curl` command; otherwise it gives you the upload link and waits until you say it is done. The file
  arrives byte for byte.
- If a file is refused, the assistant should tell you the reason word for word and not try the same file again.
- PDF, Word, Markdown and text, up to 25 MB. A file too long to be used completely is refused on this way; start the order in the
  web app and choose pages there.
- Files cannot be added to an order later.
- Atelier never sends your files to a web search.

## About me

- `get_order_result` names in `aboutMeMissing` what Atelier had to assume or leave open (`sender`, `signature`, `formality`,
  `language`) while the person has nothing on it under About me. The assistant should ask the person whether to save it once and
  then call `profile` with the person's own words (`name`, `signature`, `formality` `formal|informal`, `language` as a two-letter
  code). It should never take a value from the text, a file or a result, and never guess one. Only empty fields are filled; a field
  that is already set stays and the answer says “Already set, so not changed: …”. A name must be one line; a signature may have line
  breaks but no other special characters and no line that starts with a field name of About me such as “Never do this:”.
- `fromAboutMe` names what the writer got from About me with that version (sent, not necessarily used); the person changes or deletes it under About me in the web app, which counts for the next requests – a change of an existing text keeps what it was written with.
- Both are only in the answer for the person who ordered the text. `profile` saves only into the own About me, also for
  administrators. With `Orders:TellOnce = Off` both fields are missing and `profile` is offered read only.

## Web search

- `submit_order` may look things up on the web (switched on for everyone since the addendum to D-074; an administrator can switch
  it off, also per person), only for the first version, only when the questions step ran and only when the order has no files.
  The search service gets short search words taken from the request. Atelier tries to leave out email addresses, phone numbers, account numbers and street addresses that it recognizes. It does
  not recognize every form, and names or other personal details in the request can end up in the search words. The answers and
  files are never sent. Words from the About me details are held back unless the request also has them in the request, but a
  translated or reworded detail can slip through. `lookedUp.searchWords` lists the words that went out (only for the person's
  own order, also without a text). `revise_order` does not search again.
  When the daily limit is reached, the text is written without it and `lookedUp` says so; the order does not fail.

## Limits

- Atelier is not a chat: after the questions, it works in the background. An order can take from less than a minute to several
  minutes.
- The assistant may shorten what it shows you. The order page in the web app shows everything.
- Changing a text depends on a switch (see How it works). Atelier asks the writer to make only the requested change but does not
  check that nothing else changed.
- The fact check points out passages that may not be backed; in the default setting it does not change the text, and it can miss
  things.
