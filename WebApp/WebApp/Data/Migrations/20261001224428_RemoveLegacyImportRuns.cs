using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyImportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacyImportRuns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegacyImportRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BackupName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BackupVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    Imported = table.Column<int>(type: "INTEGER", nullable: false),
                    RanByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    RanUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Skipped = table.Column<int>(type: "INTEGER", nullable: false),
                    Verified = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyImportRuns", x => x.Id);
                });
        }
    }
}
