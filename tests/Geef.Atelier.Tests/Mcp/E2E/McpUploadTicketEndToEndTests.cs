using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Mcp.E2E;

/// <summary>
/// The upload ticket over the real <c>/mcp</c> endpoint and the real token upload: <c>prepare_attachment</c> → <c>PUT</c> with the
/// command's header → <c>submit_order(uploadIds)</c> → <c>get_order_status</c> → the writer's request holds a marker sentence of the
/// first and of the last page (scripted model).
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class McpUploadTicketEndToEndTests(PostgresFixture postgres)
{
    private const string Issuer = "https://atelier.example";
    private const string Request = "Fasse die angehängte Datei kurz zusammen.";
    private const string FirstMarker = "Der Leuchtturm am Nordkap wurde im Jahr 1911 gebaut.";
    private const string LastMarker = "Die letzte Seite endet mit dem Satz vom blauen Kranich.";
    private const int Pages = 40;

    private static readonly Regex Command = new(
        @"^curl --fail-with-body -sS --globoff -T '<path to the file>' -H 'X-Upload-Token: (?<token>[A-Za-z0-9_-]+)' (?<url>\S+)$", RegexOptions.CultureInvariant);

    [Fact]
    public async Task A_file_prepared_over_mcp_and_uploaded_with_its_command_reaches_the_writer_in_full()
    {
        await using var host = await StartAsync(UploadTicketsMode.On);
        var pdf = SyntheticDocuments.Pdf(Pages, page => page switch
        {
            1 => FirstMarker + " " + Filler(page),
            Pages => Filler(page) + " " + LastMarker,
            _ => Filler(page),
        });
        Assert.True(pdf.Length >= 20_000, $"The synthetic PDF has only {pdf.Length} bytes.");
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(pdf));

        var prepared = await host.CallMcpToolAsync("prepare_attachment", new { fileName = "Muster-Bericht.pdf", sizeBytes = pdf.LongLength });
        var uploadId = prepared.GetProperty("uploadId").GetString()!;
        var match = Command.Match(prepared.GetProperty("curlCommand").GetString()!);
        Assert.True(match.Success, prepared.ToString());
        var token = match.Groups["token"].Value;
        var url = new Uri(match.Groups["url"].Value);
        Assert.Equal($"{Issuer}/api/uploads/{uploadId}", url.ToString());
        Assert.Equal($"{Issuer}/u/{uploadId}", prepared.GetProperty("uploadPage").GetString());
        Assert.DoesNotContain(token, prepared.GetProperty("uploadPage").GetString()!, StringComparison.Ordinal);

        using var client = host.CreateAnonymousClient();
        using (var response = await PutAsync(client, url, token, pdf))
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.Created, body);
            using var receipt = JsonDocument.Parse(body);
            Assert.Equal(sha256, receipt.RootElement.GetProperty("sha256").GetString());
            Assert.Equal(uploadId, receipt.RootElement.GetProperty("uploadId").GetString());
        }

        using (var again = await PutAsync(client, url, token, pdf))
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request, uploadIds = new[] { uploadId } });
        host.ReleaseOrchestrator();
        Assert.Equal("Muster-Bericht.pdf", Assert.Single(submitted.GetProperty("sources").EnumerateArray()).GetProperty("fileName").GetString());
        var orderId = submitted.GetProperty("orderId").GetString()!;

        JsonElement status = default;
        for (var attempt = 0; attempt < 10 && !(status.ValueKind == JsonValueKind.Object && status.GetProperty("done").GetBoolean()); attempt++)
            status = await host.CallMcpToolAsync("get_order_status", new { orderId, waitSeconds = 50 });
        Assert.True(status.GetProperty("done").GetBoolean(), $"Order not done: {status}\n{host.Llm.Describe()}");

        Assert.True(host.Llm.AnyExecutorPromptContains(FirstMarker), host.Llm.Describe());
        Assert.True(host.Llm.AnyExecutorPromptContains(LastMarker), host.Llm.Describe());

        var stored = await host.QueryAsync(db => db.Sources.AsNoTracking().SingleAsync());
        Assert.Equal(sha256, stored.Sha256);
        Assert.Equal(SourceOrigin.McpAttachment, stored.Origin);
        var ticket = await host.QueryAsync(db => db.Set<UploadTicketEntity>().AsNoTracking().SingleAsync());
        Assert.Equal((UploadTicketState.Used, stored.Id, stored.OwnerUsername), (ticket.State, ticket.SourceId, ticket.OwnerUsername));
        Assert.Equal(64, ticket.TokenHash.Length);
        Assert.NotEqual(token, ticket.TokenHash);
    }

    [Fact]
    public async Task An_upload_id_whose_file_has_not_arrived_starts_nothing_and_names_the_upload_page()
    {
        await using var host = await StartAsync(UploadTicketsMode.On);

        var prepared = await host.CallMcpToolAsync("prepare_attachment", new { fileName = "Muster-Bericht.pdf" });
        var uploadId = prepared.GetProperty("uploadId").GetString()!;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.CallMcpToolAsync("submit_order", new { request = Request, uploadIds = new[] { uploadId } }));

        Assert.Contains($"The file for upload {uploadId} has not arrived yet.", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"{Issuer}/u/{uploadId}", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, await host.QueryAsync(db => db.Orders.CountAsync()));
    }

    [Fact]
    public async Task With_tickets_off_the_endpoint_offers_no_ticket_tool_and_refuses_upload_ids()
    {
        await using var host = await StartAsync(UploadTicketsMode.Off);

        var list = await host.PostMcpAsync("tools/list");
        var tools = list.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        Assert.DoesNotContain(tools, t => t.GetProperty("name").GetString() == "prepare_attachment");
        var submit = tools.Single(t => t.GetProperty("name").GetString() == "submit_order");
        Assert.False(submit.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("uploadIds", out _));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.CallMcpToolAsync("submit_order", new { request = Request, uploadIds = new[] { Guid.NewGuid().ToString() } }));
        Assert.Contains(UploadTicketLines.NotAvailable, ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, await host.QueryAsync(db => db.Orders.CountAsync()));
    }

    [Fact]
    public async Task A_rejected_attachment_over_the_endpoint_leaves_exactly_one_count_without_its_name()
    {
        await using var host = await StartAsync(UploadTicketsMode.On);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.CallMcpToolAsync("submit_order", new
        {
            request = Request,
            attachments = JsonSerializer.Serialize(new[]
            {
                new { filename = "Muster-Vertrag.md", contentType = "text/markdown", contentBase64 = Convert.ToBase64String("Siehe angehängte Vollversion"u8.ToArray()) },
            }),
        }));

        var row = await host.QueryAsync(db => db.Set<SourceRefusalEntity>().AsNoTracking().SingleAsync());
        Assert.Equal((RefusalPath.McpInline, SourceRefusalCodes.McpPlaceholder, "md"), (row.Path, row.Code, row.Extension));
        Assert.DoesNotContain("Muster", JsonSerializer.Serialize(row), StringComparison.Ordinal);
        Assert.Equal(0, await host.QueryAsync(db => db.Sources.CountAsync()));
    }

    private Task<AtelierEndToEndHost> StartAsync(UploadTicketsMode tickets) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.Configure<UploadOptions>(o => o.Tickets = tickets);
            services.Configure<OAuthOptions>(o => o.Issuer = Issuer);
        });

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Uri url, string token, byte[] content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url.PathAndQuery) { Content = new ByteArrayContent(content) };
        request.Headers.Add(UploadTicketRoutes.TokenHeader, token);
        return await client.SendAsync(request);
    }

    private static string Filler(int page) =>
        string.Concat(Enumerable.Repeat($"Seite {page} beschreibt den Ablauf des Projekts mit vielen einfachen Worten. ", 12));
}
