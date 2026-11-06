using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Kuryo.Api.Common;
using Kuryo.Api.Features.Auth;
using Kuryo.Api.Features.Companies;
using Kuryo.Api.Features.Couriers;
using Kuryo.Api.Features.Live;
using Kuryo.Api.Features.Matching;
using Kuryo.Api.Features.Merchants;
using Kuryo.Api.Features.Orders;
using Kuryo.Api.Features.Zones;
using Kuryo.Api.Logging;
using Kuryo.Infrastructure;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("Kuryo");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Kuryo is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__Kuryo environment variable.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddKuryoPersistence(connectionString);
builder.Services.AddKuryoAuth(builder.Configuration);

// Live updates. With several API instances, a Redis connection string lets them share the hubs' groups.
var signalR = builder.Services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment())
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var redis = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redis))
{
    signalR.AddStackExchangeRedis(redis, options => options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("kuryo"));
}

builder.Services.AddSingleton<LivePublisher>();

builder.Services.Configure<MatchingOptions>(builder.Configuration.GetSection(MatchingOptions.Section));
builder.Services.AddSingleton<MatchingSignal>();
builder.Services.AddSingleton<Matcher>();
if (builder.Configuration.GetValue($"{MatchingOptions.Section}:Enabled", true))
{
    builder.Services.AddHostedService<MatchingWorker>();
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Sign-up and sign-in per address: plenty for a person, slow for guessing.
    var perMinute = builder.Configuration.GetValue("Auth:AttemptsPerMinute", 10);
    options.AddPolicy(AuthEndpoints.RateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // The public tracking page polls and reconnects: generous for a customer, slow for scanning tokens.
    var trackPerMinute = builder.Configuration.GetValue("Tracking:RequestsPerMinute", 120);
    options.AddPolicy(TrackEndpoints.RateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = trackPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await TypedResults.Problem(
            title: "Too many requests",
            detail: "Slow down and try again in a minute.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { ["code"] = Kuryo.Core.ErrorCodes.RateLimited })
            .ExecuteAsync(context.HttpContext);
    };
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddOpenApi(options => options
    .AddDocumentTransformer<DocumentInfoTransformer>()
    .AddDocumentTransformer<SecuritySchemesTransformer>()
    .AddOperationTransformer<SecuritySchemesTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<KuryoDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<LiveFeedMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Kuryo")
    .WithTheme(ScalarTheme.Default)
    .ForceLightMode()
    .HideDarkModeToggle()
    .DisableDefaultFonts()
    .WithCustomCss(ScalarStyle.Css)
    .AddPreferredSecuritySchemes(SecuritySchemesTransformer.Scheme)
    .HideDeveloperTools()
    .HideClientButton()
    .DisableAgent()
    .DisableMcp()
    .DisableTelemetry());

app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapAuthEndpoints();
app.MapCompanyEndpoints();
app.MapMemberEndpoints();
app.MapMerchantEndpoints();
app.MapZoneEndpoints();
app.MapCourierEndpoints();
app.MapOrderEndpoints();
app.MapOfferEndpoints();
app.MapTrackEndpoints();
app.MapHub<LiveHub>(LiveHub.Path);
app.MapHub<TrackHub>(TrackHub.Path).RequireRateLimiting(TrackEndpoints.RateLimit);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await DependencyInjection.MigrateAsync(app.Services);
}

await app.RunAsync();

public partial class Program;
