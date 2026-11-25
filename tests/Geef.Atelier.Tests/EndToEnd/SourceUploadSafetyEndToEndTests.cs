using System.Net;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>Hostile uploads and races over the real <c>Program</c> wiring, including the separate extraction process.</summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class SourceUploadSafetyEndToEndTests(PostgresFixture postgres)
{
    [Fact]
    public async Task AHostileWordFileOverTheRealEndpointIsRefusedAndTheHostKeepsAnswering()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var client = await host.CreateSignedInClientAsync("bomb-" + Guid.NewGuid().ToString("N")[..6]);

        using (var bomb = await host.PostSourceAsync(client, "deep.docx", HostileDocuments.DeeplyNestedDocx(20_000)))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bomb.StatusCode);
            var body = await bomb.Content.ReadAsStringAsync();
            Assert.Contains("\"usable\":false", body, StringComparison.Ordinal);
        }

        using (var flate = await host.PostSourceAsync(client, "bomb.pdf", HostileDocuments.FlateBombPdf(300 * 1_048_576)))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, flate.StatusCode);

        using (var anonymous = host.CreateAnonymousClient())
        using (var health = await anonymous.GetAsync("/health"))
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var pdf = SyntheticDocuments.Pdf(2, p => string.Concat(Enumerable.Repeat($"Page {p} of the plan explains the steps. ", 10)));
        using var fine = await host.PostSourceAsync(client, "plan.pdf", pdf);
        Assert.Equal(HttpStatusCode.Created, fine.StatusCode);
    }

    [Fact]
    public async Task AFileDeletedJustBeforeTheOrderIsStoredGivesAPlainAnswer()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.RemoveAll<IOrderRepository>();
            services.AddScoped<IOrderRepository>(sp => new DeleteSourcesFirst(ActivatorUtilities.CreateInstance<OrderRepository>(sp), sp.GetRequiredService<AtelierDbContext>()));
        });
        var user = "late-" + Guid.NewGuid().ToString("N")[..6];
        var client = await host.CreateSignedInClientAsync(user);
        var receipt = await host.UploadSourceAsync(client, "notes.md", System.Text.Encoding.UTF8.GetBytes("Notes for the letter " + Guid.NewGuid()));
        var sourceId = receipt.GetProperty("sourceId").GetGuid();

        var refused = await Assert.ThrowsAsync<OrderSubmissionException>(() => host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SubmitAsync(
            new SubmitOrderRequest("Write a short letter.", user, OrderEntry.Web, [new OrderSourceSelection(sourceId)]))));

        Assert.Equal("One of the files was not found. Please add it again.", refused.Message);
        Assert.False(await host.QueryAsync(db => db.Orders.AnyAsync(o => o.CreatedByUser == user)));
        Assert.False(await host.QueryAsync(db => db.Runs.AnyAsync(r => r.CreatedByUser == user && r.Status == RunStatus.Pending)));
    }

    private sealed class DeleteSourcesFirst(IOrderRepository inner, AtelierDbContext db) : IOrderRepository
    {
        public async Task CreateWithFirstRunAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, RunEntity run, CancellationToken ct = default)
        {
            var ids = sources.Select(s => s.SourceId).ToList();
            await db.Sources.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(ct);
            await inner.CreateWithFirstRunAsync(order, sources, run, ct);
        }

        public Task CreateAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, CancellationToken ct = default) => inner.CreateAsync(order, sources, ct);

        public Task<int> AddRunAsync(Guid orderId, RunEntity run, CancellationToken ct = default) => inner.AddRunAsync(orderId, run, ct);

        public Task<OrderEntity?> GetAsync(Guid orderId, CancellationToken ct = default) => inner.GetAsync(orderId, ct);

        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(Guid orderId, CancellationToken ct = default) => inner.ListRunsAsync(orderId, ct);

        public Task<IReadOnlyList<OrderListRow>> ListAsync(string? ownerUsername, int limit, CancellationToken ct = default) => inner.ListAsync(ownerUsername, limit, ct);

        public Task<IReadOnlyList<RunEventRow>> ListRunEventsAsync(Guid runId, CancellationToken ct = default) => inner.ListRunEventsAsync(runId, ct);

        public Task<bool> TrySetStateAsync(Guid orderId, OrderState expected, OrderState next, CancellationToken ct = default) => inner.TrySetStateAsync(orderId, expected, next, ct);

        public Task<(Guid OrderId, int? Version)?> GetLinkForRunAsync(Guid runId, CancellationToken ct = default) => inner.GetLinkForRunAsync(runId, ct);

        public Task<Guid> LinkLegacyRunAsync(Guid runId, OrderEntry entry, CancellationToken ct = default) => inner.LinkLegacyRunAsync(runId, entry, ct);

        public Task TouchAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default) => inner.TouchAsync(orderId, at, ct);

        public Task<bool> DeleteAsync(Guid orderId, CancellationToken ct = default) => inner.DeleteAsync(orderId, ct);

        public Task<IReadOnlyList<Guid>> ListIntakeCandidatesAsync(DateTimeOffset staleBefore, int maxAttempts, int limit, CancellationToken ct = default) =>
            inner.ListIntakeCandidatesAsync(staleBefore, maxAttempts, limit, ct);

        public Task<bool> TryClaimForIntakeAsync(Guid orderId, DateTimeOffset now, DateTimeOffset staleBefore, int maxAttempts, CancellationToken ct = default) =>
            inner.TryClaimForIntakeAsync(orderId, now, staleBefore, maxAttempts, ct);

        public Task<IReadOnlyList<Guid>> ListStuckIntakesAsync(DateTimeOffset staleBefore, DateTimeOffset unclaimedBefore, int maxAttempts, int limit, CancellationToken ct = default) =>
            inner.ListStuckIntakesAsync(staleBefore, unclaimedBefore, maxAttempts, limit, ct);

        public Task<bool> TrySaveQuestionsAsync(Guid orderId, string clarifyJson, DateTimeOffset at, CancellationToken ct = default) =>
            inner.TrySaveQuestionsAsync(orderId, clarifyJson, at, ct);

        public Task<bool> TryStartWithRunAsync(OrderStart start, CancellationToken ct = default) => inner.TryStartWithRunAsync(start, ct);

        public Task<IReadOnlyList<Guid>> ListUnansweredAsync(DateTimeOffset askedBefore, int limit, CancellationToken ct = default) =>
            inner.ListUnansweredAsync(askedBefore, limit, ct);
    }
}
