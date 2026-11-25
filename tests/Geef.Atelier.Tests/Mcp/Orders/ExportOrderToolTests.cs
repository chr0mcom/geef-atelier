using System.Text;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary><c>export_order</c> hands over the file of the export service unchanged and says plainly why there is none.</summary>
public sealed class ExportOrderToolTests
{
    private readonly FakeOrderService _orders = new();
    private readonly RecordingExports _exports = new();
    private readonly FakeUser _alice = new("alice");

    [Fact]
    public async Task Markdown_comes_as_file_and_as_the_same_text()
    {
        var id = Owned("alice");
        const string text = "# Einladung\n\nLiebe Eltern, der Elternabend ist am [Datum].\n";
        _exports.Next = new OrderExport(Encoding.UTF8.GetBytes(text), "text/markdown; charset=utf-8", "einladung-v2.md");

        var dto = await ExportOrderTool.ExportOrder(_orders, _exports, _alice, id.ToString(), "md");

        Assert.Equal(("einladung-v2.md", "text/markdown; charset=utf-8"), (dto.FileName, dto.ContentType));
        Assert.Equal(text, dto.Text);
        Assert.Equal(text, Encoding.UTF8.GetString(Convert.FromBase64String(dto.ContentBase64)));
        Assert.Equal((id, ExportFormat.Markdown, (int?)null, (string?)"alice"), Assert.Single(_exports.Calls));
    }

    [Theory]
    [InlineData("docx", ExportFormat.Docx)]
    [InlineData(" PDF ", ExportFormat.Pdf)]
    public async Task Word_and_pdf_come_as_the_bytes_of_the_export_without_text(string format, ExportFormat expected)
    {
        var id = Owned("alice");
        byte[] content = [0x50, 0x4B, 0x03, 0x04, 0x00, 0xFF, 0x10];
        _exports.Next = new OrderExport(content, "application/octet-stream", "brief-v1.bin");

        var dto = await ExportOrderTool.ExportOrder(_orders, _exports, _alice, id.ToString(), format, version: 1);

        Assert.Equal(content, Convert.FromBase64String(dto.ContentBase64));
        Assert.Null(dto.Text);
        Assert.Equal((id, expected, (int?)1, (string?)"alice"), Assert.Single(_exports.Calls));
    }

    [Fact]
    public async Task Without_format_the_file_is_markdown()
    {
        var id = Owned("alice");
        _exports.Next = new OrderExport("Text"u8.ToArray(), "text/markdown; charset=utf-8", "text-v1.md");

        var dto = await ExportOrderTool.ExportOrder(_orders, _exports, _alice, id.ToString());

        Assert.Equal("Text", dto.Text);
        Assert.Equal(ExportFormat.Markdown, Assert.Single(_exports.Calls).Format);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other")]
    public async Task A_foreign_or_unknown_order_is_not_found_and_nothing_is_exported(string? owner)
    {
        var id = owner is null ? Guid.NewGuid() : Owned(owner);
        _exports.Next = new OrderExport("secret"u8.ToArray(), "text/markdown", "x-v1.md");

        var ex = await Assert.ThrowsAsync<McpException>(() => ExportOrderTool.ExportOrder(_orders, _exports, _alice, id.ToString(), "md"));

        Assert.Equal("Order not found.", ex.Message);
        Assert.Empty(_exports.Calls);
    }

    [Fact]
    public async Task A_foreign_order_with_a_wrong_format_says_only_not_found()
    {
        var id = Owned("other");

        var ex = await Assert.ThrowsAsync<McpException>(() => ExportOrderTool.ExportOrder(_orders, _exports, _alice, id.ToString(), "html"));

        Assert.Equal("Order not found.", ex.Message);
    }

    [Fact]
    public async Task An_unknown_format_names_the_ones_that_work()
    {
        var id = Owned("alice");

        var ex = await Assert.ThrowsAsync<McpException>(() => ExportOrderTool.ExportOrder(_orders, _exports, _alice, id.ToString(), "html"));

        Assert.Equal("Unknown format \"html\". Use md, docx or pdf.", ex.Message);
        Assert.Empty(_exports.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_version_below_one_is_refused(int version)
    {
        var id = Owned("alice");

        var ex = await Assert.ThrowsAsync<McpException>(() => ExportOrderTool.ExportOrder(_orders, _exports, _alice, id.ToString(), "md", version));

        Assert.Equal(ExportOrderTool.VersionMessage, ex.Message);
        Assert.Empty(_exports.Calls);
    }

    [Fact]
    public async Task Without_a_text_the_answer_says_so()
    {
        var id = Owned("alice");

        var shown = await Assert.ThrowsAsync<McpException>(() => ExportOrderTool.ExportOrder(_orders, _exports, _alice, id.ToString(), "md"));
        var version = await Assert.ThrowsAsync<McpException>(() => ExportOrderTool.ExportOrder(_orders, _exports, _alice, id.ToString(), "docx", 3));

        Assert.Equal("This order has no text to download yet.", shown.Message);
        Assert.Equal("Version 3 of this order has no text to download.", version.Message);
    }

    [Fact]
    public async Task An_administrator_exports_without_owner_filter()
    {
        var id = Owned("bob");
        _exports.Next = new OrderExport("Text"u8.ToArray(), "text/markdown", "t-v1.md");

        await ExportOrderTool.ExportOrder(_orders, _exports, new FakeUser("root", isAdmin: true), id.ToString(), "md");

        Assert.Null(Assert.Single(_exports.Calls).RequestingUsername);
    }

    private Guid Owned(string owner)
    {
        var id = Guid.NewGuid();
        _orders.Add(owner, OrderToolTestData.View(id, OrderDisplayState.Delivered));
        return id;
    }

    private sealed class RecordingExports : IOrderExportService
    {
        public OrderExport? Next { get; set; }

        public List<(Guid OrderId, ExportFormat Format, int? Version, string? RequestingUsername)> Calls { get; } = [];

        public Task<OrderExport?> ExportAsync(Guid orderId, ExportFormat format, int? version, string? requestingUsername, CancellationToken ct = default)
        {
            Calls.Add((orderId, format, version, requestingUsername));
            return Task.FromResult(Next);
        }
    }
}
