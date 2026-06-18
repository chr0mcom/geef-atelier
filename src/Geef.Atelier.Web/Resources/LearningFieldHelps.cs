namespace Geef.Atelier.Web.Resources;

/// <summary>Field help texts for the Learnings management UI.</summary>
public static class LearningFieldHelps
{
    public const string StatusFilter = "Filter by status: All, Proposed (still in the gate), Approved (active in the retriever) or Rejected.";
    public const string DomainFilter = "Filter by domain — matches the crew-template name of the source run (e.g. 'juristisch', 'akademisch').";
    public const string Status = "Current status of the learning: Proposed = waiting for the gate, Approved = used during retrieval, Rejected = inactive.";
    public const string Domain = "Domain of the learning — derived from the template of the source run. Influences domain-aware retrieval (boost for the same domain).";
    public const string SourceRun = "The run this learning was extracted from.";
    public const string LearningRun = "The learning-evaluation run that served as the quality gate for this learning.";
    public const string ApprovedAt = "Time of approval — either automatically by the gate or manually by an admin.";
    public const string StructuredFacts = "Structured facts the extractor obtained from the source run (raw basis for the learning text).";
    public const string ApproveAction = "Approve manually: the learning is activated for retrieval immediately. The embedding is recomputed.";
    public const string RejectAction = "Reject: the learning is deactivated and ignored during retrieval.";
    public const string DeleteAction = "Delete permanently — cannot be undone.";
}
