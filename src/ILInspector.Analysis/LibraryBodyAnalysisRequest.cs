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
        Func<TypeRef, bool>? bodyTypeScope,
        ResourceEffectAdmission? resourceEffects,
        bool includeResourceLifecycle,
        ImplementationMetricAnalysisRequest? implementationMetrics)
    {
        ImmutableHashSet<int>? bodyScopeSnapshot =
            bodyScope?.ToImmutableHashSet();
        Features = features;
        BodyScope = bodyScopeSnapshot;
        BodyTypeScope = bodyTypeScope;
        ResourceEffects = resourceEffects;
        IncludesResourceLifecycle = includeResourceLifecycle;
        ImplementationMetrics = implementationMetrics;
        Plan = LibraryBodyAnalysisPlan.Create(
            features,
            bodyScopeSnapshot,
            bodyTypeScope,
            resourceEffects,
            includeResourceLifecycle,
            implementationMetrics);
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

    /// <summary>
    /// Admitted declarations to resolve and project as terminal-resource
    /// occurrences. Null leaves the producer entirely inactive.
    /// </summary>
    public ResourceEffectAdmission? ResourceEffects { get; }

    /// <summary>
    /// Whether the explicit admitted effects also select Resource Lifecycle
    /// Analysis. Occurrence-only requests leave this false.
    /// </summary>
    public bool IncludesResourceLifecycle { get; }

    internal ImplementationMetricAnalysisRequest? ImplementationMetrics
    { get; }

    internal LibraryBodyAnalysisPlan Plan { get; }

    public static LibraryBodyAnalysisRequest Create(
        LibraryBodyAnalysisFeatures features,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null) =>
        new(
            features,
            bodyScope,
            bodyTypeScope,
            resourceEffects: null,
            includeResourceLifecycle: false,
            implementationMetrics: null);

    /// <summary>
    /// Selects the current complete implementation profile through the
    /// parameterized metric-plan migration path.
    /// </summary>
    public static LibraryBodyAnalysisRequest
        CreateCompleteImplementationProfile(
            IReadOnlySet<int>? bodyScope = null,
            Func<TypeRef, bool>? bodyTypeScope = null) =>
        new(
            LibraryBodyAnalysisFeatures.None,
            bodyScope,
            bodyTypeScope,
            resourceEffects: null,
            includeResourceLifecycle: false,
            ImplementationMetricAnalysisRequest
                .CompleteProfileCompatibility());

    internal static LibraryBodyAnalysisRequest
        CreateImplementationMetrics(
            ImplementationMetricEvidenceKind evidence,
            ImplementationMetricWorkLimits limits,
            IReadOnlySet<int> bodyScope,
            LibraryBodyAnalysisFeatures features =
                LibraryBodyAnalysisFeatures.None)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(bodyScope);
        if (bodyScope.Count == 0)
        {
            throw new ArgumentException(
                "Implementation metric body scope cannot be empty.",
                nameof(bodyScope));
        }
        return new(
            features,
            bodyScope,
            bodyTypeScope: null,
            resourceEffects: null,
            includeResourceLifecycle: false,
            new ImplementationMetricAnalysisRequest(
                evidence,
                limits,
                ImplementationMetricRequestOrigin.Explicit));
    }

    /// <summary>
    /// Selects Resource Occurrence Analysis with explicit admitted effect
    /// semantics. The producer is parameterized and therefore intentionally
    /// does not participate in <see cref="LibraryBodyAnalysisFeatures.All"/>.
    /// </summary>
    public static LibraryBodyAnalysisRequest CreateResourceOccurrences(
        ResourceEffectAdmission resourceEffects,
        LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.None,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null)
    {
        ArgumentNullException.ThrowIfNull(resourceEffects);
        return new(
            features,
            bodyScope,
            bodyTypeScope,
            resourceEffects,
            includeResourceLifecycle: false,
            implementationMetrics: null);
    }

    /// <summary>
    /// Selects Resource Lifecycle Analysis and its Resource Occurrence
    /// prerequisite with explicit admitted effect semantics.
    /// </summary>
    public static LibraryBodyAnalysisRequest CreateResourceLifecycle(
        ResourceEffectAdmission resourceEffects,
        LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.None,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null)
    {
        ArgumentNullException.ThrowIfNull(resourceEffects);
        return new(
            features,
            bodyScope,
            bodyTypeScope,
            resourceEffects,
            includeResourceLifecycle: true,
            implementationMetrics: null);
    }
}
