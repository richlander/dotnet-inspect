using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Explicit producer and scope selection for one library-body Analysis
/// execution. Method tokens are snapshotted; the optional type predicate
/// remains caller-supplied behavior for that execution.
/// </summary>
public sealed class LibraryBodyAnalysisRequest
{
    private LibraryBodyAnalysisRequest(
        LibraryBodyAnalysisFeatures features,
        IReadOnlySet<int>? bodyScope,
        Func<TypeRef, bool>? bodyTypeScope)
    {
        ImmutableHashSet<int>? bodyScopeSnapshot =
            bodyScope?.ToImmutableHashSet();
        Features = features;
        BodyScope = bodyScopeSnapshot;
        BodyTypeScope = bodyTypeScope;
        Plan = LibraryBodyAnalysisPlan.Create(
            features,
            bodyScopeSnapshot,
            bodyTypeScope);
    }

    /// <summary>The features requested before prerequisite expansion.</summary>
    public LibraryBodyAnalysisFeatures Features { get; }

    /// <summary>
    /// Optional physical MethodDef token scope, snapshotted when the request is
    /// created.
    /// </summary>
    public IReadOnlySet<int>? BodyScope { get; }

    /// <summary>Optional caller-supplied type predicate for this execution.</summary>
    public Func<TypeRef, bool>? BodyTypeScope { get; }

    internal LibraryBodyAnalysisPlan Plan { get; }

    public static LibraryBodyAnalysisRequest Create(
        LibraryBodyAnalysisFeatures features,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null) =>
        new(features, bodyScope, bodyTypeScope);
}
