namespace Geef.Atelier.Mcp.Models;

/// <summary>A passage of the text the fact check flagged; never the check's own wording, only quotes.</summary>
/// <param name="Quote">The passage of the text, word for word.</param>
/// <param name="Kind"><c>not_backed</c>, <c>against_instruction</c> or <c>replaced_in_files</c>.</param>
/// <param name="Instruction">For <c>against_instruction</c>: the instruction, word for word; otherwise <c>null</c>. <c>null</c> as well for an instruction from the About me details when the caller is not the owner of the order.</param>
/// <param name="Blocking">True when the passage must be fixed or checked before the text is used.</param>
/// <param name="InstructionFrom">For <c>against_instruction</c>: where the instruction stands – <c>request</c>, <c>answers</c> (the person's answers), <c>about_me</c> (the person's About me details, not this request) or <c>assistant</c> (answers of the AI assistant); otherwise <c>null</c>.</param>
public sealed record OrderUnbackedPassageDto(string Quote, string Kind, string? Instruction, bool Blocking, string? InstructionFrom = null);
