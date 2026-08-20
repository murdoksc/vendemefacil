using System.Net;
using System.Threading.Channels;
using Azure;
using Azure.Communication.Email;
using VendemeFacil.Api.Domain;

namespace VendemeFacil.Api.Infrastructure;

public sealed record OutboundEmail(string Recipient, string Subject, string HtmlContent, string PlainTextContent);

public static class OutboundEmailFactory
{
    public static OutboundEmail ProspectLead(string recipient, ProspectLead lead)
    {
        var fields = new[]
        {
            ("Contacto", lead.ContactName), ("Negocio", lead.BusinessName), ("Telefono", lead.Phone),
            ("Correo", lead.Email ?? "No proporcionado"), ("Ciudad", lead.City ?? "No proporcionada"),
            ("Giro", lead.BusinessType ?? "No proporcionado"),
            ("Horario preferido", lead.PreferredContactTime ?? "Cualquier horario"),
            ("Necesidades", lead.Notes ?? "Sin comentarios")
        };
        var htmlFields = string.Join("", fields.Select(field => $"<p><strong>{WebUtility.HtmlEncode(field.Item1)}:</strong> {WebUtility.HtmlEncode(field.Item2)}</p>"));
        var plainFields = string.Join("\n", fields.Select(field => $"{field.Item1}: {field.Item2}"));
        return new OutboundEmail(
            recipient,
            $"Nuevo interesado: {lead.BusinessName}",
            $"<html><body style=\"font-family:Arial,sans-serif;color:#17251f;line-height:1.6\"><h2>Nuevo interesado en Vendeme Facil</h2>{htmlFields}</body></html>",
            $"Nuevo interesado en Vendeme Facil\n{plainFields}");
    }

    public static OutboundEmail PasswordReset(string recipient, string displayName, string businessName, string resetUrl)
    {
        var safeName = WebUtility.HtmlEncode(displayName);
        var safeBusiness = WebUtility.HtmlEncode(businessName);
        var safeUrl = WebUtility.HtmlEncode(resetUrl);
        return new OutboundEmail(
            recipient,
            "Restablece tu contraseña de Véndeme Fácil",
            $"""<html><body style="font-family:Arial,sans-serif;color:#17251f;line-height:1.6"><h2>Restablece tu contraseña</h2><p>Hola {safeName}.</p><p>Recibimos una solicitud para restablecer tu contraseña de <strong>{safeBusiness}</strong>.</p><p><a href="{safeUrl}" style="display:inline-block;padding:12px 18px;border-radius:8px;background:#196651;color:white;text-decoration:none;font-weight:bold">Crear nueva contraseña</a></p><p>El enlace vence en 30 minutos y solo puede utilizarse una vez.</p><p style="color:#66776f;font-size:12px">Si no realizaste esta solicitud, puedes ignorar este mensaje.</p></body></html>""",
            $"Hola {displayName}. Recibimos una solicitud para restablecer tu contraseña de {businessName}. Abre este enlace, válido durante 30 minutos: {resetUrl} Si no realizaste la solicitud, ignora este mensaje.");
    }

    public static OutboundEmail Document(string recipient, string businessName, string documentLabel, string reference, string content)
    {
        var safeBusiness = WebUtility.HtmlEncode(businessName);
        var safeLabel = WebUtility.HtmlEncode(documentLabel);
        var safeReference = WebUtility.HtmlEncode(reference);
        var safeContent = WebUtility.HtmlEncode(content);
        return new OutboundEmail(
            recipient,
            $"{documentLabel} {reference} · {businessName}",
            $"""<html><body style="margin:0;background:#f4f7f5;font-family:Arial,sans-serif;color:#17251f"><div style="max-width:620px;margin:24px auto;padding:28px;background:#fff;border:1px solid #dfe7e2;border-radius:14px"><p style="margin:0;color:#196651;font-size:12px;font-weight:bold;text-transform:uppercase">{safeBusiness}</p><h2 style="margin:8px 0 4px">{safeLabel}</h2><p style="margin:0 0 22px;color:#66776f">Referencia: {safeReference}</p><pre style="margin:0;padding:18px;background:#f7f8f6;border-radius:10px;white-space:pre-wrap;font:14px/1.55 Arial,sans-serif">{safeContent}</pre><p style="margin:22px 0 0;color:#7b8982;font-size:12px">Este documento fue enviado desde Véndeme Fácil.</p></div></body></html>""",
            $"{businessName}\n{documentLabel} {reference}\n\n{content}");
    }
}

public sealed class OutboundEmailQueue
{
    private readonly Channel<OutboundEmail> _channel = Channel.CreateUnbounded<OutboundEmail>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public bool TryQueue(OutboundEmail message) => _channel.Writer.TryWrite(message);
    public IAsyncEnumerable<OutboundEmail> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class OutboundEmailWorker(
    OutboundEmailQueue queue,
    IConfiguration configuration,
    ILogger<OutboundEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration["Email:ConnectionString"];
        var senderAddress = configuration["Email:SenderAddress"]?.Trim();

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(senderAddress))
            logger.LogWarning("El envío de recuperación de contraseña está deshabilitado porque Email no está configurado.");

        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(senderAddress))
                continue;

            try
            {
                var client = new EmailClient(connectionString);
                await client.SendAsync(
                    WaitUntil.Started,
                    senderAddress,
                    message.Recipient,
                    message.Subject,
                    message.HtmlContent,
                    message.PlainTextContent,
                    stoppingToken);
                logger.LogInformation("Correo {Subject} aceptado por Azure Email.", message.Subject);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "No se pudo enviar el correo {Subject}.", message.Subject);
            }
        }
    }
}
