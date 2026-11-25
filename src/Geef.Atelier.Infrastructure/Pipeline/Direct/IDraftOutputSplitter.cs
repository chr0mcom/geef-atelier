namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>The writer's answer split into the text for the person and an optional trailer for the system.</summary>
internal sealed record DraftOutput(string Text, string? Trailer);

/// <summary>
/// Splits the writer's answer. Stage 1 delivers the text only (<see cref="IdentityDraftOutputSplitter"/>); since stage 3 the
/// writer gets an <see cref="IWriterNotesSource"/>, and <see cref="TrailerNotesSource"/> removes the notes block at the end of the
/// answer from the text.
/// </summary>
internal interface IDraftOutputSplitter
{
    /// <summary>Splits <paramref name="raw"/>, the complete answer of the writer.</summary>
    DraftOutput Split(string raw);
}

/// <summary>Keeps the whole answer as the text.</summary>
internal sealed class IdentityDraftOutputSplitter : IDraftOutputSplitter
{
    /// <summary>Shared instance.</summary>
    public static IdentityDraftOutputSplitter Instance { get; } = new();

    /// <inheritdoc/>
    public DraftOutput Split(string raw) => new(raw, null);
}
