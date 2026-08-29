using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VocabularyApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class CorrectUserWordIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1
                    FROM dbo.UserWords
                    GROUP BY UserId, WordId
                    HAVING COUNT_BIG(*) > 1
                )
                    THROW 51000, 'R5 cannot enforce unique UserWords(UserId, WordId): duplicate rows exist.', 1;
            ");

            migrationBuilder.DropIndex(
                name: "IX_UserWords_UserId_WordId_PartOfSpeechId",
                table: "UserWords");

            migrationBuilder.CreateIndex(
                name: "IX_UserWords_UserId_WordId",
                table: "UserWords",
                columns: new[] { "UserId", "WordId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserWords_UserId_WordId",
                table: "UserWords");

            migrationBuilder.CreateIndex(
                name: "IX_UserWords_UserId_WordId_PartOfSpeechId",
                table: "UserWords",
                columns: new[] { "UserId", "WordId", "PartOfSpeechId" },
                unique: true);
        }
    }
}
