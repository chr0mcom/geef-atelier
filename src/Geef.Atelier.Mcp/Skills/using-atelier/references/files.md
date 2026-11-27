# Files

Files must reach Atelier before submit_order; they cannot be added to an order later. Atelier reads PDF, Word, Markdown and
plain text.

## With prepare_attachment (if it is in your tool list)

1. For each file the person gives you, call prepare_attachment with the name of the file.
2. If you can run shell commands and read the file, upload it with its curlCommand, with the path quoted for the shell as
   prepare_attachment describes. For a path with a quote, a line break or another unusual character, follow the description
   of prepare_attachment exactly.
3. If the answer has usable false, the file was refused: tell the person its reason word for word and do not ask them to
   upload the same file again.
4. If you cannot run the command, or it fails without an answer from Atelier, give the person its uploadPage link word for
   word and wait until they say it is done.
5. Then call submit_order with the upload IDs in uploadIds.

Only very small text files (a few kilobytes) may go into attachments as Base64 instead.

## Without prepare_attachment

Files go only into the attachments parameter of submit_order, with the complete original file as Base64 and the matching
contentType. If a file is too large to pass as an argument, ask the person to start the order in the Atelier web app and add
the file there.

## Always

- Never send a placeholder, a file name, a summary or shortened content instead of a file.
- Never type, summarise or Base64-encode a file from memory, and never paste file content into the request.
- The server rejects attachments that look like placeholders, contain Base64 encoded twice, or whose content does not match
  the contentType.
- Say that a file has arrived only when an answer from Atelier shows it: the answer of curlCommand or the sources in the
  answer of submit_order. Tell the person the warnings in sources.
