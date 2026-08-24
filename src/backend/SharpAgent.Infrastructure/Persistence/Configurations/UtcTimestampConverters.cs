using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SharpAgent.Infrastructure.Persistence.Configurations;

/// <summary>Round-trips DateTimeOffset as ISO-8601 "O" text, normalized to UTC.</summary>
internal sealed class UtcTimestampConverter : ValueConverter<DateTimeOffset, string>
{
    public UtcTimestampConverter()
        : base(
            static value => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            static text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
    {
    }
}

internal sealed class NullableUtcTimestampConverter : ValueConverter<DateTimeOffset?, string>
{
    public NullableUtcTimestampConverter()
        : base(
            static value => value.HasValue
                ? value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                : null!,
            static text => text == null
                ? null
                : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
    {
    }
}
