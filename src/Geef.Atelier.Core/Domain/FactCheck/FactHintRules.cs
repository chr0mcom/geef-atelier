namespace Geef.Atelier.Core.Domain.FactCheck;

/// <summary>
/// Why a finding about a passage that is not backed is shown as a hint instead of a blocking finding: a sentence with a gap to fill
/// in (<see cref="GapSentence"/>) or a short general closing sentence near the end of the text (<see cref="GeneralClosing"/>).
/// </summary>
public enum FactHint { GapSentence = 0, GeneralClosing = 1 }

/// <summary>
/// Which hint rules <see cref="FactFindingFilter.Apply"/> applies to findings of kind <see cref="FactFindingKind.Unsupported"/>
/// after all other rules (<c>Direct:GapSentenceHint</c>, <c>Direct:ClosingSentenceHint</c>). The default applies none.
/// </summary>
public readonly record struct FactHintRules(bool GapSentence, bool GeneralClosing);
