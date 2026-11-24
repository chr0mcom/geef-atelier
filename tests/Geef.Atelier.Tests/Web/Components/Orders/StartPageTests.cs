using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// The start page: one field, files with receipts, "Extra careful", one button. Uploads are simulated through the
/// callbacks the upload script calls; the script itself is replaced by bUnit's JS interop.
/// </summary>
public sealed class StartPageTests : TestContext
{
    private const string ModulePath = "./js/source-upload.js";

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly FakeOrderService _orders = new();
    private readonly FakeSourceIntakeService _intake = new();
    private readonly BunitJSModuleInterop _module;
    private readonly TestAuthorizationContext _auth;

    public StartPageTests()
    {
        Services.AddSingleton<IOrderService>(_orders);
        Services.AddSingleton<ISourceIntakeService>(_intake);
        Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
        Services.AddScoped<CircuitDataGate>();
        Services.AddOptions();
        _auth = this.AddTestAuthorization();
        _auth.SetAuthorized("alice");
        _module = JSInterop.SetupModule(ModulePath);
        _module.SetupModule("init", _ => true);
        _module.Setup<int>("removeSource", _ => true).SetResult(204);
        _module.Setup<bool>("retry", _ => true).SetResult(true);
        _module.SetupVoid("forget", _ => true).SetVoidResult();
        _module.SetupVoid("dispose", _ => true).SetVoidResult();
    }

