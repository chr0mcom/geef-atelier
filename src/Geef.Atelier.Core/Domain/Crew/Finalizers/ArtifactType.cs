namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>Classifies what a <see cref="RunArtifact"/> represents.</summary>
public enum ArtifactType
{
    /// <summary>A file stored on the export volume; downloadable via the artifact endpoint.</summary>
    File = 0,

    /// <summary>A URL returned by an external sink (e.g. CMS permalink after publish).</summary>
    Url = 1,

    /// <summary>A status record describing the outcome of a finalizer step (used for both success notes and errors).</summary>
    Status = 2,
}
