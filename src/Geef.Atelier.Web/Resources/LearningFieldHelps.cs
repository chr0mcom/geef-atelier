namespace Geef.Atelier.Web.Resources;

/// <summary>German-language field help texts for the Learnings management UI.</summary>
public static class LearningFieldHelps
{
    public const string StatusFilter = "Filter nach Status: Alle, Vorgeschlagen (noch im Gate), Genehmigt (im Retriever aktiv) oder Abgelehnt.";
    public const string DomainFilter = "Filter nach Domäne — entspricht dem Crew-Template-Namen des Ursprungs-Runs (z. B. 'juristisch', 'akademisch').";
    public const string Status = "Aktueller Status des Learnings: Vorgeschlagen = wartet auf Gate, Genehmigt = wird beim Retrieval genutzt, Abgelehnt = inaktiv.";
    public const string Domain = "Domäne des Learnings — abgeleitet aus dem Template des Ursprungs-Runs. Beeinflusst domänen-bewusstes Retrieval (Boost bei gleicher Domäne).";
    public const string SourceRun = "Der Run, aus dem dieses Learning extrahiert wurde.";
    public const string LearningRun = "Der Learning-Evaluation-Run, der als Qualitäts-Gate für dieses Learning diente.";
    public const string ApprovedAt = "Zeitpunkt der Genehmigung — entweder automatisch durch das Gate oder manuell durch einen Admin.";
    public const string StructuredFacts = "Strukturierte Fakten, die der Extraktor aus dem Ursprungs-Run gewonnen hat (Rohbasis für den Learning-Text).";
    public const string ApproveAction = "Manuell genehmigen: Das Learning wird sofort für das Retrieval aktiviert. Das Embedding wird neu berechnet.";
    public const string RejectAction = "Ablehnen: Das Learning wird deaktiviert und beim Retrieval ignoriert.";
    public const string DeleteAction = "Permanent löschen — kann nicht rückgängig gemacht werden.";
}
