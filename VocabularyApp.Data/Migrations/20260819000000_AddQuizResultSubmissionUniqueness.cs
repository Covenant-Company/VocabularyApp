using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VocabularyApp.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260819000000_AddQuizResultSubmissionUniqueness")]
    public partial class AddQuizResultSubmissionUniqueness : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_QuizResults_UserId_QuizSessionId_UserWordId",
                table: "QuizResults",
                columns: new[] { "UserId", "QuizSessionId", "UserWordId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuizResults_UserId_QuizSessionId_UserWordId",
                table: "QuizResults");
        }
    }
}
