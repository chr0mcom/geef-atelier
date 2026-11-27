namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>Role semantics of <see cref="ModelSlot"/>.</summary>
public static class ModelSlotExtensions
{
    /// <summary>True for the slots that review drafts (including the helper used by lightweight reviewers).</summary>
    public static bool IsReviewerRole(this ModelSlot slot) =>
        slot is ModelSlot.FactReviewer or ModelSlot.StyleReviewer or ModelSlot.DomainReviewer or ModelSlot.Helper;
}
