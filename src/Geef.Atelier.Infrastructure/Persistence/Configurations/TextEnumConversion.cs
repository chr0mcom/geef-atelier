using Geef.Atelier.Core.Domain.Crew.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stores required enums of orders and sources by name and reads unknown names as a fixed fallback instead of
/// throwing, so later stages can add values without breaking older readers.
/// </summary>
internal static class TextEnumConversion
{
    /// <summary>Configures <paramref name="property"/> as a required <c>varchar(<paramref name="maxLength"/>)</c> holding the enum name.</summary>
    public static PropertyBuilder<TEnum> HasTextEnumConversion<TEnum>(this PropertyBuilder<TEnum> property, int maxLength, TEnum fallback)
        where TEnum : struct, Enum
    {
        var converter = new ValueConverter<TEnum, string>(
            v => v.ToString(),
            v => EnumNames.ParseExact<TEnum>(v) ?? fallback);

        return property.HasConversion(converter).HasMaxLength(maxLength).IsRequired();
    }
}
