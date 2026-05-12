namespace Geef.Atelier.Infrastructure.Configuration;

public sealed class ConvergenceOptions
{
    public int MaxIterations { get; init; } = 3;
    public bool AbortOnCritical { get; init; } = false;
    public bool DetectRegression { get; init; } = true;
    public int StagnationThreshold { get; init; } = 3;
}
