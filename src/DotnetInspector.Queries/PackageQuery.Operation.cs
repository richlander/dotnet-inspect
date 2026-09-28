using System.Collections.Immutable;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
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
    public const string QuerySpaceIdentity =
        "package-query/query-space/v1";
    public const string PackageRowsScopeIdentity =
        "package-query/package-rows/v1";
    public const string ResultContractIdentity =
        "package-query/document/v1";
    public const string PackageContentCapability =
        "package-content-provider";

    private static readonly Lazy<QuerySpaceBinding> QuerySpaceValue =
        new(CreateQuerySpace);

    /// <summary>The effective Package Query operation route.</summary>
    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    /// <summary>The complete executable Package Query space.</summary>
    public static QuerySpaceBinding QuerySpace => QuerySpaceValue.Value;

    public static QuerySpaceRowScopeBinding<PackageQueryMatch>
        PackageRowsScope { get; } =
            new(
                new QuerySpaceRowScopeDescriptor(
                    PackageRowsScopeIdentity,
                    ResultContractIdentity,
                    [OperationPackagesRowSet],
                    [],
                    [],
                    []),
                RowQueryVocabulary<PackageQueryMatch>.Create(
                    RowQueryVocabularyIdentity.Create(),
                    [],
                    []));

    /// <summary>
    /// The Package Query terms admitted by the effective operation route.
    /// </summary>
    public static ImmutableArray<PackageQueryRegisteredTerm> RegisteredTerms =>
        OperationRegistration.RegisteredTerms;

    /// <summary>
    /// The owner-approved Package Query facet subset exposed by production
    /// interactive hosts.
    /// </summary>
    public static ImmutableArray<string> InspectionTermBindingIdentities =>
        OperationRegistration.InspectionTermBindingIdentities;

    public static string TermBindingIdentity(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        QueryOperationTermCapability? capability =
            OperationRegistration.Route.Capabilities.Terms
                .SingleOrDefault(term =>
                    string.Equals(
                        term.Binding.Key,
                        key,
                        StringComparison.Ordinal));
        return capability?.Binding.Identity
            ?? throw new ArgumentException(
                $"Package Query declares no term '{key}'.",
                nameof(key));
    }

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

        internal static readonly ImmutableArray<string>
            InspectionTermBindingIdentities =
        [
            .. Route.Capabilities.Terms
                .Where(capability =>
                    TermsByKey[capability.Binding.Key].Role
                    == PackageQueryTermRole.Inspection)
                .Select(static capability =>
                    capability.Binding.Identity),
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

    private static QuerySpaceBinding CreateQuerySpace() =>
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRoute,
            [PackageRowsScope],
            [QuerySpaceTerminalRequirement.Rows],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    ResultContractIdentity),
            ]);
}
