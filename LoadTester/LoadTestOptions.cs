using Highload.Realtime.Shared;

namespace LoadTester;

/// <summary>
/// Конфигурация self-load теста. Все параметры можно переопределить через env vars или CLI.
/// </summary>
internal sealed class LoadTestOptions
{
    public string BaseUrl { get; init; } = "http://localhost:8080";

    public string GroupName { get; init; } = RealtimeRoutes.DefaultGroup;

    public int Connections { get; init; } = 1_000;

    public int RampUpSeconds { get; init; } = 60;

    public int SteadySeconds { get; init; } = 120;

    public int RampDownSeconds { get; init; } = 15;

    public int ReceiveTimeoutMs { get; init; } = 5_000;

    public int PayloadBytes { get; init; } = 128;

    public int BatchEvery { get; init; } = 4;

    public string ScenarioName { get; init; } = "signalr-mixed-traffic";

    public static LoadTestOptions Parse(string[] args)
    {
        var values = args
            .Select(static argument => argument.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(static parts => parts.Length == 2 && parts[0].StartsWith("--", StringComparison.Ordinal))
            .ToDictionary(static parts => parts[0][2..], static parts => parts[1], StringComparer.OrdinalIgnoreCase);

        return new LoadTestOptions
        {
            BaseUrl = GetString(values, "base-url", Environment.GetEnvironmentVariable("LOADTEST_BASEURL")) ?? "http://localhost:8080",
            GroupName = GetString(values, "group", Environment.GetEnvironmentVariable("LOADTEST_GROUP")) ?? RealtimeRoutes.DefaultGroup,
            Connections = GetInt(values, "connections", Environment.GetEnvironmentVariable("LOADTEST_CONNECTIONS"), 1_000),
            RampUpSeconds = GetInt(values, "ramp-up", Environment.GetEnvironmentVariable("LOADTEST_RAMP_UP_SECONDS"), 60),
            SteadySeconds = GetInt(values, "steady", Environment.GetEnvironmentVariable("LOADTEST_STEADY_SECONDS"), 120),
            RampDownSeconds = GetInt(values, "ramp-down", Environment.GetEnvironmentVariable("LOADTEST_RAMP_DOWN_SECONDS"), 15),
            ReceiveTimeoutMs = GetInt(values, "receive-timeout-ms", Environment.GetEnvironmentVariable("LOADTEST_RECEIVE_TIMEOUT_MS"), 5_000),
            PayloadBytes = GetInt(values, "payload-bytes", Environment.GetEnvironmentVariable("LOADTEST_PAYLOAD_BYTES"), 128),
            BatchEvery = GetInt(values, "batch-every", Environment.GetEnvironmentVariable("LOADTEST_BATCH_EVERY"), 4),
            ScenarioName = GetString(values, "scenario", Environment.GetEnvironmentVariable("LOADTEST_SCENARIO")) ?? "signalr-mixed-traffic"
        };
    }

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key, string? envValue, int fallback)
    {
        if (values.TryGetValue(key, out var cliValue) && int.TryParse(cliValue, out var parsedCli))
        {
            return parsedCli;
        }

        if (int.TryParse(envValue, out var parsedEnv))
        {
            return parsedEnv;
        }

        return fallback;
    }

    private static string? GetString(IReadOnlyDictionary<string, string> values, string key, string? envValue)
    {
        if (values.TryGetValue(key, out var cliValue) && !string.IsNullOrWhiteSpace(cliValue))
        {
            return cliValue;
        }

        return string.IsNullOrWhiteSpace(envValue) ? null : envValue;
    }
}
