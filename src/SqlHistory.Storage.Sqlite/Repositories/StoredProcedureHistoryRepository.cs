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

    public async Task<StoredProcedureVersion?> GetLatestVersionAsync(string schemaName, string procedureName,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ProcedureVersions.AsNoTracking()
            .Where(x =>
                x.SchemaName == schemaName &&
                x.ProcedureName == procedureName)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        
        if (entity == null) return null;
        
        var result = Map(entity);
        return result;
    }

    public async Task<StoredProcedureVersion> AddVersionAsync(StoredProcedureSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);
        try
        {
            var latestVersionNumber =
                await _dbContext.ProcedureVersions
                    .Where(x =>
                        x.SchemaName == snapshot.SchemaName &&
                        x.ProcedureName == snapshot.ProcedureName)
                    .MaxAsync(
                        x => (int?)x.VersionNumber,
                        cancellationToken)
                ?? 0;

            var nextVersionNumber =
                latestVersionNumber + 1;

            var entity =
                new StoredProcedureVersionEntity
                {
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

    private static StoredProcedureVersion Map(StoredProcedureVersionEntity entity)
    {
        return new StoredProcedureVersion
        {
            Id = entity.Id,
            VersionNumber = entity.VersionNumber,
            ObjectId = entity.ObjectId,
            SchemaName = entity.SchemaName,
            ProcedureName = entity.ProcedureName,
            Definition = entity.Definition,
            DefinitionHash = entity.DefinitionHash,
            CreatedAt = entity.CreatedAt,
            ModifiedAt = entity.ModifiedAt,
            CapturedAtUtc = entity.CapturedAtUtc,
        };
    }
}