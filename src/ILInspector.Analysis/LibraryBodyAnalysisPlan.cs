namespace ILInspector.Analysis;

internal sealed record LibraryBodyAnalysisPlan(
    LibraryBodyAnalysisFeatures Features,
    IReadOnlySet<int>? MethodScope,
    Func<TypeRef, bool>? TypeScope,
    IReadOnlyDictionary<int, TypeRef>?
        TypeScopeEvidenceSources = null,
    IReadOnlySet<int>? RequestedMethodScope = null)
{
    internal bool IsScoped
        => MethodScope is not null || TypeScope is not null;

    internal bool Includes(LibraryBodyAnalysisFeatures feature)
        => (Features & feature) != 0;

    internal static LibraryBodyAnalysisPlan Create(
        LibraryBodyAnalysisFeatures features,
        IReadOnlySet<int>? methodScope,
        Func<TypeRef, bool>? typeScope)
    {
        if ((features & ~LibraryBodyAnalysisFeatures.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(features));
        if ((features
                & LibraryBodyAnalysisFeatures.OptimizationOpportunities) != 0)
        {
            features |=
                LibraryBodyAnalysisFeatures.Allocations
                | LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities;
        }
        if ((features & LibraryBodyAnalysisFeatures.Allocations) != 0)
            features |= LibraryBodyAnalysisFeatures.MethodEvidence;
        if ((features
                & LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities) != 0)
        {
            features |= LibraryBodyAnalysisFeatures.MethodEvidence;
        }
        if ((features & LibraryBodyAnalysisFeatures.OwnershipFlow) != 0)
            features |= LibraryBodyAnalysisFeatures.MethodEvidence;
        if ((features & LibraryBodyAnalysisFeatures.LeakTriage) != 0
            && (methodScope is not null || typeScope is not null))
        {
            throw new ArgumentException(
                "Leak Triage requires a full assembly body census.");
        }

        return new(
            features,
            methodScope,
            typeScope,
            RequestedMethodScope: methodScope);
    }
}
