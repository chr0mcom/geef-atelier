using Markdig;
using Microsoft.AspNetCore.Components;

namespace Geef.Atelier.Web.Services;

/// <summary>Renders Markdown to safe HTML for display in Blazor components.</summary>
public static class MarkdownRenderer
{
    // No raw HTML passthrough — disables inline HTML blocks from being passed to output.
    private static readonly MarkdownPipeline SafePipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    /// <summary>Renders Markdown to an HTML string.</summary>
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        return Markdown.ToHtml(markdown, SafePipeline);
    }

    /// <summary>Converts Markdown to a <see cref="MarkupString"/> safe for <c>@((MarkupString)...)</c> use.</summary>
    public static MarkupString ToMarkupString(string? markdown) =>
        new(ToHtml(markdown));
}
