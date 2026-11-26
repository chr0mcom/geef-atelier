namespace Geef.Atelier.Application.Orders;

/// <summary>Sentences about changing a delivered text, the same on the web and in MCP. Plain English.</summary>
public static class RevisionLines
{
    /// <summary>Answer to a second change while a version is still being written; nothing changes.</summary>
    public const string Busy = "A change is already being made.";

    /// <summary>Answer while changing a text is switched off for the person.</summary>
    public const string NotAvailable = "Changing a text is not available yet.";

    /// <summary>Refusal of a change without the person's words and without a quick button.</summary>
    public const string NoChangeGiven = "Please write what should change or choose one of the buttons.";

    /// <summary>Refusal of a change that is too long: <c>{0}</c> its characters, <c>{1}</c> the limit.</summary>
    public const string TooLongFormat = "Your change is too long ({0:N0} characters, the limit is {1:N0}). Please shorten it.";

    /// <summary>Refusal when the order has the most versions: <c>{0}</c> is that number.</summary>
    public const string TooManyVersionsFormat = "This order already has {0} versions, the most one order can have. Please start a new order for further changes.";

    /// <summary>Refusal when the order has no text yet that could be changed.</summary>
    public const string NothingToChange = "There is no text to change yet.";

    /// <summary>Refusal for a stopped order.</summary>
    public const string Stopped = "This order was stopped, so its text cannot be changed.";

    /// <summary>Refusal for an order that does not exist or belongs to someone else.</summary>
    public const string NotFound = "This order was not found.";

    /// <summary>
    /// Refusal of "Resume" on a version that changed an earlier one and already has its own text: resuming it would make the same change a
    /// second time to the text that already has it.
    /// </summary>
    public const string ResumeChangedText = "This version already has a text with this change, so it is not made again. To change the text further, use “Change something” on the order page.";

    /// <summary>
    /// Refusal of "Resume" on a version that changed an earlier one, ended without a text and was followed by a later version with a text:
    /// resuming it would put an older change on top of the newer text.
    /// </summary>
    public const string ResumeLaterVersion = "A later version of this order already has a text, so this older change is not made again. To change the text, use “Change something” on the order page.";

    /// <summary>Shown with the earlier version while the new one is written: <c>{0}</c> is the shown version.</summary>
    public const string StillWritingFormat = "A new version is being written. Version {0} stays here until it is ready.";

    /// <summary>Shown when the newest version failed or was stopped: <c>{0}</c> is that version, <c>{1}</c> the shown one.</summary>
    public const string NotFinishedFormat = "Version {0} could not be finished. This is version {1}.";

    /// <summary>Lead of the open points of an earlier version that the shown version does not repeat: <c>{0}</c> is that version.</summary>
    public const string EarlierPointsLeadFormat = "Noted for version {0} – your change may have settled it:";

    /// <summary>
    /// Lead of the fixed sentences of Atelier about an earlier version (what of it could not be shown or taken over): <c>{0}</c> is that
    /// version. Neutral, because a change does not settle them.
    /// </summary>
    public const string EarlierNotesLeadFormat = "About version {0}:";

    /// <summary>
    /// Sentence with a passage against an instruction from before the latest change (the request, the answers, the About me details or an
    /// earlier change): it never blocks, because the latest change may have replaced the instruction.
    /// </summary>
    public const string BeforeLastChangeNote = "This instruction applied before your latest change; your change may have replaced it.";

    /// <summary>Words before an assumption the writer made for an earlier version, listed with the points noted for that version.</summary>
    public const string EarlierAssumptionLead = "Assumed: ";

    /// <summary>
    /// Lead of the assumptions of the clarifier when the shown version changed an earlier one: <c>{0}</c> is the version they were noted
    /// for, before the person asked for changes.
    /// </summary>
    public const string AssumptionsNotedLeadFormat = "Noted for version {0} – you had not said this then, so it was assumed. Your change may have settled it:";

