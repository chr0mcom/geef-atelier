using Markdig;

namespace Geef.Atelier.Infrastructure.Finalizers.FormatConverters;

internal static class MarkdownToHtmlConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string Convert(string markdown, string title = "")
    {
        var body = Markdown.ToHtml(markdown, Pipeline);
        var escapedTitle = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Document" : title);
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{escapedTitle}}</title>
                <style>
                    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; max-width: 800px; margin: 2rem auto; padding: 0 1rem; line-height: 1.6; }
                    pre { background: #f5f5f5; padding: 1rem; overflow-x: auto; border-radius: 4px; }
                    code { font-family: 'SF Mono', Consolas, monospace; font-size: 0.9em; }
                    blockquote { border-left: 4px solid #ddd; margin: 0; padding-left: 1rem; color: #555; }
                </style>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }
}
