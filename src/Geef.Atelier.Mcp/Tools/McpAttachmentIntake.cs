using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;
using ModelContextProtocol;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>Hands checked MCP attachments to the source intake and turns unusable files into plain-English errors.</summary>
internal static class McpAttachmentIntake
{
    /// <summary>
    /// Reads every attachment as a source of <paramref name="ownerUsername"/>. When any file is unusable, the files stored by
    /// this call are released again and an <see cref="McpException"/> lists every unusable file with its reason.
    /// </summary>
    public static async Task<IReadOnlyList<SourceReceipt>> IntakeAttachmentsAsync(
        this ISourceIntakeService intake,
        IReadOnlyList<(string FileName, string ContentType, byte[] Content)> attachments,
        string ownerUsername,
        CancellationToken cancellationToken)
    {
        var receipts = new List<SourceReceipt>(attachments.Count);
        try
        {
            foreach (var (fileName, contentType, content) in attachments)
            {
                using var stream = new MemoryStream(content, writable: false);
                var upload = new SourceUpload(fileName, contentType, content.LongLength, stream);
                receipts.Add(await intake.IntakeAsync(upload, ownerUsername, SourceOrigin.McpAttachment, cancellationToken));
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
        throw new McpException($"{string.Join(" ", reasons)} Nothing was started. {McpAttachmentParser.RightWay}");
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
