using Kuryo.Infrastructure.Live;
using Kuryo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Kuryo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKuryoPersistence(this IServiceCollection services, string connectionString)
    {
        var dataSource = BuildDataSource(connectionString);
        services.AddSingleton(dataSource);
        services.AddScoped<ChangeFeed>();
        services.AddDbContext<KuryoDbContext>((scope, options) =>
            ConfigureDbContext(options, dataSource).AddInterceptors(new ChangeFeedInterceptor(scope.GetRequiredService<ChangeFeed>())));
        return services;
    }

    public static NpgsqlDataSource BuildDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        // Points and areas travel as PostGIS geography, read and written as NetTopologySuite geometries.
        builder.UseNetTopologySuite();
        return builder.Build();
    }

    /// <summary>
    /// Brings the schema up to date. Several instances start at once in Compose (and in any scaled deployment), so
    /// they take turns behind a PostgreSQL advisory lock: the first migrates, the others find nothing left to do.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        const long lockKey = 0x4B7572796F; // "Kuryo"
        await using var connection = await services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({lockKey})", connection))
        {
            await take.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<KuryoDbContext>().Database.MigrateAsync(ct);
        }
        finally
        {
            await using var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({lockKey})", connection);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    internal static DbContextOptionsBuilder ConfigureDbContext(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history").UseNetTopologySuite())
            .UseSnakeCaseNamingConvention();
}
