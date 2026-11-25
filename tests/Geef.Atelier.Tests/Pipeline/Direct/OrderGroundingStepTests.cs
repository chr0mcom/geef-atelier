using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Runs;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Orders;
using Geef.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// <see cref="OrderGroundingStep"/> for the checks of stage 3: the material of the fact check (never assumptions or notes), the mode of
/// every run, the scope above the size limit and the recalculated totals of the files.
/// </summary>
public sealed class OrderGroundingStepTests
{
    private const string Request = "Schreib einen Elternbrief aus den Notizen und nenne kurz die Gesamtkosten.";

    private const string ParentNotes = """
        Elternabend Klasse 3b – Notizen
        Materialkosten je Kind:
        Bastelpapier 22 €
        Farben 31 €
        Ausflug 19 €
        Summe: 62 €
        """;

    private sealed class Setup
    {
        public InMemoryOrderRepository Orders { get; } = new();
        public InMemorySourceRepository Sources { get; } = new();
        public RecordingRunFactCheckModeStore Modes { get; } = new();
        public DirectOptions Options { get; } = new();
        public Guid RunId { get; } = Guid.NewGuid();

        public OrderGroundingStep Step(Guid orderId, string? seedDraft = null)
        {
            var services = new ServiceCollection()
                .AddSingleton<IOrderRepository>(Orders)
                .AddSingleton<ISourceRepository>(Sources)
                .AddSingleton<IRunFactCheckModeStore>(Modes)
                .AddSingleton<IOptions<DirectOptions>>(_ => Microsoft.Extensions.Options.Options.Create(Options))
                .BuildServiceProvider();
            return new OrderGroundingStep(RunId, orderId, seedDraft, services.GetRequiredService<IServiceScopeFactory>(), new SourceOptions(),
                new InMemoryRunWarnings(), NullLogger<OrderGroundingStep>.Instance);
        }
    }

