using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VocabularyApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizSessionIdToQuizResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuizResults_UserId_AttemptedAt",
                table: "QuizResults");

            migrationBuilder.AddColumn<Guid>(
                name: "QuizSessionId",
                table: "QuizResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(@"
                DECLARE @SessionMap TABLE
                (
                    AttemptedAt datetime2 NOT NULL PRIMARY KEY,
                    QuizSessionId uniqueidentifier NOT NULL
                );

                INSERT INTO @SessionMap (AttemptedAt, QuizSessionId)
                SELECT distinctAttempts.AttemptedAt, NEWID()
                FROM (
                    SELECT DISTINCT AttemptedAt
                    FROM QuizResults
                ) AS distinctAttempts;

                UPDATE qr
                SET qr.QuizSessionId = sm.QuizSessionId
                FROM QuizResults qr
                INNER JOIN @SessionMap sm ON qr.AttemptedAt = sm.AttemptedAt
                WHERE qr.QuizSessionId IS NULL;
            ");

            migrationBuilder.AlterColumn<Guid>(
                name: "QuizSessionId",
                table: "QuizResults",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizResults_UserId_QuizSessionId_AttemptedAt",
                table: "QuizResults",
                columns: new[] { "UserId", "QuizSessionId", "AttemptedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuizResults_UserId_QuizSessionId_AttemptedAt",
                table: "QuizResults");

            migrationBuilder.DropColumn(
                name: "QuizSessionId",
                table: "QuizResults");

            migrationBuilder.CreateIndex(
                name: "IX_QuizResults_UserId_AttemptedAt",
                table: "QuizResults",
                columns: new[] { "UserId", "AttemptedAt" });
        }
    }
}
