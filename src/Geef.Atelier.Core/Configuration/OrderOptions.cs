using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Core.Configuration;

/// <summary>Behaviour of orders (section <c>Orders</c>).</summary>
public sealed class OrderOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Orders";

    /// <summary>Mode of <c>submit_request</c> calls without a crew template or custom crew.</summary>
    public OrderMode LegacySubmitDefaultMode { get; set; } = OrderMode.Direct;

    /// <summary>Whether new orders go through the intake step first (stage 2). Off in stage 1.</summary>
    public bool IntakeEnabled { get; set; }

    /// <summary>Longest accepted request, in characters.</summary>
    public int RequestMaxChars { get; set; } = 50_000;

    /// <summary>How often a long poll checks the database.</summary>
    public TimeSpan WaitPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Default wait of a status long poll, in seconds.</summary>
    public int DefaultWaitSeconds { get; set; } = 25;

    /// <summary>Longest wait of a status long poll, in seconds.</summary>
    public int MaxWaitSeconds { get; set; } = 50;
}
