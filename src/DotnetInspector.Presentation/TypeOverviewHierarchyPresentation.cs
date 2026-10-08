using System.Text;

using CSharpText;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Presentation;

public enum TypeOverviewHierarchyPresentationFormat
{
    Tree,
    Mermaid,
}

public sealed record TypeOverviewHierarchyPresentationPlan
{
    internal const int MaximumMemberGroupRows = 4096;

    public TypeOverviewHierarchyPresentationPlan(
        TypeOverviewHierarchyPresentationFormat format,
        TypeMemberGroupPopulationRequest members,
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology>
            hierarchy)
    {
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));

        Format = format;
        Members = members
            ?? throw new ArgumentNullException(nameof(members));
        Hierarchy = hierarchy
            ?? throw new ArgumentNullException(nameof(hierarchy));
        TypeOverviewHierarchyProjection.ValidateRequest(
            Hierarchy,
            Members,
            nameof(hierarchy));
    }

    public TypeOverviewHierarchyPresentationFormat Format { get; }
    public TypeMemberGroupPopulationRequest Members { get; }
    public InspectionHierarchyRequest<TypeOverviewHierarchyTopology>
        Hierarchy { get; }
}

public static class TypeOverviewHierarchyPresentation
{
    public static TypeOverviewDocumentInspectionPlan CreateInspectionPlan(
        MetadataTypeDefinitionName type,
        TypeOverviewHierarchyPresentationPlan presentation,
        ApiSurfaceExtractionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(bounds);
        return new(
            type,
            presentation.Members.Rows!,
            bounds,
            presentation.Members.Spelling,
            presentation.Members.Accessibility,
            presentation.Members.Receiver,
            presentation.Members.IncludeHidden,
            presentation.Hierarchy);
    }

    /// <summary>
    /// Creates the default Type hierarchy plan: the Type, its member
    /// categories, and each category's MemberGroups by Name as leaves. The
    /// default requests no exact-Member Counts; overloads belong to the inner
    /// <c>member</c> command.
    /// </summary>
    public static TypeOverviewHierarchyPresentationPlan CreateCompactPlan(
        TypeOverviewHierarchyPresentationFormat format,
        bool includeNonPublic)
    {
        TypeMemberGroupAccessibilityFilter accessibility =
            includeNonPublic
                ? TypeMemberGroupAccessibilityFilter.All
                : TypeMemberGroupAccessibilityFilter.Public;
        var declarations =
            new TypeMemberGroupPopulationRequest(
                count: null,
                rows: new TypeMemberGroupRowsRequest(
                    TypeOverviewHierarchyPresentationPlan
                        .MaximumMemberGroupRows,
                    includeExactMemberCount: false),
                spelling: TypeMemberGroupSpelling.CSharp,
                accessibility: accessibility,
                includeHidden: includeNonPublic);
        var hierarchy =
            new InspectionHierarchyRequest<
                TypeOverviewHierarchyTopology>(
                TypeOverviewHierarchyTopology
                    .TypeCategoriesAndMemberGroups,
                InspectionHierarchyNodeSpelling.FullSpelling,
                new InspectionHierarchyPopulationRequest.Rows(
                    InspectionHierarchyNodeSpelling.Name,
                    new InspectionHierarchyPopulationRequest.Rows(
                        InspectionHierarchyNodeSpelling.Name)));
        return new(format, declarations, hierarchy);
    }

    public static void Write(
        TypeOverviewDocument document,
        TypeOverviewHierarchyPresentationPlan plan,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(output);
        TypeOverviewHierarchyProjection.ValidateDocument(
            document,
            plan.Hierarchy,
            plan.Members);

        string root =
            FormatTypeDeclaration(
                document.Subject,
                plan.Hierarchy.RootSpelling);
        switch (plan.Format)
        {
            case TypeOverviewHierarchyPresentationFormat.Tree:
                WriteTree(document, plan.Hierarchy, root, output);
                break;
            case TypeOverviewHierarchyPresentationFormat.Mermaid:
                WriteMermaid(
                    document,
                    plan.Hierarchy,
                    root,
                    output);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Type hierarchy presentation format.");
        }
    }

    private static void WriteTree(
        TypeOverviewDocument document,
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology>
            hierarchy,
        string root,
        TextWriter output)
    {
        output.WriteLine(root);
        var writer =
            new MarkoutWriter(
                output,
                new MarkdownFormatter());
        writer.WriteTree(tree =>
        {
            var sink =
                new MarkoutHierarchySink<TypeOverviewHierarchyNode>(
                    tree,
                    FormatNode);
            TypeOverviewHierarchyProjection.Write(
                document,
                hierarchy,
                sink);
        });
        writer.Flush();
    }

    private static void WriteMermaid(
        TypeOverviewDocument document,
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology>
            hierarchy,
        string root,
        TextWriter output)
    {
        output.WriteLine("graph TD");
        output.Write("  n0[\"");
        output.Write(MermaidHierarchySink<TypeOverviewHierarchyNode>
            .Escape(root));
        output.WriteLine("\"]");
        var sink =
            new MermaidHierarchySink<TypeOverviewHierarchyNode>(
                output,
                rootNodeId: 0,
                FormatNode);
        TypeOverviewHierarchyProjection.Write(
            document,
            hierarchy,
            sink);
    }

