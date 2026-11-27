using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp.Forms;
using Geef.Atelier.Mcp.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Mcp.Forms;

/// <summary>The <c>requestState</c> of the form (D-088): signed and encrypted by the server, bound to order, user, client and call, and expiring.</summary>
public sealed class OrderFormStatesTests
{
    private readonly SteppedTime _time = new(new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly EphemeralDataProtectionProvider _keys = new();

    private OrderFormStates States(TimeSpan? lifetime = null) =>
        new(_keys, _time, Options.Create(new McpOptions { ElicitationLifetime = lifetime ?? TimeSpan.FromMinutes(30) }));

    private static OrderFormState Sample(OrderFormStates states) =>
        states.Create(Guid.NewGuid(), "alice", "client-1", OrderFormStates.HashArguments("Schreib eine Mail.", null, false, null), ["length", "q1"],
            [new OrderSourceDto("angebot.pdf", 2, 1200, "good", ["Seite 2 ist schwer lesbar."])], ["Eine Datei war doppelt."]);

    [Fact]
    public void A_state_round_trips_without_showing_what_it_holds()
    {
        var states = States();
        var state = Sample(states);

        var protectedState = states.Protect(state);
        var (check, read) = states.Read(protectedState);

        Assert.Equal(OrderFormStateCheck.Valid, check);
        Assert.Equal(state with { Questions = read!.Questions, Sources = read.Sources, Notices = read.Notices }, read);
        Assert.Equal(state.Questions, read.Questions);
        Assert.Equal(state.Sources.Single(), read.Sources.Single() with { Warnings = state.Sources.Single().Warnings });
        Assert.Equal(state.Sources.Single().Warnings, read.Sources.Single().Warnings);
        Assert.Equal(state.Notices, read.Notices);
        Assert.DoesNotContain("alice", protectedState, StringComparison.Ordinal);
        Assert.DoesNotContain(state.OrderId.ToString(), protectedState, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("angebot", protectedState, StringComparison.Ordinal);
    }

    [Fact]
    public void A_state_expires_after_the_lifetime()
    {
        var states = States(TimeSpan.FromMinutes(5));
        var protectedState = states.Protect(Sample(states));

        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(OrderFormStateCheck.Valid, states.Read(protectedState).Check);
        _time.Advance(TimeSpan.FromMinutes(1));
        var (check, state) = states.Read(protectedState);
        Assert.Equal(OrderFormStateCheck.Expired, check);
        Assert.NotNull(state);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("order-123")]
    [InlineData("eyJvcmRlcklkIjoiMDAwMDAwMDAtMDAwMC0wMDAwLTAwMDAtMDAwMDAwMDAwMDAwIn0")]
    public void A_forged_state_is_invalid(string? forged)
    {
        Assert.Equal((OrderFormStateCheck.Invalid, null), States().Read(forged));
    }

    [Fact]
    public void A_changed_state_is_invalid()
    {
        var states = States();
        var protectedState = states.Protect(Sample(states));
        var changed = protectedState[..^3] + (protectedState[^3] == 'A' ? 'B' : 'A') + protectedState[^2..];

        Assert.Equal(OrderFormStateCheck.Invalid, states.Read(changed).Check);
    }

    [Fact]
    public void A_state_of_another_purpose_or_key_ring_is_invalid()
    {
        var states = States();
        var json = System.Text.Json.JsonSerializer.Serialize(Sample(states), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var otherPurpose = _keys.CreateProtector("Geef.Atelier.Something.Else").Protect(json);
        var otherKeys = new OrderFormStates(new EphemeralDataProtectionProvider(), _time, Options.Create(new McpOptions())).Protect(Sample(states));

        Assert.Equal(OrderFormStateCheck.Invalid, states.Read(otherPurpose).Check);
        Assert.Equal(OrderFormStateCheck.Invalid, states.Read(otherKeys).Check);
    }

    [Fact]
    public void The_hash_of_the_arguments_changes_with_every_argument()
    {
        var baseline = OrderFormStates.HashArguments("Schreib eine Mail.", null, false, null);

        Assert.Equal(baseline, OrderFormStates.HashArguments("Schreib eine Mail.", null, false, []));
        Assert.NotEqual(baseline, OrderFormStates.HashArguments("Schreib eine Mail!", null, false, null));
        Assert.NotEqual(baseline, OrderFormStates.HashArguments("Schreib eine Mail.", "[]", false, null));
        Assert.NotEqual(baseline, OrderFormStates.HashArguments("Schreib eine Mail.", null, true, null));
        Assert.NotEqual(baseline, OrderFormStates.HashArguments("Schreib eine Mail.", null, false, [Guid.Empty.ToString()]));
    }

    [Fact]
    public void Forms_are_off_by_default_and_in_the_shipped_settings()
    {
        Assert.Equal(McpElicitationMode.Off, new McpOptions().Elicitation);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Geef.Atelier.slnx")))
            root = root.Parent;
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "src", "Geef.Atelier.Web", "appsettings.json")));
        Assert.Equal("Off", document.RootElement.GetProperty("Mcp").GetProperty("Elicitation").GetString());
    }

    internal sealed class SteppedTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
