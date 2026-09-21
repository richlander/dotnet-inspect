using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using QuerySpace;
using DotnetInspector.QueryOperations;
using QuerySpace.Rows;
using DotnetInspector.Sections;

namespace DotnetInspector.Queries;

public sealed record LibraryQueryTermDescriptor(
    string Key,
    string Label,
    string Summary,
    string ValueKind,
    string ExampleValue);

public sealed record LibraryQueryRegisteredTerm(
    LibraryQueryTermDescriptor Descriptor,
    ImmutableArray<PortableQueryOperator> Operators);

internal sealed record LibraryQueryPredicate(
    string DisplayName,
    string NormalizedName);

internal sealed class LibraryQueryKeyDeclaration(
    LibraryQueryTermDescriptor descriptor,
    Func<PortableQueryOperator, string,
        PortableQueryBinding<LibraryQueryPredicate>> bind)
    : PortableQueryKeyDeclaration<LibraryQueryPredicate>
{
    internal LibraryQueryTermDescriptor Descriptor { get; } = descriptor;

    public override string Key => Descriptor.Key;

    public override string? Family => null;

    public override PortableQueryFamilyKind FamilyKind =>
        PortableQueryFamilyKind.Combining;

    public override bool AdmitsOperator(PortableQueryOperator @operator) =>
        @operator == PortableQueryOperator.Equal;

    public override PortableQueryBinding<LibraryQueryPredicate> Bind(
        PortableQueryOperator @operator,
        string value) => bind(@operator, value);
}

internal sealed class LibraryQueryCandidateDimension
    : PortableQueryDimensionDeclaration<LibraryQueryPredicate>
{
    public override string Dimension =>
        LibraryQuery.CandidatesDimension;

    public override bool Admits(
        int requestedMaximum,
        IReadOnlyList<PortableQueryResolvedTerm<LibraryQueryPredicate>>
            boundTerms) =>
        requestedMaximum is > 0 and <= LibraryQuery.MaximumCandidates;
}

internal sealed class LibraryQueryVocabulary
    : PortableQueryVocabulary<LibraryQueryPredicate, LibraryQueryPlan>
{
    private readonly IReadOnlyDictionary<
        string,
        LibraryQueryKeyDeclaration> _keys;
    private readonly LibraryQueryCandidateDimension _candidateDimension =
        new();

    internal LibraryQueryVocabulary(
        IEnumerable<LibraryQueryKeyDeclaration> keys)
    {
        _keys = keys.ToDictionary(key => key.Key, StringComparer.Ordinal);
    }

    public override string Identity => LibraryQuery.VocabularyIdentity;

    public override IReadOnlyList<string> RequiredDimensions =>
        [LibraryQuery.CandidatesDimension];

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out PortableQueryKeyDeclaration<LibraryQueryPredicate>?
            declaration)
    {
        bool found = _keys.TryGetValue(
            key,
            out LibraryQueryKeyDeclaration? value);
        declaration = value;
        return found;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)]
        out PortableQueryDimensionDeclaration<LibraryQueryPredicate>?
            declaration)
    {
        bool found = dimension.Equals(
            LibraryQuery.CandidatesDimension,
            StringComparison.Ordinal);
        declaration = found ? _candidateDimension : null;
        return found;
    }

    public override bool AdmitsStageKind(
        RowSelectionStageKind kind) =>
        kind is RowSelectionStageKind.Head
            or RowSelectionStageKind.Tail
            or RowSelectionStageKind.Window;

    public override bool TryGetNamedOrder(
        string reference,
        out PortableQueryOrderPurpose purpose)
    {
        purpose = default;
        return false;
    }

    public override bool IsOrderable(string key) => false;

    public override bool CollapsesDuplicateBindings => true;

    public override bool AreTermsCompatible(
        PortableQueryResolvedTerm<LibraryQueryPredicate> first,
        PortableQueryResolvedTerm<LibraryQueryPredicate> second) =>
        true;

    public override LibraryQueryPlan CreatePlan(
        PortableQueryResolvedIntent<LibraryQueryPredicate> resolved)
    {
        int maximumCandidates = resolved.Bounds.Single(bound =>
            bound.Dimension == LibraryQuery.CandidatesDimension)
            .RequestedMaximum;
        RowSelectionIntent<string> rows =
            RowSelectionIntent<string>.Create(
            [
                .. resolved.Stages.Select(static stage =>
                    stage.Kind switch
                    {
                        RowSelectionStageKind.Head =>
                            RowSelectionIntentOperation<string>.Head(
                                stage.Count),
                        RowSelectionStageKind.Tail =>
                            RowSelectionIntentOperation<string>.Tail(
                                stage.Count),
                        RowSelectionStageKind.Window =>
                            RowSelectionIntentOperation<string>.Window(
                                stage.Start,
                                stage.End),
                        _ => throw new InvalidOperationException(
                            "Library Query resolved an unsupported row stage."),
                    }),
            ]);
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [.. resolved.Terms.Select(term => term.Term)],
            [.. resolved.Bounds],
            [.. resolved.Stages],
            []);

        return new(
            intent,
            [
                .. resolved.Terms.Select(term =>
                    new LibraryQueryReferencePredicate(
                        term.Predicate.DisplayName,
                        term.Predicate.NormalizedName)),
            ],
            maximumCandidates,
            rows);
    }
}

