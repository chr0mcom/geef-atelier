using System.Globalization;
using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Models;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary>
/// <c>get_order_result</c> after a change (stage 5): the shown and the newest version, open points of the version before and passages
/// carried over from it reach the client, and the note never lets a client take an earlier text for the new one.
/// </summary>
public sealed class GetOrderResultVersionsTests
{
    private const string V1Text = "Liebe Eltern, der Elternabend ist am 12. Mai um 19 Uhr.";

    private static readonly OrderChecksView ReviewChecks = new(
        [OrderCheckLines.AutomaticLead + OrderCheckLines.AutomaticEnd, OrderCheckLines.FactsChecked], [], OrderCheckLines.ToReviewOpenPoints, OrderCheckLines.Limits);

    private readonly FakeOrderService _orders = new();
    private readonly FakeUser _alice = new("alice");

    [Fact]
    public async Task The_newest_version_open_points_of_the_version_before_and_carried_passages_reach_the_client()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Delivered, V1Text) with
        {
            Version = 2,
            LatestVersion = 2,
            EarlierOpenPoints = [new OrderEarlierPoint(1, "Der Raum fehlt in den Notizen."), new OrderEarlierPoint(1, "  ")],
            UnbackedPassages =
            [
                new OrderUnbackedPassage("um 19 Uhr", FactFindingKind.Unsupported, null, true, FromVersion: 1),
                new OrderUnbackedPassage("am 12. Mai", FactFindingKind.Unsupported, null, true),
            ],
            FactCheck = FactCheckState.OpenPoints,
            Checks = ReviewChecks,
        });

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal((2, 2), (dto.Version, dto.LatestVersion));
        Assert.Equal([new OrderEarlierPointDto(1, "Der Raum fehlt in den Notizen.")], dto.EarlierOpenPoints!);
        Assert.Equal(1, dto.UnbackedPassages!.Single(p => p.Quote == "um 19 Uhr").FromVersion);
        Assert.Null(dto.UnbackedPassages!.Single(p => p.Quote == "am 12. Mai").FromVersion);
        Assert.Contains(OrderDtoMappings.ReviewOpenPointsNote, dto.Note);
        Assert.Contains("earlierOpenPoints", OrderDtoMappings.ReviewOpenPointsNote, StringComparison.Ordinal);
        Assert.Contains(OrderDtoMappings.EarlierPointsNote, dto.Note);
        Assert.Contains(OrderDtoMappings.UncheckedContentNote, dto.Note);
    }

    [Fact]
    public async Task Open_points_of_the_version_before_alone_still_ask_the_client_to_show_them_as_content()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Delivered, V1Text) with
        {
            Version = 2,
            LatestVersion = 2,
            EarlierOpenPoints = [new OrderEarlierPoint(1, "Bitte die Uhrzeit prüfen.")],
            FactCheck = FactCheckState.Checked,
            Checks = ReviewChecks,
        });

        var note = (await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString())).Note!;

        Assert.Contains(OrderDtoMappings.EarlierPointsNote, note);
        Assert.EndsWith(OrderDtoMappings.UncheckedContentNote, note);
    }

    [Fact]
    public async Task While_a_new_version_is_written_the_earlier_text_comes_with_the_sentence_and_the_next_step()
    {
        var id = Guid.NewGuid();
        var sentence = string.Format(CultureInfo.InvariantCulture, RevisionLines.StillWritingFormat, 1);
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Working, V1Text, note: sentence) with
        {
            LatestVersion = 2,
            FactCheck = FactCheckState.Checked,
            Checks = ReviewChecks,
        });

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal(("working", V1Text, 1, 2), (dto.State, dto.Text, dto.Version, dto.LatestVersion));
        Assert.StartsWith(sentence, dto.Note, StringComparison.Ordinal);
        Assert.Contains(OrderDtoMappings.NewVersionNote, dto.Note);
        Assert.Contains(OrderDtoMappings.ShowChecksNote, dto.Note);
        Assert.NotNull(dto.Checks);
    }

    [Fact]
    public async Task When_the_new_version_could_not_be_finished_the_note_says_which_version_the_text_is()
    {
        var id = Guid.NewGuid();
        var sentence = string.Format(CultureInfo.InvariantCulture, RevisionLines.NotFinishedFormat, 2, 1);
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Failed, V1Text, note: sentence) with { LatestVersion = 2 });

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal(("failed", V1Text, 1, 2), (dto.State, dto.Text, dto.Version, dto.LatestVersion));
        Assert.StartsWith("Version 2 could not be finished. This is version 1.", dto.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(OrderDtoMappings.NewVersionNote, dto.Note);
    }

    [Fact]
    public async Task A_working_order_without_an_earlier_text_keeps_its_short_note()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Working, null, note: "The text is not ready yet.") with { LatestVersion = 1 });

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal("The text is not ready yet.", dto.Note);
    }

    [Fact]
    public async Task Results_without_the_new_fields_keep_them_null()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Delivered, V1Text) with
        {
            UnbackedPassages = [new OrderUnbackedPassage("um 19 Uhr", FactFindingKind.Unsupported, null, true)],
        });

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Null(dto.LatestVersion);
        Assert.Null(dto.EarlierOpenPoints);
        Assert.Null(Assert.Single(dto.UnbackedPassages!).FromVersion);
        Assert.DoesNotContain(OrderDtoMappings.EarlierPointsNote, dto.Note ?? string.Empty);
    }

    [Fact]
    public void An_answer_of_an_older_server_reads_with_the_new_fields_null()
    {
        const string old = """
            {"orderId":"1","state":"delivered","text":"T","wordCount":1,"version":1,"placeholders":[],"openChecks":[],"sourcesUsed":[],"warnings":[],"note":null,
             "unbackedPassages":[{"quote":"q","kind":"not_backed","instruction":null,"blocking":true}]}
            """;

        var dto = JsonSerializer.Deserialize<OrderResultDto>(old, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Null(dto.LatestVersion);
        Assert.Null(dto.EarlierOpenPoints);
        Assert.Null(Assert.Single(dto.UnbackedPassages!).FromVersion);
    }

    [Fact]
    public void The_description_explains_versions_and_earlier_open_points()
    {
        Assert.Contains(GetOrderResultTool.VersionRule, GetOrderResultTool.ToolDescription, StringComparison.Ordinal);
        Assert.Contains("latestVersion", GetOrderResultTool.VersionRule, StringComparison.Ordinal);
        Assert.Contains("earlierOpenPoints", GetOrderResultTool.ContentRule, StringComparison.Ordinal);
    }
}
