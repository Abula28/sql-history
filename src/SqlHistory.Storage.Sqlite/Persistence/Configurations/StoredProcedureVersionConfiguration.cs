using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SqlHistory.Storage.Sqlite.Entities;

namespace SqlHistory.Storage.Sqlite.Persistence.Configurations;

internal class StoredProcedureVersionConfiguration : IEntityTypeConfiguration<StoredProcedureVersionEntity>
{
    public void Configure(EntityTypeBuilder<StoredProcedureVersionEntity> builder)
    {
        builder.ToTable("ProcedureVersions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.Provider).IsRequired().HasMaxLength(32);

        builder.Property(x => x.RoutineType).IsRequired().HasConversion<string>().HasMaxLength(16);

        builder.Property(x => x.IdentityArguments).IsRequired();

        builder.Property(x => x.SchemaName).IsRequired().HasMaxLength(128);

        builder.Property(x => x.ProcedureName).IsRequired().HasMaxLength(128);

        builder.Property(x => x.VersionNumber).IsRequired();

        builder.Property(x => x.Definition).IsRequired(false);

        builder.Property(x => x.DefinitionHash).IsRequired().HasMaxLength(64);

        builder.Property(x => x.CreatedAt).IsRequired(false);

        builder.Property(x => x.ModifiedAt).IsRequired(false);

        builder.Property(x => x.CapturedAtUtc).IsRequired();

        builder.HasIndex(x => new
        {
            x.Provider,
            x.SchemaName,
            x.ProcedureName,
            x.RoutineType,
            x.IdentityArguments,
            x.VersionNumber
        }).IsUnique();

        builder.HasIndex(x => new
        {
            x.Provider,
            x.SchemaName,
            x.ProcedureName,
            x.RoutineType,
            x.IdentityArguments
        });
    }
}