using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;
using ModelContextProtocol;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>
/// Checks MCP attachments, counts their rejections, hands them to the source intake and turns unusable files into
/// plain-English errors. Counting only observes: the answer to the client never depends on it.
/// </summary>
internal static class McpAttachmentIntake
{
    /// <summary>
    /// Parses <paramref name="attachmentsJson"/> with <see cref="McpAttachmentParser.Parse"/>. A rejection is counted once for
    /// <paramref name="ownerUsername"/> on the way <see cref="RefusalPath.McpInline"/> (without the file name or content) and then
    /// thrown unchanged; without a known user nothing is counted.
    /// </summary>
    public static async Task<IReadOnlyList<(string FileName, string ContentType, byte[] Content)>> ParseAttachmentsAsync(
        this ISourceRefusalLog refusals,
        string? attachmentsJson,
        string? ownerUsername,
        bool tickets = false)
    {
        try
        {
            return McpAttachmentParser.Parse(attachmentsJson, tickets);
        }
        catch (McpAttachmentRejectedException ex)
        {
            if (!string.IsNullOrEmpty(ownerUsername))
                await refusals.RecordQuietlyAsync(ex, ownerUsername);
            throw;
        }
    }

    private static async Task RecordQuietlyAsync(this ISourceRefusalLog refusals, McpAttachmentRejectedException rejection, string ownerUsername)
    {
        try
        {
            await refusals.RecordAsync(rejection.ToRecord(ownerUsername), CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Reads every attachment as a source of <paramref name="ownerUsername"/> on the way <see cref="RefusalPath.McpInline"/>. When any
    /// file is unusable, the files stored by this call are released again and an <see cref="McpException"/> lists every unusable
    /// file with its reason; it ends with <see cref="McpAttachmentParser.RightWayFor"/> of <paramref name="tickets"/>.
    /// </summary>
    public static async Task<IReadOnlyList<SourceReceipt>> IntakeAttachmentsAsync(
        this ISourceIntakeService intake,
        IReadOnlyList<(string FileName, string ContentType, byte[] Content)> attachments,
        string ownerUsername,
        CancellationToken cancellationToken,
        bool tickets = false)
    {
        var receipts = new List<SourceReceipt>(attachments.Count);
        try
        {
            foreach (var (fileName, contentType, content) in attachments)
            {
                using var stream = new MemoryStream(content, writable: false);
                var upload = new SourceUpload(fileName, contentType, content.LongLength, stream);
                receipts.Add(await intake.IntakeAsync(upload, ownerUsername, SourceOrigin.McpAttachment, cancellationToken, RefusalPath.McpInline));
            }
        }
        catch
        {
            await intake.ReleaseNewSourcesAsync(receipts, ownerUsername);
            throw;
        }

        var unusable = receipts.Where(r => !r.Usable || r.SourceId is null).ToList();
        if (unusable.Count == 0)
            return receipts;

        await intake.ReleaseNewSourcesAsync(receipts, ownerUsername);
        var reasons = unusable.Select(r => $"The file '{r.FileName}' cannot be used: {AsSentence(r.Reason)}");
        throw new McpException($"{string.Join(" ", reasons)} Nothing was started. {McpAttachmentParser.RightWayFor(tickets)}");
    }

    private static string AsSentence(string? reason)
    {
        var text = string.IsNullOrWhiteSpace(reason) ? "it could not be read" : reason.Trim();
        return text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') ? text : text + ".";
    }

    /// <summary>Deletes the sources this call stored for the first time; reused duplicates stay. Best effort, never throws.</summary>
    public static async Task ReleaseNewSourcesAsync(this ISourceIntakeService intake, IEnumerable<SourceReceipt> receipts, string ownerUsername)
    {
        foreach (var receipt in receipts.Where(r => r is { Usable: true, Duplicate: false, SourceId: not null }))
        {
            try
            {
                await intake.DeleteAsync(receipt.SourceId!.Value, ownerUsername, CancellationToken.None);
            }
            catch (Exception)
            {
            }
        }
    }
}
