using System.Net;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.EndToEnd.Fakes;

namespace Geef.Atelier.Tests.Llm;

public sealed class OpenAiCompatibleClientFailoverSwitchTests
{
    [Theory]
    [InlineData(true, "same-model")]
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
