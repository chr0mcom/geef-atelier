namespace Geef.Atelier.Core.Domain.Providers;

/// <summary>Result of a connectivity test against a provider endpoint.</summary>
/// <param name="Success">True when the provider responded successfully.</param>
/// <param name="LatencyMs">Round-trip time in milliseconds.</param>
/// <param name="ErrorMessage">Failure description when <see cref="Success"/> is false, otherwise null.</param>
/// <param name="ResponseSample">Optional excerpt from the provider response for diagnostics.</param>
public sealed record ConnectionTestResult(
    bool Success,
    long LatencyMs,
    string? ErrorMessage,
    string? ResponseSample
);
