using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp.Models;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>
/// Tool <c>prepare_attachment</c>: prepares the upload of one file with a one-time token for <c>curl</c> and a link to the
/// upload page. Offered only while <c>Uploads:Tickets</c> is <c>On</c> (<see cref="McpToolCatalog.OrderToolsFor"/>). The token goes
/// only into a header of the command, never into an address; the link holds no secret and works only for the signed-in owner.
/// </summary>
[McpServerToolType]
public static partial class PrepareAttachmentTool
{
    /// <summary>Path of the plain copy in <see cref="CopyAndUploadCommand"/>, in double quotes so that the shell replaces <c>$d</c>.</summary>
    public const string CopyPath = "\"$d/upload\"";

    /// <summary>Slot of <see cref="CopyAndUploadCommand"/> for the pattern of the file.</summary>
    public const string PatternSlot = "<pattern>";

    /// <summary>Slot of <see cref="CopyAndUploadCommand"/> for <c>curlCommand</c> with <see cref="CopyPath"/> as its path.</summary>
    public const string CurlSlot = "<curlCommand>";

    /// <summary>
    /// The one shell command for an unusual name: a new empty folder, a copy under a plain name made with a pattern, the upload of the
    /// copy and the removal of the folder. It must run as one command because <c>$d</c> does not survive into a later one.
    /// </summary>
    public const string CopyAndUploadCommand = "d=$(mktemp -d) && cp -- " + PatternSlot + " " + CopyPath + " && " + CurlSlot + "; rm -rf \"$d\"";

    /// <summary>
    /// How to put a local path into <see cref="CurlCommandFormat"/> without letting the shell run any part of the file name: the path
    /// in single quotes, or, for an unusual name, <see cref="CopyAndUploadCommand"/> with a pattern of characters that mean nothing to
    /// the shell. The result of a pattern is never read again as a command.
    /// </summary>
    public const string QuotingRule =
        "Put the path of the file in single quotes in place of '<path to the file>'. " +
        "If the path contains a quote, a line break or another unusual character, do not type the name in a command. " +
        "Instead copy the file into a new empty folder under a plain name and upload the copy, both in ONE shell command, never in two, " +
        "because $d is empty in a later command: " + CopyAndUploadCommand + " " +
        "Replace " + PatternSlot + " by the path of the file written only with letters, digits and / . - _ * (a * in place of every other character), " +
        "for example /home/me/Downloads/Offer*for*.pdf. " +
        "Replace " + CurlSlot + " by curlCommand with " + CopyPath + " (with the double quotes) in place of '<path to the file>'; " +
        "in single quotes $d would not be replaced. " +
        "If cp fails because the pattern matches more than one file, make it more exact. " +
        "If this is not possible, give the person the link instead.";

    public const string ToolDescription =
        "Prepares the upload of one file the person gave you (PDF, Word, Markdown or text, up to 25 MB). Returns a command and a link. " +
        "If you can run shell commands and can read the file, run curlCommand with the path of the file. " + QuotingRule + " " +
        "The answer of curlCommand says what was read. If it has usable false, the file was refused (curl then exits with an error, " +
        "for example for HTTP 422, but still prints the answer): tell the person its reason word for word and do not ask them to upload " +
        "the same file again. " +
        "If you cannot run the command, or it fails without an answer from Atelier (for example with a network error or 403), " +
        "give the person uploadPage word for word and wait until they say the file is uploaded. Then pass uploadId in uploadIds of submit_order. " +
        "Tell the person a file has arrived only when an answer from Atelier shows it: the answer of curlCommand or the sources in the answer of submit_order. " +
        "Never type, summarise or Base64-encode the file yourself.";

    public const string FileNameDescription = "The name of the file as the person gave it, for example offer.pdf. It is only shown; it is not a path.";

    public const string SizeBytesDescription = "Optional size of the file in bytes. An upload of another size is refused.";

    /// <summary>Text of <see cref="PreparedAttachmentDto.Next"/>: <c>{0}</c> is the upload page.</summary>
    public const string NextFormat =
        "Run curlCommand with the path of the file in single quotes; for a path with a quote, a line break or another unusual character, " +
        "copy it under a plain name and upload the copy in ONE shell command as the description of prepare_attachment says, or use the link. " +
        "If the answer has usable false, tell the person its reason word for word and do not upload the same file again. " +
        "If the command is not possible or fails without an answer from Atelier, give the person this link word for word: {0}. " +
        "Then call submit_order with uploadIds.";

    /// <summary>Text of <see cref="PreparedAttachmentDto.CurlCommand"/>: <c>{0}</c> is the token, <c>{1}</c> the full address of the token upload.</summary>
    public const string CurlCommandFormat =
        "curl --fail-with-body -sS --globoff -T '<path to the file>' -H '" + UploadTicketRoutes.TokenHeader + ": {0}' {1}";

    [McpServerTool, Description(ToolDescription)]
    public static async Task<PreparedAttachmentDto> PrepareAttachment(
        IUploadTicketService uploads,
        ICurrentUserService currentUser,
        IOptions<OAuthOptions> oauth,
        [Description(FileNameDescription)] string fileName,
        [Description(SizeBytesDescription)] long? sizeBytes = null,
        CancellationToken cancellationToken = default)
    {
        var username = currentUser.RequireUsername();
        if (!uploads.Enabled)
            throw new McpException(UploadTicketLines.NotAvailable);

        PreparedUpload prepared;
        try
        {
            prepared = await uploads.PrepareAsync(username, fileName, sizeBytes, cancellationToken);
        }
        catch (UploadTicketException ex)
        {
            throw new McpException(ex.Message, ex);
        }

        if (!TokenPattern().IsMatch(prepared.Token))
            throw new InvalidOperationException("The upload token is not base64url and cannot go into the command.");

        var page = PageLink(oauth.Value, prepared.UploadId);
        return new PreparedAttachmentDto(
            prepared.UploadId.ToString("D"),
            string.Format(CultureInfo.InvariantCulture, CurlCommandFormat, prepared.Token, TokenUploadLink(oauth.Value, prepared.UploadId)),
            page,
            prepared.TokenExpiresAt,
            prepared.PageExpiresAt,
            prepared.MaxBytes,
            string.Format(CultureInfo.InvariantCulture, NextFormat, page));
    }

    /// <summary>The upload page of <paramref name="uploadId"/> under the public address <see cref="OAuthOptions.Issuer"/>.</summary>
    internal static string PageLink(OAuthOptions oauth, Guid uploadId) =>
        oauth.Issuer.TrimEnd('/') + string.Format(CultureInfo.InvariantCulture, UploadTicketRoutes.PagePathFormat, uploadId.ToString("D"));

    /// <summary>The address of the token upload of <paramref name="uploadId"/> under the public address <see cref="OAuthOptions.Issuer"/>.</summary>
    internal static string TokenUploadLink(OAuthOptions oauth, Guid uploadId) =>
        oauth.Issuer.TrimEnd('/') + string.Format(CultureInfo.InvariantCulture, UploadTicketRoutes.TokenUploadPathFormat, uploadId.ToString("D"));

    [GeneratedRegex("^[A-Za-z0-9_-]{16,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
