using System.Text.Json;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Repositories;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Infrastructure.Persistence;

[Collection("Postgres")]
public sealed class ToolDefinitionRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private ToolDefinitionRepository Repo() => new(fixture.NewContext());

    // ── helpers ─────────────────────────────────────────────────────────────

    private static ToolDefinition BuildTool(
        string? name = null,
        bool isSystem = false,
        string toolType = "web-search")
    {
        var uniqueSuffix = name ?? Guid.NewGuid().ToString("N")[..8];
        return new ToolDefinition(
            Name: $"test-tool-{uniqueSuffix}",
            DisplayName: "Test Tool",
            Description: "A tool used in tests.",
            ToolType: toolType,
            Settings: new Dictionary<string, string> { ["key"] = "value" }.AsReadOnly(),
            SecretRef: null,
            LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: isSystem);
    }

    // ── GetByNameAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByNameAsync_ExistingTool_ReturnsDomainRecord()
    {
        var tool = BuildTool();
        await Repo().UpsertAsync(tool);

        var result = await Repo().GetByNameAsync(tool.Name);

        Assert.NotNull(result);
        Assert.Equal(tool.Name, result!.Name);
        Assert.Equal(tool.DisplayName, result.DisplayName);
        Assert.Equal(tool.Description, result.Description);
        Assert.Equal(tool.ToolType, result.ToolType);
        Assert.Equal(tool.IsSystem, result.IsSystem);
        Assert.Equal(tool.AccessClass, result.AccessClass);
    }

    [Fact]
    public async Task GetByNameAsync_UnknownName_ReturnsNull()
    {
        var result = await Repo().GetByNameAsync("does-not-exist-xyz");

        Assert.Null(result);
    }

    // ── GetSystemToolsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetSystemToolsAsync_ReturnsOnlySystemTools()
    {
        var system = BuildTool(name: "sys-" + Guid.NewGuid().ToString("N")[..6], isSystem: true);
        var custom = BuildTool(name: "cus-" + Guid.NewGuid().ToString("N")[..6], isSystem: false);

        await Repo().UpsertAsync(system);
        await Repo().UpsertAsync(custom);

        var results = await Repo().GetSystemToolsAsync();

        Assert.Contains(results, t => t.Name == system.Name);
        Assert.DoesNotContain(results, t => t.Name == custom.Name);
        Assert.All(results, t => Assert.True(t.IsSystem));
    }

    // ── GetCustomToolsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetCustomToolsAsync_ReturnsOnlyCustomTools()
    {
        var system = BuildTool(name: "sys2-" + Guid.NewGuid().ToString("N")[..6], isSystem: true);
        var custom = BuildTool(name: "cus2-" + Guid.NewGuid().ToString("N")[..6], isSystem: false);

        await Repo().UpsertAsync(system);
        await Repo().UpsertAsync(custom);

        var results = await Repo().GetCustomToolsAsync();

        Assert.Contains(results, t => t.Name == custom.Name);
        Assert.DoesNotContain(results, t => t.Name == system.Name);
        Assert.All(results, t => Assert.False(t.IsSystem));
    }

    // ── UpsertAsync — insert ─────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_NewTool_Adds()
    {
        var tool = BuildTool();

        await Repo().UpsertAsync(tool);

        var stored = await Repo().GetByNameAsync(tool.Name);
        Assert.NotNull(stored);
        Assert.Equal(tool.Name, stored!.Name);
    }

    // ── UpsertAsync — update ─────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_ExistingTool_Updates()
    {
        var tool = BuildTool();
        await Repo().UpsertAsync(tool);

        var updated = tool with { DisplayName = "Updated Display Name", Description = "Updated description." };
        await Repo().UpsertAsync(updated);

        var stored = await Repo().GetByNameAsync(tool.Name);
        Assert.NotNull(stored);
        Assert.Equal("Updated Display Name", stored!.DisplayName);
        Assert.Equal("Updated description.", stored.Description);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingTool_RemovesIt()
    {
        var tool = BuildTool();
        await Repo().UpsertAsync(tool);

        await Repo().DeleteAsync(tool.Name);

        var stored = await Repo().GetByNameAsync(tool.Name);
        Assert.Null(stored);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentTool_DoesNotThrow()
    {
        // Should be a no-op, not throw.
        await Repo().DeleteAsync("ghost-tool-xyz");
    }
}
