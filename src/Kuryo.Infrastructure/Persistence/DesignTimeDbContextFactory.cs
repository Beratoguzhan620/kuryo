using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kuryo.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; generating migrations does not open a connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KuryoDbContext>
{
    public KuryoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KuryoDbContext>();
        DependencyInjection.ConfigureDbContext(options, DependencyInjection.BuildDataSource("Host=localhost;Database=kuryo_design"));
        return new KuryoDbContext(options.Options);
    }
}
