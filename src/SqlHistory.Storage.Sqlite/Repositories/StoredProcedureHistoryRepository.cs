using Microsoft.EntityFrameworkCore;
using SqlHistory.Core.Abstractions;
using SqlHistory.Core.Models;
using SqlHistory.Storage.Sqlite.Entities;
using SqlHistory.Storage.Sqlite.Persistence;

namespace SqlHistory.Storage.Sqlite.Repositories;

public class StoredProcedureHistoryRepository : IStoredProcedureHistoryRepository
{
    private readonly SqlHistoryDbContext _dbContext;

    public StoredProcedureHistoryRepository(SqlHistoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<StoredProcedureVersion?> GetLatestVersionAsync(string schemaName, string procedureName,
        CancellationToken cancellationToken = default)
    {
        var identity = new StoredProcedureIdentity(
            SourceProviders.SqlServer, schemaName, procedureName, RoutineType.Procedure, string.Empty);

        return GetLatestVersionAsync(identity, cancellationToken);
    }

    public async Task<StoredProcedureVersion?> GetLatestVersionAsync(StoredProcedureIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var entity = await ForIdentity(identity)
            .AsNoTracking()
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        
        if (entity == null) return null;
        
        var result = Map(entity);
        return result;
    }

    public async Task<IReadOnlyList<StoredProcedureVersion>> GetVersionsAsync(StoredProcedureIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var entities = await ForIdentity(identity)
            .AsNoTracking()
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<StoredProcedureVersion?> GetVersionAsync(StoredProcedureIdentity identity, int versionNumber,
        CancellationToken cancellationToken = default)
    {
        var entity = await ForIdentity(identity)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.VersionNumber == versionNumber, cancellationToken);

        return entity == null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<StoredProcedureVersion>> GetLatestVersionsAsync(string provider,
        CancellationToken cancellationToken = default)
    {
        var versions = _dbContext.ProcedureVersions;

        var entities = await versions
            .AsNoTracking()
            .Where(x => x.Provider == provider)
            .Where(x => x.VersionNumber == versions
                .Where(y =>
                    y.Provider == x.Provider &&
                    y.SchemaName == x.SchemaName &&
                    y.ProcedureName == x.ProcedureName &&
                    y.RoutineType == x.RoutineType &&
                    y.IdentityArguments == x.IdentityArguments)
                .Max(y => y.VersionNumber))
            .OrderBy(x => x.SchemaName)
            .ThenBy(x => x.ProcedureName)
            .ThenBy(x => x.IdentityArguments)
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<StoredProcedureVersion> AddVersionAsync(StoredProcedureSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);
        try
        {
            var latestVersionNumber =
                await ForIdentity(snapshot.Identity)
                    .MaxAsync(
                        x => (int?)x.VersionNumber,
                        cancellationToken)
                ?? 0;

            var nextVersionNumber =
                latestVersionNumber + 1;

            var entity =
                new StoredProcedureVersionEntity
                {
                    Provider = snapshot.Provider,
                    RoutineType = snapshot.RoutineType,
                    IdentityArguments = snapshot.IdentityArguments,
                    SchemaName = snapshot.SchemaName,
                    ProcedureName = snapshot.ProcedureName,
                    ObjectId = snapshot.ObjectId,
                    VersionNumber = nextVersionNumber,
                    Definition = snapshot.Definition,
                    DefinitionHash = snapshot.DefinitionHash,
                    CreatedAt = snapshot.CreatedAt,
                    ModifiedAt = snapshot.ModifiedAt,
                    CapturedAtUtc = snapshot.CapturedAtUtc
                };

            _dbContext.ProcedureVersions.Add(entity);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return Map(entity);
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }

    private IQueryable<StoredProcedureVersionEntity> ForIdentity(StoredProcedureIdentity identity)
    {
        return _dbContext.ProcedureVersions.Where(x =>
            x.Provider == identity.Provider &&
            x.SchemaName == identity.SchemaName &&
            x.ProcedureName == identity.ProcedureName &&
            x.RoutineType == identity.RoutineType &&
            x.IdentityArguments == identity.IdentityArguments);
    }

    private static StoredProcedureVersion Map(StoredProcedureVersionEntity entity)
    {
        return new StoredProcedureVersion
        {
            Id = entity.Id,
            VersionNumber = entity.VersionNumber,
            ObjectId = entity.ObjectId,
            Provider = entity.Provider,
            RoutineType = entity.RoutineType,
            IdentityArguments = entity.IdentityArguments,
            SchemaName = entity.SchemaName,
            ProcedureName = entity.ProcedureName,
            Definition = entity.Definition,
            DefinitionHash = entity.DefinitionHash,
            CreatedAt = entity.CreatedAt,
            ModifiedAt = entity.ModifiedAt,
            // SQLite returns DateTimeKind.Unspecified; this column is always written as UTC.
            CapturedAtUtc = DateTime.SpecifyKind(entity.CapturedAtUtc, DateTimeKind.Utc),
        };
    }
}
