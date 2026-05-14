namespace Geef.Atelier.Application.Runs;

public sealed record RunAttachmentInput(
    string Filename,
    string ContentType,
    Stream Content);
