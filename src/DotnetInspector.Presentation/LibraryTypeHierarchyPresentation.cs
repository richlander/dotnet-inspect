using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Presentation;

public enum LibraryTypeHierarchyPresentationFormat
{
    Tree,
    Mermaid,
}

public sealed record LibraryTypeHierarchyPresentationPlan
{
    internal const int MaximumTypeRows = 16_384;

    public LibraryTypeHierarchyPresentationPlan(
        LibraryTypeHierarchyPresentationFormat format,
        LibraryTypePopulationRequest types,
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology>
            hierarchy)
    {
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));

        Format = format;
        Types = types
            ?? throw new ArgumentNullException(nameof(types));
        Hierarchy = hierarchy
            ?? throw new ArgumentNullException(nameof(hierarchy));
        LibraryTypeHierarchyProjection.ValidateRequest(
            Hierarchy,
            Types,
            nameof(hierarchy));
    }

    public LibraryTypeHierarchyPresentationFormat Format { get; }
    public LibraryTypePopulationRequest Types { get; }
    public InspectionHierarchyRequest<LibraryTypeHierarchyTopology>
        Hierarchy { get; }
}

public static class LibraryTypeHierarchyPresentation
{
    /// <summary>
    /// Creates the default Library hierarchy plan: the Library, its
    /// namespaces, each namespace's public-surface Type declarations by Name,
    /// and each definition's Member Count.
    /// </summary>
    public static LibraryTypeHierarchyPresentationPlan CreateDefaultPlan(
        LibraryTypeHierarchyPresentationFormat format)
    {
        var types =
            new LibraryTypePopulationRequest(
                LibraryTypeAccessibility.Public,
                count: null,
                rows: new LibraryTypePopulationRowsRequest(
                    LibraryTypeHierarchyPresentationPlan.MaximumTypeRows,
                    memberCount: new LibraryTypeMemberCountRequest()));
        var hierarchy =
            new InspectionHierarchyRequest<LibraryTypeHierarchyTopology>(
                LibraryTypeHierarchyTopology.LibraryNamespacesAndTypes,
                InspectionHierarchyNodeSpelling.FullSpelling,
                new InspectionHierarchyPopulationRequest.Rows(
                    InspectionHierarchyNodeSpelling.Name,
                    new InspectionHierarchyPopulationRequest.Rows(
                        InspectionHierarchyNodeSpelling.Name,
                        new InspectionHierarchyPopulationRequest
                            .Count())));
        return new(format, types, hierarchy);
    }

    public static void Write(
        LibraryDocument document,
        LibraryTypeHierarchyPresentationPlan plan,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(output);
        LibraryTypeHierarchyProjection.ValidateDocument(
            document,
            plan.Hierarchy,
            plan.Types);

        string root =
            FormatLibrary(
                document.Assembly,
                plan.Hierarchy.RootSpelling);
        switch (plan.Format)
        {
            case LibraryTypeHierarchyPresentationFormat.Tree:
                WriteTree(document, plan.Hierarchy, root, output);
                break;
            case LibraryTypeHierarchyPresentationFormat.Mermaid:
                WriteMermaid(document, plan.Hierarchy, root, output);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Library hierarchy presentation format.");
        }
    }

    private static void WriteTree(
        LibraryDocument document,
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology>
            hierarchy,
        string root,
        TextWriter output)
    {
        output.WriteLine(root);
        var writer =
            new MarkoutWriter(
                output,
                new MarkdownFormatter());
        var sink =
            new MarkoutHierarchySink<LibraryTypeHierarchyNode>(
                writer,
                FormatNode);
        LibraryTypeHierarchyProjection.Write(
            document,
            hierarchy,
            sink);
        writer.Flush();
    }

    private static void WriteMermaid(
        LibraryDocument document,
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology>
            hierarchy,
        string root,
        TextWriter output)
    {
        output.WriteLine("graph TD");
        output.Write("  n0[\"");
        output.Write(MermaidHierarchySink<LibraryTypeHierarchyNode>
            .Escape(root));
        output.WriteLine("\"]");
        var sink =
            new MermaidHierarchySink<LibraryTypeHierarchyNode>(
                output,
                rootNodeId: 0,
                FormatNode);
        LibraryTypeHierarchyProjection.Write(
            document,
            hierarchy,
            sink);
    }

    private static string FormatNode(LibraryTypeHierarchyNode node) =>
        node switch
        {
            LibraryTypeHierarchyNode.Namespace @namespace =>
                FormatNamespace(@namespace),
            LibraryTypeHierarchyNode.Type type =>
                FormatType(type),
            _ => throw new InvalidOperationException(
                "Unknown Library hierarchy node."),
        };

    private static string FormatNamespace(
        LibraryTypeHierarchyNode.Namespace @namespace)
    {
        string name =
            @namespace.Name.IsEmpty
                ? "(global namespace)"
                : @namespace.Name.ToString();
        return @namespace.DeclarationCount == 1
            ? $"{name} (1 type)"
            : $"{name} ({@namespace.DeclarationCount} types)";
    }

    private static string FormatType(LibraryTypeHierarchyNode.Type type)
    {
        LibraryTypeShape declaration = type.Value;
        string spelling = type.Spelling.ToString();
        switch (declaration.MemberCount)
        {
            case LibraryTypeMemberCountOutcome.NotApplicable:
                return $"{spelling} (forwarded)";
            case LibraryTypeMemberCountOutcome.Counted counted:
                string kind = declaration.DefinitionKind switch
                {
                    ApiTypeInventoryKind.Class => "class",
                    ApiTypeInventoryKind.Struct => "struct",
                    ApiTypeInventoryKind.Interface => "interface",
                    ApiTypeInventoryKind.Enum => "enum",
                    ApiTypeInventoryKind.Delegate => "delegate",
                    _ => throw new InvalidOperationException(
                        "A Library hierarchy Type definition is missing its kind."),
                };
                return counted.Value == 1
                    ? $"{kind} {spelling} (1 member)"
                    : $"{kind} {spelling} ({counted.Value} members)";
            default:
                throw new InvalidOperationException(
                    "A Library hierarchy Type is missing its Member Count outcome.");
        }
    }

    private static string FormatLibrary(
        LibraryAssemblyIdentity assembly,
        InspectionHierarchyNodeSpelling spelling) =>
        spelling is InspectionHierarchyNodeSpelling.FullSpelling
            ? $"{assembly.Name} {assembly.Version}"
            : assembly.Name.ToString();
}
