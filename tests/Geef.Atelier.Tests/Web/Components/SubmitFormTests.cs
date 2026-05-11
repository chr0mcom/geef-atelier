using Bunit;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class SubmitFormTests : TestContext
{
    [Fact]
    public void EmptyBriefing_ButtonEnabled_SubmitShowsValidationError()
    {
        SubmitFormResult? received = null;
        var cut = RenderComponent<SubmitForm>(p =>
            p.Add(c => c.OnSubmitted, (SubmitFormResult r) => received = r));

        // Submit without filling anything in
        cut.Find("button[type='submit']").Click();

        // Validation error should appear, callback should NOT have been called
        Assert.Null(received);
        Assert.Contains("required", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidBriefing_RaisesOnSubmittedWithValues()
    {
        SubmitFormResult? received = null;
        var cut = RenderComponent<SubmitForm>(p =>
            p.Add(c => c.OnSubmitted, (SubmitFormResult r) => received = r));

        cut.Find("textarea#briefing").Change("My briefing text for testing.");
        cut.Find("button[type='submit']").Click();

        Assert.NotNull(received);
        Assert.Equal("My briefing text for testing.", received!.Briefing);
    }
}
