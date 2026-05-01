using Microsoft.EntityFrameworkCore;

namespace UniMap360.Models;

public sealed class UniMap360PostgresContext : UniMap360ProContext
{
    public UniMap360PostgresContext()
    {
    }

    public UniMap360PostgresContext(DbContextOptions<UniMap360ProContext> options)
        : base(options)
    {
    }
}
