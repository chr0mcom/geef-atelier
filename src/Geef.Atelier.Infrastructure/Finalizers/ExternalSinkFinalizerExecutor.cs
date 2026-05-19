using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Finalizers.FormatConverters;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Geef.Atelier.Infrastructure.Finalizers;

internal sealed class ExternalSinkFinalizerExecutor(
    IHttpClientFactory httpClientFactory,
    IOptions<FinalizerOptions> options,
    ILogger<ExternalSinkFinalizerExecutor> logger) : IFinalizerExecutor
{
    private readonly FinalizerOptions _options = options.Value;

    public FinalizerType Type => FinalizerType.ExternalSink;

    public async Task<FinalizerExecutionResult> ExecuteAsync(
        FinalizerProfile profile,
        FinalizerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var sinkKind = profile.Settings.GetValueOrDefault(WebhookSinkSettings.KeySinkKind, WebhookSinkSettings.SinkKindValue)
            .ToLowerInvariant();

        RunArtifact artifact;
        try
        {
            artifact = sinkKind switch
            {
                EmailSinkSettings.SinkKindValue => await SendEmailAsync(profile, context, cancellationToken),
                _ => await SendWebhookAsync(profile, context, cancellationToken),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExternalSink failed for profile {Profile} (kind={Kind})",
                profile.Name, sinkKind);
            artifact = StatusArtifact(context.RunId, profile.Name, $"Sink delivery failed: {ex.Message}");
        }

        return new FinalizerExecutionResult(
            UpdatedText: null,
            Artifact: artifact,
            CostEur: null,
            ActorName: profile.Name);
    }

    private async Task<RunArtifact> SendWebhookAsync(
        FinalizerProfile profile,
        FinalizerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var settings = WebhookSinkSettings.From(profile.Settings);

        using var client = httpClientFactory.CreateClient("finalizer-webhook");
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

        var payload = JsonSerializer.Serialize(new
        {
            run_id = context.RunId,
            template = context.TemplateName,
            content = context.CurrentText,
            generated_at = context.RunCompletedAt.ToString("O"),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Url)
        {
            Content = new StringContent(payload, Encoding.UTF8, settings.ContentType),
        };

        if (!string.IsNullOrWhiteSpace(settings.AuthHeader))
        {
            // AuthHeader stored as "Scheme value" or raw value — never logged
            var parts = settings.AuthHeader.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            request.Headers.Authorization = parts.Length == 2
                ? new AuthenticationHeaderValue(parts[0], parts[1])
                : new AuthenticationHeaderValue("Bearer", parts[0]);
        }

        var response = await client.SendAsync(request, cancellationToken);
        var statusCode = (int)response.StatusCode;
        var ok = response.IsSuccessStatusCode;

        logger.LogInformation(
            "Webhook {Url} → HTTP {Status} (profile={Profile})",
            MaskUrl(settings.Url), statusCode, profile.Name);

        return new RunArtifact
        {
            Id = Guid.NewGuid(),
            RunId = context.RunId,
            FinalizerProfileName = profile.Name,
            ArtifactType = ok ? ArtifactType.Url : ArtifactType.Status,
            StorageUri = ok ? settings.Url : "error",
            StatusMessage = ok ? null : $"HTTP {statusCode}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private async Task<RunArtifact> SendEmailAsync(
        FinalizerProfile profile,
        FinalizerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var settings = EmailSinkSettings.From(profile.Settings);

        var smtpConfigured = !string.IsNullOrWhiteSpace(_options.SmtpHost);
        if (!smtpConfigured)
        {
            logger.LogWarning("SMTP is not configured; email sink skipped for profile {Profile}",
                profile.Name);
            return StatusArtifact(context.RunId, profile.Name, "SMTP not configured — email skipped");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.SenderName, _options.SenderAddress));
        message.To.Add(MailboxAddress.Parse(settings.ToAddress));
        message.Subject = settings.Subject;

        BodyBuilder bodyBuilder;
        if (settings.AttachAsFile)
        {
            var format = settings.AttachmentFormat.ToLowerInvariant();
            var (bytes, ext, mime) = BuildAttachment(context.CurrentText, format,
                context.TemplateName ?? "document");
            bodyBuilder = new BodyBuilder
            {
                TextBody = $"Please find the document attached.\n\nGenerated: {context.RunCompletedAt:R}",
            };
            bodyBuilder.Attachments.Add($"result.{ext}", bytes, ContentType.Parse(mime));
        }
        else
        {
            bodyBuilder = new BodyBuilder
            {
                TextBody = context.CurrentText,
            };
        }
        message.Body = bodyBuilder.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_options.SmtpHost, _options.SmtpPort,
            SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.SmtpUser))
        {
            // credentials: configured vs. absent — not logged in detail
            await smtp.AuthenticateAsync(_options.SmtpUser, _options.SmtpPassword, cancellationToken);
            logger.LogInformation("SMTP: authenticated as {User} (configured)", _options.SmtpUser);
        }
        else
        {
            logger.LogInformation("SMTP: anonymous relay (credentials absent)");
        }

        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);

        logger.LogInformation("Email sent to {To} via {Host} (profile={Profile})",
            settings.ToAddress, _options.SmtpHost, profile.Name);

        return new RunArtifact
        {
            Id = Guid.NewGuid(),
            RunId = context.RunId,
            FinalizerProfileName = profile.Name,
            ArtifactType = ArtifactType.Status,
            StorageUri = "email-sent",
            StatusMessage = $"Email delivered to {settings.ToAddress}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static (byte[] Bytes, string Extension, string Mime) BuildAttachment(
        string text, string format, string title) => format switch
    {
        "html" => (Encoding.UTF8.GetBytes(MarkdownToHtmlConverter.Convert(text, title)),
            "html", "text/html"),
        "pdf" => (MarkdownToPdfConverter.Convert(text, title),
            "pdf", "application/pdf"),
        "docx" => (MarkdownToDocxConverter.Convert(text, title),
            "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        "txt" => (Encoding.UTF8.GetBytes(PlaintextStripper.Strip(text)),
            "txt", "text/plain"),
        _ => (Encoding.UTF8.GetBytes(text),
            "md", "text/markdown"),
    };

    private static RunArtifact StatusArtifact(Guid runId, string profileName, string message) =>
        new()
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            FinalizerProfileName = profileName,
            ArtifactType = ArtifactType.Status,
            StorageUri = "info",
            StatusMessage = message,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static string MaskUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
        }
        catch
        {
            return "(url)";
        }
    }
}
