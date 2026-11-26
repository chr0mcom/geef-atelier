namespace Geef.Atelier.Application.Sources;

/// <summary>Sentences of the upload ticket for assistants and people, the same on the web and in MCP. Plain English, word for word.</summary>
public static class UploadTicketLines
{
    /// <summary>Refusal of a ticket whose file name is empty or longer than 255 characters.</summary>
    public const string FileNameInvalid = "Pass the name of the file in fileName (1 to 255 characters).";

    /// <summary>Refusal of a ticket whose announced size is above the file limit: <c>{0}</c> the limit in MB.</summary>
    public const string TooLargeFormat = "The file is larger than {0} MB. Ask the person for a smaller file or a shorter version.";

    /// <summary>Refusal of a ticket when the person has too many open tickets or prepared too many within 24 hours.</summary>
    public const string TooManyTickets = "Too many open uploads. Use or let the earlier ones expire first.";

    /// <summary>Answer 404 of the token upload: unknown, wrong token, expired, used or foreign look the same.</summary>
    public const string LinkNotValid = "This upload link is not valid any more. Ask for a new one with prepare_attachment.";

    /// <summary>Answer 409 while an upload of the same ticket is running.</summary>
    public const string AlreadyRunning = "An upload for this link is already running.";

    /// <summary>Refusal when the received size differs from the announced one: <c>{0}</c> the received bytes, <c>{1}</c> the announced bytes.</summary>
    public const string SizeMismatchFormat = "The file is {0} bytes, not the {1} bytes announced. Nothing was stored.";

    /// <summary>Refusal when more bytes arrive than were announced, so the real size is not known: <c>{0}</c> the announced bytes.</summary>
    public const string LargerThanAnnouncedFormat = "The file is larger than the {0} bytes announced. Nothing was stored.";

    /// <summary>Next step after a usable token upload: <c>{0}</c> the upload id.</summary>
    public const string AfterUploadFormat = "Now call submit_order with uploadIds: [\"{0}\"].";

    /// <summary>Heading of the open upload page.</summary>
    public const string PageHeading = "Add a file for your AI assistant";

    /// <summary>Line of the open upload page: <c>{0}</c> the file name the assistant gave.</summary>
    public const string PageIntroFormat = "Your AI assistant asked for this file: {0}. Drop it here or choose it. Only you can see it.";

    /// <summary>Line of the upload page after a usable file arrived.</summary>
    public const string PageDone = "Done. The file has arrived. Go back to your AI assistant and tell it the file is uploaded.";

    /// <summary>Line of the upload page of a used ticket.</summary>
    public const string PageAlreadyUsed = "This file has already arrived. You can close this page.";

    /// <summary>Line of the upload page of a ticket whose time ran out.</summary>
    public const string PageExpired = "This link has expired. Ask your AI assistant for a new one.";

    /// <summary>Line of the upload page of a ticket whose attempts were all used by files that were checked and refused.</summary>
    public const string PageFailed = "This link cannot be used any more because the files sent with it could not be used. If you have a different file, ask your AI assistant for a new link.";

    /// <summary>
    /// Refusal of a file that arrived over a ticket but is too long to use completely and has pages: pages can be chosen only in the
    /// web app. <c>{0}</c> the characters of the file, <c>{1}</c> the limit, <c>{2}</c> <see cref="NothingStored"/> or <see cref="NotRemovedYet"/>.
    /// </summary>
    public const string TooLongWithPagesFormat = "This file is too long to use completely ({0} characters, the limit is {1}), and pages cannot be chosen when it is added this way. Use a shorter file, or start the order in the Atelier web app and choose the pages there. {2}";

    /// <summary>
    /// Refusal of a file that arrived over a ticket but is too long to use completely and has no pages: <c>{0}</c> the characters,
    /// <c>{1}</c> the limit, <c>{2}</c> <see cref="NothingStored"/> or <see cref="NotRemovedYet"/>.
    /// </summary>
    public const string TooLongFormat = "This file is too long to use completely ({0} characters, the limit is {1}). Use a shorter version. {2}";

