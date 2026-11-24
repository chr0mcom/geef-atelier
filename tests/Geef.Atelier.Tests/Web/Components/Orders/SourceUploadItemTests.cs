using Geef.Atelier.Web.Components.UI.Orders;

namespace Geef.Atelier.Tests.Web.Components.Orders;

public sealed class SourceUploadItemTests
{
    [Theory]
    [InlineData(408, """{ "reason": "The upload took too long. Check your connection and try again." }""", "The upload took too long. Check your connection and try again.")]
    [InlineData(429, """{ "fileName": "a.pdf", "usable": false, "reason": "You are already adding several files. Wait until they are done." }""", "You are already adding several files. Wait until they are done.")]
    [InlineData(503, "", "The service is busy right now. Try again in a minute.")]
    public void A_busy_or_timed_out_upload_can_be_tried_again(int status, string body, string problem)
    {
        var item = new SourceUploadItem("u1", "a.pdf", 10);

        item.ApplyResponse(status, body, 30 * 1_048_576);

        Assert.Equal(SourceUploadStatus.Failed, item.Status);
        Assert.Equal(SourceUploadRecovery.Retry, item.Recovery);
        Assert.Equal(problem, item.Problem);
        Assert.DoesNotContain("different file", item.Problem);
    }

    [Fact]
    public void An_unreadable_file_still_asks_for_a_different_file()
    {
        var item = new SourceUploadItem("u1", "a.pdf", 10);

        item.ApplyResponse(422, """{ "fileName": "a.pdf", "usable": false, "reason": "This file has no readable text." }""", 30 * 1_048_576);

        Assert.Equal(SourceUploadStatus.Unusable, item.Status);
        Assert.Equal(SourceUploadRecovery.None, item.Recovery);
        Assert.Equal("This file has no readable text. Remove it, or try a different file.", item.Problem);
    }
}
