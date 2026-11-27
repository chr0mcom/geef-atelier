using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence.Intake;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Profiles;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Intake;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Pipeline.Direct;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Geef.Atelier.Tests.Intake;

public sealed class IntakeRegistrationTests
{
    private sealed class NoProfiles : IUserProfileRepository
    {
        public Task<UserProfileEntity?> GetAsync(string username, CancellationToken ct = default) => Task.FromResult<UserProfileEntity?>(null);
        public Task UpsertAsync(UserProfileEntity profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<UserProfileFillResult> FillEmptyAsync(string username, UserProfileFill fill, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string username, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoModels : IModelAssignmentService
    {
        public Task<ModelAssignment> AssignAsync(ModelSlot slot, ModelDomain domain, ModelCandidate? executor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModelAssignment>> AssignReviewersAsync(IReadOnlyList<ReviewerAssignmentRequest> reviewers, ModelDomain crewDomain, ModelCandidate executor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, ICollection<ModelAssignmentWarning> warnings, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoResolver : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens) => throw new NotSupportedException();
        public bool SupportsStructuredOutputs(string providerName) => true;
    }

    [Fact]
    public void The_intake_resolves_with_its_dependencies_and_registers_the_worker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<IntakeOptions>(_ => { });
        services.Configure<OrderOptions>(_ => { });
        services.AddScoped<IOrderRepository, IntakeOrders>();
        services.AddScoped<ISourceRepository, InMemorySourceRepository>();
        services.AddScoped<IIntakeShadowRepository, IntakeShadows>();
        services.AddScoped<IUserProfileRepository, NoProfiles>();
        services.AddScoped<IOrderRunStarter, RecordingRunStarter>();
        services.AddSingleton<IOrderNotifier, RecordingOrderNotifier>();
        services.AddSingleton<IModelAssignmentService, NoModels>();
        services.AddSingleton<ILlmClientResolver, NoResolver>();

        services.AddOrderIntakeServices();
        services.AddOrderIntake();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<OrderIntakeService>(scope.ServiceProvider.GetRequiredService<IOrderIntakeService>());
        Assert.IsType<LlmClarifier>(scope.ServiceProvider.GetRequiredService<IClarifier>());
        Assert.IsType<ProfileMemoryResolution>(scope.ServiceProvider.GetRequiredService<IMemoryResolution>());
        Assert.Same(provider.GetRequiredService<IOrderIntakeSignal>(), provider.GetRequiredService<IOrderIntakeSignal>());
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is OrderIntakeWorker);
    }
}
