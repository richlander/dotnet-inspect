using System.Text;

using CSharpText;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class TypeDocumentHierarchyOutput
{
    internal static void Write(
        TypeDocument document,
        InspectionHierarchyRequest request,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine(
            FormatTypeDeclaration(document.Subject));

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
            request,
            sink);
        writer.Flush();
    }

    static string FormatNode(TypeDocumentHierarchyNode node) =>
        node switch
        {
            TypeDocumentHierarchyNode.Category category =>
                FormatCategory(category),
            TypeDocumentHierarchyNode.Member member =>
                FormatMember(member.Value),
            _ => throw new InvalidOperationException(
                "Unknown Type document hierarchy node."),
        };

    static string FormatCategory(
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

    static string FormatMember(TypeMemberGroupShape member)
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

    static bool IsOverloadGrouped(MemberGroupCategory category) =>
        category
            is MemberGroupCategory.Constructor
            or MemberGroupCategory.Method
            or MemberGroupCategory.Operator
            or MemberGroupCategory.ExplicitInterfaceImplementation;

    static string FormatTypeDeclaration(TypeSubject subject)
    {
        var fullName =
            new StringBuilder();
        if (!string.IsNullOrEmpty(subject.Type.Namespace))
        {
            fullName.Append(subject.Type.Namespace);
            fullName.Append('.');
        }
        for (int segmentIndex = 0;
             segmentIndex < subject.Type.Segments.Length;
             segmentIndex++)
        {
            if (segmentIndex > 0)
                fullName.Append('.');

            string segment =
                subject.Type.Segments[segmentIndex];
            int aritySeparator =
                segment.LastIndexOf('`');
            fullName.Append(
                aritySeparator >= 0
                    ? segment[..aritySeparator]
                    : segment);
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

        var declaration =
            new StringBuilder();
        if (subject.Attributes.HasFlag(
                System.Reflection.TypeAttributes.Abstract)
            && subject.Attributes.HasFlag(
                System.Reflection.TypeAttributes.Sealed)
            && subject.Category
                == ILInspector.Metadata.MetadataTypeDeclarationCategory
                    .Class)
        {
            declaration.Append("static ");
        }
        else
        {
            if (subject.Attributes.HasFlag(
                    System.Reflection.TypeAttributes.Abstract)
                && subject.Category
                    == ILInspector.Metadata.MetadataTypeDeclarationCategory
                        .Class)
            {
                declaration.Append("abstract ");
            }
            if (subject.Attributes.HasFlag(
                    System.Reflection.TypeAttributes.Sealed)
                && subject.Category
                    == ILInspector.Metadata.MetadataTypeDeclarationCategory
                        .Class)
            {
                declaration.Append("sealed ");
            }
        }

        declaration.Append(
            subject.Category switch
            {
                ILInspector.Metadata.MetadataTypeDeclarationCategory
                    .Class => "class",
                ILInspector.Metadata.MetadataTypeDeclarationCategory
                    .Struct => subject.IsByRefLike
                        ? "ref struct"
                        : "struct",
                ILInspector.Metadata.MetadataTypeDeclarationCategory
                    .Interface => "interface",
                ILInspector.Metadata.MetadataTypeDeclarationCategory
                    .Enum => "enum",
                ILInspector.Metadata.MetadataTypeDeclarationCategory
                    .Delegate => "delegate",
                _ => "type",
            });
        declaration.Append(' ');
        declaration.Append(fullName);
        return CSharpIdentifier.ContainRenderedText(
            declaration.ToString());
    }
}
