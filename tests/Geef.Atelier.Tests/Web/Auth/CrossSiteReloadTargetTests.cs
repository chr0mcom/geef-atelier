using Geef.Atelier.Web.Auth;
using Microsoft.AspNetCore.Http;

namespace Geef.Atelier.Tests.Web.Auth;

/// <summary>The address of the reload page is always a path on this site.</summary>
public sealed class CrossSiteReloadTargetTests
{
    private static HttpRequest Navigation(string path, string query = "", string pathBase = "")
    {
        var request = new DefaultHttpContext().Request;
        request.Method = "GET";
        request.Host = new HostString("atelier.example.com");
        request.PathBase = pathBase;
        request.Path = path;
        request.QueryString = new QueryString(query);
        request.Headers["Sec-Fetch-Site"] = "cross-site";
        request.Headers["Sec-Fetch-Mode"] = "navigate";
        request.Headers["Sec-Fetch-Dest"] = "document";
        return request;
    }

    [Theory]
    [InlineData("/work", "", "/work?same_site_reload=1")]
    [InlineData("/oauth/authorize", "?redirect_uri=https%3A%2F%2Fother.example%2Fcb", "/oauth/authorize?redirect_uri=https%3A%2F%2Fother.example%2Fcb&same_site_reload=1")]
    [InlineData("/", "?next=//other.example", "/?next=//other.example&same_site_reload=1")]
    public void A_path_on_this_site_is_opened_again(string path, string query, string expected) =>
        Assert.Equal(expected, CrossSiteReloadExtensions.ReloadTarget(Navigation(path, query)));

    [Theory]
    [InlineData("//other.example/work")]
    [InlineData("//other.example")]
    public void A_path_that_a_browser_reads_as_another_host_gets_no_reload(string path) =>
        Assert.Null(CrossSiteReloadExtensions.ReloadTarget(Navigation(path)));

    [Fact]
    public void A_backslash_is_encoded_and_stays_a_path_on_this_site() =>
        Assert.Equal("/%5Cother.example?same_site_reload=1", CrossSiteReloadExtensions.ReloadTarget(Navigation("/\\other.example")));

    [Theory]
    [InlineData("?same_site_reload=1")]
    [InlineData("?a=1&same_site_reload=")]
    [InlineData("?SAME_SITE_RELOAD=1")]
    public void The_second_request_gets_no_reload_so_there_is_no_loop(string query) =>
        Assert.Null(CrossSiteReloadExtensions.ReloadTarget(Navigation("/work", query)));
}
