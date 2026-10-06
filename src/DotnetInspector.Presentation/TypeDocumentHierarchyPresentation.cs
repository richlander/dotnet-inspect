using System.Text;

using CSharpText;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Presentation;

public enum TypeDocumentHierarchyPresentationFormat
{
    Tree,
    Mermaid,
}

public sealed record TypeDocumentHierarchyPresentationPlan
{
    internal const int MaximumMemberGroupRows = 4096;

    public TypeDocumentHierarchyPresentationPlan(
        TypeDocumentHierarchyPresentationFormat format,
        TypeMemberGroupPopulationRequest declarations,
        InspectionHierarchyRequest<TypeDocumentHierarchyTopology>
            hierarchy)
    {
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));

        Format = format;
        Declarations = declarations
            ?? throw new ArgumentNullException(nameof(declarations));
        Hierarchy = hierarchy
            ?? throw new ArgumentNullException(nameof(hierarchy));
        TypeDocumentHierarchyProjection.ValidateRequest(
            Hierarchy,
            Declarations,
            nameof(hierarchy));
    }

    public TypeDocumentHierarchyPresentationFormat Format { get; }
    public TypeMemberGroupPopulationRequest Declarations { get; }
    public InspectionHierarchyRequest<TypeDocumentHierarchyTopology>
        Hierarchy { get; }
}

public static class TypeDocumentHierarchyPresentation
{
    public static TypeDocumentHierarchyPresentationPlan CreateCompactPlan(
        TypeDocumentHierarchyPresentationFormat format,
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
                    TypeDocumentHierarchyPresentationPlan
                        .MaximumMemberGroupRows,
                    includeExactMemberCount: true),
                spelling: TypeMemberGroupSpelling.CSharp,
                accessibility: accessibility,
                includeHidden: includeNonPublic);
        var hierarchy =
            new InspectionHierarchyRequest<
                TypeDocumentHierarchyTopology>(
                TypeDocumentHierarchyTopology
                    .TypeCategoriesAndMemberGroups,
                InspectionHierarchyNodeSpelling.FullSpelling,
                new InspectionHierarchyPopulationRequest.Rows(
                    InspectionHierarchyNodeSpelling.Name,
                    new InspectionHierarchyPopulationRequest.Rows(
                        InspectionHierarchyNodeSpelling.Name,
                        new InspectionHierarchyPopulationRequest
                            .Count())));
        return new(format, declarations, hierarchy);
    }

    public static void Write(
        TypeDocumentInspectionContent document,
        TypeDocumentHierarchyPresentationPlan plan,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(output);

        string root =
            FormatTypeDeclaration(
                document.Subject,
                plan.Hierarchy.RootSpelling);
        switch (plan.Format)
        {
            case TypeDocumentHierarchyPresentationFormat.Tree:
                WriteTree(document, plan.Hierarchy, root, output);
                break;
            case TypeDocumentHierarchyPresentationFormat.Mermaid:
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
        TypeDocumentInspectionContent document,
        InspectionHierarchyRequest<TypeDocumentHierarchyTopology>
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
            new MarkoutHierarchySink<TypeDocumentHierarchyNode>(
                writer,
                FormatNode);
        TypeDocumentHierarchyProjection.Write(
            document,
            hierarchy,
            sink);
        writer.Flush();
    }

    private static void WriteMermaid(
        TypeDocumentInspectionContent document,
        InspectionHierarchyRequest<TypeDocumentHierarchyTopology>
            hierarchy,
        string root,
        TextWriter output)
    {
        output.WriteLine("graph TD");
        output.Write("  n0[\"");
        output.Write(MermaidHierarchySink<TypeDocumentHierarchyNode>
            .Escape(root));
        output.WriteLine("\"]");
        var sink =
            new MermaidHierarchySink<TypeDocumentHierarchyNode>(
                output,
                rootNodeId: 0,
                FormatNode);
        TypeDocumentHierarchyProjection.Write(
            document,
            hierarchy,
            sink);
    }

    private static string FormatNode(
        TypeDocumentHierarchyNode node) =>
        node switch
        {
            TypeDocumentHierarchyNode.Category category =>
                FormatCategory(category),
            TypeDocumentHierarchyNode.Member member =>
                FormatMember(member.Value),
            _ => throw new InvalidOperationException(
                "Unknown Type document hierarchy node."),
        };

    private static string FormatCategory(
        TypeDocumentHierarchyNode.Category category)
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
            && category.LogicalCount != category.ExactMemberCount
                ? $"{noun} ({category.LogicalCount} logical, "
                    + $"{category.ExactMemberCount} overloads)"
                : $"{noun} ({category.LogicalCount})";
    }

    private static string FormatMember(TypeMemberGroupShape member)
    {
        string name =
            CSharpIdentifier.ContainRenderedText(
                member.Binding.Name.ToString());
        if (!IsOverloadGrouped(member.Binding.Category))
            return name;
        if (member.ExactMemberCount is not { } exactMemberCount)
        {
            throw new InvalidOperationException(
                "A Type document hierarchy member is missing its exact-member Count.");
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
