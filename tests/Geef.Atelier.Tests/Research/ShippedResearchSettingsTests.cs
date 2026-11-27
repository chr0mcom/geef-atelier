using Geef.Atelier.Application.Research;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Research;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Research;

/// <summary>
/// Holds the research settings that ship in <c>src/Geef.Atelier.Web/appsettings.json</c> (contract stage 4, section 10, and the addendum
/// to D-074): research is on for everyone since the measurement R3′ passed, with the daily limits unchanged; the code default stays off,
/// overrides only come from the environment, and no audit directory is set. No versioned compose file switches research or names a
/// user; <c>Research__Mode=Off</c> is the way back for everyone, and a variable of the form <c>Research__ModeOverrides__&lt;user&gt;=Off</c>
/// set on the host only turns it off for exactly that user.
/// It also holds the part of <c>Program.cs</c> the passage picker relies on: the shared LLM client retries only by
/// <c>LlmHttpRetry.MayRetry</c>, which never repeats a request with a gateway time limit.
/// </summary>
public sealed class ShippedResearchSettingsTests
{
    [Fact]
    public void Research_ships_on_for_everyone_with_the_contract_limits()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();

        var research = configuration.GetSection(ResearchOptions.SectionName).Get<ResearchOptions>() ?? new ResearchOptions();

        Assert.Equal("On", configuration["Research:Mode"]);
        Assert.Equal(ResearchMode.On, research.Mode);
        Assert.Equal(ResearchMode.Off, new ResearchOptions().Mode);
        Assert.Empty(research.ModeOverrides);
        Assert.Equal((4, 3, 6_000, 12, 50, 20), (research.MaxSearches, research.ResultsPerSearch, research.RawCharsPerPage, research.MaxEntries, research.DailyOrderCap, research.DailyOrderCapPerUser));
        Assert.Equal((TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60)), (research.SearchTimeout, research.CondenseTimeout));
        Assert.True(string.IsNullOrEmpty(research.AuditDirectory));
    }

    [Fact]
    public void The_way_back_switches_research_off_for_everyone_from_the_environment()
    {
        var prefix = "WAYBACK" + Guid.NewGuid().ToString("N") + "_";
        var variable = prefix + "Research__Mode";
        Environment.SetEnvironmentVariable(variable, "Off");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
                .AddEnvironmentVariables(prefix)
                .Build();

            Assert.Equal(ResearchMode.Off, (configuration.GetSection(ResearchOptions.SectionName).Get<ResearchOptions>() ?? new ResearchOptions()).Mode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void The_shared_llm_client_retries_only_by_the_rule_that_spares_time_limited_requests()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "Program.cs"));

        Assert.Contains("LlmHttpRetry.MayRetry(args.Outcome.Result)", program, StringComparison.Ordinal);
        Assert.Equal(1, Count(program, "AddStandardResilienceHandler"));
        Assert.Equal(1, Count(program, "builder.Services.AddResearch(builder.Configuration);"));
        Assert.DoesNotContain("ConfigureHttpClientDefaults", program, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docker-compose.yml")]
    [InlineData("docker-compose.dev.yml")]
    public void No_versioned_compose_file_switches_research_or_names_a_user(string file)
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot(), file));

        Assert.DoesNotContain("Research__", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Research:", compose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_override_from_the_environment_turns_research_off_for_that_user_only()
    {
        var user = "measure-" + Guid.NewGuid().ToString("N");
        var variable = $"Research__ModeOverrides__{user}";
        Environment.SetEnvironmentVariable(variable, "Off");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();
            var search = new FakeSearch(_ => new([], null, 1));
            var orders = new ResearchOrders();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IOrderRepository>(orders);
            services.AddSingleton<ITavilySearchClient>(search);
            services.AddSingleton<IDossierCondenser>(new FakeCondenser((_, _) => throw new InvalidOperationException("no pages, no call")));
            services.AddResearch(configuration);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            var options = provider.GetRequiredService<IOptions<ResearchOptions>>().Value;
            Assert.Equal(ResearchMode.On, options.Mode);
            Assert.Equal(ResearchMode.Off, Assert.Single(options.ModeOverrides, o => o.Key.Equals(user, StringComparison.OrdinalIgnoreCase)).Value);
            Assert.True(string.IsNullOrEmpty(options.AuditDirectory));

            await using var scope = provider.CreateAsyncScope();
            var research = scope.ServiceProvider.GetRequiredService<IOrderResearchService>();
            var excepted = await research.EnsureAsync(orders.Put(Order(user)), 1, false, false, null, CancellationToken.None);
            var other = await research.EnsureAsync(orders.Put(Order("someone-" + Guid.NewGuid().ToString("N"))), 1, false, false, null, CancellationToken.None);

            Assert.Null(excepted);
            Assert.Equal(ResearchStatus.NothingFound, other!.Status);
            Assert.Equal(["Mindestlohn 2027 Höhe"], search.Queries);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static OrderEntity Order(string user) => new()
    {
        Id = Guid.NewGuid(), CreatedByUser = user, RequestText = "Schreib einen Aushang zum neuen Mindestlohn.",
        State = OrderState.Ready, Entry = OrderEntry.Web, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        ClarifyJson = IntakeRecordJson.Serialize(IntakeRecord.Fallback(IntakeRecordStatus.Ok, null, 1, "m", BriefMode.AppendAnswers) with { ResearchTopics = ["Mindestlohn 2027 Höhe"], ResearchAllowed = true }),
    };

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
