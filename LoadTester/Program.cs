using System.Collections.Concurrent;
using System.Diagnostics;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using LoadTester;

var options = LoadTestOptions.Parse(args);
var createdSessions = new ConcurrentBag<SignalRClientSession>();
var connectGate = new SemaphoreSlim(Math.Max(1, options.ConnectConcurrency));
var metrics = new LoadTestMetrics();
var globalSequence = 0L;
var connectErrorLogSequence = 0L;
var reportFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "nbomber"));

Directory.CreateDirectory(reportFolder);

if (!await RunPreflightAsync(options))
{
    return;
}

// Performance tuning: warm-up прогревает JIT/PGO, Redis connection и SignalR handshake path до основного замера.
var warmUpSimulation = Simulation.KeepConstant(
    copies: Math.Min(options.WarmUpConnections, options.Connections),
    during: TimeSpan.FromSeconds(Math.Max(1, options.WarmUpSeconds)));
var rampUpSimulation = Simulation.RampingConstant(copies: options.Connections, during: TimeSpan.FromSeconds(options.RampUpSeconds));
var steadySimulation = Simulation.KeepConstant(copies: options.Connections, during: TimeSpan.FromSeconds(options.SteadySeconds));
var rampDownSimulation = Simulation.RampingConstant(copies: 0, during: TimeSpan.FromSeconds(options.RampDownSeconds));

var scenario = Scenario.Create(options.ScenarioName, async context =>
    {
        try
        {
            var acquire = await TryGetOrCreateSessionAsync(context);
            if (!acquire.IsReady)
            {
                await ApplyConnectBackoffAsync(context.ScenarioInfo.InstanceNumber, context.ScenarioCancellationToken);
                return Response.Ok(statusCode: acquire.StatusCode, message: acquire.Message ?? string.Empty, customLatencyMs: acquire.LatencyMs);
            }

            if (acquire.IsNewConnection)
            {
                return Response.Ok(statusCode: acquire.StatusCode, customLatencyMs: acquire.LatencyMs);
            }

            var session = acquire.Session!;
            var pacingDelay = session.GetPacingDelay(options.SendIntervalMs);
            if (pacingDelay > TimeSpan.Zero)
            {
                await ApplyPacingAsync(pacingDelay, context.ScenarioCancellationToken);
                metrics.RecordIdle();
                return Response.Ok(statusCode: "idle", customLatencyMs: 0);
            }

            var sequence = Interlocked.Increment(ref globalSequence);
            var publishStartedAt = Stopwatch.GetTimestamp();
            var ack = await session.PublishAndWaitAsync(
                sequenceNumber: sequence,
                payloadBytes: options.PayloadBytes,
                batchEvery: Math.Max(1, options.BatchEvery),
                receiveTimeoutMs: options.ReceiveTimeoutMs,
                cancellationToken: context.ScenarioCancellationToken);
            var publishLatencyMs = Stopwatch.GetElapsedTime(publishStartedAt).TotalMilliseconds;
            session.MarkPublishCompleted(options.SendIntervalMs);

            if (ack.Accepted)
            {
                metrics.RecordPublishSucceeded();
                return Response.Ok(statusCode: "publish-ok", customLatencyMs: publishLatencyMs);
            }

            metrics.RecordPublishRejected();
            return Response.Fail(statusCode: "rejected", message: ack.Reason ?? "Server rejected the publish request.", sizeBytes: 0, customLatencyMs: publishLatencyMs);
        }
        catch (OperationCanceledException) when (context.ScenarioCancellationToken.IsCancellationRequested)
        {
            metrics.RecordCanceled();
            return Response.Ok(statusCode: "canceled");
        }
        catch (TimeoutException exception)
        {
            metrics.RecordPublishTimedOut();
            return Response.Fail(statusCode: "timeout", message: exception.Message, sizeBytes: 0, customLatencyMs: 0);
        }
        catch (InvalidOperationException exception) when (IsInactiveConnectionError(exception))
        {
            await DisposeScenarioSessionAsync(context);
            return Response.Ok(statusCode: "disconnected", message: exception.Message, customLatencyMs: 0);
        }
        catch (Exception exception)
        {
            metrics.RecordUnhandledException();
            return Response.Fail(statusCode: "exception", message: FormatException(exception), sizeBytes: 0, customLatencyMs: 0);
        }
    })
    .WithInit(context =>
    {
        context.Logger.Information(
            "Load test init: baseUrl={0}, connections={1}, group={2}, profile={3}, sendIntervalMs={4}, connectConcurrency={5}, connectAcquireTimeoutMs={6}, connectTimeoutMs={7}",
            options.BaseUrl,
            options.Connections,
            options.GroupName,
            options.TrafficProfile,
            options.SendIntervalMs,
            options.ConnectConcurrency,
            options.ConnectAcquireTimeoutMs,
            options.ConnectTimeoutMs);

        return Task.CompletedTask;
    })
    .WithClean(async _ =>
    {
        while (createdSessions.TryTake(out var session))
        {
            await session.DisposeAsync();
            RecordDisconnectedOnce(session);
        }
    })
    .WithMaxFailCount(Math.Max(options.MaxFailCount, options.Connections * 10))
    .WithLoadSimulations(warmUpSimulation, rampUpSimulation, steadySimulation, rampDownSimulation);

