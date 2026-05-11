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

    public int WarmUpSeconds { get; init; } = 30;

    public int WarmUpConnections { get; init; } = 100;

    public int SteadySeconds { get; init; } = 120;

    public int RampDownSeconds { get; init; } = 15;

    public int ReceiveTimeoutMs { get; init; } = 5_000;

    public int SendIntervalMs { get; init; } = 1_000;

    public int ConnectConcurrency { get; init; } = 32;

    public int ConnectAcquireTimeoutMs { get; init; } = 250;

    public int ConnectTimeoutMs { get; init; } = 15_000;

    public int ConnectRetryDelayMs { get; init; } = 500;

    public int MaxFailCount { get; init; } = 50_000;

    public bool PreflightEnabled { get; init; } = true;

    public bool PreflightRequired { get; init; } = true;

    public int PreflightTimeoutMs { get; init; } = 5_000;

    public int EarlyErrorLogEvery { get; init; } = 100;

    public int PayloadBytes { get; init; } = 128;

    public int BatchEvery { get; init; } = 0;

    public string TrafficProfile { get; init; } = "targeted";

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
            WarmUpSeconds = GetInt(values, "warm-up", Environment.GetEnvironmentVariable("LOADTEST_WARM_UP_SECONDS"), 30),
            WarmUpConnections = GetInt(values, "warm-up-connections", Environment.GetEnvironmentVariable("LOADTEST_WARM_UP_CONNECTIONS"), 100),
            SteadySeconds = GetInt(values, "steady", Environment.GetEnvironmentVariable("LOADTEST_STEADY_SECONDS"), 120),
            RampDownSeconds = GetInt(values, "ramp-down", Environment.GetEnvironmentVariable("LOADTEST_RAMP_DOWN_SECONDS"), 15),
            ReceiveTimeoutMs = GetInt(values, "receive-timeout-ms", Environment.GetEnvironmentVariable("LOADTEST_RECEIVE_TIMEOUT_MS"), 5_000),
            SendIntervalMs = GetInt(values, "send-interval-ms", Environment.GetEnvironmentVariable("LOADTEST_SEND_INTERVAL_MS"), 1_000),
            ConnectConcurrency = GetInt(values, "connect-concurrency", Environment.GetEnvironmentVariable("LOADTEST_CONNECT_CONCURRENCY"), 32),
            ConnectAcquireTimeoutMs = GetInt(values, "connect-acquire-timeout-ms", Environment.GetEnvironmentVariable("LOADTEST_CONNECT_ACQUIRE_TIMEOUT_MS"), 250),
            ConnectTimeoutMs = GetInt(values, "connect-timeout-ms", Environment.GetEnvironmentVariable("LOADTEST_CONNECT_TIMEOUT_MS"), 15_000),
            ConnectRetryDelayMs = GetInt(values, "connect-retry-delay-ms", Environment.GetEnvironmentVariable("LOADTEST_CONNECT_RETRY_DELAY_MS"), 500),
            MaxFailCount = GetInt(values, "max-fail-count", Environment.GetEnvironmentVariable("LOADTEST_MAX_FAIL_COUNT"), 50_000),
            PreflightEnabled = GetBool(values, "preflight-enabled", Environment.GetEnvironmentVariable("LOADTEST_PREFLIGHT_ENABLED"), fallback: true),
            PreflightRequired = GetBool(values, "preflight-required", Environment.GetEnvironmentVariable("LOADTEST_PREFLIGHT_REQUIRED"), fallback: true),
            PreflightTimeoutMs = GetInt(values, "preflight-timeout-ms", Environment.GetEnvironmentVariable("LOADTEST_PREFLIGHT_TIMEOUT_MS"), 5_000),
            EarlyErrorLogEvery = GetInt(values, "early-error-log-every", Environment.GetEnvironmentVariable("LOADTEST_EARLY_ERROR_LOG_EVERY"), 100),
            PayloadBytes = GetInt(values, "payload-bytes", Environment.GetEnvironmentVariable("LOADTEST_PAYLOAD_BYTES"), 128),
            BatchEvery = GetInt(values, "batch-every", Environment.GetEnvironmentVariable("LOADTEST_BATCH_EVERY"), 0),
            TrafficProfile = GetString(values, "traffic-profile", Environment.GetEnvironmentVariable("LOADTEST_TRAFFIC_PROFILE")) ?? "targeted",
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

    private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, string? envValue, bool fallback)
    {
        if (values.TryGetValue(key, out var cliValue) && bool.TryParse(cliValue, out var parsedCli))
        {
            return parsedCli;
        }

        if (bool.TryParse(envValue, out var parsedEnv))
        {
            return parsedEnv;
        }

        return fallback;
    }
}
