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

// Performance tuning: заранее поднимаем ThreadPool, чтобы ramp-up WebSocket-клиентов не создавал очередь > десятков тысяч work items.
ThreadPool.SetMinThreads(
    Math.Max(serverOptions.ThreadPool.MinWorkerThreads, Environment.ProcessorCount * 8),
    Math.Max(serverOptions.ThreadPool.MinCompletionPortThreads, Environment.ProcessorCount * 8));
ThreadPool.SetMaxThreads(
    Math.Max(serverOptions.ThreadPool.MaxWorkerThreads, serverOptions.ThreadPool.MinWorkerThreads),
    Math.Max(serverOptions.ThreadPool.MaxCompletionPortThreads, serverOptions.ThreadPool.MinCompletionPortThreads));

builder.Services.Configure<RealtimeServerOptions>(builder.Configuration.GetSection("Performance"));
builder.Services.Configure<SocketTransportOptions>(options =>
{
    // Performance tuning: отключаем Nagle и увеличиваем backlog, чтобы handshake burst не застревал до Kestrel.
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
        // Performance tuning: HTTP/1.1 обязателен для WebSocket; HTTP/2 оставляем для обычных API. HTTP/3 требует TLS/QUIC и не включается в локальном TCP compose.
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });

    // Performance tuning: явные лимиты защищают процесс от OOM при неконтролируемом количестве TCP/WebSocket соединений.
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
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var partitionKey = IsRealtimePath(context.Request.Path)
                ? "signalr-concurrency"
                : "default-http-concurrency";

            return RateLimitPartition.GetConcurrencyLimiter(
                partitionKey,
                key => new ConcurrencyLimiterOptions
                {
                    // Performance tuning: ограничиваем одновременную обработку handshakes/API, чтобы ThreadPool не уходил в бесконечную очередь.
                    PermitLimit = key == "signalr-concurrency"
                        ? serverOptions.HttpGuard.RealtimePermitLimit
                        : serverOptions.HttpGuard.DefaultPermitLimit,
                    QueueLimit = serverOptions.HttpGuard.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
        }),
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var partitionKey = IsRealtimePath(context.Request.Path)
                ? "signalr-token-bucket"
                : "default-http-token-bucket";

            return RateLimitPartition.GetTokenBucketLimiter(
                partitionKey,
                _ => new TokenBucketRateLimiterOptions
                {
                    // Performance tuning: token bucket режет handshake/RPS bursts раньше, чем они превращаются в GC и Redis pressure.
                    TokenLimit = serverOptions.HttpGuard.TokenLimit,
                    TokensPerPeriod = serverOptions.HttpGuard.TokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromMilliseconds(serverOptions.HttpGuard.ReplenishmentPeriodMs),
                    QueueLimit = serverOptions.HttpGuard.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                });
        }));
});

builder.Services.AddSingleton<NodeIdentity>();
builder.Services.AddSingleton<RealtimeMetrics>();
builder.Services.AddSingleton<ConnectionRateLimiter>();
builder.Services.AddSingleton<BatchedMessageDispatcher>();
builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<BatchedMessageDispatcher>());

var signalRBuilder = builder.Services
    .AddSignalR(options =>
    {
        // Performance tuning: уменьшаем per-connection буферы и время handshake, чтобы 3k-5k WebSocket не раздували память.
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
            var connection = await ConnectionMultiplexer.ConnectAsync(configuration, writer);
            connection.ConnectionFailed += (_, args) => writer.WriteLine($"Redis backplane connection failed: {args.FailureType}");
            connection.ConnectionRestored += (_, args) => writer.WriteLine($"Redis backplane connection restored: {args.ConnectionType}");
            return connection;
        };
    });

    builder.Services.AddHealthChecks().AddCheck<RedisBackplaneHealthCheck>("redis-backplane");
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
    // Performance tuning: WebSocket keep-alive согласован с SignalR keep-alive, чтобы не плодить лишние ping/pong.
    KeepAliveInterval = TimeSpan.FromSeconds(serverOptions.SignalR.KeepAliveIntervalSeconds)
});
app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready");

app.MapHub<RealtimeHub>(RealtimeRoutes.HubPath);
// Алиасы нужны для демонстрации разных L7-маршрутов без смены канонического пути клиентов.
app.MapHub<RealtimeHub>("/realtime/hub");
app.MapHub<RealtimeHub>("/hub/realtime");
app.MapFallbackToFile("index.html");

app.Run();

static bool IsRealtimePath(PathString path)
{
    return path.StartsWithSegments(RealtimeRoutes.HubPath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/realtime", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/hub", StringComparison.OrdinalIgnoreCase);
}

static ConfigurationOptions CreateRedisConfiguration(RedisBackplaneOptions options)
{
    var configuration = ConfigurationOptions.Parse(options.Configuration, true);
    // Performance tuning: StackExchange.Redis использует мультиплексирование; держим один устойчивый backplane connection вместо per-request подключений.
    configuration.AbortOnConnectFail = options.AbortOnConnectFail;
    configuration.ConnectRetry = options.ConnectRetry;
    configuration.ConnectTimeout = options.ConnectTimeoutMs;
    configuration.SyncTimeout = options.SyncTimeoutMs;
    configuration.AsyncTimeout = options.AsyncTimeoutMs;
    configuration.KeepAlive = options.KeepAliveSeconds;
    configuration.ReconnectRetryPolicy = new ExponentialRetry(options.ExponentialRetryBaseMs);
    configuration.ClientName = $"signalr-server-{Environment.MachineName.ToLowerInvariant()}";
    configuration.ChannelPrefix = RedisChannel.Literal(options.ChannelPrefix);
    configuration.BacklogPolicy = BacklogPolicy.Default;
    return configuration;
}