NBomberRunner
    .RegisterScenarios(scenario)
    .WithTestSuite("highload-realtime-signalr-demo")
    .WithTestName(options.ScenarioName)
    .WithReportFolder(reportFolder)
    .WithReportFormats(ReportFormat.Html, ReportFormat.Txt)
    .Run();

PrintLoadTestSummary(metrics, options);

return;

async Task<SessionAcquireResult> TryGetOrCreateSessionAsync(IScenarioContext context)
{
    const string SessionKey = "signalr-session";

    if (context.ScenarioInstanceData.TryGetValue(SessionKey, out var existingSession))
    {
        var session = (SignalRClientSession)existingSession;
        if (session.IsActive)
        {
            return SessionAcquireResult.Existing(session);
        }

        await DisposeScenarioSessionAsync(context);
        return SessionAcquireResult.Disconnected();
    }

    var gateAcquired = await connectGate.WaitAsync(
        TimeSpan.FromMilliseconds(Math.Max(1, options.ConnectAcquireTimeoutMs)),
        context.ScenarioCancellationToken);

    if (!gateAcquired)
    {
        metrics.RecordConnectWait();
        return SessionAcquireResult.Wait();
    }

    try
    {
        if (context.ScenarioInstanceData.TryGetValue(SessionKey, out existingSession))
        {
            var existing = (SignalRClientSession)existingSession;
            if (existing.IsActive)
            {
                return SessionAcquireResult.Existing(existing);
            }

            await DisposeScenarioSessionAsync(context);
            return SessionAcquireResult.Disconnected();
        }

        var session = new SignalRClientSession(
            baseUrl: options.BaseUrl,
            senderId: $"nb-{Guid.NewGuid():N}".Substring(0, 16),
            groupName: options.GroupName,
            trafficProfile: options.TrafficProfile);

        try
        {
            metrics.RecordConnectAttempt();
            // Performance tuning: ограничиваем concurrent handshakes, иначе 500+ одновременных Upgrade могут забить edge/load-generator.
            var connectStartedAt = Stopwatch.GetTimestamp();
            await session.StartAsync(options.ConnectTimeoutMs, context.ScenarioCancellationToken);
            var connectLatencyMs = Stopwatch.GetElapsedTime(connectStartedAt).TotalMilliseconds;
            context.ScenarioInstanceData[SessionKey] = session;
            createdSessions.Add(session);
            metrics.RecordConnectSucceeded();

            return SessionAcquireResult.Connected(session, connectLatencyMs);
        }
        catch (Exception exception) when (!context.ScenarioCancellationToken.IsCancellationRequested)
        {
            await session.DisposeAsync();
            var message = FormatException(exception);
            metrics.RecordConnectRetry(message);
            LogEarlyConnectError(message);
            context.Logger.Warning(
                "SignalR connect failed for scenario instance {0}: {1}",
                context.ScenarioInfo.InstanceNumber,
                message);

            return SessionAcquireResult.Retry(0, message);
        }
    }
    finally
    {
        connectGate.Release();
    }
}

async Task ApplyPacingAsync(TimeSpan pacingDelay, CancellationToken cancellationToken)
{
    var maxIdleDelay = TimeSpan.FromMilliseconds(500);
    var boundedDelay = pacingDelay > maxIdleDelay
        ? maxIdleDelay
        : pacingDelay;

    if (boundedDelay <= TimeSpan.Zero)
    {
        return;
    }

    // Performance tuning: idle-итерации короче NBomber operation timeout, но сохраняют целевой publish cadence.
    await Task.Delay(boundedDelay, cancellationToken);
}

async Task DisposeScenarioSessionAsync(IScenarioContext context)
{
    const string SessionKey = "signalr-session";

    if (!context.ScenarioInstanceData.Remove(SessionKey, out var existingSession))
    {
        return;
    }

    var session = (SignalRClientSession)existingSession;
    RecordDisconnectedOnce(session);

    try
    {
        await session.DisposeAsync();
    }
    catch
    {
        // Performance tuning: cleanup не должен превращать ожидаемый reconnect в failed iteration.
    }
}

