using System.Collections.Immutable;

using ILInspector.Decompiler;

namespace ILInspector.DecompilerHarness;

internal enum FidelityResidualDisposition
{
    RecoverableRoadmap,
    PolicyFloor,
    Unclassified,
}

internal readonly record struct FidelityResidualClassification(
    FidelityResidualDisposition Disposition,
    string RuleId);

internal static class FidelityResidualPolicy
{
    public const int Version = 1;

    static readonly ImmutableHashSet<string> RecoverableNameDiscriminators =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            DecompilerFidelityDiscriminators.AccessorMetadataUnavailable,
            DecompilerFidelityDiscriminators.DisplayClassTypeName,
            DecompilerFidelityDiscriminators.EscapableFieldName,
            DecompilerFidelityDiscriminators.EscapableInitializerMemberName,
            DecompilerFidelityDiscriminators.EscapablePropertyName,
            DecompilerFidelityDiscriminators.GeneratedFieldName,
            DecompilerFidelityDiscriminators.GeneratedGenericParameterName,
            DecompilerFidelityDiscriminators.GeneratedInitializerMemberName,
            DecompilerFidelityDiscriminators.GeneratedMethodName,
            DecompilerFidelityDiscriminators.GeneratedPropertyName,
            DecompilerFidelityDiscriminators.GeneratedTypeName,
            DecompilerFidelityDiscriminators.LambdaHolderTypeName,
            DecompilerFidelityDiscriminators.LambdaMethodName,
            DecompilerFidelityDiscriminators.LocalFunctionMethodName,
            DecompilerFidelityDiscriminators.StateMachineTypeName);

    static readonly ImmutableHashSet<string> PolicyFloorNameDiscriminators =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            DecompilerFidelityDiscriminators.UnspellableFieldName,
            DecompilerFidelityDiscriminators.UnspellableGenericParameterName,
            DecompilerFidelityDiscriminators.UnspellableInitializerMemberName,
            DecompilerFidelityDiscriminators.UnspellableLocalFunctionName,
            DecompilerFidelityDiscriminators.UnspellableMethodName,
            DecompilerFidelityDiscriminators.UnspellablePropertyName,
            DecompilerFidelityDiscriminators.UnspellableTypeName);

    public static FidelityResidualClassification Classify(
        CorpusFidelityCauseSnapshot cause)
    {
        if (cause.Code == DiagnosticIds.UnrepresentableMetadataName)
        {
            if (cause.Discriminator is { } discriminator
                && RecoverableNameDiscriminators.Contains(discriminator))
            {
                return Recoverable("name-shape-raising");
            }
            if (cause.Discriminator is { } floorDiscriminator
                && PolicyFloorNameDiscriminators.Contains(floorDiscriminator))
            {
                return PolicyFloor("metadata-name-policy-floor");
            }
        }

        return (cause.Code, cause.Discriminator) switch
        {
            (DiagnosticIds.UnsupportedConstruct, "iterator") =>
                Recoverable("iterator-raising"),
            (DiagnosticIds.UnsupportedConstruct, "call .ctor") =>
                Recoverable("constructor-call-raising"),
            (DiagnosticIds.UnsupportedType, DecompilerFidelityDiscriminators.PrivateImplementationDetailsType) =>
                Recoverable("private-implementation-details-raising"),
            (DiagnosticIds.UnknownResultType, _) =>
                Recoverable("result-type-recovery"),
            (DiagnosticIds.UnverifiedContinue, _) =>
                Recoverable("continue-verification"),
            (DiagnosticIds.UnraisedPinnedLocal, _) =>
                Recoverable("fixed-statement-raising"),
            _ => new FidelityResidualClassification(
                FidelityResidualDisposition.Unclassified,
                "unclassified"),
        };
    }

    static FidelityResidualClassification Recoverable(string ruleId)
        => new(FidelityResidualDisposition.RecoverableRoadmap, ruleId);

    static FidelityResidualClassification PolicyFloor(string ruleId)
        => new(FidelityResidualDisposition.PolicyFloor, ruleId);
}

internal sealed record FidelityResidualFacetSummary(
    string Code,
    string? Discriminator,
    FidelityResidualDisposition Disposition,
    string RuleId,
    int CauseSites,
    int Methods,
    ImmutableArray<string> Examples);

internal sealed record FidelityResidualPortfolio(
    int PolicyVersion,
    int TotalMethods,
    int FullyRaisedMethods,
    int FidelityPrimaryMethods,
    int FidelityCauseSites,
    int RecoverableMethods,
    int PolicyFloorMethods,
    int UnclassifiedMethods,
    int MissingCauseMethods,
    int StructuralPrimaryMethodsWithFidelityCauses,
    int StructuralPrimaryFidelityCauseSites,
    int RoadmapTargetLowerMethods,
    int RoadmapTargetUpperMethods,
    ImmutableArray<FidelityResidualFacetSummary> Facets);

