using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Tests.Web.Uploads;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// The upload page <c>/u/{id}</c> for people whose AI assistant asked for a file: one drop field for one file, the receipt
/// and the line to go back, and plain lines for a used, expired or running link. A foreign or unknown link and the switch
/// <c>Off</c> show the normal "Not found" page. The upload script is replaced by bUnit's JS interop; uploads are simulated
/// through the callbacks the script calls.
/// </summary>
public sealed class UploadPageTests : TestContext
{
    private const string ModulePath = "./js/source-upload.js";

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly FakeUploadTickets _tickets = new();
    private readonly BunitJSModuleInterop _module;
    private readonly TestAuthorizationContext _auth;

    public UploadPageTests()
    {
        Services.AddSingleton<IUploadTicketService>(_tickets);
        Services.AddSingleton<IUploadTicketRepository>(_tickets);
        Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
        Services.AddScoped<CircuitDataGate>();
        Services.AddOptions();
        _auth = this.AddTestAuthorization();
        _auth.SetAuthorized("alice");
        _module = JSInterop.SetupModule(ModulePath);
        _module.SetupModule("init", _ => true);
        _module.Setup<bool>("retry", _ => true).SetResult(true);
        _module.SetupVoid("forget", _ => true).SetVoidResult();
        _module.SetupVoid("dispose", _ => true).SetVoidResult();
    }

