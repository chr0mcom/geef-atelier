using Geef.Atelier.Core.Domain.Crew.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stores nullable enums by name and reads unknown names as <c>null</c> instead of throwing (D-066). A single
/// mistyped value in a data migration must not take down every profile listing; an unreadable slot simply
/// makes the profile manual.
/// </summary>
internal static class TolerantEnumConversion
{
    /// <summary>Configures <paramref name="property"/> as a nullable <c>varchar(40)</c> holding the enum name.</summary>
    public static PropertyBuilder<TEnum?> HasTolerantEnumConversion<TEnum>(this PropertyBuilder<TEnum?> property)
        where TEnum : struct, Enum
    {
        var converter = new ValueConverter<TEnum?, string?>(
            v => v.HasValue ? v.Value.ToString() : null,
            v => Parse<TEnum>(v));

        return property.HasConversion(converter).HasMaxLength(40).IsRequired(false);
    }

    private static TEnum? Parse<TEnum>(string? value)
        where TEnum : struct, Enum =>
        EnumNames.ParseExact<TEnum>(value);
}
