namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Cost breakdown Sankey: by provider and actor role (30-day).</summary>
public sealed record CostForge(IReadOnlyList<CostFlow> Flows, decimal TotalEur);

/// <summary>One cost flow segment: from a provider to an actor role.</summary>
public sealed record CostFlow(
    string Provider,
    string ActorRole,   // "Executor" | "Reviewer" | "Advisor" | "Finalizer"
    decimal CostEur,
    double Share);
