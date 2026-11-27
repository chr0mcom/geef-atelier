using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>Where the notes of a writing pass come from: the block at the end of the answer, or (fallback C of V1) an extra call.</summary>
internal interface IWriterNotesSource
{
    /// <summary>True when the writer's prompt must ask for the block at the end of the answer (<see cref="DirectWriterPrompts.NotesBody"/>).</summary>
    bool AsksForBlock { get; }

    /// <summary>Splits <paramref name="raw"/>, the complete answer of the writer, into the text for the person and the notes; <paramref name="brief"/> is the briefing of the run.</summary>
    Task<WriterNotesSplit> SplitAsync(string raw, string brief, CancellationToken ct);
}

/// <summary>Reads the notes from the block at the end of the answer (<see cref="WriterNotesFormat.Split"/>, with the request of the briefing as what the request treats).</summary>
internal sealed class TrailerNotesSource : IWriterNotesSource
{
    /// <summary>Shared instance.</summary>
    public static TrailerNotesSource Instance { get; } = new();

    /// <inheritdoc/>
    public bool AsksForBlock => true;

    /// <inheritdoc/>
    public Task<WriterNotesSplit> SplitAsync(string raw, string brief, CancellationToken ct) =>
        Task.FromResult(WriterNotesFormat.Split(raw, BriefingSections.RequestPart(brief ?? string.Empty)));
}

/// <summary>No notes: the whole answer is the text, status <see cref="WriterNotesStatus.Off"/>.</summary>
internal sealed class NoNotesSource : IWriterNotesSource
{
    /// <summary>Shared instance.</summary>
    public static NoNotesSource Instance { get; } = new();

    /// <inheritdoc/>
    public bool AsksForBlock => false;

    /// <inheritdoc/>
    public Task<WriterNotesSplit> SplitAsync(string raw, string brief, CancellationToken ct) =>
        Task.FromResult(new WriterNotesSplit(raw, WriterNotes.Empty, WriterNotesStatus.Off));
}

/// <summary>Chooses the notes source of a run.</summary>
internal static class WriterNotesSources
{
    /// <summary>The notes source the writer of a run gets: the block at the end of the answer when <see cref="DirectOptions.WriterNotes"/> is on, otherwise none.</summary>
    public static IWriterNotesSource For(DirectOptions options) => options.WriterNotes ? TrailerNotesSource.Instance : NoNotesSource.Instance;
}
