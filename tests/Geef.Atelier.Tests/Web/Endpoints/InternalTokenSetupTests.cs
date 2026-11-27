using Bunit;
using Geef.Atelier.Web.Components.Pages.Public;

namespace Geef.Atelier.Tests.Web.Endpoints;

/// <summary>
/// A new installation learns to set the one internal token of web app and gateway, Docker Compose passes it to both, and the example
/// file ships no value that could be taken over as it is.
/// </summary>
public sealed class InternalTokenSetupTests : TestContext
{
    private const string Setting = "ATELIER_INTERNAL_API_TOKEN";

    [Fact]
    public void The_example_file_names_the_setting_and_leaves_it_empty()
    {
        var lines = Read(".env.example").Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        Assert.Equal(Setting + "=", Assert.Single(lines, l => l.StartsWith(Setting, StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.StartsWith('#') && l.Contains("openssl rand -hex 32", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_passes_the_one_value_to_the_web_app_and_the_gateway()
    {
        var compose = Read("docker-compose.yml");

        Assert.Contains($"CliProxy__InternalApiToken=${{{Setting}:-}}", compose, StringComparison.Ordinal);
        Assert.Contains($"- {Setting}=${{{Setting}:-}}", compose, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("README_de.md")]
    public void The_readme_says_how_to_set_it(string file) => Assert.Contains(Setting + "=<", Read(file), StringComparison.Ordinal);

    [Fact]
    public void The_self_host_page_says_how_to_set_it()
    {
        var cut = RenderComponent<SelfHost>();

        var quickStart = cut.Find(".sh-quickstart").TextContent;
        Assert.Contains($"openssl rand -hex 32          # put the result in .env as {Setting}", quickStart, StringComparison.Ordinal);
    }

    private static string Read(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir?.FullName ?? throw new InvalidOperationException("The repository root was not found."), file));
    }
}
