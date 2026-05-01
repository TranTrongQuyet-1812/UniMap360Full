using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UniMap360.Models;

public sealed class UniMap360PostgresContextFactory : IDesignTimeDbContextFactory<UniMap360PostgresContext>
{
    public UniMap360PostgresContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=unimap360_tmp;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<UniMap360ProContext>();
        optionsBuilder.UseNpgsql(connectionString, x => x.UseNetTopologySuite());
        return new UniMap360PostgresContext(optionsBuilder.Options);
    }
}
