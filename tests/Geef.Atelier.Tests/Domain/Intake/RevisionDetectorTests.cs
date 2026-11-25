using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

public sealed class RevisionDetectorTests
{
    private static readonly RevisionCandidate Letter = new(1, Guid.NewGuid(), "brief.docx");
    private static readonly RevisionCandidate Notes = new(2, Guid.NewGuid(), "notizen.txt");

    [Theory]
    [InlineData("Bitte überarbeite den angehängten Brief.")]
    [InlineData("Überarbeitung des Textes, bitte.")]
    [InlineData("Kannst du das verbessern?")]
    [InlineData("Kürze den Text auf das Wesentliche.")]
    [InlineData("Bitte korrigiere die Rechtschreibung.")]
    [InlineData("Redigiere den Artikel.")]
    [InlineData("Glätte die Formulierungen.")]
    [InlineData("Schärfe die Argumente.")]
    [InlineData("Schreib den Absatz um, nein: umschreiben bitte.")]
    [InlineData("Formuliere den Text freundlicher um.")]
    [InlineData("Bitte umformulieren.")]
    [InlineData("Lektoriere das Kapitel.")]
    [InlineData("Prüfe den Text und verbessere ihn.")]
    [InlineData("Please revise the attached letter.")]
    [InlineData("Improve my cover letter.")]
    [InlineData("Shorten it to one page.")]
    [InlineData("Can you rewrite this?")]
    [InlineData("Edit the draft.")]
    [InlineData("Proofreading needed.")]
    [InlineData("Polish the wording.")]
    [InlineData("Tighten the intro.")]
    [InlineData("Rephrasing the summary would help.")]
    public void Recognises_revision_verbs(string request) => Assert.True(RevisionDetector.MentionsRevision(request));

    [Theory]
    [InlineData("Schreib eine Einladung zum Sommerfest.")]
    [InlineData("Wir haben kürzlich umgebaut, schreib einen Post dazu.")]
    [InlineData("Write an editorial about city parks.")]
    [InlineData("Formuliere eine Einladung. Sie gilt um acht Uhr.")]
    [InlineData("")]
    public void Ignores_requests_without_a_revision_verb(string request) => Assert.False(RevisionDetector.MentionsRevision(request));

    [Fact]
    public void Contains_the_listed_verbs()
    {
        Assert.Equal(20, RevisionDetector.Verbs.Count);
        Assert.Contains("formulier … um", RevisionDetector.Verbs);
    }

    [Fact]
    public void Without_a_verb_no_file_is_chosen_even_if_the_model_says_so()
    {
        Assert.Null(RevisionDetector.Choose("Schreib einen neuen Brief.", [Letter], 1, true));
    }

    [Fact]
    public void Without_files_nothing_is_chosen()
    {
        Assert.Null(RevisionDetector.Choose("Überarbeite den Brief.", [], 1, true));
    }

    [Theory]
    [InlineData("Überarbeite den Brief.", null)]
    [InlineData("Überarbeite den Brief.", 1)]
    [InlineData("Please proofread and polish the attached letter.", null)]
    [InlineData("Kürze den angehängten Text auf die Hälfte.", 1)]
    [InlineData("Improve the attached cover letter, keep its length.", null)]
    public void With_one_file_that_file_is_chosen_when_verb_and_model_agree(string request, int? choice)
    {
        Assert.Equal(Letter, RevisionDetector.Choose(request, [Letter], choice, true));
    }

    [Theory]
    [InlineData("Schreib eine E-Mail an mein Team, wie wir unsere Meetings verbessern können. Meine Notizen hängen an.")]
    [InlineData("Write a proposal to improve our onboarding. My notes are attached.")]
    [InlineData("Write a cover letter for the Video Edit position. The job ad is attached.")]
    [InlineData("Schreib auf Basis der Datei eine Pressemitteilung, dass wir die Öffnungszeiten kürzen.")]
    [InlineData("Überarbeite den Brief.")]
    public void With_one_file_nothing_is_chosen_without_the_models_judgement(string request)
    {
        Assert.Null(RevisionDetector.Choose(request, [Letter], null, false));
        Assert.Null(RevisionDetector.Choose(request, [Letter], 1, false));
    }

    [Fact]
    public void With_several_files_the_valid_model_choice_wins()
    {
        Assert.Equal(Notes, RevisionDetector.Choose("Überarbeite die Notizen.", [Letter, Notes], 2, true));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(3, true)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    public void With_several_files_and_no_valid_choice_nothing_is_chosen(int? choice, bool modelSaysRevision)
    {
        Assert.Null(RevisionDetector.Choose("Überarbeite die Notizen.", [Letter, Notes], choice, modelSaysRevision));
    }
}