internal static class FidelityResidualPortfolioBuilder
{
    const int MaxExamples = 3;

    readonly record struct Facet(string Code, string? Discriminator);

    sealed class FacetAccumulator
    {
        public int CauseSites;
        public HashSet<string> Methods { get; } = new(StringComparer.Ordinal);
    }

    public static FidelityResidualPortfolio Build(
        IReadOnlyList<CorpusMethodSnapshot> methods,
        int totalMethods,
        int fullyRaisedMethods)
    {
        var facets = new Dictionary<Facet, FacetAccumulator>();
        int fidelityPrimaryMethods = 0;
        int fidelityCauseSites = 0;
        int recoverableMethods = 0;
        int policyFloorMethods = 0;
        int unclassifiedMethods = 0;
        int missingCauseMethods = 0;
        int structuralPrimaryMethods = 0;
        int structuralPrimarySites = 0;

        foreach (var method in methods)
        {
            var causes = method.FidelityCauses;
            bool fidelityPrimary = method.Residual?.StartsWith(
                "fidelity:",
                StringComparison.Ordinal) == true;

            if (!fidelityPrimary)
            {
                if (IsEarlierStructuralResidual(method.Residual)
                    && causes is { Count: > 0 })
                {
                    structuralPrimaryMethods++;
                    structuralPrimarySites += causes.Sum(static cause => cause.SiteCount);
                }
                continue;
            }

            fidelityPrimaryMethods++;
            if (causes is not { Count: > 0 })
            {
                missingCauseMethods++;
                unclassifiedMethods++;
                continue;
            }

            fidelityCauseSites += causes.Sum(static cause => cause.SiteCount);
            string methodKey = method.StableKey;
            var classifications = new FidelityResidualClassification[causes.Count];
            for (int i = 0; i < causes.Count; i++)
            {
                var cause = causes[i];
                var classification = FidelityResidualPolicy.Classify(cause);
                classifications[i] = classification;
                var facet = new Facet(cause.Code, cause.Discriminator);
                if (!facets.TryGetValue(facet, out var accumulator))
                {
                    accumulator = new FacetAccumulator();
                    facets.Add(facet, accumulator);
                }
                accumulator.CauseSites += cause.SiteCount;
                accumulator.Methods.Add(methodKey);
            }

            if (classifications.All(static classification =>
                    classification.Disposition == FidelityResidualDisposition.RecoverableRoadmap))
            {
                recoverableMethods++;
            }
            else if (classifications.Any(static classification =>
                         classification.Disposition == FidelityResidualDisposition.PolicyFloor))
            {
                policyFloorMethods++;
            }
            else
            {
                unclassifiedMethods++;
            }
        }

        var summaries = facets
            .Select(pair =>
            {
                var cause = new CorpusFidelityCauseSnapshot(
                    pair.Key.Code,
                    pair.Key.Discriminator);
                var classification = FidelityResidualPolicy.Classify(cause);
                return new FidelityResidualFacetSummary(
                    pair.Key.Code,
                    pair.Key.Discriminator,
                    classification.Disposition,
                    classification.RuleId,
                    pair.Value.CauseSites,
                    pair.Value.Methods.Count,
                    [.. pair.Value.Methods
                        .Order(StringComparer.Ordinal)
                        .Take(MaxExamples)]);
            })
            .OrderByDescending(static summary => summary.Methods)
            .ThenByDescending(static summary => summary.CauseSites)
            .ThenBy(static summary => summary.Code, StringComparer.Ordinal)
            .ThenBy(static summary => summary.Discriminator, StringComparer.Ordinal)
            .ToImmutableArray();

        return new FidelityResidualPortfolio(
            FidelityResidualPolicy.Version,
            totalMethods,
            fullyRaisedMethods,
            fidelityPrimaryMethods,
            fidelityCauseSites,
            recoverableMethods,
            policyFloorMethods,
            unclassifiedMethods,
            missingCauseMethods,
            structuralPrimaryMethods,
            structuralPrimarySites,
            fullyRaisedMethods + recoverableMethods,
            fullyRaisedMethods + recoverableMethods + unclassifiedMethods,
            summaries);
    }

    static bool IsEarlierStructuralResidual(string? residual)
        => residual?.StartsWith("structuring:", StringComparison.Ordinal) == true
            || residual?.StartsWith("eh:", StringComparison.Ordinal) == true;
}
