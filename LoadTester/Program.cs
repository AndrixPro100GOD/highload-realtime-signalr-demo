using System.Collections.Concurrent;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using LoadTester;

var options = LoadTestOptions.Parse(args);
var createdSessions = new ConcurrentBag<SignalRClientSession>();
var globalSequence = 0L;
var reportFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "nbomber"));

Directory.CreateDirectory(reportFolder);

var scenario = Scenario.Create(options.ScenarioName, async context =>
    {
        var session = await GetOrCreateSessionAsync(context);
        var sequence = Interlocked.Increment(ref globalSequence);

        try
        {
            var ack = await session.PublishAndWaitAsync(
                sequenceNumber: sequence,
                payloadBytes: options.PayloadBytes,
                batchEvery: Math.Max(1, options.BatchEvery),
                receiveTimeoutMs: options.ReceiveTimeoutMs,
                cancellationToken: context.ScenarioCancellationToken);

            return ack.Accepted
                ? Response.Ok()
                : Response.Fail(statusCode: "rejected", message: ack.Reason ?? "Server rejected the publish request.", sizeBytes: 0, customLatencyMs: 0);
        }
        catch (OperationCanceledException) when (context.ScenarioCancellationToken.IsCancellationRequested)
        {
            return Response.Ok(statusCode: "canceled");
        }
        catch (TimeoutException exception)
        {
            return Response.Fail(statusCode: "timeout", message: exception.Message, sizeBytes: 0, customLatencyMs: 0);
        }
        catch (Exception exception)
        {
            return Response.Fail(statusCode: "exception", message: exception.Message, sizeBytes: 0, customLatencyMs: 0);
        }
    })
    .WithoutWarmUp()
    .WithInit(context =>
    {
        context.Logger.Information(
            "Load test init: baseUrl={0}, connections={1}, group={2}",
            options.BaseUrl,
            options.Connections,
            options.GroupName);

        return Task.CompletedTask;
    })
    .WithClean(async _ =>
    {
        while (createdSessions.TryTake(out var session))
        {
            await session.DisposeAsync();
        }
    })
    .WithLoadSimulations(
        Simulation.RampingConstant(copies: options.Connections, during: TimeSpan.FromSeconds(options.RampUpSeconds)),
        Simulation.KeepConstant(copies: options.Connections, during: TimeSpan.FromSeconds(options.SteadySeconds)),
        Simulation.RampingConstant(copies: 0, during: TimeSpan.FromSeconds(options.RampDownSeconds))
    );

NBomberRunner
    .RegisterScenarios(scenario)
    .WithTestSuite("highload-realtime-signalr-demo")
    .WithTestName(options.ScenarioName)
    .WithReportFolder(reportFolder)
    .WithReportFormats(ReportFormat.Html, ReportFormat.Txt)
    .Run();

return;

async Task<SignalRClientSession> GetOrCreateSessionAsync(IScenarioContext context)
{
    const string SessionKey = "signalr-session";

    if (context.ScenarioInstanceData.TryGetValue(SessionKey, out var existingSession))
    {
        return (SignalRClientSession)existingSession;
    }

    var session = new SignalRClientSession(
        baseUrl: options.BaseUrl,
        senderId: $"nb-{Guid.NewGuid():N}".Substring(0, 16),
        groupName: options.GroupName);

    await session.StartAsync(context.ScenarioCancellationToken);
    context.ScenarioInstanceData[SessionKey] = session;
    createdSessions.Add(session);

    return session;
}
