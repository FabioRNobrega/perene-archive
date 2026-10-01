using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerUserMediaData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomStorageViews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    CategoryKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FolderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IsWholeArchive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    MaxSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomStorageViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomStorageViews_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LegacyImportRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RanUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RanByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    BackupName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BackupVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    Imported = table.Column<int>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<int>(type: "INTEGER", nullable: false),
                    Verified = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FolderId = table.Column<long>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContentFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IdentityKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ContentRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    SupersededByMediaItemId = table.Column<long>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MissingSince = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaItems_Folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MediaItems_MediaItems_SupersededByMediaItemId",
                        column: x => x.SupersededByMediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ReaderThemePreferences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    FontFamily = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FontSizePx = table.Column<int>(type: "INTEGER", nullable: false),
                    LineHeight = table.Column<double>(type: "REAL", nullable: false),
                    ForegroundColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    ContentPaddingPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedThemePublicId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderThemePreferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_ReaderThemePreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReaderThemes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FontFamily = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FontSizePx = table.Column<int>(type: "INTEGER", nullable: false),
                    LineHeight = table.Column<double>(type: "REAL", nullable: false),
                    ForegroundColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    ContentPaddingPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderThemes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReaderThemes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookHighlights",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HighlightKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    MediaItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChapterId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TextOffsetStart = table.Column<int>(type: "INTEGER", nullable: false),
                    TextOffsetEnd = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedText = table.Column<string>(type: "TEXT", nullable: false),
                    ContextBefore = table.Column<string>(type: "TEXT", nullable: false),
                    ContextAfter = table.Column<string>(type: "TEXT", nullable: false),
                    ContentRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookHighlights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookHighlights_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookHighlights_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookNotes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NoteKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    MediaItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    BookTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    BookAuthor = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ChapterIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    TextOffsetStart = table.Column<int>(type: "INTEGER", nullable: true),
                    TextOffsetEnd = table.Column<int>(type: "INTEGER", nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookNotes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookNotes_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComicProgresses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    MediaItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    PageIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComicProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComicProgresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComicProgresses_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Favorites",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    MediaItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Favorites_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Favorites_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReadingProgresses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    MediaItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChapterId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    WordOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    ScrollFraction = table.Column<double>(type: "REAL", nullable: true),
                    ContentRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingProgresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingProgresses_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookHighlights_MediaItemId",
                table: "BookHighlights",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BookHighlights_UserId_HighlightKey",
                table: "BookHighlights",
                columns: new[] { "UserId", "HighlightKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookHighlights_UserId_MediaItemId",
                table: "BookHighlights",
                columns: new[] { "UserId", "MediaItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_BookNotes_MediaItemId",
                table: "BookNotes",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BookNotes_UserId_MediaItemId",
                table: "BookNotes",
                columns: new[] { "UserId", "MediaItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_BookNotes_UserId_NoteKey",
                table: "BookNotes",
                columns: new[] { "UserId", "NoteKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComicProgresses_MediaItemId",
                table: "ComicProgresses",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ComicProgresses_UserId_MediaItemId",
                table: "ComicProgresses",
                columns: new[] { "UserId", "MediaItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomStorageViews_PublicId",
                table: "CustomStorageViews",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomStorageViews_UserId",
                table: "CustomStorageViews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_MediaItemId",
                table: "Favorites",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId_MediaItemId",
                table: "Favorites",
                columns: new[] { "UserId", "MediaItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ContentFingerprint",
                table: "MediaItems",
                column: "ContentFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_FolderId_RelativePath",
                table: "MediaItems",
                columns: new[] { "FolderId", "RelativePath" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_Status",
                table: "MediaItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_SupersededByMediaItemId",
                table: "MediaItems",
                column: "SupersededByMediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderThemes_PublicId",
                table: "ReaderThemes",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReaderThemes_UserId_Name",
                table: "ReaderThemes",
                columns: new[] { "UserId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingProgresses_MediaItemId",
                table: "ReadingProgresses",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingProgresses_UserId_MediaItemId",
                table: "ReadingProgresses",
                columns: new[] { "UserId", "MediaItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookHighlights");

            migrationBuilder.DropTable(
                name: "BookNotes");

            migrationBuilder.DropTable(
                name: "ComicProgresses");

            migrationBuilder.DropTable(
                name: "CustomStorageViews");

            migrationBuilder.DropTable(
                name: "Favorites");

            migrationBuilder.DropTable(
                name: "LegacyImportRuns");

            migrationBuilder.DropTable(
                name: "ReaderThemePreferences");

            migrationBuilder.DropTable(
                name: "ReaderThemes");

            migrationBuilder.DropTable(
                name: "ReadingProgresses");

            migrationBuilder.DropTable(
                name: "MediaItems");
        }
    }
}
