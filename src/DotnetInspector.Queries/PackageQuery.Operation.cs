using System.Collections.Immutable;
using QuerySpace;
using DotnetInspector.QueryOperations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

/// <summary>
/// One Package Query term projected from the effective operation route.
/// </summary>
public sealed record PackageQueryRegisteredTerm(
    PackageQueryTermDescriptor Descriptor,
    ImmutableArray<PortableQueryOperator> Operators);

public static partial class PackageQuery
{
    public const string OperationIdentity = "package-query";
    public const string OperationRouteIdentity = "package-query/default";
    public const string OperationSubjectRole = "package-population";
    public const string OperationResultGrain = "package";
    public const string OperationPackagesRowSet = "packages";
    public const string OperationProfileIdentity = "default";
    public const string PackageContentCapability =
        "package-content-provider";

    /// <summary>The effective Package Query operation route.</summary>
    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    /// <summary>
    /// The Package Query terms admitted by the effective operation route.
    /// </summary>
    public static ImmutableArray<PackageQueryRegisteredTerm> RegisteredTerms =>
        OperationRegistration.RegisteredTerms;

    private static class OperationRegistration
    {
        internal static readonly QueryOperationDefinition<
            PackageQueryPredicate,
            PackageQueryPlan> Definition =
            CreateDefinition();

        internal static readonly QueryOperationRoute<
            PackageQueryPredicate,
            PackageQueryPlan> Route =
            QueryOperationRoute<
                PackageQueryPredicate,
                PackageQueryPlan>.Create(
                OperationRouteIdentity,
                Definition,
                OperationSubjectRole,
                OperationResultGrain,
                [OperationPackagesRowSet],
                OperationProfileIdentity,
                [
                    PackageQueryVocabulary.CandidatesDimension,
                    PackageQueryVocabulary.MatchesDimension,
                ],
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Tail,
                    RowSelectionStageKind.Window,
                ]);

        internal static readonly ImmutableArray<
            PackageQueryRegisteredTerm> RegisteredTerms =
        [
            .. Route.Capabilities.Terms.Select(capability =>
                new PackageQueryRegisteredTerm(
                    TermsByKey[capability.Binding.Key],
                    [.. capability.Operators])),
        ];

        internal static readonly ImmutableArray<
            PackageQueryTermDescriptor> TermDescriptors =
        [
            .. RegisteredTerms.Select(term => term.Descriptor),
        ];

        private static QueryOperationDefinition<
            PackageQueryPredicate,
            PackageQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [OperationSubjectRole],
                [OperationResultGrain],
                []);
            QueryOperationTermBinding[] terms =
            [
                .. DeclaredTerms.Select(term =>
                    new QueryOperationTermBinding(
                        $"package-query.term.{term.Key}",
                        term.Key,
                        Role(term),
                        applicability,
                        new QueryOperationTermDescription(
                            term.Label,
                            term.ValueKind,
                            [
                                .. term.Options.Select(option =>
                                    option.Value),
                            ],
                            term.Summary),
                        Effects(term))),
            ];

            return QueryOperationDefinition<
                PackageQueryPredicate,
                PackageQueryPlan>.Create(
                OperationIdentity,
                Vocabulary,
                [OperationSubjectRole],
                [OperationResultGrain],
                [OperationPackagesRowSet],
                terms,
                [],
                [
                    new QueryOperationProfile(
                        OperationProfileIdentity,
                        [.. terms.Select(term => term.Identity)],
                        []),
                ]);
        }

        private static QueryOperationTermRole Role(
            PackageQueryTermDescriptor term) =>
            term.Key == PrereleaseTermKey
                ? QueryOperationTermRole.OperationSelector
                : QueryOperationTermRole.SubjectQualification;

        private static QueryOperationEffect[] Effects(
            PackageQueryTermDescriptor term) =>
            term.Tier == PackageQueryAcquisitionTier.PackageContent
                ?
                [
                    new(
                        QueryOperationEffectKind.AcquisitionTier,
                        AcquisitionTier(term.Tier)),
                    new(
                        QueryOperationEffectKind.Capability,
                        PackageContentCapability),
                ]
                :
                [
                    new(
                        QueryOperationEffectKind.AcquisitionTier,
                        AcquisitionTier(term.Tier)),
                ];

        private static string AcquisitionTier(
            PackageQueryAcquisitionTier tier) =>
            tier switch
            {
                PackageQueryAcquisitionTier.SearchMetadata =>
                    "search-metadata",
                PackageQueryAcquisitionTier.Nuspec => "nuspec",
                PackageQueryAcquisitionTier.PackageContent =>
                    "package-content",
                _ => throw new InvalidOperationException(
                    "Unknown Package Query acquisition tier."),
            };
    }
}
