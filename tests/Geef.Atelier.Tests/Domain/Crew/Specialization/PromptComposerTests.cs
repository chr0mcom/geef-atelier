using Geef.Atelier.Core.Domain.Crew.Specialization;

namespace Geef.Atelier.Tests.Domain.Crew.Specialization;

public sealed class PromptComposerTests
{
    private static SpecializationPack Pack(string name, string text) => new(
        Name: name,
        DisplayName: name,
        Description: "",
        SpecializationText: text,
        Scope: PackScope.General,
        Domain: null,
        ApplicableActorTypes: [PackActorType.Any],
        OwningCrewId: null,
        IsSystem: false);

    [Fact]
    public void Compose_NoPacks_ReturnsRolePromptUnchanged()
    {
        var role = "You are a reviewer. {specialization}";
        var result = PromptComposer.Compose(role, []);
        Assert.Equal(role, result);
    }

    [Fact]
    public void Compose_WithSlot_ReplacesSlotWithBlock()
    {
        var role = "Role start.\n{specialization}\nRole end.";
        var result = PromptComposer.Compose(role, [Pack("a", "Alpha delta.")]);
        Assert.Equal("Role start.\nAlpha delta.\nRole end.", result);
        Assert.DoesNotContain(PromptComposition.SpecializationSlot, result);
    }

    [Fact]
    public void Compose_WithoutSlot_AppendsUnderHeading()
    {
        var role = "Generic role prompt.";
        var result = PromptComposer.Compose(role, [Pack("a", "Alpha delta.")]);
        Assert.Equal("Generic role prompt.\n\n## Specialization\nAlpha delta.", result);
    }

    [Fact]
    public void Compose_MultiplePacks_ConcatenatesInOrder()
    {
        var role = "Role. {specialization}";
        var result = PromptComposer.Compose(role, [Pack("a", "First."), Pack("b", "Second.")]);
        Assert.Equal("Role. First.\n\nSecond.", result);
        Assert.True(result.IndexOf("First.", StringComparison.Ordinal) < result.IndexOf("Second.", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_EmptyOrWhitespacePackText_IsSkipped()
    {
        var role = "Role. {specialization}";
        var result = PromptComposer.Compose(role, [Pack("a", "   "), Pack("b", "Only this.")]);
        Assert.Equal("Role. Only this.", result);
    }
}
