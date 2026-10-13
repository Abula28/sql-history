using Microsoft.EntityFrameworkCore;
using SqlHistory.Storage.Sqlite.Entities;

namespace SqlHistory.Storage.Sqlite.Persistence;

public class SqlHistoryDbContext : DbContext
{
    public SqlHistoryDbContext(DbContextOptions<SqlHistoryDbContext> options) : base(options)
    {
    }
    
    internal DbSet<StoredProcedureVersionEntity> ProcedureVersions => Set<StoredProcedureVersionEntity>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SqlHistoryDbContext).Assembly);
    }
}