    /// <summary>Last sentence of a refusal for a file too long for the ticket when nothing of it is kept.</summary>
    public const string NothingStored = "Nothing was stored.";

    /// <summary>Last sentence of a refusal for a file too long for the ticket when its copy could not be removed at once.</summary>
    public const string NotRemovedYet = "The file could not be removed right away, so it may still be stored for a while. It is not added to any order this way.";

    /// <summary>Next step for the assistant after the token upload of a file that was checked and refused.</summary>
    public const string AfterRefusal = "The file was refused. Tell the person the reason word for word. Do not upload the same file again; it would be refused again.";

    /// <summary>Next step for the assistant after a token upload that was not checked (busy, broken off, not saved); the attempt was not used.</summary>
    public const string AfterNotChecked = "This was not a problem with the file. Wait a moment, then run curlCommand again.";

    /// <summary>Next step for the assistant after a token upload refused because the person's files that are not used yet are at their limit.</summary>
    public const string AfterQuota = "This was not a problem with the file, but running curlCommand again now will not help: the files of the person that are not used in an order yet are at their limit. Tell the person the reason word for word. It helps to send the order first with the files that have already arrived, or to try again later, when files that were never used have been deleted automatically. If this link has expired by then, call prepare_attachment again.";

    /// <summary>Next step for the assistant after a token upload refused because files cannot be read on the server at the moment.</summary>
    public const string AfterUnavailable = "This was not a problem with the file, but running curlCommand again now will not help: files cannot be read on the server at the moment. Tell the person the reason word for word. Try again later; if this link has expired by then, call prepare_attachment again. If it keeps happening, the person can tell an administrator.";

    /// <summary>Refusal of an order with an unknown or foreign upload id: <c>{0}</c> the id.</summary>
    public const string NotFoundFormat = "Upload {0} was not found. Nothing was started.";

    /// <summary>Refusal of an order whose file has not arrived: <c>{0}</c> the id, <c>{1}</c> the upload page.</summary>
    public const string NotArrivedFormat = "The file for upload {0} has not arrived yet. If the upload command failed, give the person this link: {1}. Call submit_order again when they say it is done. Nothing was started.";

    /// <summary>
    /// Refusal of an order whose ticket is still open but whose last file was checked and refused: <c>{0}</c> the id, <c>{1}</c> the
    /// reason, <c>{2}</c> the upload page.
    /// </summary>
    public const string RefusedSoFarFormat = "No usable file has arrived for upload {0} yet. The last file sent was refused: {1} Tell the person this reason and do not upload the same file again. If they have a different file, upload that one or give them this link: {2}. Nothing was started.";

    /// <summary>Refusal of an order whose file arrived and was deleted in the meantime: <c>{0}</c> the id.</summary>
    public const string NoLongerStoredFormat = "The file for upload {0} is no longer stored. Prepare it again with prepare_attachment. Nothing was started.";

    /// <summary>Refusal of an order whose ticket expired before any usable file arrived and no file was refused: <c>{0}</c> the id.</summary>
    public const string NoUsableFileFormat = "No usable file arrived for upload {0}, and its link is no longer valid. If the person still wants to add the file, call prepare_attachment again. Nothing was started.";

    /// <summary>
    /// Refusal of an order whose ticket ended (expired or out of attempts) and whose last file was checked and refused: <c>{0}</c> the id,
    /// <c>{1}</c> the reason.
    /// </summary>
    public const string NoUsableFileRefusedFormat = "No usable file arrived for upload {0}. The last file sent was refused: {1} Tell the person this reason and do not upload the same file again. Nothing was started.";

    /// <summary>Refusal of an order with upload ids while upload tickets are switched off.</summary>
    public const string NotAvailable = "Uploading files this way is not available. Attach small files in attachments or ask the person to use the Atelier web app. Nothing was started.";

    /// <summary>Refusal of an order with more attachments and upload ids together than allowed: <c>{0}</c> the limit.</summary>
    public const string TooManyFilesFormat = "You can use at most {0} files per order. Nothing was started.";
}
