using Microsoft.Extensions.Configuration;

namespace Geef.Atelier.Tests;

public sealed class ShippedLoggingTests
{
    [Fact]
    public void The_pipeline_events_stay_out_of_the_log_because_they_carry_the_whole_request()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Geef.Atelier.Web", "appsettings.json"))
            .Build();

        Assert.Equal("Warning", shipped["Logging:LogLevel:Geef.Atelier.Pipeline"]);
    }
}