    private static string ComposedBriefing()
    {
        var composer = new IntakeBriefComposer(Microsoft.Extensions.Options.Options.Create(new IntakeOptions()));
        var record = IntakeBriefComposerTests.Record() with
        {
            Assumptions =
            [
                new IntakeAssumption("a1", GapTopic.Tone, "Lockerer, herzlicher Ton", AssumptionConfidence.Unsure),
                new IntakeAssumption("a2", GapTopic.Facts, "Fotos liegen als Anlage bei", AssumptionConfidence.Unsure),
                new IntakeAssumption("a3", GapTopic.Date, "Datum als Platzhalter", AssumptionConfidence.Sure),
            ],
        };
        IReadOnlyList<OrderAnswer> answers =
        [
            new("length", "short", null, false, AnswerOrigin.User),
            new("q1", "o2", "Bitte mit Grill", false, AnswerOrigin.Client),
            new("a3", null, "Am 12. Juli", false, AnswerOrigin.User),
        ];
        var profile = new UserProfileEntity { Username = "e2e-user", DisplayName = "Anna Beispiel", NoGos = "Keine Emojis", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        return composer.Compose(new OrderBriefInput(OrderTestData.Order("e2e-user", Request), record, answers, profile, AssumptionsShown: true)).Text;
    }

    private static IEnumerable<string> AllText(FactMaterial m) =>
        new[] { m.Request, m.Profile ?? string.Empty, m.FileMap ?? string.Empty }
            .Concat(m.PersonAnswers).Concat(m.AssistantAnswers).Concat(m.Files.Select(f => f.Text));

    [Fact]
    public async Task The_material_has_request_answers_profile_and_files_but_no_assumption()
    {
        var setup = new Setup();
        var order = setup.Orders.Add(Request);
        setup.Sources.Attach(order.Id, "notizen.txt", SourceKind.Text, ParentNotes);
        var briefing = ComposedBriefing();

        var result = await setup.Step(order.Id).RunAsync(briefing, CancellationToken.None);

        var material = result.Context.GetRequired(AtelierContextKeys.FactMaterial);
        Assert.Equal(Request, material.Request);
        Assert.Equal(["Wie lang soll der Text ungefähr werden? → Kurz (etwa 150 Wörter)", "Am 12. Juli"], material.PersonAnswers);
        Assert.Equal(["Wer kommt? → Ganzes Team – \"Bitte mit Grill\""], material.AssistantAnswers);
        Assert.Contains("Never do this: Keine Emojis", material.Profile, StringComparison.Ordinal);
        var file = Assert.Single(material.Files);
        Assert.Equal(("S1", "notizen.txt"), (file.Id, file.FileName));
        Assert.Contains("Summe: 62 €", file.Text, StringComparison.Ordinal);
        Assert.Equal(FactCheckScope.Full, material.Scope);
        foreach (var assumption in new[] { "Lockerer", "Fotos liegen", "Datum als Platzhalter", "Einladung zum Sommerfest des Teams" })
        {
            Assert.DoesNotContain(AllText(material), t => t.Contains(assumption, StringComparison.Ordinal));
            Assert.DoesNotContain(assumption, FactCheckPrompts.User(material, "Ein Text."), StringComparison.Ordinal);
        }
        Assert.Contains(briefing, result.Context.GetRequired(AtelierContextKeys.GroundedBrief), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revision_adds_the_text_to_revise_as_S0_before_the_files()
    {
        var setup = new Setup();
        var order = setup.Orders.Add("Überarbeite den Brief.");
        setup.Sources.Attach(order.Id, "brief.md", SourceKind.Markdown, "Sehr geehrte Damen und Herren, der Termin ist am 3. März.", role: SourceRole.TemplateToRevise);
        setup.Sources.Attach(order.Id, "notizen.txt", SourceKind.Text, ParentNotes);

        var result = await setup.Step(order.Id).RunAsync("Überarbeite den Brief.", CancellationToken.None);

        var material = result.Context.GetRequired(AtelierContextKeys.FactMaterial);
        Assert.Equal(["S0", "S1"], material.Files.Select(f => f.Id));
        Assert.Equal(("brief.md", "Sehr geehrte Damen und Herren, der Termin ist am 3. März."), (material.Files[0].FileName, material.Files[0].Text));
        Assert.Equal("notizen.txt", material.Files[1].FileName);
    }

    [Fact]
    public async Task An_earlier_version_of_the_text_is_never_material()
    {
        var setup = new Setup();
        var order = setup.Orders.Add(Request);
        setup.Sources.Attach(order.Id, "notizen.txt", SourceKind.Text, ParentNotes);

        var result = await setup.Step(order.Id, seedDraft: "Liebe Eltern, wir treffen uns in der Gruppe.").RunAsync(Request, CancellationToken.None);

        Assert.Equal(["S1"], result.Context.GetRequired(AtelierContextKeys.FactMaterial).Files.Select(f => f.Id));
    }

    public static TheoryData<string, int, bool, FactCheckMode> EveryKindOfOrder()
    {
        var data = new TheoryData<string, int, bool, FactCheckMode>();
        foreach (var mode in Enum.GetValues<FactCheckMode>())
        {
            data.Add("Danke.", 0, false, mode);
            data.Add(Request, 1, false, mode);
            data.Add("Fasse den Bericht zusammen.", 1, true, mode);
            data.Add("Überarbeite den Brief.", 2, false, mode);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryKindOfOrder))]
    public async Task Every_run_gets_a_mode_and_it_is_stored(string request, int files, bool longFile, FactCheckMode global)
    {
        var setup = new Setup();
        setup.Options.FactCheckMode = global;
        var order = setup.Orders.Add(request);
        if (files >= 1)
            setup.Sources.Attach(order.Id, "datei.txt", SourceKind.Text, longFile ? string.Concat(Enumerable.Repeat("Ein langer Satz des Berichts. ", 8_000)) : ParentNotes);
        if (files >= 2)
            setup.Sources.Attach(order.Id, "brief.md", SourceKind.Markdown, "Alter Brief.", role: SourceRole.TemplateToRevise);

        var result = await setup.Step(order.Id).RunAsync(request, CancellationToken.None);

        Assert.Equal(global, result.Context.GetRequired(AtelierContextKeys.FactCheckMode));
        Assert.Equal(global, Assert.Single(setup.Modes.Modes).Value);
        Assert.Equal(setup.RunId, setup.Modes.Modes.Keys.Single());
        Assert.True(result.Context.TryGet(AtelierContextKeys.FactMaterial, out _));
        Assert.Contains(result.Notes, n => n.StartsWith($"fact-check mode={global}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("messnutzer", FactCheckMode.On)]
    [InlineData("MESSNUTZER", FactCheckMode.On)]
    [InlineData("jemand", FactCheckMode.Report)]
    [InlineData(null, FactCheckMode.Report)]
    public void The_override_of_the_person_wins_over_the_global_switch(string? user, FactCheckMode expected)
    {
        var options = new DirectOptions { FactCheckMode = FactCheckMode.Report };
        options.FactCheckModeOverrides["messnutzer"] = FactCheckMode.On;

        Assert.Equal(expected, OrderGroundingStep.Mode(options, user));
    }

    [Fact]
    public async Task The_override_reaches_the_run_of_that_person()
    {
        var setup = new Setup();
        setup.Options.FactCheckMode = FactCheckMode.Off;
        setup.Options.FactCheckModeOverrides["e2e-user"] = FactCheckMode.On;
        var order = setup.Orders.Add(Request);

        var result = await setup.Step(order.Id).RunAsync(Request, CancellationToken.None);

        Assert.Equal(FactCheckMode.On, result.Context.GetRequired(AtelierContextKeys.FactCheckMode));
        Assert.Equal(FactCheckMode.On, setup.Modes.Modes[setup.RunId]);
    }

    [Fact]
    public async Task Material_over_the_limit_is_checked_without_files_but_with_a_file_map()
    {
        var setup = new Setup();
        setup.Options.FactCheckMaxMaterialChars = 500;
        var order = setup.Orders.Add(Request);
        setup.Sources.Attach(order.Id, "bericht.txt", SourceKind.Text, string.Concat(Enumerable.Repeat("Ein langer Satz des Berichts. ", 40)));
        setup.Sources.Attach(order.Id, "notizen.txt", SourceKind.Text, ParentNotes);

        var result = await setup.Step(order.Id).RunAsync(Request, CancellationToken.None);

        var material = result.Context.GetRequired(AtelierContextKeys.FactMaterial);
        Assert.Equal(FactCheckScope.WithoutFiles, material.Scope);
        Assert.Contains("S1 \"bericht.txt\"", material.FileMap, StringComparison.Ordinal);
        Assert.Contains("S2 \"notizen.txt\"", material.FileMap, StringComparison.Ordinal);
        Assert.Equal(FactCheckScope.Full, OrderGroundingStep.Material(Request, material.Files, new DirectOptions()).Scope);
    }

    [Fact]
    public async Task The_totals_of_the_files_and_of_the_text_to_revise_are_recalculated()
    {
        var setup = new Setup();
        var order = setup.Orders.Add(Request);
        setup.Sources.Attach(order.Id, "brief.md", SourceKind.Markdown, "Kosten: 10 € + 5 € = 16 €", role: SourceRole.TemplateToRevise);
        setup.Sources.Attach(order.Id, "notizen.txt", SourceKind.Text, ParentNotes);

        var result = await setup.Step(order.Id).RunAsync(Request, CancellationToken.None);

        var totals = result.Context.GetRequired(AtelierContextKeys.SourceArithmetic);
        Assert.Equal(2, totals.Issues.Count);
        Assert.Contains(totals.Issues, i => i.SourceId == "S0" && i.Stated == "16 €");
        var notes = Assert.Single(totals.Issues, i => i.SourceId == "S1");
        Assert.Equal(("notizen.txt", "62 €", "72 €"), (notes.FileName, notes.Stated, notes.Computed));
    }

    [Fact]
    public async Task Without_the_switch_no_totals_are_recalculated()
    {
        var setup = new Setup();
        setup.Options.SourceArithmetic = false;
        var order = setup.Orders.Add(Request);
        setup.Sources.Attach(order.Id, "notizen.txt", SourceKind.Text, ParentNotes);

        var result = await setup.Step(order.Id).RunAsync(Request, CancellationToken.None);

        Assert.False(result.Context.TryGet(AtelierContextKeys.SourceArithmetic, out _));
    }

    [Fact]
    public async Task A_total_that_does_not_add_up_reaches_the_writer_prompt()
    {
        var harness = new DirectPipelineHarness();
        harness.DirectOptions.WriterNotes = true;
        var (run, orderId) = harness.CreateRun(Request);
        harness.Sources.Attach(orderId, "notizen.txt", SourceKind.Text, ParentNotes);
        harness.Script.ExecutorText = "Liebe Eltern, die Materialkosten betragen 72 € je Kind.\n\n<!-- atelier-notes\n{\"assumptions\": [], \"open_points\": [\"In den Notizen steht 62 €, die Posten ergeben 72 €.\"], \"quotes\": []}\n-->";

        var outcome = await harness.RunAsync(run);

        var prompt = Assert.Single(harness.WriterCalls).Messages.Last(m => m.Role == "user").Content!;
        Assert.Contains("Note: in S1 \"notizen.txt\" the total 62 € does not match 22 € + 31 € + 19 € = 72 €. Do not correct or adopt this silently; list it under \"open_points\".\nWrite the complete text into draft.md now.", prompt, StringComparison.Ordinal);
        Assert.Equal(3, Assert.Single(outcome.Result.FinalContext!.GetRequired(AtelierContextKeys.SourceArithmetic).Issues).Items.Count);
        Assert.Equal(WriterNotesStatus.Ok, outcome.Result.FinalContext!.GetRequired(AtelierContextKeys.WriterNotes).Status);
        Assert.Equal(FactCheckMode.Off, harness.FactCheckModes.Modes[run.Id]);
    }
}
