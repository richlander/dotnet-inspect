using ILInspector.Metadata;
using InertText;
using InertText.Encoding;

namespace DotnetInspector.Sections;

public enum LibraryTypeHierarchyTopology
{
    LibraryNamespacesAndTypes,
}

public abstract record LibraryTypeHierarchyNode
{
    private LibraryTypeHierarchyNode()
    {
    }

    /// <summary>
    /// One namespace of the active Library Type population. An empty name is
    /// the global namespace.
    /// </summary>
    public sealed record Namespace(
        InertString Name,
        int DeclarationCount)
        : LibraryTypeHierarchyNode;

    /// <summary>
    /// One owner-issued Type declaration row and its requested spelling.
    /// </summary>
    public sealed record Type(
        LibraryTypeShape Value,
        InertString Spelling)
        : LibraryTypeHierarchyNode;
}

/// <summary>
/// Projects one complete Library Type declaration population as
/// <c>Library -> Namespace -> Type -> Member Count</c>.
/// </summary>
public static class LibraryTypeHierarchyProjection
{
    public static void Write(
        LibraryDocument document,
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology> request,
        IInspectionHierarchySink<LibraryTypeHierarchyNode> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        NamespaceGroup[] groups = GetValidatedGroups(document, request);
        for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            NamespaceGroup group = groups[groupIndex];
            sink.WriteNode(
                group.Node,
                isLastSibling: groupIndex == groups.Length - 1,
                children =>
                {
                    for (int typeIndex = 0;
                         typeIndex < group.Declarations.Length;
                         typeIndex++)
                    {
                        children.WriteNode(
                            group.Declarations[typeIndex],
                            isLastSibling:
                                typeIndex
                                == group.Declarations.Length - 1);
                    }
                });
        }
    }

    public static void ValidateDocument(
        LibraryDocument document,
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology> request,
        LibraryTypePopulationRequest types)
    {
        _ = GetValidatedGroups(document, request);
        ArgumentNullException.ThrowIfNull(types);
        ValidateRequest(request, types, nameof(types));
        if (!Matches(document.Types!.Binding, types))
        {
            throw new InvalidOperationException(
                "The Library Type hierarchy document population binding does not match the selected presentation plan.");
        }
    }

    public static void ValidateRequest(
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology>
            request) =>
        ValidateRequestShape(request, nameof(request));

    public static void ValidateRequest(
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology> request,
        LibraryTypePopulationRequest? types,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        ValidateRequestShape(request, parameterName);
        if (types?.Rows is not { } rows)
        {
            throw new ArgumentException(
                "A Library Type hierarchy requires a Type declaration Rows request.",
                parameterName);
        }
        if (rows.MemberCount is null)
        {
            throw new ArgumentException(
                "A Library Type hierarchy requires a Member Count for every Type definition Row.",
                parameterName);
        }
        if (rows.Continuation is not null)
        {
            throw new ArgumentException(
                "A Library Type hierarchy does not admit continued Type declaration Rows.",
                parameterName);
        }
        if (types.DeclarationSelection
                is not LibraryTypeDeclarationSelection
                    .DefinitionsAndForwarders
            || types.DefinitionKinds != ApiTypeInventoryKinds.All
            || types.Namespace is not null)
        {
            throw new ArgumentException(
                "A Library Type hierarchy requires the unfaceted Type declaration population.",
                parameterName);
        }
    }

    private static LibraryTypePopulationRowsOutcome.Read GetValidatedRows(
        LibraryDocument document,
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology> request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        LibraryTypePopulationRowsOutcome.Read rows =
            document.Types?.Rows is LibraryTypePopulationRowsOutcome.Read read
                ? read
                : throw new InvalidOperationException(
                    "The Library Type declaration Rows are unavailable.");
        LibraryTypePopulationBinding binding = document.Types.Binding;
        if (binding.DeclarationSelection
                is not LibraryTypeDeclarationSelection
                    .DefinitionsAndForwarders
            || binding.DefinitionKinds != ApiTypeInventoryKinds.All
            || binding.Namespace is not null)
        {
            throw new InvalidOperationException(
                "A Library Type hierarchy requires the unfaceted Type declaration population.");
        }
        if (rows.Continuation is not null)
        {
            throw new InvalidOperationException(
                "A Library Type hierarchy requires complete Type declaration Rows.");
        }
        foreach (LibraryTypeShape row in rows.Items)
        {
            switch (row.MemberCount)
            {
                case LibraryTypeMemberCountOutcome.Counted
                    when row.DeclarationKind
                        is LibraryTypeDeclarationKind.Definition:
                case LibraryTypeMemberCountOutcome.NotApplicable
                    when row.DeclarationKind
                        is LibraryTypeDeclarationKind.Forwarder:
                    break;
                default:
                    throw new InvalidOperationException(
                        "A Library Type hierarchy requires a Member Count outcome for every Type declaration Row.");
            }
        }

        return rows;
    }

    private static bool Matches(
        LibraryTypePopulationBinding binding,
        LibraryTypePopulationRequest types) =>
        types.Rows is { } rows
        && binding.Accessibility == types.Accessibility
        && binding.DeclarationSelection == types.DeclarationSelection
        && binding.DefinitionKinds == types.DefinitionKinds
        && binding.Namespace == types.Namespace
        && binding.NamespaceMatch == types.NamespaceMatch
        && rows.Ordering == LibraryTypePopulationOrdering.Metadata;

    private static void ValidateRequestShape(
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology> request,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Topology
            is not LibraryTypeHierarchyTopology.LibraryNamespacesAndTypes)
        {
            throw new ArgumentException(
                "The Library document does not admit the requested hierarchy topology.",
                parameterName);
        }
        if (request.Children
            is not InspectionHierarchyPopulationRequest.Rows
            {
                Spelling:
                    InspectionHierarchyNodeSpelling.Name,
                Children:
                    InspectionHierarchyPopulationRequest.Rows
                    {
                        Children:
                            InspectionHierarchyPopulationRequest.Count,
                    },
            })
        {
            throw new ArgumentException(
                "The Library Type hierarchy requires namespace Rows by Name, Type declaration Rows, and Member Count.",
                parameterName);
        }
    }

    /// <summary>
    /// Validates the document and forms every node before any is written.
    /// The hierarchy groups the complete Metadata-ordered population by
    /// namespace and orders namespaces, then declarations within each
    /// namespace, by ordinal spelling; Rows ordering itself remains Metadata
    /// order.
    /// </summary>
    private static NamespaceGroup[] GetValidatedGroups(
        LibraryDocument document,
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology> request)
    {
        LibraryTypePopulationRowsOutcome.Read rows =
            GetValidatedRows(document, request);
        InspectionHierarchyNodeSpelling spelling =
            ((InspectionHierarchyPopulationRequest.Rows)
                ((InspectionHierarchyPopulationRequest.Rows)request.Children)
                    .Children!)
            .Spelling;

        return
        [
            .. rows.Items
                .GroupBy(
                    static row => row.Namespace.ToString(),
                    StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(
                    group =>
                    {
                        LibraryTypeHierarchyNode.Type[] declarations =
                        [
                            .. group
                                .OrderBy(
                                    static row => row.DisplayName.ToString(),
                                    StringComparer.Ordinal)
                                .Select(
                                    row => new LibraryTypeHierarchyNode.Type(
                                        row,
                                        Spell(row, spelling))),
                        ];
                        return new NamespaceGroup(
                            new LibraryTypeHierarchyNode.Namespace(
                                group.First().Namespace,
                                declarations.Length),
                            declarations);
                    }),
        ];
    }

    /// <summary>
    /// Issues one declaration's spelling. Name removes the namespace qualifier
    /// from the raw qualified name and re-encodes only the remainder, because
    /// the namespace and the qualified name are encoded as independent values.
    /// </summary>
    private static InertString Spell(
        LibraryTypeShape declaration,
        InspectionHierarchyNodeSpelling spelling)
    {
        if (spelling is InspectionHierarchyNodeSpelling.FullSpelling
            || declaration.Namespace.IsEmpty)
        {
            return declaration.DisplayName;
        }

        string displayName = Decode(declaration.DisplayName);
        string @namespace = Decode(declaration.Namespace);
        if (displayName.Length <= @namespace.Length + 1
            || !displayName.StartsWith(@namespace, StringComparison.Ordinal)
            || displayName[@namespace.Length] != '.')
        {
            throw new InvalidOperationException(
                "A Library Type declaration display name is not qualified by its namespace.");
        }

        return new InertString(
            TextPolicy.Field,
            displayName.AsSpan(@namespace.Length + 1));
    }

    private static string Decode(InertString value) =>
        VisualEncoder.TryDecode(value.ToString(), out string? decoded)
            ? decoded
            : throw new InvalidOperationException(
                "A Library Type declaration spelling is not well-formed encoded text.");

    private sealed record NamespaceGroup(
        LibraryTypeHierarchyNode.Namespace Node,
        LibraryTypeHierarchyNode.Type[] Declarations);
}
