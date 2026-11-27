namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>Column limits of the <c>RunWarnings</c> table.</summary>
internal static class RunWarningLimits
{
    public const int SourceLength = 100;
    public const int CodeLength = 100;
    public const int MessageLength = 1000;
}
