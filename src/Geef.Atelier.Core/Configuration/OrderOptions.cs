using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Core.Configuration;

/// <summary>Whether a delivered text can be changed by a new version of the same order (<c>Orders:Revise</c>).</summary>
public enum ReviseMode { Off = 0, On = 1 }

/// <summary>Behaviour of orders (section <c>Orders</c>).</summary>
public sealed class OrderOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Orders";

    /// <summary>Mode of <c>submit_request</c> calls without a crew template or custom crew.</summary>
    public OrderMode LegacySubmitDefaultMode { get; set; } = OrderMode.Direct;

    /// <summary>Whether orders from the start page and submit_order are clarified first (stage 2). Replaces the unused IntakeEnabled.</summary>
    public IntakeMode IntakeMode { get; set; } = IntakeMode.Shadow;

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
}
