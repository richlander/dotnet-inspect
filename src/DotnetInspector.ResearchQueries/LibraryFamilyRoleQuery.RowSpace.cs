using System.Globalization;

using ILInspector.Metadata;
using ILInspector.Research;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public static partial class LibraryFamilyRoleQuery
{
    public const string FamilyKindKey = "kind";
    public const string FamilyWordKey = "word";
    public const string FamilyTypeCountKey = "type-count";
    public const string FamilyFoundationCountKey = "foundation-count";
    public const string FamilyHubCountKey = "hub-count";
    public const string FamilyOrchestratorCountKey =
        "orchestrator-count";
    public const string FamilySeaLevelCountKey = "sea-level-count";
    public const string FamilyMountainPeakCountKey =
        "mountain-peak-count";
    public const string FamilyNoRoleCountKey = "no-issued-role-count";
    public const string FamilyNamespaceCountKey = "namespace-count";
    public const string FamilyStructuralDispositionKey =
        "family-structural-disposition";
    public const string FamilyPrevalenceOrder = "prevalence";

    public const string TypeNamespaceKey = "namespace";
    public const string TypeDefinitionTokenKey = "definition-token";
    public const string TypeDefinitionKindKey = "definition-kind";
    public const string TypeFamilyKindKey = "family-kind";
    public const string TypeFamilyWordKey = "family-word";
    public const string TypeSourceDispositionKey = "source-disposition";
    public const string TypeIncomingDegreeKey = "incoming-degree";
    public const string TypeOutgoingDegreeKey = "outgoing-degree";
    public const string TypeStructuralRoleKey = "structural-role";
    public const string TypeStructuralPoleKey = "structural-pole";
    public const string TypeStructuralDispositionKey =
        "type-structural-disposition";
    public const string TypeDefinitionOrder = "definition";

    private static readonly IReadOnlyList<RowQueryOperator>
        EqualityOperators =
        [
            RowQueryOperator.Equals,
            RowQueryOperator.NotEquals,
        ];

    private static readonly IReadOnlyList<RowQueryOperator>
        NumericOperators =
        [
            RowQueryOperator.Equals,
            RowQueryOperator.NotEquals,
            RowQueryOperator.GreaterOrEqual,
            RowQueryOperator.LessOrEqual,
        ];

    private static readonly RowQueryNamedOrder<LibraryFamilyRoleRow>
        FamilyPrevalenceOrderDefinition =
            new(
                RowQueryNamedOrderIdentity.Create(),
                FamilyPrevalenceOrder,
                RowQueryOrderPurpose.Ranking,
                direction => Directional(
                    LibraryFamilyRoleOrder.Prevalence,
                    direction));

    private static readonly RowQueryNamedOrder<LibraryFamilyRoleTypeRow>
        TypeDefinitionOrderDefinition =
            new(
                RowQueryNamedOrderIdentity.Create(),
                TypeDefinitionOrder,
                RowQueryOrderPurpose.Sequence,
                direction => Directional(
                    Comparer<LibraryFamilyRoleTypeRow>.Create(
                        static (left, right) =>
                            left.Type.Definition.Value.CompareTo(
                                right.Type.Definition.Value)),
                    direction));

    private static readonly RowQueryVocabulary<LibraryFamilyRoleRow>
        FamilyVocabulary =
            RowQueryVocabulary<LibraryFamilyRoleRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [
                    FamilyKindQueryKey(),
                    FamilyWordQueryKey(),
                    NumericKey<LibraryFamilyRoleRow>(
                        FamilyTypeCountKey,
                        static row => row.TypeCount),
                    NumericKey<LibraryFamilyRoleRow>(
                        FamilyFoundationCountKey,
                        static row => row.FoundationCount),
                    NumericKey<LibraryFamilyRoleRow>(
                        FamilyHubCountKey,
                        static row => row.HubCount),
                    NumericKey<LibraryFamilyRoleRow>(
                        FamilyOrchestratorCountKey,
                        static row => row.OrchestratorCount),
                    NumericKey<LibraryFamilyRoleRow>(
                        FamilySeaLevelCountKey,
                        static row => row.SeaLevelCount),
                    NumericKey<LibraryFamilyRoleRow>(
                        FamilyMountainPeakCountKey,
                        static row => row.MountainPeakCount),
                    NumericKey<LibraryFamilyRoleRow>(
                        FamilyNoRoleCountKey,
                        static row => row.NoIssuedStructuralRoleCount),
                    NumericKey<LibraryFamilyRoleRow>(
                        FamilyNamespaceCountKey,
                        static row => row.DistinctNamespaceCount),
                    EnumKey<
                        LibraryFamilyRoleRow,
                        LibraryStructuralEvidenceDisposition>(
                            FamilyStructuralDispositionKey,
                            static row => row.StructuralDisposition),
                ],
                [FamilyPrevalenceOrderDefinition],
                defaultBaselineOrder:
                    new(
                        FamilyPrevalenceOrderDefinition,
                        RowQueryOrderDirection.Ascending),
                defaultTopRanking:
                    new(
                        FamilyPrevalenceOrderDefinition,
                        RowQueryOrderDirection.Ascending));

    private static readonly RowQueryVocabulary<LibraryFamilyRoleTypeRow>
        TypeVocabulary =
            RowQueryVocabulary<LibraryFamilyRoleTypeRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [
                    OrdinalTextKey<LibraryFamilyRoleTypeRow>(
                        TypeNamespaceKey,
                        static row => row.Namespace),
                    NumericKey<LibraryFamilyRoleTypeRow>(
                        TypeDefinitionTokenKey,
                        static row => row.Type.Definition.Value),
                    EnumKey<
                        LibraryFamilyRoleTypeRow,
                        AssemblyTypeDefinitionKind>(
                            TypeDefinitionKindKey,
                            static row => row.DefinitionKind),
                    TypeFamilyKindQueryKey(),
                    TypeFamilyWordQueryKey(),
                    NullableEnumKey<
                        LibraryFamilyRoleTypeRow,
                        PdbTypeSourceDisposition>(
                            TypeSourceDispositionKey,
                            static row => row.SourceDisposition),
                    NullableNumericKey<LibraryFamilyRoleTypeRow>(
                        TypeIncomingDegreeKey,
                        static row => row.SignatureIncomingDegree),
                    NullableNumericKey<LibraryFamilyRoleTypeRow>(
                        TypeOutgoingDegreeKey,
                        static row => row.SignatureOutgoingDegree),
                    NullableEnumKey<
                        LibraryFamilyRoleTypeRow,
                        LibraryStructuralTypeRole>(
                            TypeStructuralRoleKey,
                            static row => row.StructuralRole),
                    NullableEnumKey<
                        LibraryFamilyRoleTypeRow,
                        LibraryStructuralTypePole>(
                            TypeStructuralPoleKey,
                            static row => row.StructuralPole),
                    EnumKey<
                        LibraryFamilyRoleTypeRow,
                        LibraryStructuralEvidenceDisposition>(
                            TypeStructuralDispositionKey,
                            static row => row.StructuralDisposition),
                ],
                [TypeDefinitionOrderDefinition],
                defaultBaselineOrder:
                    new(
                        TypeDefinitionOrderDefinition,
                        RowQueryOrderDirection.Ascending));

    public static QuerySpaceRowScopeBinding<LibraryFamilyRoleRow>
        FamilyRowsScope
    { get; } =
        new(
            new QuerySpaceRowScopeDescriptor(
                FamilyRowsScopeIdentity,
                FamilyRowsResultContract,
                [FamilyRowsRowSet],
                [
                    Facet(
                        "library-family-roles.family.kind",
                        FamilyKindKey,
                        EqualityOperators,
                        "name-family kind",
                        "family kind",
                        ["one-word", "two-word"],
                        "Matches the exact one- or two-word suffix kind.",
                        supportsOrdering: true),
                    Facet(
                        "library-family-roles.family.word",
                        FamilyWordKey,
                        EqualityOperators,
                        "ordinal identifier word",
                        "family word",
                        [],
                        "Matches any exact owner-issued suffix word.",
                        supportsOrdering: false),
                    NumericFacet(
                        "library-family-roles.family.type-count",
                        FamilyTypeCountKey,
                        "Type count",
                        "Matches the exact supporting Type count."),
                    NumericFacet(
                        "library-family-roles.family.foundation-count",
                        FamilyFoundationCountKey,
                        "foundation count",
                        "Matches the owner-issued foundation count."),
                    NumericFacet(
                        "library-family-roles.family.hub-count",
                        FamilyHubCountKey,
                        "hub count",
                        "Matches the owner-issued hub count."),
                    NumericFacet(
                        "library-family-roles.family.orchestrator-count",
                        FamilyOrchestratorCountKey,
                        "orchestrator count",
                        "Matches the owner-issued orchestrator count."),
                    NumericFacet(
                        "library-family-roles.family.sea-level-count",
                        FamilySeaLevelCountKey,
                        "sea-level count",
                        "Matches the owner-issued sea-level pole count."),
                    NumericFacet(
                        "library-family-roles.family.mountain-peak-count",
                        FamilyMountainPeakCountKey,
                        "mountain-peak count",
                        "Matches the owner-issued mountain-peak pole count."),
                    NumericFacet(
                        "library-family-roles.family.no-role-count",
                        FamilyNoRoleCountKey,
                        "no-issued-role count",
                        "Matches the count without an issued structural role."),
                    NumericFacet(
                        "library-family-roles.family.namespace-count",
                        FamilyNamespaceCountKey,
                        "namespace count",
                        "Matches the exact distinct namespace count."),
                    EnumFacet<LibraryStructuralEvidenceDisposition>(
                        "library-family-roles.family.structural-disposition",
                        FamilyStructuralDispositionKey,
                        "structural disposition",
                        "Matches the owner-issued structural disposition."),
                ],
                [
                    new(
                        FamilyPrevalenceOrder,
                        ranking: true),
                ],
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Tail,
                    RowSelectionStageKind.Window,
                    RowSelectionStageKind.Top,
                ]),
            FamilyVocabulary);

    public static QuerySpaceRowScopeBinding<LibraryFamilyRoleTypeRow>
        TypeRowsScope
    { get; } =
        new(
            new QuerySpaceRowScopeDescriptor(
                TypeRowsScopeIdentity,
                TypeRowsResultContract,
                [TypeRowsRowSet],
                [
                    Facet(
                        "library-family-roles.type.namespace",
                        TypeNamespaceKey,
                        EqualityOperators,
                        "ordinal metadata namespace",
                        "namespace",
                        [],
                        "Matches the exact metadata namespace.",
                        supportsOrdering: true),
                    NumericFacet(
                        "library-family-roles.type.definition-token",
                        TypeDefinitionTokenKey,
                        "TypeDef token",
                        "Matches the exact positive TypeDef token."),
                    EnumFacet<AssemblyTypeDefinitionKind>(
                        "library-family-roles.type.definition-kind",
                        TypeDefinitionKindKey,
                        "definition kind",
                        "Matches the Metadata-owned Type definition kind."),
                    Facet(
                        "library-family-roles.type.family-kind",
                        TypeFamilyKindKey,
                        EqualityOperators,
                        "name-family kind",
                        "family kind",
                        ["one-word", "two-word"],
                        "Matches an assigned family of the requested kind.",
                        supportsOrdering: false),
                    Facet(
                        "library-family-roles.type.family-word",
                        TypeFamilyWordKey,
                        EqualityOperators,
                        "ordinal identifier word",
                        "family word",
                        [],
                        "Matches any word in an assigned suffix family.",
                        supportsOrdering: false),
                    EnumFacet<PdbTypeSourceDisposition>(
                        "library-family-roles.type.source-disposition",
                        TypeSourceDispositionKey,
                        "source disposition",
                        "Matches the optional source disposition."),
                    NumericFacet(
                        "library-family-roles.type.incoming-degree",
                        TypeIncomingDegreeKey,
                        "incoming degree",
                        "Matches the optional signature incoming degree."),
                    NumericFacet(
                        "library-family-roles.type.outgoing-degree",
                        TypeOutgoingDegreeKey,
                        "outgoing degree",
                        "Matches the optional signature outgoing degree."),
                    EnumFacet<LibraryStructuralTypeRole>(
                        "library-family-roles.type.structural-role",
                        TypeStructuralRoleKey,
                        "structural role",
                        "Matches the optional owner-issued structural role."),
                    EnumFacet<LibraryStructuralTypePole>(
                        "library-family-roles.type.structural-pole",
                        TypeStructuralPoleKey,
                        "structural pole",
                        "Matches the optional owner-issued pole."),
                    EnumFacet<LibraryStructuralEvidenceDisposition>(
                        "library-family-roles.type.structural-disposition",
                        TypeStructuralDispositionKey,
                        "structural disposition",
                        "Matches the owner-issued structural disposition."),
                ],
                [
                    new(
                        TypeDefinitionOrder,
                        ranking: false),
                ],
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Tail,
                    RowSelectionStageKind.Window,
                ]),
            TypeVocabulary);

    public static QuerySpaceBinding QuerySpace { get; } =
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRegistration.Route,
            [
                FamilyRowsScope,
                TypeRowsScope,
            ],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    "library-family-roles/rows/v1"),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    "library-family-roles/count/v1"),
            ]);

    private static QuerySpaceRowFacetDescriptor NumericFacet(
        string identity,
        string key,
        string label,
        string summary) =>
        Facet(
            identity,
            key,
            NumericOperators,
            "non-negative integer",
            label,
            [],
            summary,
            supportsOrdering: true);

    private static QuerySpaceRowFacetDescriptor EnumFacet<TEnum>(
        string identity,
        string key,
        string label,
        string summary)
        where TEnum : struct, Enum =>
        Facet(
            identity,
            key,
            EqualityOperators,
            "enum",
            label,
            Enum.GetNames<TEnum>(),
            summary,
            supportsOrdering: true);

    private static QuerySpaceRowFacetDescriptor Facet(
        string identity,
        string key,
        IReadOnlyList<RowQueryOperator> operators,
        string valueKind,
        string label,
        IReadOnlyList<string> values,
        string summary,
        bool supportsOrdering) =>
        new(
            identity,
            key,
            [.. operators.Select(ToPortableOperator)],
            valueKind,
            valueVocabulary: null,
            label,
            values,
            summary,
            supportsOrdering);

    private static PortableQueryOperator ToPortableOperator(
        RowQueryOperator operation) =>
        operation switch
        {
            RowQueryOperator.Equals => PortableQueryOperator.Equal,
            RowQueryOperator.NotEquals =>
                PortableQueryOperator.NotEqual,
            RowQueryOperator.GreaterOrEqual =>
                PortableQueryOperator.AtLeast,
            RowQueryOperator.LessOrEqual =>
                PortableQueryOperator.AtMost,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation)),
        };

    private static RowQueryKey<LibraryFamilyRoleRow>
        FamilyKindQueryKey() =>
        RowQueryKey<LibraryFamilyRoleRow>.Create(
            RowQueryKeyIdentity.Create(),
            FamilyKindKey,
            EqualityOperators,
            static row =>
                RowQueryValue<LibraryNameFamilyKind>.Present(
                    row.Identity.Kind),
            BindEnum<LibraryNameFamilyKind>,
            direction => RowQueryValueOrder.Create(
                Comparer<LibraryNameFamilyKind>.Default,
                direction,
                missingLast: false));

    private static RowQueryKey<LibraryFamilyRoleRow>
        FamilyWordQueryKey() =>
        RowQueryKey<LibraryFamilyRoleRow>.Create(
            RowQueryKeyIdentity.Create(),
            FamilyWordKey,
            EqualityOperators,
            static row =>
                RowQueryValue<LibraryFamilyRoleRow>.Present(row),
            static (operation, token) =>
                BindFamilyWord<LibraryFamilyRoleRow>(
                    operation,
                    token,
                    static row => [row.Identity]));

    private static RowQueryKey<LibraryFamilyRoleTypeRow>
        TypeFamilyKindQueryKey() =>
        RowQueryKey<LibraryFamilyRoleTypeRow>.Create(
            RowQueryKeyIdentity.Create(),
            TypeFamilyKindKey,
            EqualityOperators,
            static row =>
                RowQueryValue<LibraryFamilyRoleTypeRow>.Present(row),
            static (operation, token) =>
            {
                LibraryNameFamilyKind? expected =
                    ParseFamilyKind(token);
                if (expected is null)
                    return null;

                bool HasKind(LibraryFamilyRoleTypeRow row) =>
                    row.OneWordSuffix?.Kind == expected
                    || row.TwoWordSuffix?.Kind == expected;
                return operation switch
                {
                    RowQueryOperator.Equals => HasKind,
                    RowQueryOperator.NotEquals =>
                        row => !HasKind(row),
                    _ => null,
                };
            });

    private static RowQueryKey<LibraryFamilyRoleTypeRow>
        TypeFamilyWordQueryKey() =>
        RowQueryKey<LibraryFamilyRoleTypeRow>.Create(
            RowQueryKeyIdentity.Create(),
            TypeFamilyWordKey,
            EqualityOperators,
            static row =>
                RowQueryValue<LibraryFamilyRoleTypeRow>.Present(row),
            static (operation, token) =>
                BindFamilyWord<LibraryFamilyRoleTypeRow>(
                    operation,
                    token,
                    static row =>
                        row.OneWordSuffix is null
                            ? row.TwoWordSuffix is null
                                ? []
                                : [row.TwoWordSuffix]
                            : row.TwoWordSuffix is null
                                ? [row.OneWordSuffix]
                                : [
                                    row.OneWordSuffix,
                                    row.TwoWordSuffix,
                                ]));

    private static Predicate<TRow>? BindFamilyWord<TRow>(
        RowQueryOperator operation,
        RowQueryValueToken token,
        Func<
            TRow,
            IEnumerable<LibraryNameFamilyIdentity>> families)
    {
        string expected = token.Text;
        return operation switch
        {
            RowQueryOperator.Equals =>
                row => families(row).Any(
                    family => family.Words.Contains(
                        expected,
                        StringComparer.Ordinal)),
            RowQueryOperator.NotEquals =>
                row => families(row).All(
                    family => !family.Words.Contains(
                        expected,
                        StringComparer.Ordinal)),
            _ => null,
        };
    }

    private static LibraryNameFamilyKind? ParseFamilyKind(
        RowQueryValueToken token) =>
        token.Text.ToLowerInvariant() switch
        {
            "one-word" =>
                LibraryNameFamilyKind.OneWordSuffix,
            "two-word" =>
                LibraryNameFamilyKind.TwoWordSuffix,
            _ => null,
        };

    private static RowQueryKey<TRow> NumericKey<TRow>(
        string key,
        Func<TRow, int> accessor) =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            NumericOperators,
            row => RowQueryValue<int>.Present(accessor(row)),
            BindInteger,
            direction => RowQueryValueOrder.Create(
                Comparer<int>.Default,
                direction,
                missingLast: false));

    private static RowQueryKey<TRow> NullableNumericKey<TRow>(
        string key,
        Func<TRow, int?> accessor) =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            NumericOperators,
            row => accessor(row) is { } value
                ? RowQueryValue<int>.Present(value)
                : RowQueryValue<int>.Missing,
            BindInteger,
            direction => RowQueryValueOrder.Create(
                Comparer<int>.Default,
                direction,
                missingLast: true));

    private static Predicate<int>? BindInteger(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!int.TryParse(
                token.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int expected)
            || expected < 0)
        {
            return null;
        }

        return operation switch
        {
            RowQueryOperator.Equals =>
                value => value == expected,
            RowQueryOperator.NotEquals =>
                value => value != expected,
            RowQueryOperator.GreaterOrEqual =>
                value => value >= expected,
            RowQueryOperator.LessOrEqual =>
                value => value <= expected,
            _ => null,
        };
    }

    private static RowQueryKey<TRow> EnumKey<TRow, TEnum>(
        string key,
        Func<TRow, TEnum> accessor)
        where TEnum : struct, Enum =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            EqualityOperators,
            row => RowQueryValue<TEnum>.Present(accessor(row)),
            BindEnum<TEnum>,
            direction => RowQueryValueOrder.Create(
                Comparer<TEnum>.Default,
                direction,
                missingLast: false));

    private static RowQueryKey<TRow> NullableEnumKey<TRow, TEnum>(
        string key,
        Func<TRow, TEnum?> accessor)
        where TEnum : struct, Enum =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            EqualityOperators,
            row => accessor(row) is { } value
                ? RowQueryValue<TEnum>.Present(value)
                : RowQueryValue<TEnum>.Missing,
            BindEnum<TEnum>,
            direction => RowQueryValueOrder.Create(
                Comparer<TEnum>.Default,
                direction,
                missingLast: true));

    private static Predicate<TEnum>? BindEnum<TEnum>(
        RowQueryOperator operation,
        RowQueryValueToken token)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse(
                token.Text,
                ignoreCase: true,
                out TEnum expected)
            || !Enum.IsDefined(expected))
        {
            return null;
        }

        return operation switch
        {
            RowQueryOperator.Equals =>
                value => EqualityComparer<TEnum>.Default.Equals(
                    value,
                    expected),
            RowQueryOperator.NotEquals =>
                value => !EqualityComparer<TEnum>.Default.Equals(
                    value,
                    expected),
            _ => null,
        };
    }

    private static RowQueryKey<TRow> OrdinalTextKey<TRow>(
        string key,
        Func<TRow, string> accessor) =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            EqualityOperators,
            row => RowQueryValue<string>.Present(accessor(row)),
            BindOrdinalText,
            direction => RowQueryValueOrder.Create(
                (IComparer<string>)StringComparer.Ordinal,
                direction,
                missingLast: false));

    private static Predicate<string>? BindOrdinalText(
        RowQueryOperator operation,
        RowQueryValueToken token) =>
        operation switch
        {
            RowQueryOperator.Equals =>
                value => string.Equals(
                    value,
                    token.Text,
                    StringComparison.Ordinal),
            RowQueryOperator.NotEquals =>
                value => !string.Equals(
                    value,
                    token.Text,
                    StringComparison.Ordinal),
            _ => null,
        };

    private static IComparer<T> Directional<T>(
        IComparer<T> ascending,
        RowQueryOrderDirection direction) =>
        direction == RowQueryOrderDirection.Ascending
            ? ascending
            : Comparer<T>.Create(
                (left, right) => ascending.Compare(right, left));
}
