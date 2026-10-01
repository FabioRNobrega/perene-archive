using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SharePreviouslyPersonalReaderThemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReaderThemes_AspNetUsers_UserId",
                table: "ReaderThemes");

            migrationBuilder.DropIndex(
                name: "IX_ReaderThemes_UserId_Name",
                table: "ReaderThemes");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "ReaderThemes",
                newName: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderThemes_CreatedByUserId",
                table: "ReaderThemes",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReaderThemes_AspNetUsers_CreatedByUserId",
                table: "ReaderThemes",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReaderThemes_AspNetUsers_CreatedByUserId",
                table: "ReaderThemes");

            migrationBuilder.DropIndex(
                name: "IX_ReaderThemes_CreatedByUserId",
                table: "ReaderThemes");

            migrationBuilder.RenameColumn(
                name: "CreatedByUserId",
                table: "ReaderThemes",
                newName: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderThemes_UserId_Name",
                table: "ReaderThemes",
                columns: new[] { "UserId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_ReaderThemes_AspNetUsers_UserId",
                table: "ReaderThemes",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
