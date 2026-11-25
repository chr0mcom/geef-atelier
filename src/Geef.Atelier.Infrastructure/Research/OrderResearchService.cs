using Geef.Atelier.Application.Research;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Persistence.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Research;

#pragma warning disable CS9113

/// <summary>
/// The research of an order (contract stage 4, 4.1–4.8): selects the topics, searches, lets the model pick passages, keeps only those
/// found word for word and stores the result once per order. Every failure ends as a status with <see cref="OrderResearch.ErrorCode"/>.
/// </summary>
internal sealed class OrderResearchService(
    IOrderRepository orders,
    ITavilySearchClient search,
    IDossierCondenser condenser,
    IOptions<ResearchOptions> options,
    ILogger<OrderResearchService> logger,
    TimeProvider time) : IOrderResearchService
{
    public const string NoKeyCode = "no_key";
    public const string SearchFailedCode = "search_failed";
    public const string CondenseFailedCode = "condense_failed";
    public const string TimeoutCode = "timeout";

    public Task<OrderResearch?> EnsureAsync(OrderEntity order, int? orderVersion, bool revises, IReadOnlyList<string> fileTexts, string? profileText, CancellationToken ct) =>
        throw new NotImplementedException();
}
