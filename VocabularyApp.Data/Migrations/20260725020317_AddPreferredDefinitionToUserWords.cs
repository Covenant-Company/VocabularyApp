using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VocabularyApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredDefinitionToUserWords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreferredWordDefinitionId",
                table: "UserWords",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserWords_PreferredWordDefinitionId",
                table: "UserWords",
                column: "PreferredWordDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserWords_WordDefinitions_PreferredWordDefinitionId",
                table: "UserWords",
                column: "PreferredWordDefinitionId",
                principalTable: "WordDefinitions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserWords_WordDefinitions_PreferredWordDefinitionId",
                table: "UserWords");

            migrationBuilder.DropIndex(
                name: "IX_UserWords_PreferredWordDefinitionId",
                table: "UserWords");

            migrationBuilder.DropColumn(
                name: "PreferredWordDefinitionId",
                table: "UserWords");
        }
    }
}
