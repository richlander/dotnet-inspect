using System.Globalization;

using ILInspector.Metadata;
using ILInspector.Research;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public static partial class LibraryNameFamilyQuery
{
    public const string FamilyKindKey = "kind";
    public const string FamilyWordKey = "word";
    public const string FamilyTypeCountKey = "type-count";
    public const string FamilyPublicTypeCountKey = "public-type-count";
    public const string FamilyNamespaceCountKey = "namespace-count";
    public const string FamilyPrevalenceOrder = "prevalence";

    public const string TypeSimpleNameKey = "simple-name";
    public const string TypeNamespaceKey = "namespace";
    public const string TypeDefinitionTokenKey = "definition-token";
    public const string TypeDefinitionKindKey = "definition-kind";
    public const string TypePublicSurfaceKey = "public-surface";
    public const string TypeFamilyKindKey = "family-kind";
    public const string TypeFamilyWordKey = "family-word";
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

    private static readonly RowQueryNamedOrder<LibraryNameFamilyRow>
        FamilyPrevalenceOrderDefinition =
            new(
                RowQueryNamedOrderIdentity.Create(),
                FamilyPrevalenceOrder,
                RowQueryOrderPurpose.Ranking,
                direction => Directional(
                    LibraryNameFamilyOrder.Prevalence,
                    direction));

    private static readonly RowQueryNamedOrder<LibraryNameFamilyTypeRow>
        TypeDefinitionOrderDefinition =
            new(
                RowQueryNamedOrderIdentity.Create(),
                TypeDefinitionOrder,
                RowQueryOrderPurpose.Sequence,
                direction => Directional(
                    Comparer<LibraryNameFamilyTypeRow>.Create(
                        static (left, right) =>
                            left.Type.Definition.Value.CompareTo(
                                right.Type.Definition.Value)),
                    direction));

    private static readonly RowQueryVocabulary<LibraryNameFamilyRow>
        FamilyVocabulary =
            RowQueryVocabulary<LibraryNameFamilyRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [
                    FamilyKindQueryKey(),
                    FamilyWordQueryKey(),
                    NumericKey<LibraryNameFamilyRow>(
                        FamilyTypeCountKey,
                        static row => row.TypeCount),
                    NumericKey<LibraryNameFamilyRow>(
                        FamilyPublicTypeCountKey,
                        static row => row.PublicTypeCount),
                    NumericKey<LibraryNameFamilyRow>(
                        FamilyNamespaceCountKey,
                        static row => row.DistinctNamespaceCount),
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

    private static readonly RowQueryVocabulary<LibraryNameFamilyTypeRow>
        TypeVocabulary =
            RowQueryVocabulary<LibraryNameFamilyTypeRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [
                    OrdinalTextKey<LibraryNameFamilyTypeRow>(
                        TypeSimpleNameKey,
                        static row => row.MetadataSimpleName),
                    OrdinalTextKey<LibraryNameFamilyTypeRow>(
                        TypeNamespaceKey,
                        static row => row.Name.Namespace),
                    NumericKey<LibraryNameFamilyTypeRow>(
                        TypeDefinitionTokenKey,
                        static row => row.Type.Definition.Value),
                    TypeDefinitionKindQueryKey(),
                    TypePublicSurfaceQueryKey(),
                    TypeFamilyKindQueryKey(),
                    TypeFamilyWordQueryKey(),
                ],
                [TypeDefinitionOrderDefinition],
                defaultBaselineOrder:
                    new(
                        TypeDefinitionOrderDefinition,
                        RowQueryOrderDirection.Ascending));

    public static QuerySpaceRowScopeBinding<LibraryNameFamilyRow>
        FamilyRowsScope
    { get; } =
        new(
            new QuerySpaceRowScopeDescriptor(
                FamilyRowsScopeIdentity,
                FamilyRowsResultContract,
                [FamilyRowsRowSet],
                [
                    Facet(
                        "library-name-families.family.kind",
                        FamilyKindKey,
                        EqualityOperators,
                        "name-family kind",
                        "family kind",
                        ["one-word", "two-word"],
                        "Matches the exact one- or two-word suffix kind.",
                        supportsOrdering: true),
                    Facet(
                        "library-name-families.family.word",
                        FamilyWordKey,
                        EqualityOperators,
                        "ordinal identifier word",
                        "family word",
                        [],
                        "Matches any exact owner-issued suffix word.",
                        supportsOrdering: false),
                    NumericFacet(
                        "library-name-families.family.type-count",
                        FamilyTypeCountKey,
                        "Type count",
                        "Matches the exact supporting Type count."),
                    NumericFacet(
                        "library-name-families.family.public-type-count",
                        FamilyPublicTypeCountKey,
                        "public Type count",
                        "Matches the exact public-surface Type count."),
                    NumericFacet(
                        "library-name-families.family.namespace-count",
                        FamilyNamespaceCountKey,
                        "namespace count",
                        "Matches the exact distinct namespace count."),
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

    public static QuerySpaceRowScopeBinding<LibraryNameFamilyTypeRow>
        TypeRowsScope
    { get; } =
        new(
            new QuerySpaceRowScopeDescriptor(
                TypeRowsScopeIdentity,
                TypeRowsResultContract,
                [TypeRowsRowSet],
                [
                    Facet(
                        "library-name-families.type.simple-name",
                        TypeSimpleNameKey,
                        EqualityOperators,
                        "ordinal metadata simple name",
                        "simple name",
                        [],
                        "Matches the exact innermost metadata name.",
                        supportsOrdering: true),
                    Facet(
                        "library-name-families.type.namespace",
                        TypeNamespaceKey,
                        EqualityOperators,
                        "ordinal metadata namespace",
                        "namespace",
                        [],
                        "Matches the exact metadata namespace.",
                        supportsOrdering: true),
                    NumericFacet(
                        "library-name-families.type.definition-token",
                        TypeDefinitionTokenKey,
                        "TypeDef token",
                        "Matches the exact positive TypeDef token."),
                    Facet(
                        "library-name-families.type.definition-kind",
                        TypeDefinitionKindKey,
                        EqualityOperators,
                        "metadata Type kind",
                        "definition kind",
                        Enum.GetNames<AssemblyTypeDefinitionKind>(),
                        "Matches the Metadata-owned Type definition kind.",
                        supportsOrdering: true),
                    Facet(
                        "library-name-families.type.public-surface",
                        TypePublicSurfaceKey,
                        EqualityOperators,
                        "boolean",
                        "public surface",
                        ["true", "false"],
                        "Matches public-surface inclusion.",
                        supportsOrdering: true),
                    Facet(
                        "library-name-families.type.family-kind",
                        TypeFamilyKindKey,
                        EqualityOperators,
                        "name-family kind",
                        "family kind",
                        ["one-word", "two-word"],
                        "Matches an assigned family of the requested kind.",
                        supportsOrdering: false),
                    Facet(
                        "library-name-families.type.family-word",
                        TypeFamilyWordKey,
                        EqualityOperators,
                        "ordinal identifier word",
                        "family word",
                        [],
                        "Matches any word in an assigned suffix family.",
                        supportsOrdering: false),
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
                    "library-name-families/rows/v1"),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    "library-name-families/count/v1"),
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

    private static RowQueryKey<LibraryNameFamilyRow>
        FamilyKindQueryKey() =>
        RowQueryKey<LibraryNameFamilyRow>.Create(
            RowQueryKeyIdentity.Create(),
            FamilyKindKey,
            EqualityOperators,
            static row =>
                RowQueryValue<LibraryNameFamilyKind>.Present(
                    row.Identity.Kind),
            BindFamilyKind,
            direction => RowQueryValueOrder.Create(
                Comparer<LibraryNameFamilyKind>.Default,
                direction,
                missingLast: false));

    private static RowQueryKey<LibraryNameFamilyRow>
        FamilyWordQueryKey() =>
        RowQueryKey<LibraryNameFamilyRow>.Create(
            RowQueryKeyIdentity.Create(),
            FamilyWordKey,
            EqualityOperators,
            static row =>
                RowQueryValue<LibraryNameFamilyRow>.Present(row),
            static (operation, token) =>
                BindFamilyWord<LibraryNameFamilyRow>(
                    operation,
                    token,
                    static row => [row.Identity]));

    private static RowQueryKey<LibraryNameFamilyTypeRow>
        TypeDefinitionKindQueryKey() =>
        RowQueryKey<LibraryNameFamilyTypeRow>.Create(
            RowQueryKeyIdentity.Create(),
            TypeDefinitionKindKey,
            EqualityOperators,
            static row =>
                RowQueryValue<AssemblyTypeDefinitionKind>.Present(
                    row.DefinitionKind),
            BindEnum<AssemblyTypeDefinitionKind>,
            direction => RowQueryValueOrder.Create(
                Comparer<AssemblyTypeDefinitionKind>.Default,
                direction,
                missingLast: false));

    private static RowQueryKey<LibraryNameFamilyTypeRow>
        TypePublicSurfaceQueryKey() =>
        RowQueryKey<LibraryNameFamilyTypeRow>.Create(
            RowQueryKeyIdentity.Create(),
            TypePublicSurfaceKey,
            EqualityOperators,
            static row =>
                RowQueryValue<bool>.Present(row.IsPublicSurface),
            BindBoolean,
            direction => RowQueryValueOrder.Create(
                Comparer<bool>.Default,
                direction,
                missingLast: false));

    private static RowQueryKey<LibraryNameFamilyTypeRow>
        TypeFamilyKindQueryKey() =>
        RowQueryKey<LibraryNameFamilyTypeRow>.Create(
            RowQueryKeyIdentity.Create(),
            TypeFamilyKindKey,
            EqualityOperators,
            static row =>
                RowQueryValue<LibraryNameFamilyTypeRow>.Present(row),
            static (operation, token) =>
            {
                LibraryNameFamilyKind? expected =
                    ParseFamilyKind(token);
                if (expected is null)
                    return null;

                bool HasKind(LibraryNameFamilyTypeRow row) =>
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

    private static RowQueryKey<LibraryNameFamilyTypeRow>
        TypeFamilyWordQueryKey() =>
        RowQueryKey<LibraryNameFamilyTypeRow>.Create(
            RowQueryKeyIdentity.Create(),
            TypeFamilyWordKey,
            EqualityOperators,
            static row =>
                RowQueryValue<LibraryNameFamilyTypeRow>.Present(row),
            static (operation, token) =>
                BindFamilyWord<LibraryNameFamilyTypeRow>(
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

    private static Predicate<LibraryNameFamilyKind>? BindFamilyKind(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        LibraryNameFamilyKind? expected = ParseFamilyKind(token);
        if (expected is null)
            return null;

        return operation switch
        {
            RowQueryOperator.Equals =>
                value => value == expected,
            RowQueryOperator.NotEquals =>
                value => value != expected,
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

    private static Predicate<bool>? BindBoolean(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!bool.TryParse(token.Text, out bool expected))
            return null;
        return operation switch
        {
            RowQueryOperator.Equals =>
                value => value == expected,
            RowQueryOperator.NotEquals =>
                value => value != expected,
            _ => null,
        };
    }

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
