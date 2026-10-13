using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SqlHistory.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialHistoryStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcedureVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ObjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    SchemaName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProcedureName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Definition = table.Column<string>(type: "TEXT", nullable: true),
                    DefinitionHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureVersions_SchemaName_ProcedureName",
                table: "ProcedureVersions",
                columns: new[] { "SchemaName", "ProcedureName" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureVersions_SchemaName_ProcedureName_VersionNumber",
                table: "ProcedureVersions",
                columns: new[] { "SchemaName", "ProcedureName", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcedureVersions");
        }
    }
}
