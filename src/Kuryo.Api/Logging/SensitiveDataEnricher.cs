using Serilog.Core;
using Serilog.Events;

namespace Kuryo.Api.Logging;

/// <summary>
/// Last line of defence for log output: any property whose name says it holds a secret or personal data
/// is masked before it reaches a sink, whoever wrote the log call.
/// </summary>
internal sealed class SensitiveDataEnricher : ILogEventEnricher
{
    private const string TrackPrefix = "/api/track/";

    private static readonly HashSet<string> Secrets = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApiKey", "Key", "WebhookSecret", "Signature", "PrivateKey", "Password", "Token", "Authorization", "ConnectionString", "AccessToken", "access_token",
    };

    private static readonly HashSet<string> Emails = new(StringComparer.OrdinalIgnoreCase) { "Email", "RecipientEmail" };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var masked = name switch
            {
                _ when Secrets.Contains(name) => Masking.Hidden,
                _ when Emails.Contains(name) => Masking.Email(value is ScalarValue { Value: { } scalar } ? scalar.ToString() : null),
                // A tracking link is the key to a customer's delivery; request logs show its path without the token.
                "RequestPath" when value is ScalarValue { Value: string path } && path.StartsWith(TrackPrefix, StringComparison.OrdinalIgnoreCase)
                    => TrackPrefix + Masking.Hidden,
                _ => null,
            };

            if (masked is not null)
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(masked)));
            }
        }
    }
}
