using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Exact synchronous call and callable async alternative behind one
/// <c>sync-call-in-async</c> opportunity.
/// </summary>
public sealed record AsyncSiblingOpportunityEvidence(
    DirectCall SynchronousCall,
    MemberRef AsyncCandidate);
