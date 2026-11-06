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

    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KuryoDbContext>().Database.MigrateAsync(ct);
    }

    internal static DbContextOptionsBuilder ConfigureDbContext(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history").UseNetTopologySuite())
            .UseSnakeCaseNamingConvention();
}
