namespace VocabularyApp.WebApi.Configuration;

/// <summary>Independent runtime gates, disabled by default. The additive schema is always required.</summary>
public sealed class ReviewOptions
{
    public const string SectionName = "Reviews";

    public bool EnrollmentEnabled { get; set; } = false;
    public bool Enabled { get; set; } = false;
}
