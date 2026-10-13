using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Services;

public sealed class ReviewService(ApplicationDbContext db, ReviewStateCoordinator coordinator,
    IReviewScheduler scheduler, TimeProvider clock, IOptionsMonitor<ReviewOptions> options,
    IDataProtectionProvider protection, IServiceScopeFactory scopes, ILogger<ReviewService> logger) : IReviewService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _cursor = protection.CreateProtector("F6.DueCursor.v1");
    private sealed record Cursor(int Owner, DateTime AsOf, DateTime LastDue, int LastId);
    internal sealed record Option(int OptionId, string Text, int SourceUserWordId, int SourceDefinitionId, long SourceDefinitionRevision);
    private bool Enabled => options.CurrentValue.Enabled;
    private static ServiceResult<T> Fail<T>(string code, ServiceFailureType type = ServiceFailureType.Conflict) => ServiceResult<T>.Failure("Review operation unavailable.", type, code);
    private static ServiceResult<T> Disabled<T>() => Fail<T>("reviews_disabled", ServiceFailureType.ServiceUnavailable);
    private static string Status(int value) => value switch { 0 => "new", 1 => "learning", 2 => "reviewing", _ => throw new InvalidOperationException("Invalid review status.") };

    public async Task<ServiceResult<ReviewDueResponseDto>> DueAsync(int userId, ReviewDueQueryDto query)
    {
        if (!Enabled) return Disabled<ReviewDueResponseDto>();
        var now = clock.GetUtcNow().UtcDateTime;
        Cursor? cursor = null;
        if (query.Cursor != null)
        {
            try { cursor = JsonSerializer.Deserialize<Cursor>(_cursor.Unprotect(query.Cursor), Json); }
            catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
            { return Fail<ReviewDueResponseDto>("validation_failed", ServiceFailureType.Validation); }
            if (cursor == null || cursor.Owner != userId || cursor.AsOf.Kind != DateTimeKind.Utc ||
                cursor.LastDue.Kind != DateTimeKind.Utc || cursor.LastId <= 0 || cursor.LastDue > cursor.AsOf ||
                cursor.AsOf > now || now - cursor.AsOf >= TimeSpan.FromMinutes(30))
                return Fail<ReviewDueResponseDto>("validation_failed", ServiceFailureType.Validation);
        }
        var asOf = cursor?.AsOf ?? now;
        var states = db.UserWordReviewStates.AsNoTracking().Where(s => s.UserId == userId && s.NextReviewAt <= asOf);
        if (cursor != null)
        {
            var last = cursor.LastDue; var id = cursor.LastId;
            states = states.Where(s => s.NextReviewAt > last || s.NextReviewAt == last && s.UserWordId > id);
        }
        var page = await states.OrderBy(s => s.NextReviewAt).ThenBy(s => s.UserWordId).Take(query.Take + 1).ToListAsync();
        var more = page.Count > query.Take;
        if (more) page.RemoveAt(page.Count - 1);
        var ids = page.Select(s => s.UserWordId).ToArray();
        var words = await coordinator.Words(userId).AsNoTracking().Where(w => ids.Contains(w.Id)).ToDictionaryAsync(w => w.Id);
        var response = new ReviewDueResponseDto { AsOfUtc = asOf, Items = page.Select(s => new ReviewDueItemDto
        {
            UserWordId = s.UserWordId, Word = words[s.UserWordId].Word.Text, NextReviewAt = s.NextReviewAt,
            ReviewStatus = Status(s.ReviewStatus), IsUsable = ReviewStateCoordinator.Usable(words[s.UserWordId])
        }).ToList() };
        if (more)
        {
            var last = page[^1];
            response.NextCursor = _cursor.Protect(JsonSerializer.Serialize(new Cursor(userId, asOf, last.NextReviewAt, last.UserWordId), Json));
        }
        return ServiceResult<ReviewDueResponseDto>.Success(response);
    }

    public async Task<ServiceResult<ReviewSummaryResponseDto>> SummaryAsync(int userId)
    {
        if (!Enabled) return Disabled<ReviewSummaryResponseDto>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var now = clock.GetUtcNow().UtcDateTime;
        var states = db.UserWordReviewStates.AsNoTracking().Where(s => s.UserId == userId);
        var response = new ReviewSummaryResponseDto
        {
            AsOfUtc = now, TotalEnrolledCount = await states.CountAsync(),
            DueCount = await states.CountAsync(s => s.NextReviewAt <= now), NewCount = await states.CountAsync(s => s.ReviewStatus == 0),
            OverdueCount = await states.CountAsync(s => s.NextReviewAt < now),
            NextReviewAt = await states.Where(s => s.NextReviewAt > now).OrderBy(s => s.NextReviewAt).Select(s => (DateTime?)s.NextReviewAt).FirstOrDefaultAsync()
        };
        // Unicode whitespace semantics cannot be faithfully expressed with SQL Server TRIM.
        // Inspect only due words in bounded keyset batches, keeping all counts server-side.
        var after = 0;
        while (true)
        {
            var ids = await states.Where(s => s.NextReviewAt <= now && s.UserWordId > after)
                .OrderBy(s => s.UserWordId).Select(s => s.UserWordId).Take(100).ToArrayAsync();
            if (ids.Length == 0) break;
            var words = await coordinator.Words(userId).AsNoTracking().Where(w => ids.Contains(w.Id)).ToListAsync();
            response.BlockedCount += words.Count(w => !ReviewStateCoordinator.Usable(w));
            after = ids[^1];
        }
        response.UsableDueCount = response.DueCount - response.BlockedCount;
        await transaction.CommitAsync();
        return ServiceResult<ReviewSummaryResponseDto>.Success(response);
    }

    public async Task<ServiceResult<ReviewStartResponseDto>> StartAsync(int userId, ReviewStartRequestDto request)
    {
        if (!Enabled) return Disabled<ReviewStartResponseDto>();
        // Start has no answer side effects; snapshots are checked again under serializable submit.
        if (request.ResumeSessionId is { } resume)
        {
            var session = await db.ReviewSessions.AsNoTracking().Include(s => s.Items).SingleOrDefaultAsync(s => s.UserId == userId && s.Id == resume);
            if (session == null) return Fail<ReviewStartResponseDto>("review_session_unavailable", ServiceFailureType.NotFound);
            if (clock.GetUtcNow().UtcDateTime >= session.ExpiresAt) return Fail<ReviewStartResponseDto>("review_session_expired");
            foreach (var item in session.Items.Where(i => i.AcceptedAt == null))
                if (!await ValidAsync(userId, item)) return Fail<ReviewStartResponseDto>("review_state_changed");
            return ServiceResult<ReviewStartResponseDto>.Success(Project(session));
        }
        var now = clock.GetUtcNow().UtcDateTime;
        if (!await db.UserWordReviewStates.AnyAsync(s => s.UserId == userId && s.NextReviewAt <= now))
            return ServiceResult<ReviewStartResponseDto>.Success(new());
        // Establish the minimum, stopping at four. Never materialize the whole owner inventory.
        var usableCount = 0;
        var after = 0;
        while (usableCount < 4)
        {
            var batch = await coordinator.Words(userId).AsNoTracking().Where(w => w.Id > after).OrderBy(w => w.Id).Take(100).ToListAsync();
            if (batch.Count == 0) break;
            usableCount += batch.Count(ReviewStateCoordinator.Usable);
            after = batch[^1].Id;
        }
        var sessionNew = new ReviewSession { Id = Guid.NewGuid(), UserId = userId, CreatedAt = now,
            ExpiresAt = now.AddMinutes(30), RequestedQuestionCount = request.QuestionCount };
        var usableTarget = false;
        DateTime? lastDue = null; var lastId = 0;
        while (sessionNew.Items.Count < request.QuestionCount)
        {
            var due = db.UserWordReviewStates.AsNoTracking().Where(s => s.UserId == userId && s.NextReviewAt <= now);
            if (lastDue is { } date) due = due.Where(s => s.NextReviewAt > date || s.NextReviewAt == date && s.UserWordId > lastId);
            var batch = await due.OrderBy(s => s.NextReviewAt).ThenBy(s => s.UserWordId).Take(100).ToListAsync();
            if (batch.Count == 0) break;
            var targetIds = batch.Select(s => s.UserWordId).ToArray();
            var targets = await coordinator.Words(userId).AsNoTracking().Where(w => targetIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id);
            foreach (var state in batch)
            {
                if (!targets.TryGetValue(state.UserWordId, out var target) || !ReviewStateCoordinator.Usable(target)) continue;
                usableTarget = true;
                if (usableCount < 4) return Unavailable("insufficient_usable_vocabulary");
                var definition = ReviewStateCoordinator.Select(target)!;
                if (state.ScheduledWordDefinitionId != definition.Id || state.ScheduledDefinitionRevision != definition.ContentRevision) continue;
                var texts = target.Word.WordDefinitions.Select(d => ReviewStateCoordinator.Normalize(d.Definition)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var choices = new List<UserWord> { target };
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ReviewStateCoordinator.Normalize(definition.Definition) };
                var candidateAfter = 0;
                while (choices.Count < 4)
                {
                    var candidates = await coordinator.Words(userId).AsNoTracking().Where(w => w.Id > candidateAfter).OrderBy(w => w.Id).Take(100).ToListAsync();
                    if (candidates.Count == 0) break;
                    foreach (var candidate in candidates.Where(ReviewStateCoordinator.Usable))
                    {
                        var text = ReviewStateCoordinator.Normalize(ReviewStateCoordinator.Select(candidate)!.Definition);
                        if (choices.Any(w => w.WordId == candidate.WordId || ReviewStateCoordinator.Normalize(w.Word.Text).Equals(ReviewStateCoordinator.Normalize(candidate.Word.Text), StringComparison.OrdinalIgnoreCase)) ||
                            candidate.Word.WordDefinitions.Any(d => ReviewStateCoordinator.Normalize(d.Definition).Equals(ReviewStateCoordinator.Normalize(definition.Definition), StringComparison.OrdinalIgnoreCase)) ||
                            texts.Contains(text) || !seen.Add(text)) continue;
                        choices.Add(candidate);
                        if (choices.Count == 4) break;
                    }
                    candidateAfter = candidates[^1].Id;
                }
                if (choices.Count != 4) continue;
                // Random placement is presentation only; correctness remains in the persisted snapshot.
                for (var i = choices.Count - 1; i > 0; i--) { var j = RandomNumberGenerator.GetInt32(i + 1); (choices[i], choices[j]) = (choices[j], choices[i]); }
                var snapshot = choices.Select((w, i) => { var d = ReviewStateCoordinator.Select(w)!; return new Option(i, d.Definition, w.Id, d.Id, d.ContentRevision); }).ToList();
                sessionNew.Items.Add(new ReviewSessionItem
                {
                    Id = Guid.NewGuid(), UserId = userId, UserWordId = target.Id, Ordinal = sessionNew.Items.Count,
                    CapturedScheduleVersion = state.ScheduleVersion, WordDefinitionId = definition.Id, DefinitionRevision = definition.ContentRevision,
                    WordText = target.Word.Text, DefinitionText = definition.Definition, PartOfSpeech = definition.PartOfSpeech.Name,
                    Prompt = $"What is the meaning of '{target.Word.Text}'?", OptionsJson = JsonSerializer.Serialize(snapshot, Json),
                    CorrectOptionId = snapshot.Single(o => o.SourceUserWordId == target.Id).OptionId
                });
                if (sessionNew.Items.Count == request.QuestionCount) break;
            }
            lastDue = batch[^1].NextReviewAt; lastId = batch[^1].UserWordId;
        }
        if (!usableTarget) return Unavailable("no_usable_due_words");
        if (sessionNew.Items.Count == 0) return Unavailable("ambiguous_options");
        sessionNew.ItemCount = sessionNew.Items.Count;
        db.ReviewSessions.Add(sessionNew);
        await db.SaveChangesAsync();
        await CleanupAsync(userId);
        return ServiceResult<ReviewStartResponseDto>.Success(Project(sessionNew));
    }
    private static ServiceResult<ReviewStartResponseDto> Unavailable(string reason) => ServiceResult<ReviewStartResponseDto>.Success(new() { Availability = "unavailable", Reason = reason });
    private static List<Option> Options(ReviewSessionItem item)
    {
        var values = JsonSerializer.Deserialize<List<Option>>(item.OptionsJson, Json) ?? throw new InvalidOperationException("Missing snapshot.");
        if (values.Count != 4 || !values.Select(o => o.OptionId).Order().SequenceEqual(new[] { 0, 1, 2, 3 }) ||
            values.Select(o => ReviewStateCoordinator.Normalize(o.Text)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 ||
            values.Any(o => string.IsNullOrWhiteSpace(o.Text) || o.SourceDefinitionRevision < 1) ||
            values.Count(o => o.SourceUserWordId == item.UserWordId && o.SourceDefinitionId == item.WordDefinitionId && o.SourceDefinitionRevision == item.DefinitionRevision && o.OptionId == item.CorrectOptionId && o.Text == item.DefinitionText) != 1)
            throw new InvalidOperationException("Invalid review snapshot.");
        return values;
    }
    private static ReviewSessionSummaryDto Summary(ReviewSession session)
    {
        var accepted = session.Items.Count(i => i.AcceptedAt != null);
        var correct = session.Items.Count(i => i.IsCorrect == true);
        return new() { TotalItems = session.ItemCount, AcceptedCount = accepted, CorrectCount = correct,
            IncorrectCount = accepted - correct, PendingCount = session.ItemCount - accepted, IsComplete = accepted == session.ItemCount };
    }
    private static ReviewStartResponseDto Project(ReviewSession session) => new()
    {
        Availability = "ready", SessionId = session.Id, ExpiresAtUtc = session.ExpiresAt, SessionSummary = Summary(session),
        Items = session.Items.OrderBy(i => i.Ordinal).Select(i => new ReviewItemDto
        {
            ItemId = i.Id, UserWordId = i.UserWordId, QuestionType = i.QuestionType, Prompt = i.Prompt,
            Options = Options(i).Select(o => new ReviewOptionDto { OptionId = o.OptionId, Text = o.Text }).ToList(),
            AcceptedReceipt = i.ReceiptJson == null ? null : JsonSerializer.Deserialize<ReviewSubmitResponseDto>(i.ReceiptJson, Json)
        }).ToList()
    };

    private async Task<bool> ValidAsync(int userId, ReviewSessionItem item)
    {
        var snapshot = Options(item);
        // Stable canonical -> saved word -> state order, also used by preference/content writers.
        var definitionIds = snapshot.Select(o => o.SourceDefinitionId).Order().ToArray();
        var definitions = await db.WordDefinitions.AsNoTracking().Where(d => definitionIds.Contains(d.Id)).OrderBy(d => d.Id).ToDictionaryAsync(d => d.Id);
        var ids = snapshot.Select(o => o.SourceUserWordId).Order().ToArray();
        var words = await coordinator.Words(userId).AsNoTracking().Where(w => ids.Contains(w.Id)).OrderBy(w => w.Id).ToDictionaryAsync(w => w.Id);
        foreach (var option in snapshot)
        {
            if (!words.TryGetValue(option.SourceUserWordId, out var word) || !definitions.TryGetValue(option.SourceDefinitionId, out var d) ||
                !ReviewStateCoordinator.Usable(word) || ReviewStateCoordinator.Select(word)?.Id != d.Id ||
                d.ContentRevision != option.SourceDefinitionRevision || ReviewStateCoordinator.Normalize(d.Definition) != ReviewStateCoordinator.Normalize(option.Text)) return false;
        }
        var target = words[item.UserWordId]; var selected = ReviewStateCoordinator.Select(target)!;
        var state = await db.UserWordReviewStates.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId && s.UserWordId == item.UserWordId);
        return state != null && state.ScheduleVersion == item.CapturedScheduleVersion && state.ScheduledWordDefinitionId == item.WordDefinitionId &&
            state.ScheduledDefinitionRevision == item.DefinitionRevision && selected.PartOfSpeech.Name == item.PartOfSpeech &&
            ReviewStateCoordinator.Normalize(target.Word.Text) == ReviewStateCoordinator.Normalize(item.WordText) &&
            !target.Word.WordDefinitions.Any(d => d.Id != item.WordDefinitionId && snapshot.Any(o => o.OptionId != item.CorrectOptionId &&
                ReviewStateCoordinator.Normalize(o.Text).Equals(ReviewStateCoordinator.Normalize(d.Definition), StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<ServiceResult<ReviewSubmitResponseDto>?> ReplayAsync(ApplicationDbContext context, int owner, ReviewSubmitRequestDto request)
    {
        var history = await context.ReviewHistories.AsNoTracking().SingleOrDefaultAsync(h => h.UserId == owner && h.SubmissionId == request.SubmissionId);
        if (history == null) return null;
        var receipt = JsonSerializer.Deserialize<ReviewSubmitResponseDto>(history.ReceiptJson, Json) ?? throw new InvalidOperationException("Missing receipt.");
        if (history.ReceiptContractVersion != 1 || receipt.SubmissionId != history.SubmissionId || receipt.ItemId != history.SessionItemId ||
            receipt.SelectedOptionId != history.SelectedOptionId || receipt.IsCorrect != history.IsCorrect)
            throw new InvalidOperationException("Invalid persisted receipt.");
        return receipt.SessionId == request.SessionId && receipt.ItemId == request.ItemId && receipt.SelectedOptionId == request.SelectedOptionId
            ? ServiceResult<ReviewSubmitResponseDto>.Success(receipt) : Fail<ReviewSubmitResponseDto>("review_submission_conflict");
    }
    public async Task<ServiceResult<ReviewSubmitResponseDto>> SubmitAsync(int userId, ReviewSubmitRequestDto request)
    {
        if (!Enabled) return Disabled<ReviewSubmitResponseDto>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var replay = await ReplayAsync(db, userId, request);
            if (replay != null) return replay;
            try
            {
                var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                    var saved = await ReplayAsync(db, userId, request);
                    if (saved != null) return saved;
                    // Nontracking locator contains only immutable snapshot data; authoritative reads follow.
                    var locator = await db.ReviewSessionItems.AsNoTracking().SingleOrDefaultAsync(i => i.UserId == userId && i.SessionId == request.SessionId && i.Id == request.ItemId);
                    if (locator == null) return Fail<ReviewSubmitResponseDto>("review_session_unavailable", ServiceFailureType.NotFound);
                    var valid = locator.AcceptedAt != null || await ValidAsync(userId, locator);
                    var state = await db.UserWordReviewStates.SingleOrDefaultAsync(s => s.UserId == userId && s.UserWordId == locator.UserWordId);
                    var session = await db.ReviewSessions.Include(s => s.Items).SingleOrDefaultAsync(s => s.UserId == userId && s.Id == request.SessionId);
                    if (session == null) return Fail<ReviewSubmitResponseDto>("review_session_unavailable", ServiceFailureType.NotFound);
                    var item = session.Items.Single(i => i.Id == request.ItemId);
                    if (item.AcceptedAt != null) return Fail<ReviewSubmitResponseDto>("review_submission_conflict");
                    var now = clock.GetUtcNow().UtcDateTime;
                    if (now >= session.ExpiresAt) return Fail<ReviewSubmitResponseDto>("review_session_expired");
                    if (!valid) return Fail<ReviewSubmitResponseDto>("review_state_changed");
                    var snapshot = Options(item);
                    var selection = snapshot.SingleOrDefault(o => o.OptionId == request.SelectedOptionId);
                    if (selection == null) return Fail<ReviewSubmitResponseDto>("invalid_review_answer", ServiceFailureType.Validation);
                    if (state == null || state.ScheduleVersion != item.CapturedScheduleVersion || state.NextReviewAt > now) return Fail<ReviewSubmitResponseDto>("review_state_changed");
                    var transition = scheduler.Review(ReviewStateCoordinator.Read(state), request.SelectedOptionId == item.CorrectOptionId, now);
                    var correct = snapshot.Single(o => o.OptionId == item.CorrectOptionId);
                    ReviewStateCoordinator.Apply(state, transition.NextState);
                    item.AcceptedSubmissionId = request.SubmissionId; item.AcceptedAt = now;
                    item.SelectedOptionId = request.SelectedOptionId; item.IsCorrect = transition.IsCorrect;
                    session.Version = checked(session.Version + 1);
                    var summary = Summary(session);
                    if (summary.IsComplete) session.CompletedAt = now;
                    var receipt = new ReviewSubmitResponseDto
                    {
                        SessionId = session.Id, ItemId = item.Id, SubmissionId = request.SubmissionId, ReviewedAtUtc = now,
                        IsCorrect = transition.IsCorrect, SelectedOptionId = request.SelectedOptionId, CorrectOptionId = item.CorrectOptionId,
                        SelectedAnswer = selection.Text, CorrectAnswer = correct.Text, NextReviewAt = state.NextReviewAt,
                        ReviewIntervalDays = state.ReviewIntervalDays, ReviewStatus = Status(state.ReviewStatus), SessionSummary = summary
                    };
                    item.ReceiptJson = JsonSerializer.Serialize(receipt, Json); item.ReceiptContractVersion = 1;
                    var before = transition.PreviousState; var next = transition.NextState;
                    db.ReviewHistories.Add(new ReviewHistory
                    {
                        Id = Guid.NewGuid(), UserId = userId, UserWordId = item.UserWordId, SessionItemId = item.Id, SubmissionId = request.SubmissionId,
                        ReviewedAt = now, WordDefinitionId = item.WordDefinitionId, DefinitionRevision = item.DefinitionRevision,
                        WordText = item.WordText, DefinitionText = item.DefinitionText, IsCorrect = transition.IsCorrect,
                        SelectedOptionId = request.SelectedOptionId, SelectedAnswer = selection.Text, CorrectAnswer = correct.Text,
                        AlgorithmVersion = next.AlgorithmVersion, PreviousScheduleVersion = before.ScheduleVersion, NextScheduleVersion = next.ScheduleVersion,
                        PreviousNextReviewAt = before.NextReviewAt, NextReviewAt = next.NextReviewAt, PreviousLastReviewedAt = before.LastReviewedAt,
                        PreviousIntervalDays = before.ReviewIntervalDays, NextIntervalDays = next.ReviewIntervalDays,
                        PreviousCorrectCount = before.ConsecutiveCorrectCount, NextCorrectCount = next.ConsecutiveCorrectCount,
                        PreviousLapseCount = before.LapseCount, NextLapseCount = next.LapseCount, PreviousStatus = (int)before.ReviewStatus, NextStatus = (int)next.ReviewStatus,
                        ElapsedSincePreviousReviewTicks = before.LastReviewedAt.HasValue ? (now - before.LastReviewedAt.Value).Ticks : null,
                        ReceiptJson = item.ReceiptJson, ReceiptContractVersion = 1
                    });
                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return ServiceResult<ReviewSubmitResponseDto>.Success(receipt);
                });
                if (result.IsSuccess) await CleanupAsync(userId);
                return result;
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                // Fresh scoped context/connection: never infer rollback from a thrown Commit.
                using var scope = scopes.CreateScope();
                var recovery = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var recovered = await ReplayAsync(recovery, userId, request);
                if (recovered != null) return recovered;
                // Unknown commit failures with no receipt are unresolved; only known provider/concurrency failures retry.
                if (!Retryable(ex) || attempt == 2) throw;
                await Task.Delay(RandomNumberGenerator.GetInt32(10, 40));
            }
        }
        throw new InvalidOperationException("Review retry budget exhausted.");
    }

    internal static bool Retryable(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException) return true;
        var root = exception.GetBaseException();
        if (root is SqlException sql) return sql.Number == 1205 || sql.IsTransient ||
            sql.Number is 2601 or 2627 && (sql.Message.Contains("IX_ReviewHistories_UserId_SubmissionId", StringComparison.Ordinal) ||
                sql.Message.Contains("IX_ReviewHistories_SessionItemId", StringComparison.Ordinal) || sql.Message.Contains("IX_ReviewSessionItems_UserId_AcceptedSubmissionId", StringComparison.Ordinal));
        // No SQLite dependency in the production project; provider metadata identifies only BUSY/LOCKED or accepted-key uniqueness.
        if (root.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException")
        {
            var code = (int?)root.GetType().GetProperty("SqliteErrorCode")?.GetValue(root);
            return code is 5 or 6 || code == 19 && (root.Message.Contains("ReviewHistories.UserId, ReviewHistories.SubmissionId", StringComparison.Ordinal) ||
                root.Message.Contains("ReviewHistories.SessionItemId", StringComparison.Ordinal) || root.Message.Contains("ReviewSessionItems.UserId, ReviewSessionItems.AcceptedSubmissionId", StringComparison.Ordinal));
        }
        return false;
    }
    private async Task CleanupAsync(int owner)
    {
        try
        {
            // Independent transaction; never roll a committed answer back because cleanup failed.
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var now = clock.GetUtcNow().UtcDateTime;
            var ids = await context.ReviewSessions.Where(s => s.UserId == owner && s.ExpiresAt <= now && !s.Items.Any(i => i.AcceptedAt != null))
                .OrderBy(s => s.ExpiresAt).ThenBy(s => s.Id).Select(s => s.Id).Take(100).ToArrayAsync();
            if (ids.Length == 0) return;
            await context.ReviewSessionItems.Where(i => i.UserId == owner && ids.Contains(i.SessionId)).ExecuteDeleteAsync();
            await context.ReviewSessions.Where(s => s.UserId == owner && ids.Contains(s.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex) { logger.LogWarning("Review cleanup failed ({ExceptionType}).", ex.GetType().Name); }
    }
}
