using System.Threading.RateLimiting;
using Highload.Realtime.Shared;
using MessagePack;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using StackExchange.Redis;
using Server.Hubs;
using Server.Options;
using Server.Services;

var builder = WebApplication.CreateBuilder(args);

var serverOptions = builder.Configuration
    .GetSection("Performance")
    .Get<RealtimeServerOptions>() ?? new RealtimeServerOptions();

// Поднимаем минимальный thread pool заранее, чтобы всплеск подключений не упирался в холодный рост воркеров.
ThreadPool.SetMinThreads(
    Math.Max(serverOptions.ThreadPool.MinWorkerThreads, Environment.ProcessorCount * 8),
    Math.Max(serverOptions.ThreadPool.MinCompletionPortThreads, Environment.ProcessorCount * 8));

builder.Services.Configure<RealtimeServerOptions>(builder.Configuration.GetSection("Performance"));
builder.Services.Configure<SocketTransportOptions>(options =>
{
    options.NoDelay = true;
    options.Backlog = serverOptions.Kestrel.SocketBacklog;

    if (serverOptions.Kestrel.IoQueueCount is > 0)
    {
        options.IOQueueCount = serverOptions.Kestrel.IoQueueCount.Value;
    }
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.ListenAnyIP(serverOptions.Kestrel.HttpPort, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });

    options.Limits.MaxConcurrentConnections = serverOptions.Kestrel.MaxConcurrentConnections;
    options.Limits.MaxConcurrentUpgradedConnections = serverOptions.Kestrel.MaxConcurrentUpgradedConnections;
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(serverOptions.Kestrel.KeepAliveSeconds);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(serverOptions.Kestrel.RequestHeadersTimeoutSeconds);
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey = context.Request.Path.StartsWithSegments(RealtimeRoutes.HubPath, StringComparison.OrdinalIgnoreCase)
            ? "signalr-negotiate"
            : "default-http";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            static key => new FixedWindowRateLimiterOptions
            {
                PermitLimit = key == "signalr-negotiate" ? 2_000 : 1_000,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(1),
                AutoReplenishment = true
            });
    });
});

builder.Services.AddSingleton<NodeIdentity>();
builder.Services.AddSingleton<RealtimeMetrics>();
builder.Services.AddSingleton<ConnectionRateLimiter>();
builder.Services.AddSingleton<BatchedMessageDispatcher>();
builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<BatchedMessageDispatcher>());

var signalRBuilder = builder.Services
    .AddSignalR(options =>
    {
        options.EnableDetailedErrors = serverOptions.SignalR.EnableDetailedErrors;
        options.KeepAliveInterval = TimeSpan.FromSeconds(serverOptions.SignalR.KeepAliveIntervalSeconds);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(serverOptions.SignalR.ClientTimeoutIntervalSeconds);
        options.HandshakeTimeout = TimeSpan.FromSeconds(serverOptions.SignalR.HandshakeTimeoutSeconds);
        options.MaximumReceiveMessageSize = serverOptions.SignalR.MaximumReceiveMessageSizeBytes;
        options.StreamBufferCapacity = serverOptions.SignalR.StreamBufferCapacity;
        options.MaximumParallelInvocationsPerClient = serverOptions.SignalR.MaximumParallelInvocationsPerClient;
    })
    .AddMessagePackProtocol(options =>
    {
        // LZ4 даёт заметную экономию трафика на burst-публикациях без ручного тюнинга DTO.
        options.SerializerOptions = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
    });

if (serverOptions.Redis.Enabled)
{
    signalRBuilder.AddStackExchangeRedis(serverOptions.Redis.Configuration, redisOptions =>
    {
        redisOptions.ConnectionFactory = async writer =>
        {
            var configuration = CreateRedisConfiguration(serverOptions.Redis);
            return await ConnectionMultiplexer.ConnectAsync(configuration, writer);
        };
    });
}

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: builder.Environment.ApplicationName,
        serviceVersion: "1.0.0",
        serviceInstanceId: Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName))
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(RealtimeMetrics.MeterName)
            .AddPrometheusExporter();
    });

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrWhiteSpace(postgresConnectionString))
{
    builder.Services.AddSingleton(_ =>
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(postgresConnectionString);
        return dataSourceBuilder.Build();
    });

    builder.Services.AddHostedService<PostgresBootstrapService>();
    builder.Services.AddHealthChecks().AddCheck<PostgresReadinessHealthCheck>("postgres");
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(serverOptions.SignalR.KeepAliveIntervalSeconds)
});
app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready");

app.MapGet("/api/performance/info", (NodeIdentity nodeIdentity, RealtimeMetrics metrics) => Results.Ok(new
{
    nodeId = nodeIdentity.NodeId,
    activeConnections = metrics.ActiveConnections,
    batchQueueDepth = metrics.BatchQueueDepth,
    hubPath = RealtimeRoutes.HubPath,
    defaultGroup = RealtimeRoutes.DefaultGroup
}));

app.MapHub<RealtimeHub>(RealtimeRoutes.HubPath);
app.MapFallbackToFile("index.html");

app.Run();

static ConfigurationOptions CreateRedisConfiguration(RedisBackplaneOptions options)
{
    var configuration = ConfigurationOptions.Parse(options.Configuration, true);
    configuration.AbortOnConnectFail = options.AbortOnConnectFail;
    configuration.ConnectRetry = options.ConnectRetry;
    configuration.ConnectTimeout = options.ConnectTimeoutMs;
    configuration.SyncTimeout = options.SyncTimeoutMs;
    configuration.AsyncTimeout = options.AsyncTimeoutMs;
    configuration.KeepAlive = options.KeepAliveSeconds;
    configuration.ReconnectRetryPolicy = new ExponentialRetry(options.ExponentialRetryBaseMs);
    configuration.ClientName = $"signalr-server-{Environment.MachineName.ToLowerInvariant()}";
    configuration.ChannelPrefix = RedisChannel.Literal(options.ChannelPrefix);
    return configuration;
}
