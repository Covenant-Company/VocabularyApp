using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VocabularyApp.Data.Configurations;

internal static class ReviewMapping
{
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        value => RequireUtc(value), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    // EF does not pass nulls through value converters; this also covers nullable properties.
    public static void Utc(PropertyBuilder<DateTime> property) =>
        property.HasColumnType("datetime2(7)").HasConversion(UtcConverter);

    public static void Utc(PropertyBuilder<DateTime?> property) =>
        property.HasColumnType("datetime2(7)").HasConversion(UtcConverter);

    public static void Text(PropertyBuilder<string> property, int? maximumLength = null)
    {
        property.IsUnicode().IsRequired();
        if (maximumLength.HasValue) property.HasMaxLength(maximumLength.Value);
    }

    // Bracket identifiers, integer comparisons and boolean combinations work on both providers.
    public static string Progression(string status, string count, string interval) =>
        $"([{status}] = 0 AND [{count}] = 0 AND [{interval}] = 0) OR " +
        $"([{status}] = 1 AND (([{count}] = 0 AND [{interval}] = 1) OR " +
        $"([{count}] = 1 AND [{interval}] = 1) OR ([{count}] = 2 AND [{interval}] = 3))) OR " +
        $"([{status}] = 2 AND (([{count}] = 3 AND [{interval}] = 7) OR " +
        $"([{count}] = 4 AND [{interval}] = 14) OR ([{count}] = 5 AND [{interval}] = 30) OR " +
        $"([{count}] = 6 AND [{interval}] = 60) OR ([{count}] = 7 AND [{interval}] = 90)))";

    private static DateTime RequireUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value : throw new InvalidOperationException("Review persistence timestamps must have UTC kind.");
}
