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
}

public sealed record SeededWord(
    int WordId,
    int WordDefinitionId,
    int PartOfSpeechId);
