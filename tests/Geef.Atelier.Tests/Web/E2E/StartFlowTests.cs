using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Components.Orders;
using Geef.Atelier.Web.Notifications;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// The one-field path in a real browser: type a sentence, add files through the upload script (file picker, drop,
/// paste), start, and watch the order page turn into the result. The order service is an in-memory fake and
/// <c>POST /api/sources</c> a stand-in that checks the antiforgery header the script sends; the real endpoints and
/// services arrive with P1.
/// </summary>
[Collection("Playwright")]
public sealed class StartFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly FakeOrderService _orders = new();
    private readonly ConcurrentQueue<string> _uploads = new();
    private readonly ConcurrentQueue<Guid> _deletes = new();
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100,
            configureServices: s => s.AddSingleton<IOrderService>(_orders),
            configureApp: MapStandInSourceEndpoints);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task TypeAddAFileStartAndSeeTheResultFirst()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await OpenStartAsync(page);
            await page.FillAsync("[data-testid='start-request']", "Write a short, friendly email about a two-week delivery delay.");
            await page.SetInputFilesAsync("[data-testid='start-file-input']", new FilePayload
            {
                Name = "notes.md",
                MimeType = "text/markdown",
                Buffer = "# Notes\nThe delivery moves by two weeks."u8.ToArray(),
            });

            await page.Locator("[data-testid='source-card'][data-status='ready']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Contains("Read completely", await page.Locator("[data-testid='source-card-summary']").InnerTextAsync());
            Assert.Contains($"of {new SourceOptions().FullTextMaxChars.ToString("N0", CultureInfo.GetCultureInfo("en-US"))} characters used completely", await page.Locator("[data-testid='start-budget']").InnerTextAsync());

            await WaitEnabledAsync(page, "[data-testid='start-submit']");
            await page.ClickAsync("[data-testid='start-submit']");
            await page.Locator("[data-testid='order-page']").WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
            Assert.Matches(new Regex(@"/orders/[0-9a-f\-]{36}$"), page.Url);

            var request = Assert.Single(_orders.Submitted);
            Assert.Equal("Write a short, friendly email about a two-week delivery delay.", request.RequestText);
            Assert.Equal(OrderMode.Direct, request.Mode);
            Assert.Single(request.Sources!);
            Assert.Equal("notes.md", Assert.Single(_uploads));

            await page.Locator("[data-testid='order-state'][data-state='queued']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Contains("Usually about 5 minutes", await page.Locator("[data-testid='order-progress-hint']").InnerTextAsync());

            var orderId = Guid.Parse(page.Url[(page.Url.LastIndexOf('/') + 1)..]);
            var done = OrderTestData.View(OrderDisplayState.Delivered, orderId: orderId);
            _orders.Update(orderId, _ => done);
            _orders.SetResult(OrderTestData.Result(done, text: "Dear customer,\n\nyour delivery will arrive two weeks later."));
            await _host!.Services.GetRequiredService<IRunUpdateFeed>().PublishOrderUpdatedAsync(orderId, "test-user");

            await page.Locator("[data-testid='order-result']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Contains("your delivery will arrive two weeks later.", await page.Locator("[data-testid='order-result']").InnerTextAsync());
            Assert.Equal("delivered", await page.Locator("[data-testid='order-state']").GetAttributeAsync("data-state"));
            Assert.Equal($"/orders/{orderId}/export/docx?version=1", await page.Locator("[data-testid='order-download-docx']").GetAttributeAsync("href"));
            Assert.Equal($"/orders/{orderId}/export/pdf?version=1", await page.Locator("[data-testid='order-download-pdf']").GetAttributeAsync("href"));
            Assert.Equal($"/orders/{orderId}/export/md?version=1", await page.Locator("[data-testid='order-download-md']").GetAttributeAsync("href"));
            await page.WaitForFunctionAsync("() => document.title.startsWith('Ready')", null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task DroppedAndPastedFilesAreUploadedToo()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await OpenStartAsync(page);

            await page.EvaluateAsync("""
                () => {
                    const drop = new DataTransfer();
                    drop.items.add(new File(['dropped text'], 'dropped.md', { type: 'text/markdown' }));
                    document.dispatchEvent(new DragEvent('dragenter', { dataTransfer: drop, bubbles: true }));
                    document.dispatchEvent(new DragEvent('drop', { dataTransfer: drop, bubbles: true, cancelable: true }));
                    const paste = new DataTransfer();
                    paste.items.add(new File(['pasted text'], 'pasted.txt', { type: 'text/plain' }));
                    document.querySelector("[data-testid='start-request']")
                        .dispatchEvent(new ClipboardEvent('paste', { clipboardData: paste, bubbles: true, cancelable: true }));
                }
                """);

            await page.Locator("[data-testid='source-card'][data-status='ready']").Nth(1).WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            var names = await page.Locator("[data-testid='source-card-name']").AllInnerTextsAsync();
            Assert.Equal(new[] { "dropped.md", "pasted.txt" }, names.OrderBy(n => n).ToArray());
            Assert.Equal("false", await page.EvaluateAsync<string>("() => String(document.querySelector(\"[data-testid='start-drop']\").classList.contains('is-dragging'))"));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task AnUnusableFileBlocksStartUntilItIsRemoved()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await OpenStartAsync(page);
            await page.FillAsync("[data-testid='start-request']", "Summarise the scan.");
            await page.SetInputFilesAsync("[data-testid='start-file-input']", new FilePayload
            {
                Name = "broken.pdf",
                MimeType = "application/pdf",
                Buffer = "%PDF-broken"u8.ToArray(),
            });

            await page.Locator("[data-testid='source-card'][data-status='unusable']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Contains("No text could be read", await page.Locator("[data-testid='source-card-problem']").InnerTextAsync());
            Assert.True(await page.Locator("[data-testid='start-submit']").IsDisabledAsync());

            await page.ClickAsync("[data-testid='source-card-remove']");

            await WaitEnabledAsync(page, "[data-testid='start-submit']");
            Assert.Equal(0, await page.Locator("[data-testid='source-card']").CountAsync());
            Assert.Empty(_deletes);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task RemovingAReadFileDeletesItOnTheServer()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await OpenStartAsync(page);
            await page.SetInputFilesAsync("[data-testid='start-file-input']", new FilePayload
            {
                Name = "notes.md",
                MimeType = "text/markdown",
                Buffer = "notes"u8.ToArray(),
            });
            await page.Locator("[data-testid='source-card'][data-status='ready']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

            await page.ClickAsync("[data-testid='source-card-remove']");

            await WaitUntilAsync(() => !_deletes.IsEmpty);
            Assert.Equal(0, await page.Locator("[data-testid='source-card']").CountAsync());
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task OpenStartAsync(IPage page)
    {
        await page.GotoAsync($"{_host!.BaseUrl}/start");
        await page.Locator("[data-testid='start-request']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await page.Locator("[data-testid='start-blocked-reason']:has-text('Describe what you need first.')")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
    }

    private static async Task WaitEnabledAsync(IPage page, string selector)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await page.Locator(selector).IsDisabledAsync())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"{selector} stayed disabled.");
            await Task.Delay(100);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(50);
        }
    }

    private void MapStandInSourceEndpoints(WebApplication app)
    {
        app.MapPost("/api/sources", async (HttpContext ctx, IAntiforgery antiforgery) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(ctx);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { reason = "The request could not be verified." });
            }
            var file = (await ctx.Request.ReadFormAsync()).Files.Single();
            _uploads.Enqueue(file.FileName);
            if (file.FileName == "broken.pdf")
                return Results.Json(OrderReceipts.Unusable(file.FileName, "No text could be read from this file."), WireJson, statusCode: 422);
            var receipt = OrderReceipts.Usable(file.FileName, chars: (int)file.Length * 10, size: file.Length);
            return Results.Json(receipt, WireJson, statusCode: 201);
        }).DisableAntiforgery();
        app.MapDelete("/api/sources/{id:guid}", async (Guid id, HttpContext ctx, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(ctx);
            _deletes.Enqueue(id);
            return Results.NoContent();
        });
    }
}
