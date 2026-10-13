using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VocabularyApp.Data;

/// <summary>Offline SQL Server model for EF tooling; never reads production settings or starts the API.</summary>
public sealed class ApplicationDbContextDesignFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Model/scaffold/script commands do not open this connection. Applying a migration is
        // a separate operator action requiring an explicitly selected disposable/approved target.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=VocabularyApp_F6_DesignOnly;Integrated Security=True")
            .Options;
        return new ApplicationDbContext(options);
    }
}
