namespace Geef.Atelier.Mcp;

/// <summary>Instructions the server sends to MCP clients during initialization (<c>McpServerOptions.ServerInstructions</c>).</summary>
public static class McpServerInstructions
{
    public const string Text =
        "Atelier writes finished texts for people (letters, emails, articles, summaries, reports) from their request and their files.\n" +
        "1. Start with submit_order. Pass the person's request word for word, as they wrote or said it. Do not write your own version of the order, " +
        "and do not summarise, translate, shorten or expand it.\n" +
        "2. Files go only into the attachments parameter, with the complete original file as Base64 and the matching contentType " +
        "(PDF, Word, Markdown or plain text). Never send a placeholder, a file name, a summary or shortened content instead of a file, " +
        "and never paste file content into the request. The server rejects attachments that look like placeholders, contain Base64 encoded twice, " +
        "or whose content does not match the contentType. If a file is too large to pass as an argument, ask the person to start the order " +
        "in the Atelier web app and add the file there.\n" +
        "3. Atelier may answer with state needs_input and at most three short questions, each with answer options. Ask the person these questions, " +
        "keep the answer options, and tell them that skipping is fine. Answer a question yourself only if the answer is certain from this conversation; " +
        "then set origin to \"client\". Answers from the person get origin \"user\". Never guess an answer. " +
        "If no person is available to answer (for example in an automated task), call answer_order with skip=true.\n" +
        "4. After answer_order, or when submit_order did not ask anything, the order runs in the background and typically takes 1 to 10 minutes. " +
        "Call get_order_status with waitSeconds (up to 50) to wait for a change instead of sleeping or polling in a loop of your own. " +
        "When done is true, call get_order_result.\n" +
        "5. Give the person the text from get_order_result as it is, together with the placeholders to fill in, the assumptions and any open checks. " +
        "If no text was written, say so and do not write one yourself.\n" +
        "6. Do not invent anything: if the person did not ask for something or did not give a file or a fact, do not add it.\n" +
        "submit_request, get_run_status and get_run_result are the older interface and remain available; submit_request never asks questions.";
}
