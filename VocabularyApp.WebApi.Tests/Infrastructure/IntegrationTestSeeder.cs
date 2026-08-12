using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.Security;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public static class IntegrationTestSeeder
{
    public static async Task<User> SeedModernUserAsync(
        VocabularyAppWebApplicationFactory factory,
        TestUserCredentials credentials)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var user = new User
        {
            Username = credentials.Username,
            Email = credentials.Email,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = passwordService.HashPassword(user, credentials.Password);

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public static async Task<SeededWord> SeedWordWithDefinitionAsync(
        VocabularyAppWebApplicationFactory factory,
        string text,
        string definition,
        string partOfSpeechName = "Noun")
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var partOfSpeech = await context.PartsOfSpeech
            .SingleAsync(candidate => candidate.Name == partOfSpeechName);
        var word = new Word { Text = text };
        context.Words.Add(word);
        await context.SaveChangesAsync();

        var wordDefinition = new WordDefinition
        {
            WordId = word.Id,
            PartOfSpeechId = partOfSpeech.Id,
            Definition = definition,
            DisplayOrder = 1
        };
        context.WordDefinitions.Add(wordDefinition);
        await context.SaveChangesAsync();

        return new SeededWord(word.Id, wordDefinition.Id, partOfSpeech.Id);
    }

    public static async Task<int> SeedDefinitionAsync(
        VocabularyAppWebApplicationFactory factory,
        int wordId,
        string definition,
        string partOfSpeechName = "Noun",
        int displayOrder = 2)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var partOfSpeechId = await context.PartsOfSpeech
            .Where(candidate => candidate.Name == partOfSpeechName)
            .Select(candidate => candidate.Id)
            .SingleAsync();
        var wordDefinition = new WordDefinition
        {
            WordId = wordId,
            PartOfSpeechId = partOfSpeechId,
            Definition = definition,
            DisplayOrder = displayOrder
        };
        context.WordDefinitions.Add(wordDefinition);
        await context.SaveChangesAsync();
        return wordDefinition.Id;
    }

    public static async Task<int> SeedUserWordAsync(
        VocabularyAppWebApplicationFactory factory,
        int userId,
        SeededWord word,
        int? preferredWordDefinitionId = null,
        bool isFavorite = false,
        int correctAnswers = 0,
        int totalAttempts = 0)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userWord = new UserWord
        {
            UserId = userId,
            WordId = word.WordId,
            PartOfSpeechId = word.PartOfSpeechId,
            PreferredWordDefinitionId = preferredWordDefinitionId ?? word.WordDefinitionId,
            IsFavorite = isFavorite,
            CorrectAnswers = correctAnswers,
            TotalAttempts = totalAttempts,
            AddedAt = DateTime.UtcNow
        };
        context.UserWords.Add(userWord);
        await context.SaveChangesAsync();
        return userWord.Id;
    }
}

public sealed record SeededWord(
    int WordId,
    int WordDefinitionId,
    int PartOfSpeechId);
