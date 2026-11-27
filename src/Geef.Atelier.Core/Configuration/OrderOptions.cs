using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Core.Configuration;

/// <summary>Whether a delivered text can be changed by a new version of the same order (<c>Orders:Revise</c>).</summary>
public enum ReviseMode { Off = 0, On = 1 }

/// <summary>
/// Whether a result offers to save a missing sender, signature, form of address or language under About me (<c>Orders:TellOnce</c>).
/// <see cref="On"/> shows the offer with the sentence that Atelier will not leave it open again; <see cref="WithoutPromise"/> shows it
/// without that sentence; <see cref="Off"/> shows nothing of it, and AI assistants cannot save About me details.
/// </summary>
public enum TellOnceMode { Off = 0, On = 1, WithoutPromise = 2 }

/// <summary>
/// Whether "Extra careful" is offered (<c>Orders:CarefulOutline</c>, decision logs D-087 and D-091). <see cref="On"/> offers it: the order is
/// written on the direct path with an outline first (one call plans sections with word budgets, one call writes along them), followed by
/// the same checks and the same report as the direct path and a check of the explicit requirements of the request. <see cref="Off"/>
/// offers no "Extra careful" at all: the web page shows no such option and <c>submit_order</c> refuses it with a plain sentence, so a
/// careful order never runs as something else.
/// </summary>
public enum CarefulOutlineMode { Off = 0, On = 1 }

/// <summary>Behaviour of orders (section <c>Orders</c>).</summary>
public sealed class OrderOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Orders";

    /// <summary>Whether orders from the start page and submit_order are clarified first (stage 2). Replaces the unused IntakeEnabled.</summary>
    public IntakeMode IntakeMode { get; set; } = IntakeMode.Shadow;

    /// <summary>Whether "Extra careful" is offered (D-087, D-091); <see cref="CarefulOutlineMode.Off"/> until a measurement shows that the outline is better.</summary>
    public CarefulOutlineMode CarefulOutline { get; set; } = CarefulOutlineMode.Off;

    /// <summary>Mode per user name, overriding <see cref="CarefulOutline"/>.</summary>
    public Dictionary<string, CarefulOutlineMode> CarefulOutlineOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Longest accepted request, in characters.</summary>
    public int RequestMaxChars { get; set; } = 50_000;

    /// <summary>How often a long poll checks the database.</summary>
    public TimeSpan WaitPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Default wait of a status long poll, in seconds.</summary>
    public int DefaultWaitSeconds { get; set; } = 25;

    /// <summary>Longest wait of a status long poll, in seconds.</summary>
    public int MaxWaitSeconds { get; set; } = 50;

    /// <summary>Whether a delivered text can be changed ("Change something", <c>revise_order</c>).</summary>
    public ReviseMode Revise { get; set; } = ReviseMode.Off;

    /// <summary>Mode per user name, overriding <see cref="Revise"/>.</summary>
    public Dictionary<string, ReviseMode> ReviseOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Longest change the person may describe, in characters.</summary>
    public int RevisionHintMaxChars { get; set; } = 2_000;

    /// <summary>Most versions of one order; no change is started beyond it.</summary>
    public int MaxVersionsPerOrder { get; set; } = 10;

    /// <summary>
    /// Whether a result offers to save what About me lacks and shows what the writer got from About me. The code default is
    /// <see cref="TellOnceMode.WithoutPromise"/>; <c>appsettings.json</c> ships <see cref="TellOnceMode.On"/> since the acceptance on the
    /// real system showed that the writer uses what was saved (addendum to D-083).
    /// </summary>
    public TellOnceMode TellOnce { get; set; } = TellOnceMode.WithoutPromise;
}

/// <summary>Helpers on <see cref="OrderOptions"/>.</summary>
public static class OrderOptionsExtensions
{
    /// <summary>
    /// Whether changing a text is on for orders of <paramref name="owner"/>: the entry of <see cref="OrderOptions.ReviseOverrides"/> for
    /// that user name, otherwise <see cref="OrderOptions.Revise"/>.
    /// </summary>
    public static ReviseMode ReviseModeFor(this OrderOptions options, string? owner)
    {
        ArgumentNullException.ThrowIfNull(options);
        return owner is not null && options.ReviseOverrides is { } overrides && overrides.TryGetValue(owner, out var mode) ? mode : options.Revise;
    }

    /// <summary>
    /// Whether "Extra careful" is offered to <paramref name="owner"/>: the entry of <see cref="OrderOptions.CarefulOutlineOverrides"/> for
    /// that user name, otherwise <see cref="OrderOptions.CarefulOutline"/>.
    /// </summary>
    public static CarefulOutlineMode CarefulOutlineFor(this OrderOptions options, string? owner)
    {
        ArgumentNullException.ThrowIfNull(options);
        return owner is not null && options.CarefulOutlineOverrides is { } overrides && overrides.TryGetValue(owner, out var mode) ? mode : options.CarefulOutline;
    }

    /// <summary>True when "Extra careful" is offered to <paramref name="owner"/> (<see cref="CarefulOutlineFor"/> is <see cref="CarefulOutlineMode.On"/>).</summary>
    public static bool OffersCareful(this OrderOptions options, string? owner) => options.CarefulOutlineFor(owner) == CarefulOutlineMode.On;

    /// <summary>
    /// True when "Extra careful" is offered to anybody: <see cref="OrderOptions.CarefulOutline"/> is on or one entry of
    /// <see cref="OrderOptions.CarefulOutlineOverrides"/> is. The tool list of the MCP server is the same for everyone, so it offers the
    /// option exactly then.
    /// </summary>
    public static bool OffersCarefulToAnyone(this OrderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.CarefulOutline == CarefulOutlineMode.On
               || (options.CarefulOutlineOverrides?.Values.Any(m => m == CarefulOutlineMode.On) ?? false);
    }
}
