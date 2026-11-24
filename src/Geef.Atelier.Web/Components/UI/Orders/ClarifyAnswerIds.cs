namespace Geef.Atelier.Web.Components.UI.Orders;

/// <summary>Fixed answer ids of the revision guess (contract: question <c>revision</c>, options <c>yes</c>/<c>no</c>).</summary>
public static class ClarifyAnswerIds
{
    /// <summary>Question id of the guess that a file is to be improved.</summary>
    public const string Revision = "revision";

    /// <summary>Option "No, write something new instead".</summary>
    public const string RevisionNo = "no";

    /// <summary>Option "Yes, improve the file".</summary>
    public const string RevisionYes = "yes";
}
