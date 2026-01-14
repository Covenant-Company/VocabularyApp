using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VocabularyApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioUrlToWords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserWords_PartsOfSpeech_PartOfSpeechId",
                table: "UserWords");

            migrationBuilder.DropIndex(
                name: "IX_UserWords_UserId",
                table: "UserWords");

            migrationBuilder.Sql(
                "UPDATE UserWords SET PartOfSpeechId = 1 WHERE PartOfSpeechId IS NULL;" +
                "UPDATE uw SET PartOfSpeechId = 1 FROM UserWords uw LEFT JOIN PartsOfSpeech pos ON uw.PartOfSpeechId = pos.Id WHERE pos.Id IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "PartOfSpeechId",
                table: "UserWords",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserWords_UserId_WordId_PartOfSpeechId",
                table: "UserWords",
                columns: new[] { "UserId", "WordId", "PartOfSpeechId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserWords_PartsOfSpeech_PartOfSpeechId",
                table: "UserWords",
                column: "PartOfSpeechId",
                principalTable: "PartsOfSpeech",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserWords_PartsOfSpeech_PartOfSpeechId",
                table: "UserWords");

            migrationBuilder.DropIndex(
                name: "IX_UserWords_UserId_WordId_PartOfSpeechId",
                table: "UserWords");

            migrationBuilder.AlterColumn<int>(
                name: "PartOfSpeechId",
                table: "UserWords",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_UserWords_UserId",
                table: "UserWords",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserWords_PartsOfSpeech_PartOfSpeechId",
                table: "UserWords",
                column: "PartOfSpeechId",
                principalTable: "PartsOfSpeech",
                principalColumn: "Id");
        }
    }
}
