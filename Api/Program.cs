using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

const string serviceName = "highload-realtime-api";
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: serviceName,
        serviceVersion: "1.0.0",
        serviceInstanceId: Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName))
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(ApiMetrics.MeterName)
            .AddPrometheusExporter();
    });

builder.Services.AddSingleton<ApiMetrics>();

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapGet("/api/ping", (ApiMetrics metrics) =>
{
    metrics.RecordRequest("ping");
    return Results.Ok(new
    {
        service = serviceName,
        status = "ok",
        nodeId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName,
        serverTimeUtc = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/performance/info", (ApiMetrics metrics) =>
{
    metrics.RecordRequest("performance-info");
    return Results.Ok(new
    {
        service = serviceName,
        role = "rest-api",
        routing = "haproxy-l4 -> nginx-l7 -> api-svc",
        nodeId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName,
        serverTimeUtc = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/shipments/{shipmentId:guid}", (Guid shipmentId, ApiMetrics metrics) =>
{
    metrics.RecordRequest("shipment-demo");

    // Минимальный REST-сценарий нужен, чтобы отделить обычный API-трафик от hot path SignalR.
    return Results.Ok(new
    {
        shipmentId,
        status = "in-transit",
        etaMinutes = 12,
        updatedAtUtc = DateTimeOffset.UtcNow
    });
});

app.Run();

/// <summary>
/// Минимальные метрики REST-сервиса, чтобы Prometheus видел отдельный профиль нагрузки API.
/// </summary>
internal sealed class ApiMetrics
{
    internal const string MeterName = "Highload.Realtime.Api";

    private readonly Counter<long> _requestsTotal;

    public ApiMetrics()
    {
        var meter = new Meter(MeterName, "1.0.0");
        _requestsTotal = meter.CreateCounter<long>("api_requests_total");
    }

    public void RecordRequest(string endpoint)
    {
        _requestsTotal.Add(1, new KeyValuePair<string, object?>("endpoint", endpoint));
    }
}