    [Fact]
    public void ShowsOneFieldFilesCarefulAndAStartButtonThatWaitsForText()
    {
        var cut = RenderComponent<Start>();

        Assert.Equal("What do you need written?", cut.Find("h1").TextContent);
        Assert.StartsWith("Just describe it.", cut.Find("[data-testid='start-request']").GetAttribute("placeholder"));
        Assert.Contains("Add files – drop, paste or browse (PDF, Word, text)", cut.Find("[data-testid='start-drop']").TextContent);
        Assert.Contains("Extra careful", cut.Find("label.start-careful").TextContent);
        Assert.Contains("several review rounds", cut.Find("label.start-careful").TextContent);
        Assert.True(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
        Assert.Equal("Describe what you need first.", cut.Find("[data-testid='start-blocked-reason']").TextContent);
        Assert.Equal("/new", cut.Find("[data-testid='start-more-options']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("select"));
        Assert.DoesNotContain("crew", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InitialisesTheUploadScriptWithTheAntiforgeryToken()
    {
        RenderComponent<Start>();

        var init = _module.VerifyInvoke("init");
        var options = init.Arguments[2]!;
        Assert.Equal("/api/sources", options.GetType().GetProperty("url")!.GetValue(options));
        Assert.Equal(FakeAntiforgeryStateProvider.Token, options.GetType().GetProperty("token")!.GetValue(options));
    }

    [Fact]
    public void TextEnablesStart()
    {
        var cut = RenderComponent<Start>();

        cut.Find("[data-testid='start-request']").Input("Write a thank-you note.");

        Assert.False(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("[data-testid='start-blocked-reason']"));
    }

    [Fact]
    public async Task StartIsLockedWhileAFileIsUploading()
    {
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Summarise the notes.");

        Assert.True(await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "notes.md", 4_096, null)));
        await cut.InvokeAsync(() => cut.Instance.OnUploadProgress("u1", 40));

        Assert.True(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
        Assert.Equal("Wait until your files are read.", cut.Find("[data-testid='start-blocked-reason']").TextContent);
        Assert.Contains("40%", cut.Find("[data-testid='source-card-progress']").TextContent);

        await CompleteAsync(cut, "u1", 201, OrderReceipts.Usable("notes.md"));

        Assert.False(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
        Assert.Equal("ready", cut.Find("[data-testid='source-card']").GetAttribute("data-status"));
    }

    [Fact]
    public async Task AnUnusableFileLocksStartUntilItIsRemoved()
    {
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Summarise the scan.");
        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "scan.pdf", 4_096, null));

        await CompleteAsync(cut, "u1", 422, OrderReceipts.Unusable("scan.pdf", "No text could be read from this file."));

        Assert.True(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
        Assert.Contains("No text could be read from this file. Remove it, or try a different file.", cut.Find("[data-testid='source-card-problem']").TextContent);
        Assert.Equal("Remove the file that cannot be used, or try it again.", cut.Find("[data-testid='start-blocked-reason']").TextContent);

        cut.Find("[data-testid='source-card-remove']").Click();

        Assert.Empty(cut.FindAll("[data-testid='source-card']"));
        Assert.False(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task SubmitsTheWordingUnchangedWithFilesPagesAndModeThenOpensTheOrder()
    {
        var cut = RenderComponent<Start>();
        const string wording = "  Write a summary of the bill.\nKeep it under 200 words.  ";
        cut.Find("[data-testid='start-request']").Input(wording);
        var small = OrderReceipts.Usable("notes.md");
        var big = OrderReceipts.Usable("bill.pdf", chars: 1_200_000, pages: 480, budget: SourceBudgetClass.TooLarge,
            warnings: [new SourceWarning(SourceWarningCodes.OverBudget, "too large")]);
        await UploadAsync(cut, "u1", small);
        await UploadAsync(cut, "u2", big);
        cut.FindAll("[data-testid='source-card-pages']").Single().Change("1-40, 55");
        cut.Find("[data-testid='start-careful']").Change(true);

        cut.Find("[data-testid='start-submit']").Click();

        var request = Assert.Single(_orders.Submitted);
        Assert.Equal(wording, request.RequestText);
        Assert.Equal("alice", request.CreatedByUser);
        Assert.Equal(OrderEntry.Web, request.Entry);
        Assert.Equal(OrderMode.Careful, request.Mode);
        Assert.Equal(
            new[] { new OrderSourceSelection(small.SourceId!.Value, null), new OrderSourceSelection(big.SourceId!.Value, "1-40, 55") },
            request.Sources!);
        var orderId = Assert.Single(await _orders.ListAsync(null)).OrderId;
        Assert.EndsWith($"/orders/{orderId}", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    [Fact]
    public void ProblemsThePersonCanFixAppearAsABanner()
    {
        _orders.SubmitError = new OrderSubmissionException("Your files are too long to use completely (1,200,000 characters, the limit is 900,000). Choose pages for: bill.pdf");
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Summarise.");

        cut.Find("[data-testid='start-submit']").Click();

        var banner = cut.Find("[data-testid='start-error']");
        Assert.Contains("This could not start.", banner.TextContent);
        Assert.Contains("Choose pages for: bill.pdf", banner.TextContent);
        Assert.NotNull(cut.Find("[data-testid='start-error-retry']"));
        Assert.Equal("Summarise.", cut.Find("[data-testid='start-request']").GetAttribute("value"));
        Assert.EndsWith("/", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    [Fact]
    public void UnexpectedErrorsKeepTheInputAndOfferToTryAgain()
    {
        _orders.SubmitError = new InvalidOperationException("database down");
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Summarise.");

        cut.Find("[data-testid='start-submit']").Click();

        var banner = cut.Find("[data-testid='start-error']");
        Assert.Contains("Something went wrong on our side.", banner.TextContent);
        Assert.Contains("Your text and files are still here.", banner.TextContent);
        Assert.DoesNotContain("database", banner.TextContent);

        _orders.SubmitError = null;
        cut.Find("[data-testid='start-error-retry']").Click();

        Assert.Equal(2, _orders.Submitted.Count);
    }

    [Fact]
    public async Task TheReceiptCardShowsEveryWarningInPlainWords()
    {
        var cut = RenderComponent<Start>();
        var codes = typeof(SourceWarningCodes).GetFields().Select(f => (string)f.GetValue(null)!).ToList();
        var warnings = codes.Select(c => new SourceWarning(c, "raw " + c, c == SourceWarningCodes.PagesWithoutText ? [3, 7] : null)).ToList();
        warnings.Add(new SourceWarning("unknown.code", "Something new the server says."));

        await UploadAsync(cut, "u1", OrderReceipts.Usable("bill.pdf", chars: 160_485, pages: 80, size: 2_202_009, warnings: warnings));

        Assert.Equal("80 pages · 160,485 characters · 2.1 MB · Read completely", cut.Find("[data-testid='source-card-summary']").TextContent);
        var rendered = cut.FindAll("[data-testid='source-card-warning']").Select(li => (Code: li.GetAttribute("data-code"), Text: li.TextContent)).ToList();
        Assert.Equal(codes.Count + 1, rendered.Count);
        Assert.All(rendered.Where(r => r.Code != "unknown.code"), r => Assert.DoesNotContain("raw ", r.Text));
        Assert.Contains("(pages 3, 7)", rendered.Single(r => r.Code == SourceWarningCodes.PagesWithoutText).Text);
        Assert.Equal("Something new the server says.", rendered.Single(r => r.Code == "unknown.code").Text);
        Assert.Equal("The beginning of the file.", cut.Find("[data-testid='source-card-preview']").TextContent);
        Assert.Equal("Show the beginning", cut.Find("[data-testid='source-card-preview-toggle']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='source-card-pages']"));
    }

    [Fact]
    public async Task EveryPdfAsksForPagesOnceTheFilesTogetherAreTooLong()
    {
        var limit = new SourceOptions().MapMaxChars;
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Compare the two reports.");

        await UploadAsync(cut, "u1", OrderReceipts.Usable("a.pdf", chars: limit * 2 / 3, pages: 200, budget: SourceBudgetClass.WithMap));
        Assert.Empty(cut.FindAll("[data-testid='source-card-pages']"));
        Assert.False(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));

        await UploadAsync(cut, "u2", OrderReceipts.Usable("b.pdf", chars: limit * 2 / 3, pages: 180, budget: SourceBudgetClass.WithMap));
        await UploadAsync(cut, "u3", OrderReceipts.Usable("notes.md", chars: 1_000));

        Assert.Equal(2, cut.FindAll("[data-testid='source-card-pages']").Count);
        Assert.Empty(cut.FindAll("[data-testid='source-card-too-long']"));
        var line = cut.Find("[data-testid='start-budget']");
        Assert.Contains("Enter the pages to use for each PDF file above, or remove a file.", line.TextContent);
        Assert.Contains("start-budget--over", line.ClassList);
        Assert.True(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
        Assert.Equal("Choose the pages to use, or remove a file.", cut.Find("[data-testid='start-blocked-reason']").TextContent);
        Assert.Empty(_orders.Checked);
    }

    [Fact]
    public async Task AWordFileThatIsTooLongOnItsOwnIsNotSentToThePageField()
    {
        var limit = new SourceOptions().MapMaxChars;
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Summarise the two files.");

        await UploadAsync(cut, "u1", OrderReceipts.Usable("long.docx", chars: limit + 10, budget: SourceBudgetClass.TooLarge,
            warnings: [new SourceWarning(SourceWarningCodes.OverBudget, "too large")]));
        await UploadAsync(cut, "u2", OrderReceipts.Usable("short.pdf", chars: 5_000, pages: 4));

        var wordCard = cut.FindAll("[data-testid='source-card']").Single(c => c.TextContent.Contains("long.docx"));
        var warning = wordCard.QuerySelector("[data-testid='source-card-warning']")!.TextContent;
        Assert.Equal("This file is too long to use completely.", warning);
        Assert.NotNull(wordCard.QuerySelector("[data-testid='source-card-too-long']"));
        Assert.Equal(Start.TooLongWithoutPagesBlock, cut.Find("[data-testid='start-blocked-reason']").TextContent);
        Assert.Contains("A file without pages is too long on its own", cut.Find("[data-testid='start-budget']").TextContent);
        Assert.True(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task APdfThatIsTooLongStillAsksForPages()
    {
        var limit = new SourceOptions().MapMaxChars;
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Summarise the report.");

        await UploadAsync(cut, "u1", OrderReceipts.Usable("long.pdf", chars: limit + 10, pages: 400, budget: SourceBudgetClass.TooLarge,
            warnings: [new SourceWarning(SourceWarningCodes.OverBudget, "too large")]));

        Assert.Equal("This file is too long to use completely. Enter the pages to use below.", cut.Find("[data-testid='source-card-warning']").TextContent);
        Assert.Equal("Choose the pages to use, or remove a file.", cut.Find("[data-testid='start-blocked-reason']").TextContent);
    }

    [Fact]
    public async Task ChosenPagesAreCountedByTheServerAndUnlockStart()
    {
        var limit = new SourceOptions().MapMaxChars;
        var a = OrderReceipts.Usable("a.pdf", chars: limit * 2 / 3, pages: 200, budget: SourceBudgetClass.WithMap);
        var b = OrderReceipts.Usable("b.pdf", chars: limit * 2 / 3, pages: 180, budget: SourceBudgetClass.WithMap);
        _orders.CheckSources = selections => selections.Any(x => x.PageSelection == "1-20")
            ? new SourceSelectionCheck(limit / 2, SourceBudgetClass.WithMap, null)
            : new SourceSelectionCheck(limit + 5, SourceBudgetClass.TooLarge, null);
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Compare the two reports.");
        await UploadAsync(cut, "u1", a);
        await UploadAsync(cut, "u2", b);

        cut.FindAll("[data-testid='source-card-pages']")[0].Change("1-190");

        Assert.Contains("That is still more than the limit", cut.Find("[data-testid='start-budget']").TextContent);
        Assert.True(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));

        cut.FindAll("[data-testid='source-card-pages']")[0].Change("1-20");

        Assert.Equal(
            new[] { new OrderSourceSelection(a.SourceId!.Value, "1-20"), new OrderSourceSelection(b.SourceId!.Value, null) },
            _orders.Checked.Last());
        var line = cut.Find("[data-testid='start-budget']");
        Assert.StartsWith("Files with the pages you chose:", line.TextContent);
        Assert.Contains("start-budget--map", line.ClassList);
        Assert.Equal(2, cut.FindAll("[data-testid='source-card-pages']").Count);
        Assert.False(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));

        cut.Find("[data-testid='start-submit']").Click();

        Assert.Equal("1-20", Assert.Single(_orders.Submitted).Sources![0].PageSelection);
    }

    [Fact]
    public async Task PagesThatDoNotExistAreShownOnTheCardAndLockStart()
    {
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Summarise the bill.");
        await UploadAsync(cut, "u1", OrderReceipts.Usable("huge.pdf", chars: new SourceOptions().MapMaxChars + 1, pages: 480, budget: SourceBudgetClass.TooLarge));

        cut.Find("[data-testid='source-card-pages']").Change("470-500");

        Assert.Equal("Page 500 does not exist; the file has 480 pages.", cut.Find("[data-testid='source-card-pages-problem']").TextContent);
        Assert.Equal("true", cut.Find("[data-testid='source-card-pages']").GetAttribute("aria-invalid"));
        Assert.True(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
        Assert.Equal("Check the pages you entered.", cut.Find("[data-testid='start-blocked-reason']").TextContent);
        Assert.Empty(_orders.Checked);
    }

    [Fact]
    public async Task AProblemTheServerFindsWithThePagesIsShownAndLocksStart()
    {
        _orders.CheckSources = _ => new SourceSelectionCheck(0, SourceBudgetClass.TooLarge, "One of the files was not found. Please add it again.");
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Summarise the bill.");
        await UploadAsync(cut, "u1", OrderReceipts.Usable("huge.pdf", chars: new SourceOptions().MapMaxChars + 1, pages: 480, budget: SourceBudgetClass.TooLarge));

        cut.Find("[data-testid='source-card-pages']").Change("1-40");

        Assert.Contains("One of the files was not found.", cut.Find("[data-testid='start-budget']").TextContent);
        Assert.True(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
        Assert.Equal("Change the pages you entered, or remove a file.", cut.Find("[data-testid='start-blocked-reason']").TextContent);
    }

    [Theory]
    [InlineData(SourceKind.Docx, "report.docx")]
    [InlineData(SourceKind.Text, "notes.txt")]
    public async Task AFileWithoutPagesThatIsTooLongAsksForAShorterVersion(SourceKind kind, string fileName)
    {
        var cut = RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Summarise it.");
        var receipt = OrderReceipts.Usable(fileName, chars: new SourceOptions().MapMaxChars + 1, budget: SourceBudgetClass.TooLarge) with { Kind = kind };

        await UploadAsync(cut, "u1", receipt);

        Assert.Empty(cut.FindAll("[data-testid='source-card-pages']"));
        Assert.Equal("This file is too long. Please use a shorter version.", cut.Find("[data-testid='source-card-too-long']").TextContent);
        Assert.Contains("Remove a file, or use shorter versions of your files.", cut.Find("[data-testid='start-budget']").TextContent);
        Assert.True(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
        Assert.Equal("Remove a file, or use a shorter version.", cut.Find("[data-testid='start-blocked-reason']").TextContent);

        cut.Find("[data-testid='source-card-remove']").Click();

        Assert.False(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
    }

    public static TheoryData<int, string, string> SumLineCases()
    {
        var defaults = new SourceOptions();
        var full = defaults.FullTextMaxChars;
        var map = defaults.MapMaxChars;
        var part = full / 2 + 13_000;
        return new TheoryData<int, string, string>
        {
            { part, $"Files: {EnglishGrouped(part)} of {EnglishGrouped(full)} characters used completely.", "ok" },
            { full, $"Files: {EnglishGrouped(full)} of {EnglishGrouped(full)} characters used completely.", "ok" },
            { full + 1, $"That is more than {EnglishGrouped(full)}, so the writer gets a table of contents", "map" },
            { map, "so the writer gets a table of contents", "map" },
            { map + 1, $"That is more than the limit of {EnglishGrouped(map)}.", "over" },
        };
    }

    private static string EnglishGrouped(int value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));

    [Theory]
    [MemberData(nameof(SumLineCases))]
    public async Task TheSumLineSaysHowMuchOfTheFilesIsUsed(int chars, string expected, string tone)
    {
        var cut = RenderComponent<Start>();

        await UploadAsync(cut, "u1", OrderReceipts.Usable("a.md", chars: chars));

        var line = cut.Find("[data-testid='start-budget']");
        Assert.Contains(expected, line.TextContent);
        Assert.Contains($"start-budget--{tone}", line.ClassList);
    }

    [Fact]
    public async Task TheSumLineFollowsTheConfiguredLimits()
    {
        Services.Configure<SourceOptions>(o =>
        {
            o.FullTextMaxChars = 1_000;
            o.MapMaxChars = 5_000;
        });
        var cut = RenderComponent<Start>();

        await UploadAsync(cut, "u1", OrderReceipts.Usable("a.md", chars: 1_200));

        var line = cut.Find("[data-testid='start-budget']");
        Assert.Contains("That is more than 1,000, so the writer gets a table of contents", line.TextContent);
        Assert.Contains("start-budget--map", line.ClassList);
    }

    [Fact]
    public async Task MoreFilesThanAllowedAreNotSent()
    {
        Services.Configure<SourceOptions>(o => o.MaxFilesPerOrder = 2);
        var cut = RenderComponent<Start>();

        Assert.True(await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "a.md", 10, null)));
        Assert.True(await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u2", "b.md", 10, null)));
        Assert.False(await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u3", "c.md", 10, null)));

        Assert.Equal(2, cut.FindAll("[data-testid='source-card']").Count);
        Assert.Contains("You can add up to 2 files.", cut.Find("[data-testid='start-notice']").TextContent);
    }

    [Fact]
    public async Task AFileOverTheSizeLimitIsNotSent()
    {
        var cut = RenderComponent<Start>();

        Assert.False(await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "video.pdf", 60_000_000, null)));

        Assert.Contains("larger than 25.0 MB", cut.Find("[data-testid='source-card-problem']").TextContent);
        Assert.Equal("unusable", cut.Find("[data-testid='source-card']").GetAttribute("data-status"));
    }

    [Fact]
    public async Task TheTypeHintIsShownButTheFileIsStillSent()
    {
        var cut = RenderComponent<Start>();

        Assert.True(await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "data.xyz", 10, "This does not look like a PDF, Word or text file. We will still try to read it.")));

        Assert.Contains("does not look like", cut.Find("[data-testid='source-card-hint']").TextContent);
    }

    [Theory]
    [InlineData(0, "", "source-card-retry", "The upload was interrupted.")]
    [InlineData(401, "", "source-card-sign-in", "You were signed out")]
    [InlineData(400, "", "source-card-reload", "Reload the page")]
    [InlineData(500, "", "source-card-retry", "Something went wrong while reading this file.")]
    [InlineData(413, "{\"reason\":\"This file is larger than 25 MB.\"}", null, "This file is larger than 25 MB. Remove it")]
    public async Task UploadProblemsSayWhatHappenedAndWhatToDo(int status, string body, string? action, string expected)
    {
        var cut = RenderComponent<Start>();
        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "notes.md", 10, null));

        await cut.InvokeAsync(() => cut.Instance.OnUploadCompleted("u1", status, body));

        Assert.Contains(expected, cut.Find("[data-testid='source-card-problem']").TextContent);
        if (action is not null)
            Assert.NotNull(cut.Find($"[data-testid='{action}']"));
        Assert.True(cut.Find("[data-testid='start-submit']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task RetrySendsTheFileAgain()
    {
        var cut = RenderComponent<Start>();
        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "notes.md", 10, null));
        await cut.InvokeAsync(() => cut.Instance.OnUploadCompleted("u1", 0, ""));

        cut.Find("[data-testid='source-card-retry']").Click();

        Assert.Equal("u1", _module.VerifyInvoke("retry").Arguments[1]);
        Assert.Equal("uploading", cut.Find("[data-testid='source-card']").GetAttribute("data-status"));
    }

    [Fact]
    public async Task ReceiptsWithNumericEnumsAreReadToo()
    {
        var cut = RenderComponent<Start>();
        var id = Guid.NewGuid();
        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted("u1", "a.pdf", 10, null));
        var body = $$"""{"sourceId":"{{id}}","fileName":"a.pdf","kind":0,"sizeBytes":2048,"pageCount":3,"charCount":5000,"wordCount":800,"quality":1,"usable":true,"duplicate":false,"budgetClass":0,"preview":"Hello","warnings":[{"code":"pdf.pages_without_text","message":"m","pages":[2]}],"reason":null}""";

        await cut.InvokeAsync(() => cut.Instance.OnUploadCompleted("u1", 201, body));

        Assert.Equal("3 pages · 5,000 characters · 2 KB · Read completely", cut.Find("[data-testid='source-card-summary']").TextContent);
        Assert.Contains("(page 2)", cut.Find("[data-testid='source-card-warning']").TextContent);
    }

    [Fact]
    public async Task RemovingANewFileDeletesItOnTheServer()
    {
        var cut = RenderComponent<Start>();
        var receipt = OrderReceipts.Usable("notes.md");
        await UploadAsync(cut, "u1", receipt);

        cut.Find("[data-testid='source-card-remove']").Click();

        Assert.Equal(receipt.SourceId!.Value.ToString(), _module.VerifyInvoke("removeSource").Arguments[1]);
        Assert.Empty(cut.FindAll("[data-testid='source-card']"));
    }

    [Fact]
    public async Task TheSameFileTwiceIsListedOnce()
    {
        var cut = RenderComponent<Start>();
        var receipt = OrderReceipts.Usable("notes.md");
        await UploadAsync(cut, "u1", receipt);

        await UploadAsync(cut, "u2", receipt with { Duplicate = true, Warnings = [new SourceWarning(SourceWarningCodes.Duplicate, "dup")] });

        Assert.Single(cut.FindAll("[data-testid='source-card']"));
        Assert.Contains("is already in the list", cut.Find("[data-testid='start-notice']").TextContent);
    }

    [Fact]
    public void StartAgainFillsInTheEarlierRequestAndItsFiles()
    {
        var receipt = _intake.Add("alice", OrderReceipts.Usable("bill.pdf", chars: 160_485, pages: 80));
        var source = OrderTestData.Source("bill.pdf", 160_485, 80) with { SourceId = receipt.SourceId!.Value, PageSelection = "1-10" };
        var earlier = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Failed, requestText: "Summarise the bill.", mode: OrderMode.Careful, sources: [source]));

        var cut = RenderFrom(earlier.OrderId);

        Assert.Equal("Summarise the bill.", cut.Find("[data-testid='start-request']").GetAttribute("value"));
        Assert.True(cut.Find("[data-testid='start-careful']").HasAttribute("checked"));
        Assert.Contains("Your earlier request is filled in.", cut.Find("[data-testid='start-prefill-note']").TextContent);
        Assert.Contains("80 pages", cut.Find("[data-testid='source-card-summary']").TextContent);

        cut.Find("[data-testid='start-submit']").Click();
        var request = Assert.Single(_orders.Submitted);
        Assert.Equal(new[] { new OrderSourceSelection(receipt.SourceId!.Value, "1-10") }, request.Sources!);
    }

    [Fact]
    public void RemovingAFileOfTheEarlierRequestNeverDeletesIt()
    {
        var receipt = _intake.Add("alice", OrderReceipts.Usable("bill.pdf"));
        var source = OrderTestData.Source("bill.pdf") with { SourceId = receipt.SourceId!.Value };
        var earlier = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Failed, sources: [source]));
        var cut = RenderFrom(earlier.OrderId);

        cut.Find("[data-testid='source-card-remove']").Click();

        Assert.Empty(_module.Invocations["removeSource"]);
    }

    [Fact]
    public void AnotherPersonsOrderIsNotFilledIn()
    {
        var foreign = _orders.Add("bob", OrderTestData.View(OrderDisplayState.Failed, requestText: "Bob's secret request"));

        var cut = RenderFrom(foreign.OrderId);

        Assert.Equal(string.Empty, cut.Find("[data-testid='start-request']").GetAttribute("value") ?? string.Empty);
        Assert.DoesNotContain("secret", cut.Markup);
        Assert.Contains("could not find your earlier request", cut.Find("[data-testid='start-prefill-note']").TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASignInWithoutUserNameFillsInNothingAndCannotStart(bool withEmptyName)
    {
        var foreign = _orders.Add("bob", OrderTestData.View(OrderDisplayState.Failed, requestText: "Bob's secret request"));
        if (withEmptyName)
            _auth.SetAuthorized(string.Empty);
        else
            this.SignInWithoutUserName();

        var cut = RenderFrom(foreign.OrderId);

        Assert.Equal(OwnerScope.UnknownUserMessage, cut.Find("[data-testid='start-prefill-note']").TextContent.Trim());
        Assert.DoesNotContain("secret", cut.Markup);
        Assert.Equal(0, _orders.Calls);

        cut.Find("[data-testid='start-request']").Input("Write a haiku.");
        cut.Find("[data-testid='start-submit']").Click();
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public void WithoutTheUploadScriptTheTextPathStillWorks()
    {
        using var context = new TestContext();
        context.Services.AddSingleton<IOrderService>(_orders);
        context.Services.AddSingleton<ISourceIntakeService>(_intake);
        context.Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
        context.Services.AddScoped<CircuitDataGate>();
        context.Services.AddOptions();
        context.AddTestAuthorization().SetAuthorized("alice");
        context.Services.AddSingleton<IJSRuntime, BlockedJsRuntime>();

        var cut = context.RenderComponent<Start>();
        cut.Find("[data-testid='start-request']").Input("Write a haiku.");
        cut.Find("[data-testid='start-submit']").Click();

        Assert.NotNull(cut.Find("[data-testid='start-upload-unavailable']"));
        Assert.Equal("Write a haiku.", Assert.Single(_orders.Submitted).RequestText);
    }

    private IRenderedComponent<Start> RenderFrom(Guid orderId)
    {
        Services.GetRequiredService<FakeNavigationManager>().NavigateTo($"/start?from={orderId}");
        return RenderComponent<Start>();
    }

    private static Task CompleteAsync(IRenderedComponent<Start> cut, string localId, int status, SourceReceipt receipt) =>
        cut.InvokeAsync(() => cut.Instance.OnUploadCompleted(localId, status, JsonSerializer.Serialize(receipt, WireJson)));

    private static async Task UploadAsync(IRenderedComponent<Start> cut, string localId, SourceReceipt receipt)
    {
        await cut.InvokeAsync(() => cut.Instance.OnUploadStarted(localId, receipt.FileName, receipt.SizeBytes, null));
        await CompleteAsync(cut, localId, receipt.Duplicate ? 200 : 201, receipt);
    }

    private sealed class BlockedJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new JSException("Script blocked."));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromException<TValue>(new JSException("Script blocked."));
    }
}
