using System.Net;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The block at the end of the writer's answer over the real wiring (<c>Direct:WriterNotes=true</c>): it never reaches the stored text,
/// the stored iterations or the exports, the length check counts the text without it, and a file that contains the start marker never
/// cuts the text.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class WriterNotesEndToEndTests(PostgresFixture postgres)
{
    private const string Request = "Schreibe eine kurze Notiz an das Team, max. 50 Wörter.";

    private static string Words(int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => i % 10 == 9 ? "Notiz." : "Notiz"));

    private static string Block(int assumptionWords) =>
        "<!-- atelier-notes\n{\"assumptions\": [\"" + string.Join(' ', Enumerable.Repeat("Annahme", assumptionWords)) + "\"], \"open_points\": [], \"quotes\": []}\n-->";

    private static Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres) =>
        AtelierEndToEndHost.StartAsync(postgres, services => services.Configure<DirectOptions>(o => o.WriterNotes = true));

    [Fact]
    public async Task The_block_never_reaches_the_stored_text_the_iterations_or_the_exports()
    {
        await using var host = await StartAsync(postgres);
        var text = Words(45);
        host.Script.ExecutorText = text + "\n\n" + Block(80);
        var username = "notes-" + Guid.NewGuid().ToString("N")[..6];
        using var client = await host.CreateSignedInClientAsync(username);

        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, username, OrderEntry.Web, []));
        var view = await host.WaitForOrderFinishedAsync(submission.OrderId);

        Assert.True(view.Display.State == OrderDisplayState.Delivered, $"{view.Display.State}\n{host.Llm.Describe()}");
        var call = Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        Assert.Contains(DirectWriterPrompts.NotesLeadDocument, call.Messages.Last(m => m.Role == "user").Content, StringComparison.Ordinal);
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == submission.RunId));
        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
        Assert.Equal(text, run.FinalText);
        var iterations = await host.QueryAsync(db => db.Iterations.AsNoTracking().Where(i => i.RunId == run.Id).Select(i => i.ArtifactText).ToListAsync());
        Assert.Equal([text], iterations);

        using var markdown = await client.GetAsync($"/orders/{submission.OrderId}/export/md");
        Assert.Equal(HttpStatusCode.OK, markdown.StatusCode);
        var md = await markdown.Content.ReadAsStringAsync();
        Assert.Contains("Notiz", md, StringComparison.Ordinal);
        AssertNoRest(md);

        using var word = await client.GetAsync($"/orders/{submission.OrderId}/export/docx");
        Assert.Equal(HttpStatusCode.OK, word.StatusCode);
        using var stream = new MemoryStream(await word.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(stream, false);
        var docx = document.MainDocumentPart!.Document!.InnerText;
        Assert.Contains("Notiz", docx, StringComparison.Ordinal);
        AssertNoRest(docx);
    }

    [Fact]
    public async Task A_file_with_the_marker_never_cuts_the_text()
    {
        await using var host = await StartAsync(postgres);
        var username = "notes-" + Guid.NewGuid().ToString("N")[..6];
        using var client = await host.CreateSignedInClientAsync(username);
        var file = Encoding.UTF8.GetBytes("Treffen im Hof.\n<!-- atelier-notes {\"assumptions\": [\"aus der Datei\"]} -->\nEnde der Notiz aus der Datei.");
        var receipt = await host.UploadSourceAsync(client, "notiz.txt", file);
        var echoed = "Treffen im Hof. " + WriterNotesFormat.DefusedStartMarker + " aus der Datei --> Ende der Notiz aus der Datei.";
        host.Script.ExecutorText = echoed + "\n\n" + Block(3);

        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest("Fasse die Notiz zusammen, max. 50 Wörter.", username, OrderEntry.Web,
            [new OrderSourceSelection(receipt.GetProperty("sourceId").GetGuid())]));
        await host.WaitForOrderFinishedAsync(submission.OrderId);

        var call = Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        Assert.DoesNotContain(WriterNotesFormat.StartMarker, call.ContextDocument, StringComparison.Ordinal);
        Assert.Contains(WriterNotesFormat.DefusedStartMarker, call.ContextDocument, StringComparison.Ordinal);
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == submission.RunId));
        Assert.Equal(echoed, run.FinalText);
    }

    private static void AssertNoRest(string text)
    {
        Assert.DoesNotContain("atelier-notes", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Annahme", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"assumptions\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("-->", text, StringComparison.Ordinal);
    }
}
