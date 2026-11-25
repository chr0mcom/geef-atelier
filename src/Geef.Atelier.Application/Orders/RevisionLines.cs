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

    /// <summary>Shown with the earlier version while the new one is written: <c>{0}</c> is the shown version.</summary>
    public const string StillWritingFormat = "A new version is being written. Version {0} stays here until it is ready.";

    /// <summary>Shown when the newest version failed or was stopped: <c>{0}</c> is that version, <c>{1}</c> the shown one.</summary>
    public const string NotFinishedFormat = "Version {0} could not be finished. This is version {1}.";

    /// <summary>Lead of the open points of an earlier version that the shown version does not repeat: <c>{0}</c> is that version.</summary>
    public const string EarlierPointsLeadFormat = "Noted for version {0} – your change may have settled it:";

    /// <summary>Button under the result that opens the change card.</summary>
    public const string ChangeButton = "Change something";

    /// <summary>Label of the field for the person's own words.</summary>
    public const string ChangeFieldLabel = "What should change?";

    /// <summary>Placeholder of the field.</summary>
    public const string ChangeFieldPlaceholder = "For example: The meeting is on 5 May, not 12 May.";

    /// <summary>Line on the card.</summary>
    public const string OnlyWhatYouAsk = "Only what you ask for is changed. Everything else stays as it is.";

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