    /// <summary>Like <see cref="AssumptionsNotedLeadFormat"/> for the facts not given: <c>{0}</c> is the version.</summary>
    public const string NotGivenNotedLeadFormat = "Noted for version {0} – you had not given these facts then, so they were left open instead of assumed. Your change may have given them:";

    /// <summary>Like <see cref="NotGivenNotedLeadFormat"/> when the text was written with passages looked up on the web.</summary>
    public const string NotGivenNotedLeadLookedUpFormat =
        "Noted for version {0} – you had not given these facts then, so they were not assumed. Where the pages looked up cover one, the text may use it. Your change may have given them:";

    /// <summary>
    /// Lead of the assumptions the writer of a changed version noted: they were written while making the change, so they never stand under
    /// the sentence that the person did not say them.
    /// </summary>
    public const string ChangeNotesLead = "The writer noted while making your change:";

    /// <summary>
    /// The line that folds the notes about earlier versions (<see cref="EarlierPointsLeadFormat"/>, <see cref="AssumptionsNotedLeadFormat"/>,
    /// <see cref="NotGivenNotedLeadFormat"/>): <paramref name="count"/> notes, named with their version when all of them belong to one. The fixed
    /// sentences under <see cref="EarlierNotesLeadFormat"/> (what was not shown or not taken over) are never folded and never counted here.
    /// </summary>
    public static string EarlierNotesSummary(int count, IEnumerable<int> versions)
    {
        var distinct = versions.Distinct().ToList();
        var about = distinct is [var only] ? $"version {only}" : count == 1 ? "an earlier version" : "earlier versions";
        return count == 1 ? $"1 note about {about}" : $"{count} notes about {about}";
    }

    /// <summary>Link above the text to the list of versions.</summary>
    public const string SeeVersions = "See all versions";

    /// <summary>
    /// Like <see cref="SeeVersions"/> when the list offers “Show changes” for the shown version, that is when an earlier version has a text
    /// to compare with; otherwise (for example after a first version that ended without a text) the link stays <see cref="SeeVersions"/>.
    /// </summary>
    public const string SeeChangesAndVersions = "See what changed and all versions";

    /// <summary>Second line of the banner after a change could not be finished, when the shown version still has open points.</summary>
    public const string EarlierStillToCheck = "This version still needs your attention before you use it – see “Open points”.";

    /// <summary>Button under the result that opens the change card.</summary>
    public const string ChangeButton = "Change something";

    /// <summary>Label of the field for the person's own words.</summary>
    public const string ChangeFieldLabel = "What should change?";

    /// <summary>Placeholder of the field.</summary>
    public const string ChangeFieldPlaceholder = "For example: The meeting is on 5 May, not 12 May.";

    /// <summary>Line on the card: it promises only what is done (the writer is asked for this change alone), not that nothing else changes.</summary>
    public const string OnlyWhatYouAsk = "The writer is asked to make only this change. “Show changes” then shows what was changed.";

    /// <summary>Button that starts the change.</summary>
    public const string MakeChangeButton = "Make the change";

    /// <summary>Button that closes the card without a change.</summary>
    public const string CancelButton = "Cancel";

    /// <summary>Heading of the list of versions.</summary>
    public const string VersionsHeading = "Versions";

    /// <summary>One version in the list: <c>{0}</c> the version, <c>{1}</c> the date, <c>{2}</c> the number of words.</summary>
    public const string VersionLineFormat = "Version {0} · {1} · {2}";

    /// <summary>What the person asked for a version after the first: <c>{0}</c> their words or the names of the quick buttons.</summary>
    public const string YouAskedFormat = "You asked: “{0}”";

    /// <summary>Button that shows the changes to the version before.</summary>
    public const string ShowChanges = "Show changes";

    /// <summary>Button that hides them again.</summary>
    public const string HideChanges = "Hide changes";

    /// <summary>Mark of a paragraph that is new.</summary>
    public const string Added = "Added";

    /// <summary>Mark of a paragraph that is gone.</summary>
    public const string Removed = "Removed";

    /// <summary>Button that downloads a version.</summary>
    public const string Download = "Download";
}
