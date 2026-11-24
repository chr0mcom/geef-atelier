using System.Net;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline.Direct;

public sealed class OrderSourcesGroundingProviderTests
{
    [Fact]
    public async Task Careful_mode_passes_the_order_files_in_full_text_to_the_executor()
    {
        var harness = new DirectPipelineHarness();
        var (run, orderId) = harness.CreateRun("Fasse den Bericht sorgfältig zusammen.");
        harness.Sources.Attach(orderId, "bericht.md", SourceKind.Markdown, "Der Hafen meldet 417 Möwen am ersten Morgen.");
        var provider = new OrderSourcesGroundingProvider(harness.Services.GetRequiredService<IServiceScopeFactory>());
        var snapshot = TestCrewSnapshots.Klassik with { GroundingProviders = [SystemCrew.OrderSourcesProfile] };

        var runner = AtelierPipelineFactory.Build(
            snapshot, harness.Resolver, Options.Create(new ConvergenceOptions()), harness.Warnings,
            runId: run.Id, groundingProviderFactory: new GroundingProviderFactory([provider]));
        var result = await runner.RunAsync(run.BriefingText, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(harness.Llm.AnyExecutorPromptContains("Der Hafen meldet 417 Möwen am ersten Morgen."));
        Assert.True(harness.Llm.AnyExecutorPromptContains("<source id=\"S1\" file=\"bericht.md\""));
        var usage = Assert.Single(harness.Sources.Usages);
        Assert.Equal(OrderGroundingStep.WriterTarget, usage.Target);
        Assert.Equal(run.Id, usage.RunId);
        var consultation = Assert.Single(harness.Consultations.Created);
        Assert.Equal("bericht.md", Assert.Single(consultation.Citations).Title);
        Assert.Empty(harness.Warnings.All);
    }

    [Fact]
    public async Task A_run_without_an_order_gets_no_context()
    {
        var harness = new DirectPipelineHarness();
        var provider = new OrderSourcesGroundingProvider(harness.Services.GetRequiredService<IServiceScopeFactory>());

        var result = await provider.EnrichAsync("x", SystemCrew.OrderSourcesProfile, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(string.Empty, result.EnrichedContext);
        Assert.Empty(harness.Sources.Usages);
    }
}

public sealed class OpenAiCompatibleClientFailoverSwitchTests
{
    [Theory]
    [InlineData(true, "off")]
    [InlineData(false, null)]
    public async Task The_failover_header_is_sent_only_when_asked(bool disable, string? expected)
    {
        string? header = "unset";
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            header = request.Headers.TryGetValues(OpenAiCompatibleClient.FailoverHeader, out var values) ? string.Join(",", values) : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}"),
            });
        });
        var client = new OpenAiCompatibleClient(new HttpClient(handler), "http://proxy.invalid/v1/claude", "k");

        await client.CompleteAsync(new LlmRequest { Model = "m", SystemPrompt = "s", UserPrompt = "u", DisableFailover = disable }, CancellationToken.None);

        Assert.Equal(expected, header);
    }

    [Fact]
    public async Task Error_answers_keep_their_status_and_expose_the_error_code()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("{\"error\":{\"message\":\"empty\",\"type\":\"api_error\",\"code\":\"empty_result\"}}"),
        }));
        var client = new OpenAiCompatibleClient(new HttpClient(handler), "http://proxy.invalid/v1/claude", "k");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteAsync(new LlmRequest { Model = "m", SystemPrompt = "s", UserPrompt = "u" }, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Equal(LlmApiError.EmptyResultCode, error.ErrorCode());
    }
}
