using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Polly;
using Polly.CircuitBreaker;

namespace Server.Services;

/// <summary>
/// Держит минимальный circuit breaker только вокруг редких PostgreSQL-вызовов,
/// чтобы деградация БД не тянула за собой лишние таймауты при старте и readiness-проверках.
/// </summary>
internal static class PostgresResilience
{
    internal static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 2,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(15)
        })
        .Build();
}

/// <summary>
/// Подготавливает минимальную PostgreSQL-схему для фиксации прогонов и проверки готовности зависимостей.
/// </summary>
internal sealed class PostgresBootstrapService(
    ILogger<PostgresBootstrapService> logger,
    IConfiguration configuration,
    NpgsqlDataSource dataSource) : IHostedService
{
    private const string AdminDatabaseName = "postgres";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("Строка подключения PostgreSQL отсутствует. Bootstrap пропущен.");
            return;
        }

        try
        {
            await EnsureDatabaseExistsAsync(connectionString, cancellationToken);

            await PostgresResilience.Pipeline.ExecuteAsync(async token =>
            {
                await using var command = dataSource.CreateCommand(GetBootstrapSql());
                await command.ExecuteNonQueryAsync(token);
            }, cancellationToken);

            logger.LogInformation("PostgreSQL schema и seed data подготовлены.");
        }
        catch (BrokenCircuitException exception)
        {
            logger.LogWarning(exception, "Circuit breaker PostgreSQL открыт во время старта. Приложение продолжит работу в degraded-режиме.");
        }
        catch (Exception exception)
        {
            // БД в локальном smoke-режиме может быть выключена; real-time сервер не должен из-за этого падать.
            logger.LogWarning(exception, "PostgreSQL недоступен во время старта. Приложение продолжит работу в degraded-режиме.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureDatabaseExistsAsync(string connectionString, CancellationToken cancellationToken)
    {
        var targetConnectionString = new NpgsqlConnectionStringBuilder(connectionString);
        var targetDatabaseName = targetConnectionString.Database;

        if (string.IsNullOrWhiteSpace(targetDatabaseName))
        {
            throw new InvalidOperationException("В строке подключения PostgreSQL не указано имя базы данных.");
        }

        // Создаём отдельное admin-подключение к уже существующей служебной БД,
        // чтобы иметь возможность создать целевую БД до инициализации рабочей схемы.
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = AdminDatabaseName,
            Pooling = false
        };

        await using var adminDataSource = NpgsqlDataSource.Create(adminConnectionString);

        await PostgresResilience.Pipeline.ExecuteAsync(async token =>
        {
            await using var existsCommand = adminDataSource.CreateCommand("select 1 from pg_database where datname = $1;");
            existsCommand.Parameters.AddWithValue(targetDatabaseName);

            var databaseExists = await existsCommand.ExecuteScalarAsync(token) is not null;
            if (databaseExists)
            {
                return;
            }

            var createDatabaseSql = $"create database {QuoteIdentifier(targetDatabaseName)}";
            await using var createCommand = adminDataSource.CreateCommand(createDatabaseSql);
            await createCommand.ExecuteNonQueryAsync(token);
        }, cancellationToken);
    }

    private static string GetBootstrapSql() => """
        create table if not exists load_test_runs
        (
            id bigserial primary key,
            created_at_utc timestamptz not null default timezone('utc', now()),
            scenario_name text not null,
            summary_json jsonb not null
        );

        create table if not exists demo_groups
        (
            name text primary key,
            description text not null,
            created_at_utc timestamptz not null default timezone('utc', now())
        );

        insert into demo_groups (name, description)
        values
            ('loadtest', 'Группа по умолчанию для NBomber и массовых прогонов.'),
            ('smoke', 'Группа для ручного smoke через страницу /realtime.'),
            ('perf', 'Группа для локальных performance-экспериментов.')
        on conflict (name) do update
        set description = excluded.description;
        """;

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

/// <summary>
/// Лёгкая проверка PostgreSQL для readiness, не участвующая в hot path SignalR.
/// </summary>
internal sealed class PostgresReadinessHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await PostgresResilience.Pipeline.ExecuteAsync(async token =>
            {
                await using var command = dataSource.CreateCommand("select 1;");
                await command.ExecuteScalarAsync(token);
            }, cancellationToken);

            return HealthCheckResult.Healthy("PostgreSQL connection is ready.");
        }
        catch (BrokenCircuitException exception)
        {
            return HealthCheckResult.Degraded("PostgreSQL circuit breaker is open.", exception);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", exception);
        }
    }
}
