using System.Data;
using Microsoft.EntityFrameworkCore;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;

namespace VocabularyApp.WebApi.Services;

/// <summary>
/// Controlled maintenance entry point, deliberately not exposed by HTTP.
/// Callers need a fresh scope, additive schema and an authorized maintenance window for large fan-outs.
/// Direct SQL edits while reviews are active are unsupported.
/// </summary>
public sealed class ReviewContentChangeService(ApplicationDbContext db, ReviewStateCoordinator coordinator)
{
    public Task UpdateDefinitionAsync(int id, string text, int partOfSpeechId, int wordId, string? example, int displayOrder) =>
        ChangeAsync(id, null, async definition =>
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 1000 || example?.Length > 500) throw new ArgumentException("Invalid definition content.");
            if (!await db.Words.AnyAsync(w => w.Id == wordId) || !await db.PartsOfSpeech.AnyAsync(p => p.Id == partOfSpeechId)) throw new ArgumentException("Invalid canonical identity.");
            var changed = ReviewStateCoordinator.Normalize(definition!.Definition) != ReviewStateCoordinator.Normalize(text) || definition.PartOfSpeechId != partOfSpeechId || definition.WordId != wordId;
            if (changed) definition.ContentRevision = checked(definition.ContentRevision + 1);
            definition.Definition = text; definition.PartOfSpeechId = partOfSpeechId; definition.WordId = wordId;
            definition.Example = example; definition.DisplayOrder = displayOrder;
        }, newWordId: wordId);

    public Task RemoveDefinitionAsync(int id) => ChangeAsync(id, null, _ => Task.CompletedTask, remove: true);
    public Task AddDefinitionAsync(int wordId, int partOfSpeechId, string text, int displayOrder = 1) =>
        ChangeAsync(null, wordId, _ =>
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 1000) throw new ArgumentException("Invalid definition content.");
            var word = db.Words.Local.Single(w => w.Id == wordId);
            word.WordDefinitions.Add(new WordDefinition { WordId = wordId, PartOfSpeechId = partOfSpeechId, Definition = text, DisplayOrder = displayOrder });
            return Task.CompletedTask;
        });
    public Task UpdateWordTextAsync(int wordId, string text) => ChangeAsync(null, wordId, _ =>
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100) throw new ArgumentException("Invalid word content.");
        var word = db.Words.Local.Single(w => w.Id == wordId);
        if (ReviewStateCoordinator.Normalize(word.Text) != ReviewStateCoordinator.Normalize(text))
            foreach (var d in word.WordDefinitions) d.ContentRevision = checked(d.ContentRevision + 1);
        word.Text = text;
        return Task.CompletedTask;
    });

    private async Task ChangeAsync(int? definitionId, int? wordId, Func<WordDefinition?, Task> mutate, bool remove = false, int? newWordId = null)
    {
        if (!coordinator.EnrollmentEnabled)
            throw new InvalidOperationException("Controlled content maintenance requires enrollment coordination enabled.");
        // No implicit operator retry after uncertain commits: inspect content/revisions in a fresh scope.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        WordDefinition? definition = null;
        if (definitionId.HasValue)
        {
            definition = await db.WordDefinitions.AsNoTracking().SingleAsync(d => d.Id == definitionId);
            wordId = definition.WordId;
        }
        var wordIds = new[] { wordId!.Value, newWordId ?? wordId.Value }.Distinct().Order().ToArray();
        await db.WordDefinitions.Where(d => wordIds.Contains(d.WordId)).OrderBy(d => d.Id).LoadAsync();
        await db.Words.Where(w => wordIds.Contains(w.Id)).OrderBy(w => w.Id).Include(w => w.WordDefinitions).LoadAsync();
        definition = definitionId.HasValue ? db.WordDefinitions.Local.Single(d => d.Id == definitionId) : null;
        var owners = await db.UserWords.Where(w => wordIds.Contains(w.WordId)).OrderBy(w => w.Id).Include(w => w.Word).ToListAsync();
        var ownerIds = owners.Select(w => w.Id).ToArray();
        var states = await db.UserWordReviewStates.Where(s => ownerIds.Contains(s.UserWordId)).OrderBy(s => s.UserWordId).ToDictionaryAsync(s => s.UserWordId);
        var previous = owners.ToDictionary(w => w.Id, w => (Id: ReviewStateCoordinator.Select(w)?.Id, Revision: ReviewStateCoordinator.Select(w)?.ContentRevision));
        if (remove) db.Words.Local.Single(w => w.Id == definition!.WordId).WordDefinitions.Remove(definition!);
        await mutate(definition);
        if (definition != null && !remove)
        {
            // Keep the in-memory selection graph consistent when canonical membership changes.
            foreach (var word in db.Words.Local.Where(w => wordIds.Contains(w.Id)))
            {
                if (word.Id != definition.WordId) word.WordDefinitions.Remove(definition);
                else if (!word.WordDefinitions.Contains(definition)) word.WordDefinitions.Add(definition);
            }
        }
        foreach (var owner in owners)
        {
            if (owner.PreferredWordDefinitionId == definitionId && definition != null)
            {
                if (remove || owner.WordId != definition.WordId) owner.PreferredWordDefinitionId = null;
                else owner.PartOfSpeechId = definition.PartOfSpeechId;
            }
        }
        // Assign IDs to newly added definitions before recording selected-meaning FKs.
        if (!definitionId.HasValue) await db.SaveChangesAsync();
        foreach (var owner in owners)
        {
            var selected = ReviewStateCoordinator.Select(owner);
            var before = previous[owner.Id];
            if (states.TryGetValue(owner.Id, out var state) && (before.Id != selected?.Id || before.Revision != selected?.ContentRevision)) coordinator.Reset(state, owner);
        }
        if (remove) db.WordDefinitions.Remove(definition!);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
