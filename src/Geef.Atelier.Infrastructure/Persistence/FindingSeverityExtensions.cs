using Geef.Atelier.Core.Domain;
using SdkSeverity = Geef.Sdk.Results.FindingSeverity;

namespace Geef.Atelier.Infrastructure.Persistence;

internal static class FindingSeverityExtensions
{
    internal static FindingSeverity ToAtelierSeverity(this SdkSeverity s) => s switch
    {
        SdkSeverity.Critical => FindingSeverity.Critical,
        SdkSeverity.Error    => FindingSeverity.Major,
        SdkSeverity.Warning  => FindingSeverity.Minor,
        SdkSeverity.Info     => FindingSeverity.Info,
        _                    => FindingSeverity.Minor
    };
}