    [Fact]
    public void AnOpenLinkAsksForTheNamedFileWithOneDropFieldForOneFile()
    {
        var id = _tickets.Add("alice", "Tender 2026.pdf");

        var cut = Render(id);

        Assert.Equal("open", cut.Find("[data-testid='upload-page']").GetAttribute("data-state"));
        Assert.Equal(UploadTicketLines.PageHeading, cut.Find("h1").TextContent);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.PageIntroFormat, "Tender 2026.pdf"), cut.Find("[data-testid='upload-intro']").TextContent);
        Assert.Equal("Tender 2026.pdf", cut.Find(".upload-file-name").TextContent);
        var input = cut.Find("[data-testid='upload-file-input']");
        Assert.False(input.HasAttribute("multiple"));
        Assert.False(cut.Find("[data-testid='upload-drop']").HasAttribute("hidden"));
        Assert.Contains("Choose the file", cut.Find("[data-testid='upload-drop']").TextContent);
        Assert.Contains("up to 25 MB", cut.Find("[data-testid='upload-drop']").TextContent);
        AssertPlainWords(cut.Markup);
    }

    [Fact]
    public void TheScriptSendsToThePageUploadOfThisLinkWithTheAntiforgeryTokenAndOnlyOneFile()
    {
        var id = _tickets.Add("alice", "notes.md");

        Render(id);

        var options = _module.VerifyInvoke("init").Arguments[2]!;
        Assert.Equal($"/api/uploads/{id}/page", options.GetType().GetProperty("url")!.GetValue(options));
        Assert.Equal(FakeAntiforgeryStateProvider.Token, options.GetType().GetProperty("token")!.GetValue(options));
        Assert.Equal(true, options.GetType().GetProperty("single")!.GetValue(options));
    }

    [Fact]
    public async Task AFileThatArrivesShowsItsReceiptAndTheLineToGoBack()
    {
        var id = _tickets.Add("alice", "notes.md");
        var cut = Render(id);

        Assert.True(await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "notes.md", 4_096, null)));
        Assert.Equal("uploading", State(cut));
        Assert.True(cut.Find("[data-testid='upload-drop']").HasAttribute("hidden"));
        await cut.InvokeAsync(() => cut.Instance.OnUploadProgress("u1", 40));
        Assert.Contains("Uploading… 40%", cut.Find("[data-testid='upload-state']").TextContent);

        await Complete(cut, "u1", 201, OrderReceipts.Usable("notes.md", chars: 2_000));

        Assert.Equal("done", State(cut));
        Assert.Equal(UploadTicketLines.PageDone, cut.Find("[data-testid='upload-done']").TextContent);
        Assert.Equal("notes.md", cut.Find("[data-testid='upload-receipt'] [data-testid='source-card-name']").TextContent);
        Assert.Contains("Read completely", cut.Find("[data-testid='source-card-summary']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='upload-drop']"));
        Assert.Empty(cut.FindAll("[data-testid='upload-problem']"));
        _module.VerifyInvoke("forget");
        Assert.False(await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u2", "other.md", 10, null)));
    }

    [Fact]
    public void AUsedLinkSaysTheFileHasArrivedAndOffersNoUpload()
    {
        var id = _tickets.Add("alice", "secret-name.pdf", UploadClaimProbe.Used);

        var cut = Render(id);

        Assert.Equal("used", State(cut));
        Assert.Equal(UploadTicketLines.PageAlreadyUsed, cut.Find("[data-testid='upload-used']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='upload-drop']"));
        Assert.DoesNotContain("secret-name.pdf", cut.Markup, StringComparison.Ordinal);
        _module.VerifyNotInvoke("init");
    }

    [Fact]
    public void AnExpiredOrFailedLinkSaysToAskForANewOne()
    {
        var id = _tickets.Add("alice", "notes.md", UploadClaimProbe.Expired);

        var cut = Render(id);

        Assert.Equal("expired", State(cut));
        Assert.Equal(UploadTicketLines.PageExpired, cut.Find("[data-testid='upload-expired']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='upload-drop']"));
    }

    [Fact]
    public void TheFourStatesLookDifferent()
    {
        var open = Render(_tickets.Add("alice", "a.md")).Markup;
        var used = Render(_tickets.Add("alice", "a.md", UploadClaimProbe.Used)).Find("[data-testid='upload-state']").ClassName;
        var expired = Render(_tickets.Add("alice", "a.md", UploadClaimProbe.Expired)).Find("[data-testid='upload-state']").ClassName;
        var running = Render(_tickets.Add("alice", "a.md", UploadClaimProbe.Receiving)).Find("[data-testid='upload-state']").ClassName;

        Assert.DoesNotContain("upload-state", open.Replace("upload-state-", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(3, new[] { used, expired, running }.Distinct().Count());
        Assert.Contains("upload-state--used", used, StringComparison.Ordinal);
        Assert.Contains("upload-state--expired", expired, StringComparison.Ordinal);
        Assert.Contains("upload-state--busy", running, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkOfAnotherPersonLooksExactlyLikeAnUnknownOneAndShowsNothingOfIt()
    {
        var foreign = _tickets.Add("bob", "Bob salary 2026.pdf");

        var foreignMarkup = Render(foreign).Markup;
        var unknownMarkup = Render(Guid.NewGuid()).Markup;

        Assert.Equal(unknownMarkup, foreignMarkup);
        Assert.Contains("Not Found", foreignMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob salary", foreignMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain(UploadTicketLines.PageHeading, foreignMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("source-card-preview", foreignMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain(foreign.ToString(), foreignMarkup, StringComparison.Ordinal);
        Assert.Equal(UploadClaimProbe.Open, _tickets.StateOf(foreign));
        Assert.Empty(_tickets.PageUploads);
        _module.VerifyNotInvoke("init");
    }

    [Fact]
    public void WithTheSwitchOffEvenAnOwnLinkIsNotFoundAndNothingIsAsked()
    {
        var id = _tickets.Add("alice", "notes.md");
        _tickets.Enabled = false;

        var cut = Render(id);

        Assert.NotEmpty(cut.FindAll("[data-testid='upload-not-found']"));
        Assert.Empty(_tickets.Probes);
        Assert.DoesNotContain("notes.md", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ASignInWithoutUserNameSeesTheUsualAnswerAndNoTicket()
    {
        var id = _tickets.Add("alice", "notes.md");
        _auth.SetAuthorized(string.Empty);

        var cut = Render(id);

        Assert.Contains(OwnerScope.UnknownUserMessage, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(_tickets.Probes);
    }

    [Fact]
    public async Task AnUnusableFileKeepsTheLinkOpenAndAsksForADifferentFile()
    {
        var id = _tickets.Add("alice", "scan.pdf");
        var cut = Render(id);

        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "scan.pdf", 4_096, null));
        await Complete(cut, "u1", 422, OrderReceipts.Unusable("scan.pdf", "This PDF has no readable text. It may be a scan; scanned files are not supported yet."));

        Assert.Equal("problem", State(cut));
        Assert.Equal("This PDF has no readable text. It may be a scan; scanned files are not supported yet. Choose a different file.", cut.Find("[data-testid='upload-problem'] .upload-state-line").TextContent);
        Assert.False(cut.Find("[data-testid='upload-drop']").HasAttribute("hidden"));
        Assert.Contains("Choose a different file", cut.Find("[data-testid='upload-drop']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='upload-retry']"));
        Assert.True(await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u2", "letter.docx", 4_096, null)));
        Assert.Empty(cut.FindAll("[data-testid='upload-problem']"));
    }

    [Fact]
    public async Task AfterTheLastFailedTryTheReasonIsShownAndTheLinkIsNotCalledExpired()
    {
        var id = _tickets.Add("alice", "scan.pdf");
        var cut = Render(id);

        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "scan.pdf", 4_096, null));
        _tickets.Set(id, UploadClaimProbe.Failed);
        await Complete(cut, "u1", 422, OrderReceipts.Unusable("scan.pdf", "This file is empty."));

        Assert.Equal("failed", State(cut));
        Assert.Equal("This file is empty.", cut.Find("[data-testid='upload-problem'] .upload-state-line").TextContent);
        Assert.Equal(UploadTicketLines.PageFailed, cut.Find("[data-testid='upload-failed']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='upload-expired']"));
        Assert.DoesNotContain("expired", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("[data-testid='upload-drop']"));
    }

    [Fact]
    public void ALinkWhoseFilesWereAllRefusedSaysSoInsteadOfExpired()
    {
        var cut = Render(_tickets.Add("alice", "notes.md", UploadClaimProbe.Failed));

        Assert.Equal("failed", State(cut));
        Assert.Equal(UploadTicketLines.PageFailed, cut.Find("[data-testid='upload-failed']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='upload-drop']"));
    }

    [Fact]
    public async Task AFileTooLongForThisWayIsRefusedWithAWayOutAndNoPageFieldIsAskedFor()
    {
        var id = _tickets.Add("alice", "report.pdf");
        var cut = Render(id);
        var reason = string.Format(CultureInfo.InvariantCulture, UploadTicketLines.TooLongWithPagesFormat, "700,000", "600,000", UploadTicketLines.NothingStored);

        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "report.pdf", 4_096, null));
        await Complete(cut, "u1", 422, OrderReceipts.Unusable("report.pdf", reason) with { PageCount = 250, Kind = SourceKind.Pdf });

        Assert.Equal("problem", State(cut));
        Assert.Contains("start the order in the Atelier web app and choose the pages there", cut.Find("[data-testid='upload-problem']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Enter the pages", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(UploadTicketLines.PageDone, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='source-card-pages']"));
    }

    [Fact]
    public async Task ADoneReceiptNeverAsksForPagesThatThisPageCannotTake()
    {
        var id = _tickets.Add("alice", "report.pdf");
        var cut = Render(id);
        var warning = new SourceWarning(SourceWarningCodes.OverBudget, "This file is too long to use completely. Choose the pages to use.");

        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "report.pdf", 4_096, null));
        await Complete(cut, "u1", 201, OrderReceipts.Usable("report.pdf", chars: 2_000, pages: 250, warnings: [warning]));

        Assert.DoesNotContain("Enter the pages", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='source-card-pages']"));
    }

    [Fact]
    public async Task WhenTheFileArrivedMeanwhileInAnotherWayThePageSaysSo()
    {
        var id = _tickets.Add("alice", "notes.md");
        var cut = Render(id);

        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "notes.md", 10, null));
        _tickets.Set(id, UploadClaimProbe.Used);
        await cut.InvokeAsync(() => cut.Instance.OnUploadCompleted("u1", 409, JsonSerializer.Serialize(new { reason = UploadTicketLines.PageAlreadyUsed })));

        Assert.Equal("used", State(cut));
        Assert.Empty(cut.FindAll("[data-testid='upload-problem']"));
        Assert.Equal(UploadTicketLines.PageAlreadyUsed, cut.Find("[data-testid='upload-used']").TextContent);
    }

    [Fact]
    public async Task AFileLargerThanTheLimitIsNotSent()
    {
        var cut = Render(_tickets.Add("alice", "video.pdf"));

        Assert.False(await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "video.pdf", 60_000_000, null)));

        Assert.Equal("problem", State(cut));
        Assert.Equal("This file is larger than 25 MB. Choose a smaller file.", cut.Find("[data-testid='upload-problem'] .upload-state-line").TextContent);
    }

    [Fact]
    public async Task SeveralFilesAtOnceAreToldThatOnlyTheFirstIsUsed()
    {
        var cut = Render(_tickets.Add("alice", "notes.md"));

        await cut.InvokeAsync(() => cut.Instance.OnExtraFilesIgnored(2));

        Assert.Equal(UploadPage.OneFileNotice, cut.Find("[data-testid='upload-notice']").TextContent);
    }

    [Fact]
    public async Task AnInterruptedUploadCanBeTriedAgainWithTheSameFile()
    {
        var cut = Render(_tickets.Add("alice", "notes.md"));
        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "notes.md", 10, null));

        await cut.InvokeAsync(() => cut.Instance.OnUploadCompleted("u1", 0, string.Empty));

        Assert.Equal("The upload was interrupted. Check your connection and try again.", cut.Find("[data-testid='upload-problem'] .upload-state-line").TextContent);
        cut.Find("[data-testid='upload-retry']").Click();
        _module.VerifyInvoke("retry");
        Assert.Equal("uploading", State(cut));
        _module.VerifyNotInvoke("forget");
    }

    [Fact]
    public void ARunningUploadSaysSoAndCheckingAgainOpensTheLink()
    {
        var id = _tickets.Add("alice", "notes.md", UploadClaimProbe.Receiving);
        var cut = Render(id);

        Assert.Equal("running", State(cut));
        Assert.Equal(UploadTicketLines.AlreadyRunning, cut.Find("[data-testid='upload-running']").TextContent);
        Assert.True(cut.Find("[data-testid='upload-drop']").HasAttribute("hidden"));

        _tickets.Set(id, UploadClaimProbe.Open);
        cut.Find("[data-testid='upload-check-again']").Click();

        Assert.Equal("open", State(cut));
        Assert.Equal("notes.md", cut.Find(".upload-file-name").TextContent);
        Assert.False(cut.Find("[data-testid='upload-drop']").HasAttribute("hidden"));
    }

    [Fact]
    public void WhenTheLinkCannotBeCheckedThePageSaysSoAndClaimsNoState()
    {
        var id = _tickets.Add("alice", "notes.md");
        _tickets.ProbeError = new InvalidOperationException("database down");

        var cut = Render(id);

        Assert.Equal(UploadPage.CheckFailed, cut.Find("[data-testid='upload-problem'] .upload-state-line").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='upload-expired']"));
        Assert.Empty(cut.FindAll("[data-testid='upload-drop']"));
    }

    private IRenderedComponent<UploadPage> Render(Guid id) =>
        RenderComponent<UploadPage>(p => p.Add(x => x.UploadId, id));

    private static string? State(IRenderedComponent<UploadPage> cut) =>
        cut.Find("[data-testid='upload-page']").GetAttribute("data-state");

    private static Task Complete(IRenderedComponent<UploadPage> cut, string localId, int status, SourceReceipt receipt) =>
        cut.InvokeAsync(() => cut.Instance.OnUploadCompleted(localId, status, JsonSerializer.Serialize(receipt, WireJson)));

    private static void AssertPlainWords(string markup)
    {
        foreach (var word in new[] { "crew", "run ", "ticket", "token", "grounding", "iteration", "convergence" })
            Assert.DoesNotContain(word, markup, StringComparison.OrdinalIgnoreCase);
    }
}
