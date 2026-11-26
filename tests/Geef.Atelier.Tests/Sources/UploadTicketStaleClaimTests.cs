using System.Text;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Sources;

/// <summary>
/// An upload that is still running when the cleanup releases its ticket (after <c>UploadTimeout</c> plus a minute) must not touch a
/// newer claim of the same ticket: it can neither complete it with its own file nor set it back to open. Each claim is identified by
/// the moment it started (<c>ReceivingSince</c>).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class UploadTicketStaleClaimTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_late_upload_after_the_cleanup_does_not_complete_the_newer_claim()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner);
        var late = new HeldUploadStream(Encoding.UTF8.GetBytes("# Late\nThe first copy " + Guid.NewGuid()));
        var newer = new HeldUploadStream(Encoding.UTF8.GetBytes("# Newer\nThe second copy " + Guid.NewGuid()));

        var lateRun = ReceiveAsync(host, prepared, late);
        await late.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await ReleaseHangingAsync(host);
        Assert.Equal(UploadTicketState.Open, (await TicketAsync(prepared.UploadId)).State);

        var newerRun = ReceiveAsync(host, prepared, newer);
        await newer.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        late.Release.SetResult();
        var lateOutcome = await lateRun;

        Assert.Equal((404, UploadTicketLines.LinkNotValid), (lateOutcome.StatusCode, lateOutcome.Reason));
        Assert.Equal(UploadTicketState.Receiving, (await TicketAsync(prepared.UploadId)).State);

        newer.Release.SetResult();
        var newerOutcome = await newerRun;
        Assert.Equal(201, newerOutcome.StatusCode);
        var ticket = await TicketAsync(prepared.UploadId);
        Assert.Equal((UploadTicketState.Used, newerOutcome.Receipt!.SourceId), (ticket.State, ticket.SourceId));
    }

    [Fact]
    public async Task A_late_refused_upload_after_the_cleanup_does_not_reopen_the_newer_claim()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner);
        var late = new HeldUploadStream([]);
        var newer = new HeldUploadStream(Encoding.UTF8.GetBytes("# Newer\nThe second copy " + Guid.NewGuid()));

        var lateRun = ReceiveAsync(host, prepared, late);
        await late.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await ReleaseHangingAsync(host);

        var newerRun = ReceiveAsync(host, prepared, newer);
        await newer.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        late.Release.SetResult();
        var lateOutcome = await lateRun;

        Assert.Equal(422, lateOutcome.StatusCode);
        var during = await TicketAsync(prepared.UploadId);
        Assert.Equal((UploadTicketState.Receiving, 2), (during.State, during.Attempts));

        newer.Release.SetResult();
        Assert.Equal(201, (await newerRun).StatusCode);
        Assert.Equal(UploadTicketState.Used, (await TicketAsync(prepared.UploadId)).State);
    }

    private async Task ReleaseHangingAsync(UploadTestHost host)
    {
        var cleanup = host.Services.GetServices<IHostedService>().OfType<SourceCleanupBackgroundService>().Single();
        host.Clock.Now += TimeSpan.FromMinutes(6) + TimeSpan.FromSeconds(1);
        var (reset, _, _) = await cleanup.CleanUploadsAsync(CancellationToken.None);
        Assert.True(reset >= 1);
    }

    private static Task<PreparedUpload> PrepareAsync(UploadTestHost host, string owner) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().PrepareAsync(owner, "notes.md", null));

    private static Task<UploadOutcome> ReceiveAsync(UploadTestHost host, PreparedUpload prepared, Stream body) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ReceiveByTokenAsync(prepared.UploadId, prepared.Token, body));

    private async Task<UploadTicketEntity> TicketAsync(Guid id)
    {
        await using var db = fixture.NewContext();
        return await db.UploadTickets.AsNoTracking().SingleAsync(t => t.Id == id);
    }
}