void RecordDisconnectedOnce(SignalRClientSession session)
{
    if (session.TryMarkDisconnected())
    {
        metrics.RecordDisconnected();
    }
}

async Task ApplyConnectBackoffAsync(int instanceNumber, CancellationToken cancellationToken)
{
    var jitterMs = instanceNumber % 250;
    var delayMs = Math.Min(Math.Max(0, options.ConnectRetryDelayMs) + jitterMs, 500);

    if (delayMs == 0)
    {
        return;
    }

    // Performance tuning: bounded backoff снижает retry churn, но не удерживает connect semaphore.
    await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
}

static void PrintLoadTestSummary(LoadTestMetrics metrics, LoadTestOptions options)
{
    var targetReached = metrics.PeakConnections >= options.Connections;
    var publishObserved = metrics.PublishSucceeded > 0;

    Console.WriteLine();
    Console.WriteLine("──────────────────────────── load tester summary ────────────────────────────");
    Console.WriteLine($"target connections:       {options.Connections}");
    Console.WriteLine($"peak active connections:  {metrics.PeakConnections}");
    Console.WriteLine($"connected total:          {metrics.ConnectSucceeded}");
    Console.WriteLine($"connect attempts:         {metrics.ConnectAttempts}");
    Console.WriteLine($"connect waits:            {metrics.ConnectWaits}");
    Console.WriteLine($"connect retries:          {metrics.ConnectRetries}");
    Console.WriteLine($"disconnected:             {metrics.Disconnected}");
    Console.WriteLine($"publish ok:               {metrics.PublishSucceeded}");
    Console.WriteLine($"publish rejected:         {metrics.PublishRejected}");
    Console.WriteLine($"publish timeout:          {metrics.PublishTimedOut}");
    Console.WriteLine($"idle iterations:          {metrics.IdleIterations}");
    Console.WriteLine($"canceled iterations:      {metrics.Canceled}");
    Console.WriteLine($"unhandled exceptions:     {metrics.UnhandledExceptions}");
    Console.WriteLine($"target reached:           {(targetReached ? "yes" : "no")}");
    Console.WriteLine($"publish traffic observed: {(publishObserved ? "yes" : "no")}");

    if (!string.IsNullOrWhiteSpace(metrics.LastConnectError))
    {
        Console.WriteLine($"last connect error:       {metrics.LastConnectError}");
    }

    if (!targetReached || !publishObserved)
    {
        Console.WriteLine("result:                   inconclusive baseline; connect layer still dominates");
    }
    else
    {
        Console.WriteLine("result:                   valid baseline; inspect publish latency and server metrics");
    }

    Console.WriteLine("──────────────────────────────────────────────────────────────────────────────");
}

async Task<bool> RunPreflightAsync(LoadTestOptions testOptions)
{
    if (!testOptions.PreflightEnabled)
    {
        return true;
    }

    var healthUri = new Uri(new Uri(testOptions.BaseUrl, UriKind.Absolute), "/health/ready");
    Console.WriteLine($"preflight: checking {healthUri}");

    try
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(Math.Max(1, testOptions.PreflightTimeoutMs))
        };

        using var response = await client.GetAsync(healthUri);
        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"preflight: ok ({(int)response.StatusCode})");
            return true;
        }

        Console.Error.WriteLine($"preflight: failed HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"preflight: failed {FormatException(exception)}");
    }

    if (testOptions.PreflightRequired)
    {
        Console.Error.WriteLine("preflight: stopping test early. Use --preflight-required=false to continue anyway.");
        return false;
    }

    Console.Error.WriteLine("preflight: continuing because --preflight-required=false.");
    return true;
}

void LogEarlyConnectError(string message)
{
    var sequence = Interlocked.Increment(ref connectErrorLogSequence);
    var every = Math.Max(1, options.EarlyErrorLogEvery);

    if (sequence <= 5 || sequence % every == 0)
    {
        Console.Error.WriteLine($"connect-error[{sequence}]: {message}");
    }
}

static string FormatException(Exception exception)
{
    var messages = new List<string>();
    for (var current = exception; current is not null; current = current.InnerException)
    {
        messages.Add($"{current.GetType().Name}: {current.Message}");
    }

    return string.Join(" -> ", messages);
}

static bool IsInactiveConnectionError(InvalidOperationException exception)
{
    return exception.Message.Contains("connection is not active", StringComparison.OrdinalIgnoreCase);
}
