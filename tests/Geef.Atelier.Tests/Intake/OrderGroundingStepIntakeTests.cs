using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Tests.Domain.Intake;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Pipeline.Direct;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Intake;

public sealed class OrderGroundingStepIntakeTests
{
    private static string ClarifyJson(string language = "de") => IntakeRecordJson.Serialize(new IntakeRecord(
        IntakeRecord.CurrentSchemaVersion, IntakeRecordStatus.Ok, "m", 1, null, QuestionGateTests.Output([], language), [],
        [QuestionTemplates.Length(language, LengthScale.Standard)], [], [], [], null, BriefMode.AppendAnswers));

    private static string Answers(params OrderAnswer[] answers) => IntakeRecordJson.SerializeAnswers(answers);

    private static readonly OrderAnswer Short = new("length", "short", null, false, AnswerOrigin.User);

    [Fact]
    public void An_unclarified_order_keeps_the_constraints_of_the_request()
    {
        var orders = new IntakeOrders();
        var order = orders.Add("Schreib eine Einladung, etwa 200 Wörter.");

        Assert.Equivalent(RequestConstraintExtractor.Extract(order.RequestText), OrderGroundingStep.Constraints(order), strict: true);
    }

    [Fact]
    public void The_answered_length_becomes_the_length_check()
    {
        var order = new IntakeOrders().Add("Schreib eine Einladung zum Sommerfest.") with { ClarifyJson = ClarifyJson(), AnswersJson = Answers(Short) };

        var length = OrderGroundingStep.Constraints(order).Length;

        Assert.Equal(new LengthConstraint(LengthUnit.Words, 150, 150, "Wie lang soll der Text ungefähr werden? → Kurz (etwa 150 Wörter)", LengthKind.About), length);
    }

    [Fact]
    public void The_length_of_the_request_wins_over_the_answer()
    {
        var order = new IntakeOrders().Add("Schreib eine Einladung, etwa 200 Wörter.") with { ClarifyJson = ClarifyJson(), AnswersJson = Answers(Short) };

        Assert.Equal(200, OrderGroundingStep.Constraints(order).Length?.Max);
    }

    [Fact]
    public void Without_answers_nothing_is_added()
    {
        var order = new IntakeOrders().Add("Schreib eine Einladung zum Sommerfest.") with { ClarifyJson = ClarifyJson() };

        Assert.Null(OrderGroundingStep.Constraints(order).Length);
    }

    [Fact]
    public async Task The_grounding_step_puts_the_answered_length_into_the_context()
    {
        var orders = new IntakeOrders();
        var order = orders.Add("Schreib eine Einladung zum Sommerfest.") with { ClarifyJson = ClarifyJson(), AnswersJson = Answers(Short) };
        orders.Set(order);
        var services = new ServiceCollection()
            .AddSingleton<IOrderRepository>(orders)
            .AddSingleton<ISourceRepository>(new InMemorySourceRepository())
            .BuildServiceProvider();
        var step = new OrderGroundingStep(Guid.NewGuid(), order.Id, null, services.GetRequiredService<IServiceScopeFactory>(),
            new SourceOptions(), new InMemoryRunWarnings(), NullLogger<OrderGroundingStep>.Instance);

        var result = await step.RunAsync(order.RequestText, CancellationToken.None);

        Assert.Equal(150, result.Context.GetRequired(AtelierContextKeys.OrderConstraints).Length?.Min);
    }
}
