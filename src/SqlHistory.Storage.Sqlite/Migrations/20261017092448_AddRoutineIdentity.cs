using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SqlHistory.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutineIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcedureVersions_SchemaName_ProcedureName",
                table: "ProcedureVersions");

            migrationBuilder.DropIndex(
                name: "IX_ProcedureVersions_SchemaName_ProcedureName_VersionNumber",
                table: "ProcedureVersions");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ModifiedAt",
                table: "ProcedureVersions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "ProcedureVersions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "IdentityArguments",
                table: "ProcedureVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Every row recorded before this migration came from the SQL Server CLI,
            // so backfill the new identity columns with the SQL Server values.
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "ProcedureVersions",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "SqlServer");

            migrationBuilder.AddColumn<string>(
                name: "RoutineType",
                table: "ProcedureVersions",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Procedure");

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureVersions_Provider_SchemaName_ProcedureName_RoutineType_IdentityArguments",
                table: "ProcedureVersions",
                columns: new[] { "Provider", "SchemaName", "ProcedureName", "RoutineType", "IdentityArguments" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureVersions_Provider_SchemaName_ProcedureName_RoutineType_IdentityArguments_VersionNumber",
                table: "ProcedureVersions",
                columns: new[] { "Provider", "SchemaName", "ProcedureName", "RoutineType", "IdentityArguments", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcedureVersions_Provider_SchemaName_ProcedureName_RoutineType_IdentityArguments",
                table: "ProcedureVersions");

            migrationBuilder.DropIndex(
                name: "IX_ProcedureVersions_Provider_SchemaName_ProcedureName_RoutineType_IdentityArguments_VersionNumber",
                table: "ProcedureVersions");

            migrationBuilder.DropColumn(
                name: "IdentityArguments",
                table: "ProcedureVersions");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "ProcedureVersions");

            migrationBuilder.DropColumn(
                name: "RoutineType",
                table: "ProcedureVersions");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ModifiedAt",
                table: "ProcedureVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "ProcedureVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);

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
    }
}
