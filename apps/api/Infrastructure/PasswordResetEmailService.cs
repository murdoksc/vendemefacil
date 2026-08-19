using System.Threading.Channels;
using Azure;
using Azure.Communication.Email;

namespace VendemeFacil.Api.Infrastructure;

public sealed record PasswordResetEmail(string Recipient, string DisplayName, string BusinessName, string ResetUrl);

public sealed class PasswordResetEmailQueue
{
    private readonly Channel<PasswordResetEmail> _channel = Channel.CreateUnbounded<PasswordResetEmail>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public bool TryQueue(PasswordResetEmail message) => _channel.Writer.TryWrite(message);
    public IAsyncEnumerable<PasswordResetEmail> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class PasswordResetEmailWorker(
    PasswordResetEmailQueue queue,
    IConfiguration configuration,
    ILogger<PasswordResetEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration["Email:ConnectionString"];
        var senderAddress = configuration["Email:SenderAddress"];

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(senderAddress))
            logger.LogWarning("El envío de recuperación de contraseña está deshabilitado porque Email no está configurado.");

        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(senderAddress))
                continue;

            try
            {
                var client = new EmailClient(connectionString);
                var subject = "Restablece tu contraseña de Véndeme Fácil";
                var plainText = $"Hola {message.DisplayName}. Recibimos una solicitud para restablecer tu contraseña de {message.BusinessName}. Abre este enlace, válido durante 30 minutos: {message.ResetUrl} Si no realizaste la solicitud, ignora este mensaje.";
                var html = $"""
                    <html>
                    <body style="font-family:Arial,sans-serif;color:#17251f;line-height:1.6">
                      <h2>Restablece tu contraseña</h2>
                      <p>Hola {System.Net.WebUtility.HtmlEncode(message.DisplayName)}.</p>
                      <p>Recibimos una solicitud para restablecer tu contraseña de <strong>{System.Net.WebUtility.HtmlEncode(message.BusinessName)}</strong>.</p>
                      <p><a href="{System.Net.WebUtility.HtmlEncode(message.ResetUrl)}" style="display:inline-block;padding:12px 18px;border-radius:8px;background:#196651;color:white;text-decoration:none;font-weight:bold">Crear nueva contraseña</a></p>
                      <p>El enlace vence en 30 minutos y solo puede utilizarse una vez.</p>
                      <p style="color:#66776f;font-size:12px">Si no realizaste esta solicitud, puedes ignorar este mensaje.</p>
                    </body>
                    </html>
                    """;

                await client.SendAsync(
                    WaitUntil.Started,
                    senderAddress,
                    message.Recipient,
                    subject,
                    html,
                    plainText,
                    stoppingToken);
                logger.LogInformation("Correo de recuperación encolado para el usuario solicitado.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "No se pudo enviar un correo de recuperación de contraseña.");
            }
        }
    }
}
