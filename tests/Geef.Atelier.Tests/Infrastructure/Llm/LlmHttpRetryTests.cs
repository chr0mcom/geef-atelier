using System.Net;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Infrastructure.Llm;

/// <summary>
/// <see cref="LlmHttpRetry"/>: the shared "llm" <c>HttpClient</c> leaves 502 and 503 answers to requests without
/// failover to the caller and keeps retrying everything else.
/// </summary>
public sealed class LlmHttpRetryTests
{
    private static HttpResponseMessage Answer(HttpStatusCode status, bool failoverOff)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://cli-proxy.test/v1/claude/chat/completions");
        if (failoverOff)
            request.Headers.Add(OpenAiCompatibleClient.FailoverHeader, OpenAiCompatibleClient.FailoverOff);
        return new HttpResponseMessage(status) { RequestMessage = request };
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void A_cli_proxy_signal_to_a_request_without_failover_is_left_to_the_caller(HttpStatusCode status) =>
        Assert.True(Answer(status, failoverOff: true).IsAnswerForCallerWithoutFailover());

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void The_same_answer_to_an_ordinary_request_stays_retryable(HttpStatusCode status) =>
        Assert.False(Answer(status, failoverOff: false).IsAnswerForCallerWithoutFailover());

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.OK)]
    public void Other_answers_to_a_request_without_failover_keep_the_usual_rules(HttpStatusCode status) =>
        Assert.False(Answer(status, failoverOff: true).IsAnswerForCallerWithoutFailover());

    [Fact]
    public void No_answer_or_no_request_is_never_left_to_the_caller()
    {
        Assert.False(((HttpResponseMessage?)null).IsAnswerForCallerWithoutFailover());
        Assert.False(new HttpResponseMessage(HttpStatusCode.BadGateway).IsAnswerForCallerWithoutFailover());
    }

    [Theory]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.BadGateway)]
    public void A_request_with_a_cli_time_limit_is_never_retried(HttpStatusCode status)
    {
        var answer = Answer(status, failoverOff: true);
        answer.RequestMessage!.Headers.Add(OpenAiCompatibleClient.TimeoutHeader, "85");

        Assert.False(LlmHttpRetry.MayRetry(answer));
        Assert.True(answer.RequestMessage.HasCliTimeLimit());
    }

    [Fact]
    public void Without_a_time_limit_the_retry_rules_stay_as_before()
    {
        Assert.True(LlmHttpRetry.MayRetry(Answer(HttpStatusCode.GatewayTimeout, failoverOff: true)));
        Assert.True(LlmHttpRetry.MayRetry(null));
        Assert.True(LlmHttpRetry.MayRetry(new HttpResponseMessage(HttpStatusCode.GatewayTimeout)));
        Assert.False(LlmHttpRetry.MayRetry(Answer(HttpStatusCode.BadGateway, failoverOff: true)));
        Assert.True(LlmHttpRetry.MayRetry(Answer(HttpStatusCode.BadGateway, failoverOff: false)));
    }
}
