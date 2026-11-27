using System.Security.Claims;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Web.Display;

/// <summary>How the explanation page may describe the fact check to one viewer.</summary>
public enum FactCheckOffer
{
    /// <summary>The person sees no fact check: the switch is <c>Off</c>, or <c>Shadow</c> (checks without showing anything).</summary>
    NotShown = 0,

    /// <summary>The fact check points out passages and never changes the text (<c>Report</c>, and every mode this page does not know).</summary>
    ReportsOnly = 1,

    /// <summary>The fact check points out passages and some of them may be fixed once (<c>On</c>).</summary>
    MayFixOnce = 2,
}

/// <summary>The settings the explanation page reads; one value per section, as the application has them.</summary>
public sealed record HowItWorksSettings(
    OrderOptions Orders,
    IntakeOptions Intake,
    ResearchOptions Research,
    DirectOptions Direct,
    UploadOptions Uploads,
    UiOptions Ui,
    McpOptions Mcp,
    SourceOptions Sources);

/// <summary>
/// What the explanation page may call available for one viewer. Every value follows the same rule the application uses when the
/// person places or changes an order, so the page never offers more than the person gets.
/// </summary>
/// <param name="ForSignedInPerson">True when the values are those of a signed-in person (their own exceptions included); false for everyone.</param>
/// <param name="Questions">Whether Atelier may ask questions before it starts (<c>Orders:IntakeMode = On</c>).</param>
/// <param name="MaxQuestions">Most questions per order.</param>
/// <param name="ChangeText">Whether a finished text can be changed ("Change something", <c>revise_order</c>).</param>
/// <param name="MaxVersions">Most versions of one order.</param>
/// <param name="WebResearch">
/// Whether the first version of an order on the standard way may look things up on the web (research on and questions on, because the
/// topics come from reading the request). "Extra careful" and teams chosen in the workshop are not covered by this switch.
/// </param>
/// <param name="FactCheck">How the fact check may be described.</param>
/// <param name="SumsInFiles">Whether totals in the files are recalculated without an AI model.</param>
/// <param name="UploadThroughAssistant">Whether an AI assistant can upload files with an upload ticket.</param>
/// <param name="MaxFiles">Most files per order.</param>
/// <param name="MaxFileBytes">Largest file.</param>
/// <param name="FullTextChars">Up to this many characters the writer gets the files completely.</param>
/// <param name="MapChars">Up to this many characters the writer gets the files with a table of contents.</param>
/// <param name="TellOnce">Whether a result offers to save a missing name, sign-off, form of address or language under About me (<c>Orders:TellOnce</c>).</param>
public sealed record HowItWorksFeatures(
    bool ForSignedInPerson,
    bool Questions,
    int MaxQuestions,
    bool ChangeText,
    int MaxVersions,
    bool WebResearch,
    FactCheckOffer FactCheck,
    bool SumsInFiles,
    bool UploadThroughAssistant,
    int MaxFiles,
    long MaxFileBytes,
    int FullTextChars,
    int MapChars,
    TellOnceMode TellOnce = TellOnceMode.Off);

/// <summary>Derives <see cref="HowItWorksFeatures"/> from the settings and the viewer.</summary>
public static class HowItWorksFeatureExtensions
{
    /// <summary>
    /// The features for <paramref name="viewer"/>: for a signed-in person with their exceptions per user name (changing a text,
    /// research, fact check); for nobody signed in the values for everyone.
    /// </summary>
    public static HowItWorksFeatures FeaturesFor(this HowItWorksSettings settings, ClaimsPrincipal? viewer)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var user = viewer?.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : null;
        var questions = settings.Orders.IntakeMode == IntakeMode.On;
        return new HowItWorksFeatures(
            ForSignedInPerson: user is not null,
            Questions: questions,
            MaxQuestions: Math.Max(1, settings.Intake.MaxQuestions),
            ChangeText: settings.Orders.ReviseModeFor(user) == ReviseMode.On,
            MaxVersions: settings.Orders.MaxVersionsPerOrder,
            WebResearch: questions && settings.Research.ModeFor(user) == ResearchMode.On,
            FactCheck: settings.Direct.FactCheckModeFor(user).ToOffer(),
            SumsInFiles: settings.Direct.SourceArithmetic,
            UploadThroughAssistant: settings.Uploads.Tickets == UploadTicketsMode.On,
            MaxFiles: settings.Sources.MaxFilesPerOrder,
            MaxFileBytes: settings.Sources.MaxFileBytes,
            FullTextChars: settings.Sources.FullTextMaxChars,
            MapChars: settings.Sources.MapMaxChars,
            TellOnce: settings.Orders.TellOnce);
    }

    /// <summary>
    /// The description of <paramref name="mode"/>: nothing for <c>Off</c> and <c>Shadow</c>, "may fix once" only for <c>On</c>, and
    /// "points out only" for <c>Report</c> and every value this page does not know, so a new mode is never described as more.
    /// </summary>
    public static FactCheckOffer ToOffer(this FactCheckMode mode) => mode switch
    {
        FactCheckMode.Off or FactCheckMode.Shadow => FactCheckOffer.NotShown,
        FactCheckMode.On => FactCheckOffer.MayFixOnce,
        _ => FactCheckOffer.ReportsOnly,
    };

    /// <summary>"On" or "Off", as the settings table of the page shows a switch.</summary>
    public static string OnOff(this bool on) => on ? "On" : "Off";
}
