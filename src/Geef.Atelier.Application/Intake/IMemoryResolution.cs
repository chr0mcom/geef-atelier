using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;

namespace Geef.Atelier.Application.Intake;

/// <summary>What is known about a person before an order is clarified. Stage 2: only the profile.</summary>
public sealed record MemoryFacts(UserProfileEntity? Profile);

/// <summary>Answers gaps from what is known about the person. Stage 2: only the profile (language, sender, signature, formality). Stage 7 adds remembered answers per text type.</summary>
public interface IMemoryResolution
{
    /// <summary>Loads what is known about <paramref name="username"/>.</summary>
    Task<MemoryFacts> LoadAsync(string username, CancellationToken ct = default);

    /// <summary>The gaps <paramref name="facts"/> answer for a text of <paramref name="textType"/>; source is always <c>profile</c> in stage 2.</summary>
    IReadOnlyDictionary<GapTopic, MemoryAnswer> Resolve(MemoryFacts facts, string? textType);
}
