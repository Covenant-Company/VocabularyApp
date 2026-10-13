using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VocabularyApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpacedRepetition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ContentRevision",
                table: "WordDefinitions",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_UserWords_UserId_Id",
                table: "UserWords",
                columns: new[] { "UserId", "Id" });

            migrationBuilder.CreateTable(
                name: "ReviewSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RequestedQuestionCount = table.Column<int>(type: "int", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewSessions", x => x.Id);
                    table.UniqueConstraint("AK_ReviewSessions_UserId_Id", x => new { x.UserId, x.Id });
                    table.CheckConstraint("CK_ReviewSessions_Counts", "[RequestedQuestionCount] BETWEEN 1 AND 20 AND [ItemCount] BETWEEN 1 AND [RequestedQuestionCount]");
                    table.CheckConstraint("CK_ReviewSessions_Timestamps", "[ExpiresAt] > [CreatedAt] AND ([CompletedAt] IS NULL OR [CompletedAt] >= [CreatedAt])");
                    table.CheckConstraint("CK_ReviewSessions_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_ReviewSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserWordReviewStates",
                columns: table => new
                {
                    UserWordId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ScheduledWordDefinitionId = table.Column<int>(type: "int", nullable: true),
                    ScheduledDefinitionRevision = table.Column<long>(type: "bigint", nullable: true),
                    EnrollmentAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    NextReviewAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    LastReviewedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ConsecutiveCorrectCount = table.Column<int>(type: "int", nullable: false),
                    ReviewIntervalDays = table.Column<int>(type: "int", nullable: false),
                    LapseCount = table.Column<int>(type: "int", nullable: false),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ScheduleVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWordReviewStates", x => x.UserWordId);
                    table.CheckConstraint("CK_UserWordReviewStates_Meaning", "([ScheduledWordDefinitionId] IS NULL AND [ScheduledDefinitionRevision] IS NULL) OR ([ScheduledWordDefinitionId] IS NOT NULL AND [ScheduledDefinitionRevision] IS NOT NULL AND [ScheduledDefinitionRevision] >= 1)");
                    table.CheckConstraint("CK_UserWordReviewStates_Progression", "([ReviewStatus] = 0 AND [ConsecutiveCorrectCount] = 0 AND [ReviewIntervalDays] = 0) OR ([ReviewStatus] = 1 AND (([ConsecutiveCorrectCount] = 0 AND [ReviewIntervalDays] = 1) OR ([ConsecutiveCorrectCount] = 1 AND [ReviewIntervalDays] = 1) OR ([ConsecutiveCorrectCount] = 2 AND [ReviewIntervalDays] = 3))) OR ([ReviewStatus] = 2 AND (([ConsecutiveCorrectCount] = 3 AND [ReviewIntervalDays] = 7) OR ([ConsecutiveCorrectCount] = 4 AND [ReviewIntervalDays] = 14) OR ([ConsecutiveCorrectCount] = 5 AND [ReviewIntervalDays] = 30) OR ([ConsecutiveCorrectCount] = 6 AND [ReviewIntervalDays] = 60) OR ([ConsecutiveCorrectCount] = 7 AND [ReviewIntervalDays] = 90)))");
                    table.CheckConstraint("CK_UserWordReviewStates_Timestamps", "[NextReviewAt] >= [EnrollmentAt] AND (([ReviewStatus] = 0 AND [LastReviewedAt] IS NULL) OR ([ReviewStatus] <> 0 AND [LastReviewedAt] IS NOT NULL AND [LastReviewedAt] >= [EnrollmentAt] AND [NextReviewAt] > [LastReviewedAt]))");
                    table.CheckConstraint("CK_UserWordReviewStates_Versions", "[ScheduleVersion] >= 1 AND [LapseCount] >= 0 AND [AlgorithmVersion] = 'interval-v1'");
                    table.ForeignKey(
                        name: "FK_UserWordReviewStates_UserWords_UserId_UserWordId",
                        columns: x => new { x.UserId, x.UserWordId },
                        principalTable: "UserWords",
                        principalColumns: new[] { "UserId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserWordReviewStates_WordDefinitions_ScheduledWordDefinitionId",
                        column: x => x.ScheduledWordDefinitionId,
                        principalTable: "WordDefinitions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReviewSessionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserWordId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    CapturedScheduleVersion = table.Column<long>(type: "bigint", nullable: false),
                    WordDefinitionId = table.Column<int>(type: "int", nullable: false),
                    DefinitionRevision = table.Column<long>(type: "bigint", nullable: false),
                    WordText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefinitionText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PartOfSpeech = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    QuestionType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrectOptionId = table.Column<int>(type: "int", nullable: false),
                    AcceptedSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    SelectedOptionId = table.Column<int>(type: "int", nullable: true),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: true),
                    ReceiptJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceiptContractVersion = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewSessionItems", x => x.Id);
                    table.UniqueConstraint("AK_ReviewSessionItems_UserId_Id_UserWordId", x => new { x.UserId, x.Id, x.UserWordId });
                    table.CheckConstraint("CK_ReviewSessionItems_Acceptance", "([AcceptedSubmissionId] IS NULL AND [AcceptedAt] IS NULL AND [SelectedOptionId] IS NULL AND [IsCorrect] IS NULL AND [ReceiptJson] IS NULL AND [ReceiptContractVersion] IS NULL) OR ([AcceptedSubmissionId] IS NOT NULL AND [AcceptedAt] IS NOT NULL AND [SelectedOptionId] IS NOT NULL AND [SelectedOptionId] BETWEEN 0 AND 3 AND [IsCorrect] IS NOT NULL AND [ReceiptJson] IS NOT NULL AND [ReceiptContractVersion] IS NOT NULL AND [ReceiptContractVersion] = 1)");
                    table.CheckConstraint("CK_ReviewSessionItems_Question", "[Ordinal] BETWEEN 0 AND 19 AND [CapturedScheduleVersion] >= 1 AND [DefinitionRevision] >= 1 AND [CorrectOptionId] BETWEEN 0 AND 3 AND [QuestionType] = 'word-to-definition'");
                    table.ForeignKey(
                        name: "FK_ReviewSessionItems_ReviewSessions_UserId_SessionId",
                        columns: x => new { x.UserId, x.SessionId },
                        principalTable: "ReviewSessions",
                        principalColumns: new[] { "UserId", "Id" });
                    table.ForeignKey(
                        name: "FK_ReviewSessionItems_UserWords_UserId_UserWordId",
                        columns: x => new { x.UserId, x.UserWordId },
                        principalTable: "UserWords",
                        principalColumns: new[] { "UserId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ReviewHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserWordId = table.Column<int>(type: "int", nullable: false),
                    SessionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    WordDefinitionId = table.Column<int>(type: "int", nullable: false),
                    DefinitionRevision = table.Column<long>(type: "bigint", nullable: false),
                    WordText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefinitionText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    QuestionType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OutcomeSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    SelectedOptionId = table.Column<int>(type: "int", nullable: false),
                    SelectedAnswer = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CorrectAnswer = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreviousScheduleVersion = table.Column<long>(type: "bigint", nullable: false),
                    NextScheduleVersion = table.Column<long>(type: "bigint", nullable: false),
                    PreviousNextReviewAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    NextReviewAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    PreviousLastReviewedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    PreviousIntervalDays = table.Column<int>(type: "int", nullable: false),
                    NextIntervalDays = table.Column<int>(type: "int", nullable: false),
                    PreviousCorrectCount = table.Column<int>(type: "int", nullable: false),
                    NextCorrectCount = table.Column<int>(type: "int", nullable: false),
                    PreviousLapseCount = table.Column<int>(type: "int", nullable: false),
                    NextLapseCount = table.Column<int>(type: "int", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: false),
                    NextStatus = table.Column<int>(type: "int", nullable: false),
                    ElapsedSincePreviousReviewTicks = table.Column<long>(type: "bigint", nullable: true),
                    ReceiptJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceiptContractVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewHistories", x => x.Id);
                    table.CheckConstraint("CK_ReviewHistories_NextProgression", "[NextStatus] IN (1, 2) AND (([NextStatus] = 0 AND [NextCorrectCount] = 0 AND [NextIntervalDays] = 0) OR ([NextStatus] = 1 AND (([NextCorrectCount] = 0 AND [NextIntervalDays] = 1) OR ([NextCorrectCount] = 1 AND [NextIntervalDays] = 1) OR ([NextCorrectCount] = 2 AND [NextIntervalDays] = 3))) OR ([NextStatus] = 2 AND (([NextCorrectCount] = 3 AND [NextIntervalDays] = 7) OR ([NextCorrectCount] = 4 AND [NextIntervalDays] = 14) OR ([NextCorrectCount] = 5 AND [NextIntervalDays] = 30) OR ([NextCorrectCount] = 6 AND [NextIntervalDays] = 60) OR ([NextCorrectCount] = 7 AND [NextIntervalDays] = 90))))");
                    table.CheckConstraint("CK_ReviewHistories_Outcome", "[SelectedOptionId] BETWEEN 0 AND 3 AND [PreviousLapseCount] >= 0 AND [NextLapseCount] >= 0 AND [QuestionType] = 'word-to-definition' AND [OutcomeSource] = 'server-multiple-choice'");
                    table.CheckConstraint("CK_ReviewHistories_PreviousProgression", "([PreviousStatus] = 0 AND [PreviousCorrectCount] = 0 AND [PreviousIntervalDays] = 0) OR ([PreviousStatus] = 1 AND (([PreviousCorrectCount] = 0 AND [PreviousIntervalDays] = 1) OR ([PreviousCorrectCount] = 1 AND [PreviousIntervalDays] = 1) OR ([PreviousCorrectCount] = 2 AND [PreviousIntervalDays] = 3))) OR ([PreviousStatus] = 2 AND (([PreviousCorrectCount] = 3 AND [PreviousIntervalDays] = 7) OR ([PreviousCorrectCount] = 4 AND [PreviousIntervalDays] = 14) OR ([PreviousCorrectCount] = 5 AND [PreviousIntervalDays] = 30) OR ([PreviousCorrectCount] = 6 AND [PreviousIntervalDays] = 60) OR ([PreviousCorrectCount] = 7 AND [PreviousIntervalDays] = 90)))");
                    table.CheckConstraint("CK_ReviewHistories_Timestamps", "[ReviewedAt] >= [PreviousNextReviewAt] AND [NextReviewAt] > [ReviewedAt] AND (([PreviousStatus] = 0 AND [PreviousLastReviewedAt] IS NULL AND [ElapsedSincePreviousReviewTicks] IS NULL) OR ([PreviousStatus] <> 0 AND [PreviousLastReviewedAt] IS NOT NULL AND [PreviousLastReviewedAt] <= [ReviewedAt] AND [ElapsedSincePreviousReviewTicks] IS NOT NULL AND [ElapsedSincePreviousReviewTicks] >= 0))");
                    table.CheckConstraint("CK_ReviewHistories_Versions", "[PreviousScheduleVersion] >= 1 AND [PreviousScheduleVersion] < 9223372036854775807 AND [NextScheduleVersion] = [PreviousScheduleVersion] + 1 AND [DefinitionRevision] >= 1 AND [ReceiptContractVersion] = 1 AND [AlgorithmVersion] = 'interval-v1'");
                    table.ForeignKey(
                        name: "FK_ReviewHistories_ReviewSessionItems_UserId_SessionItemId_UserWordId",
                        columns: x => new { x.UserId, x.SessionItemId, x.UserWordId },
                        principalTable: "ReviewSessionItems",
                        principalColumns: new[] { "UserId", "Id", "UserWordId" });
                    table.ForeignKey(
                        name: "FK_ReviewHistories_UserWords_UserId_UserWordId",
                        columns: x => new { x.UserId, x.UserWordId },
                        principalTable: "UserWords",
                        principalColumns: new[] { "UserId", "Id" });
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WordDefinitions_ContentRevision",
                table: "WordDefinitions",
                sql: "[ContentRevision] >= 1");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewHistories_SessionItemId",
                table: "ReviewHistories",
                column: "SessionItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewHistories_UserId_SessionItemId_UserWordId",
                table: "ReviewHistories",
                columns: new[] { "UserId", "SessionItemId", "UserWordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewHistories_UserId_SubmissionId",
                table: "ReviewHistories",
                columns: new[] { "UserId", "SubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewHistories_UserId_UserWordId_ReviewedAt_Id",
                table: "ReviewHistories",
                columns: new[] { "UserId", "UserWordId", "ReviewedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewSessionItems_SessionId_Ordinal",
                table: "ReviewSessionItems",
                columns: new[] { "SessionId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewSessionItems_SessionId_UserWordId",
                table: "ReviewSessionItems",
                columns: new[] { "SessionId", "UserWordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewSessionItems_UserId_AcceptedSubmissionId",
                table: "ReviewSessionItems",
                columns: new[] { "UserId", "AcceptedSubmissionId" },
                unique: true,
                filter: "[AcceptedSubmissionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewSessionItems_UserId_SessionId",
                table: "ReviewSessionItems",
                columns: new[] { "UserId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewSessionItems_UserId_UserWordId_AcceptedAt",
                table: "ReviewSessionItems",
                columns: new[] { "UserId", "UserWordId", "AcceptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewSessions_UserId_ExpiresAt_Id",
                table: "ReviewSessions",
                columns: new[] { "UserId", "ExpiresAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_UserWordReviewStates_ScheduledWordDefinitionId",
                table: "UserWordReviewStates",
                column: "ScheduledWordDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserWordReviewStates_UserId_NextReviewAt_UserWordId",
                table: "UserWordReviewStates",
                columns: new[] { "UserId", "NextReviewAt", "UserWordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserWordReviewStates_UserId_UserWordId",
                table: "UserWordReviewStates",
                columns: new[] { "UserId", "UserWordId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewHistories");

            migrationBuilder.DropTable(
                name: "UserWordReviewStates");

            migrationBuilder.DropTable(
                name: "ReviewSessionItems");

            migrationBuilder.DropTable(
                name: "ReviewSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WordDefinitions_ContentRevision",
                table: "WordDefinitions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_UserWords_UserId_Id",
                table: "UserWords");

            migrationBuilder.DropColumn(
                name: "ContentRevision",
                table: "WordDefinitions");
        }
    }
}