    private static string FormatNode(
        TypeOverviewHierarchyNode node) =>
        node switch
        {
            TypeOverviewHierarchyNode.Category category =>
                FormatCategory(category),
            TypeOverviewHierarchyNode.Member member =>
                FormatMember(member.Value),
            _ => throw new InvalidOperationException(
                "Unknown Type overview hierarchy node."),
        };

    private static string FormatCategory(
        TypeOverviewHierarchyNode.Category category)
    {
        string noun = category.Value switch
        {
            MemberGroupCategory.Constructor => "Constructors",
            MemberGroupCategory.Finalizer => "Finalizers",
            MemberGroupCategory.Field => "Fields",
            MemberGroupCategory.Property => "Properties",
            MemberGroupCategory.Method => "Methods",
            MemberGroupCategory.Operator => "Operators",
            MemberGroupCategory.Event => "Events",
            MemberGroupCategory.ExplicitInterfaceImplementation =>
                "Explicit Interface Implementations",
            _ => throw new InvalidOperationException(
                "Unknown member-group category."),
        };

        return IsOverloadGrouped(category.Value)
            && category.ExactMemberCount is { } exactMemberCount
            && category.LogicalCount != exactMemberCount
                ? $"{noun} ({category.LogicalCount} logical, "
                    + $"{exactMemberCount} overloads)"
                : $"{noun} ({category.LogicalCount})";
    }

    private static string FormatMember(TypeMemberGroupShape member)
    {
        string name =
            CSharpIdentifier.ContainRenderedText(
                member.Binding.Name.ToString());
        if (!IsOverloadGrouped(member.Binding.Category)
            || member.ExactMemberCount is not { } exactMemberCount)
        {
            return name;
        }

        return exactMemberCount > 1
            ? $"{name} ({exactMemberCount} overloads)"
            : name;
    }

    private static bool IsOverloadGrouped(
        MemberGroupCategory category) =>
        category
            is MemberGroupCategory.Constructor
            or MemberGroupCategory.Method
            or MemberGroupCategory.Operator
            or MemberGroupCategory.ExplicitInterfaceImplementation;

    private static string FormatTypeDeclaration(
        TypeSubject subject,
        InspectionHierarchyNodeSpelling spelling)
    {
        var fullName = new StringBuilder();
        int firstSegment =
            spelling is InspectionHierarchyNodeSpelling.Name
                ? subject.Type.Segments.Length - 1
                : 0;
        if (spelling
                is InspectionHierarchyNodeSpelling.FullSpelling
            && !string.IsNullOrEmpty(subject.Type.Namespace))
        {
            fullName.Append(subject.Type.Namespace);
            fullName.Append('.');
        }
        for (int segmentIndex = firstSegment;
             segmentIndex < subject.Type.Segments.Length;
             segmentIndex++)
        {
            if (segmentIndex > firstSegment)
                fullName.Append('.');

            fullName.Append(
                MetadataNameArity.StripFromSegment(
                    subject.Type.Segments[segmentIndex]));
            TypeDocumentGenericParameter[] parameters =
            [
                .. subject.Signature.GenericParameters
                    .Where(
                        parameter =>
                            parameter.DefinitionSegmentIndex
                                == segmentIndex)
                    .OrderBy(
                        static parameter =>
                            parameter.MetadataIndex),
            ];
            if (parameters.Length > 0)
            {
                fullName.Append('<');
                fullName.AppendJoin(
                    ", ",
                    parameters.Select(
                        static parameter =>
                            parameter.Name.ToString()));
                fullName.Append('>');
            }
        }

        var declaration = new StringBuilder();
        if (subject.Attributes.HasFlag(
                System.Reflection.TypeAttributes.Abstract)
            && subject.Attributes.HasFlag(
                System.Reflection.TypeAttributes.Sealed)
            && subject.Category
                == MetadataTypeDeclarationCategory.Class)
        {
            declaration.Append("static ");
        }
        else
        {
            if (subject.Attributes.HasFlag(
                    System.Reflection.TypeAttributes.Abstract)
                && subject.Category
                    == MetadataTypeDeclarationCategory.Class)
            {
                declaration.Append("abstract ");
            }
            if (subject.Attributes.HasFlag(
                    System.Reflection.TypeAttributes.Sealed)
                && subject.Category
                    == MetadataTypeDeclarationCategory.Class)
            {
                declaration.Append("sealed ");
            }
        }

        declaration.Append(
            subject.Category switch
            {
                MetadataTypeDeclarationCategory.Class => "class",
                MetadataTypeDeclarationCategory.Struct =>
                    (subject.IsReadOnly, subject.IsByRefLike)
                    switch
                    {
                        (true, true) => "readonly ref struct",
                        (true, false) => "readonly struct",
                        (false, true) => "ref struct",
                        _ => "struct",
                    },
                MetadataTypeDeclarationCategory.Interface =>
                    "interface",
                MetadataTypeDeclarationCategory.Enum => "enum",
                MetadataTypeDeclarationCategory.Delegate => "delegate",
                _ => "type",
            });
        declaration.Append(' ');
        declaration.Append(fullName);
        return CSharpIdentifier.ContainRenderedText(
            declaration.ToString());
    }
}
