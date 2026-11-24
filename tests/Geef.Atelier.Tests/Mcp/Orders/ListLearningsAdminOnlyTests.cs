using Geef.Atelier.Application.Crew.Learning;
using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

public sealed class ListLearningsAdminOnlyTests
{
    [Fact]
    public async Task A_normal_user_gets_a_clear_error_and_the_service_is_not_asked()
    {
        var service = new FakeLearningService();

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            ListLearningsTool.ListLearnings(service, new FakeUser("alice")));

        Assert.Equal(ListLearningsTool.AdminOnlyMessage, ex.Message);
        Assert.Equal(0, service.ListCalls);
    }

    [Fact]
    public async Task An_administrator_sees_the_entries_of_every_user()
    {
        var service = new FakeLearningService();

        var entries = await ListLearningsTool.ListLearnings(service, new FakeUser("root", isAdmin: true));

        Assert.Equal(1, service.ListCalls);
        Assert.Equal(["alice", "bob"], entries.Select(e => e.OwnerUsername));
    }

    private sealed class FakeLearningService : ILearningService
    {
        public int ListCalls { get; private set; }

        public Task<IReadOnlyList<LearningEntry>> ListAsync(LearningStatus? status = null, string? domain = null, CancellationToken ct = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<LearningEntry>>(
            [
                Entry("alice"),
                Entry("bob"),
            ]);
        }

        public Task<LearningEntry?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<LearningEntry?>(null);
        public Task ApproveAsync(Guid id, string requestingUsername, CancellationToken ct = default) => Task.CompletedTask;
        public Task RejectAsync(Guid id, string requestingUsername, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, string requestingUsername, CancellationToken ct = default) => Task.CompletedTask;

        private static LearningEntry Entry(string owner) =>
            new(Guid.NewGuid(), "Keep greetings short.", Guid.NewGuid(), null, "klassik", LearningStatus.Proposed, "{}", owner, DateTimeOffset.UtcNow, null);
    }
}
