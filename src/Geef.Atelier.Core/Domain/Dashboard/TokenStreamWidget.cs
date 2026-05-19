namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Rolling 30-day token consumption by actor type.</summary>
public sealed record TokenStream(
    long TotalTokens,
    long ExecutorTokens,
    long ReviewerTokens,
    long AdvisorTokens,
    long FinalizerTokens,
    IReadOnlyList<TokenStreamDay> Sparkline);

public sealed record TokenStreamDay(DateOnly Date, long Tokens);
