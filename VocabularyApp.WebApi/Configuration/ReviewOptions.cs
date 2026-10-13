namespace VocabularyApp.WebApi.Configuration;

/// <summary>Inactive until explicitly bound and registered in a later implementation phase.</summary>
public sealed class ReviewOptions
{
    public const string SectionName = "Reviews";

    public bool EnrollmentEnabled { get; set; } = false;
    public bool Enabled { get; set; } = false;
}
