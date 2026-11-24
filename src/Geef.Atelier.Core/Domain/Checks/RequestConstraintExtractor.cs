namespace Geef.Atelier.Core.Domain.Checks;

/// <summary>Reads constraints (length, language, placeholders, forbidden forms, forbidden and required terms) from a German or English request without an AI model.</summary>
public static class RequestConstraintExtractor
{
    /// <summary>Extracts the constraints of <paramref name="requestText"/>, the request in the person's own words.</summary>
    public static OrderConstraints Extract(string requestText) =>
        throw new NotImplementedException();
}
