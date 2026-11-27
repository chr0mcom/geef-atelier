using System.Text.Json.Nodes;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>
/// Synthetic stand-in for older production snapshots (stage 8c, D-089): a snapshot that still carries the removed
/// <c>maxQueriesPerRun</c> field, grounding profiles of the retired types and an ExternalSink finalizer stays usable for a run;
/// the retired entries are kept as they are, so the run can skip them with a warning.
/// </summary>
public sealed class RetiredPartsSnapshotCompatibilityTests
{
    private static string OlderSnapshotJson()
    {
        var root = JsonNode.Parse(TestCrewSnapshots.KlassikJson)!.AsObject();
        root["groundingProviders"] = new JsonArray(
            Grounding("academic-default", "academic-search", isSystem: true),
            Grounding("tavily-news", "news-search", isSystem: true),
            Grounding("custom-own-api", "rest-api", isSystem: false),
            Grounding("custom-pages", "url-fetch", isSystem: false));
        root["finalizers"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "webhook-sink", ["displayName"] = "Webhook Sink", ["description"] = "d",
                ["finalizerType"] = 2, ["settings"] = new JsonObject { ["SinkKind"] = "webhook", ["Url"] = "https://hook.example" },
                ["isSystem"] = true,
            },
            new JsonObject
            {
                ["name"] = "add-word-count-footer", ["displayName"] = "Word count", ["description"] = "d",
                ["finalizerType"] = 1, ["settings"] = new JsonObject { ["EnricherType"] = "word-count-footer" }, ["isSystem"] = true,
            });
        return root.ToJsonString();
    }

    private static JsonObject Grounding(string name, string type, bool isSystem) => new()
    {
        ["name"] = name, ["displayName"] = name, ["description"] = "d", ["providerType"] = type,
        ["providerSettings"] = new JsonObject(), ["maxQueriesPerRun"] = 1, ["isSystem"] = isSystem,
    };

    [Fact]
    public void An_older_snapshot_with_removed_parts_is_still_usable_for_a_run()
    {
        var snapshot = CrewSnapshot.DeserializeForRun(OlderSnapshotJson());

        Assert.Equal(
            ["academic-default", "tavily-news", "custom-own-api", "custom-pages"],
            snapshot.GroundingProviders!.Select(p => p.Name));
        Assert.Equal(3, snapshot.GroundingProviders!.Count(p => p.ProviderType.IsRetiredGroundingType()));
        Assert.False(snapshot.GroundingProviders!.Single(p => p.Name == "custom-pages").ProviderType.IsRetiredGroundingType());
        Assert.True(snapshot.Finalizers!.Single(f => f.Name == "webhook-sink").FinalizerType.IsRetired());
        Assert.Equal(FinalizerType.MetadataEnrich, snapshot.Finalizers!.Single(f => f.Name == "add-word-count-footer").FinalizerType);
    }
}