public static partial class LibraryQuery
{
    public const string OperationIdentity = "library-query";
    public const string OperationRouteIdentity =
        "library-query/default";
    public const string OperationSubjectRole =
        "explicit-library-population";
    public const string OperationResultGrain = "library";
    public const string OperationLibrariesRowSet = "libraries";
    public const string OperationProfileIdentity = "default";
    public const string ReferencesTermKey = "references";
    public const string CandidatesDimension = "candidates";
    public const string VocabularyIdentity = "library-query/v1";
    public const string MetadataCapability = "managed-metadata";

    private static readonly LibraryQueryTermDescriptor ReferencesDescriptor =
        new(
            ReferencesTermKey,
            "references assembly",
            "Matches a direct AssemblyRef simple name in each candidate Library.",
            "assembly simple name",
            "System.Text.Json");

    private static readonly LibraryQueryVocabulary Vocabulary =
        new(
        [
            new LibraryQueryKeyDeclaration(
                ReferencesDescriptor,
                BindAssemblyReference),
        ]);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static ImmutableArray<LibraryQueryRegisteredTerm>
        RegisteredTerms => OperationRegistration.RegisteredTerms;

    private static PortableQueryBinding<LibraryQueryPredicate>
        BindAssemblyReference(
            PortableQueryOperator @operator,
            string value)
    {
        if (@operator != PortableQueryOperator.Equal
            || string.IsNullOrWhiteSpace(value)
            || value.AsSpan().Trim().Length != value.Length
            || value.Contains(',')
            || value.Contains('/')
            || value.Contains('\\')
            || !InertText.InertString.IsPermitted(
                InertText.TextPolicy.Field,
                value))
        {
            return PortableQueryBinding<LibraryQueryPredicate>.Rejected;
        }

        return PortableQueryBinding<LibraryQueryPredicate>.Bound(
            $"{ReferencesTermKey}:{Normalize(value)}",
            new(value, Normalize(value)));
    }

    private static string Normalize(string value) =>
        value.ToUpperInvariant();

    private static class OperationRegistration
    {
        internal static readonly QueryOperationDefinition<
            LibraryQueryPredicate,
            LibraryQueryPlan> Definition =
            CreateDefinition();

        internal static readonly QueryOperationRoute<
            LibraryQueryPredicate,
            LibraryQueryPlan> Route =
            QueryOperationRoute<
                LibraryQueryPredicate,
                LibraryQueryPlan>.Create(
                OperationRouteIdentity,
                Definition,
                OperationSubjectRole,
                OperationResultGrain,
                [OperationLibrariesRowSet],
                OperationProfileIdentity,
                [CandidatesDimension],
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Tail,
                    RowSelectionStageKind.Window,
                ]);

        internal static readonly ImmutableArray<
            LibraryQueryRegisteredTerm> RegisteredTerms =
        [
            .. Route.Capabilities.Terms.Select(capability =>
                new LibraryQueryRegisteredTerm(
                    ReferencesDescriptor,
                    [.. capability.Operators])),
        ];

        private static QueryOperationDefinition<
            LibraryQueryPredicate,
            LibraryQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [OperationSubjectRole],
                [OperationResultGrain],
                []);
            QueryOperationTermBinding[] terms =
            [
                new(
                    "library-query.term.references",
                    ReferencesTermKey,
                    QueryOperationTermRole.SubjectQualification,
                    applicability,
                    new QueryOperationTermDescription(
                        ReferencesDescriptor.Label,
                        ReferencesDescriptor.ValueKind,
                        [],
                        ReferencesDescriptor.Summary),
                    [
                        new(
                            QueryOperationEffectKind.AcquisitionTier,
                            "metadata"),
                        new(
                            QueryOperationEffectKind.Capability,
                            MetadataCapability),
                    ]),
            ];

            return QueryOperationDefinition<
                LibraryQueryPredicate,
                LibraryQueryPlan>.Create(
                OperationIdentity,
                Vocabulary,
                [OperationSubjectRole],
                [OperationResultGrain],
                [OperationLibrariesRowSet],
                terms,
                [],
                [
                    new(
                        OperationProfileIdentity,
                        [.. terms.Select(term => term.Identity)],
                        []),
                ]);
        }
    }
}
