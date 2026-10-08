using CSharpText;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.Research;
using Markout;

namespace DotnetInspector.Presentation;

public sealed record TypeShapePresentationPlan(
    string? PackageName = null,
    string? PackageVersion = null,
    bool ExpandOverloads = false,
    int? MemberLimit = null);

public static class TypeShapePresentation
{
    public static void Write(
        ApiType type,
        IReadOnlyList<ApiMember> selectedMembers,
        TypeShapePresentationPlan plan,
        TextWriter output)
    {
        List<ApiMember> members = ApplyLimit(
            selectedMembers,
            plan.ExpandOverloads,
            plan.MemberLimit);
        var memberGroups = members
            .GroupBy(static member => member.Kind)
            .OrderBy(static group => GetKindOrder(group.Key))
            .Select(static group => new MemberGroup(
                group.Key,
                group.ToArray()))
            .ToArray();

        bool hasBaseType =
            !string.IsNullOrEmpty(type.BaseType)
            && type.BaseType != "Object";
        int nodeCount =
            (hasBaseType ? 1 : 0)
            + (type.Interfaces.Count > 0 ? 1 : 0)
            + (type.TypeParameters.Count > 0 ? 1 : 0)
            + memberGroups.Length;
        string title = FormatTitle(type, plan);

        if (nodeCount == 0)
        {
            output.WriteLine(title);
            return;
        }

        IReadOnlyList<string> modifiers = ResearchViews.TypeModifiers(type);
        string header = modifiers.Count > 0
            ? $"{string.Join(" ", modifiers)} {type.Kind} {title}"
            : title;
        output.WriteLine(header);

        var writer = new MarkoutWriter(output, new MarkdownFormatter());
        writer.WriteTree(tree =>
        {
            int nodeIndex = 0;
            if (hasBaseType)
            {
                tree.WriteNode(
                    "Inherits",
                    isLastSibling: ++nodeIndex == nodeCount,
                    type.BaseType!,
                    static (children, baseType) =>
                        children.WriteNode(
                            CSharpIdentifier.ContainRenderedText(baseType),
                            isLastSibling: true));
            }

            if (type.Interfaces.Count > 0)
            {
                tree.WriteNode(
                    "Implements",
                    isLastSibling: ++nodeIndex == nodeCount,
                    type.Interfaces,
                    static (children, interfaces) =>
                    {
                        for (int index = 0; index < interfaces.Count; index++)
                        {
                            children.WriteNode(
                                CSharpIdentifier.ContainRenderedText(
                                    interfaces[index]),
                                isLastSibling:
                                    index == interfaces.Count - 1);
                        }
                    });
            }

            if (type.TypeParameters.Count > 0)
            {
                tree.WriteNode(
                    "Type Parameters",
                    isLastSibling: ++nodeIndex == nodeCount,
                    type.TypeParameters,
                    static (children, parameters) =>
                    {
                        for (int index = 0; index < parameters.Count; index++)
                        {
                            TypeParameter parameter = parameters[index];
                            string text = parameter.Constraints.Count > 0
                                ? $"{parameter.DisplayName} : "
                                    + CSharpFormatter
                                        .FormatTypeParameterConstraints(
                                            parameter,
                                            parameters.Select(
                                                static item => item.Name))
                                : parameter.DisplayName;
                            children.WriteNode(
                                text,
                                isLastSibling:
                                    index == parameters.Count - 1);
                        }
                    });
            }

            foreach (MemberGroup group in memberGroups)
            {
                tree.WriteNode(
                    FormatKindLabel(group),
                    isLastSibling: ++nodeIndex == nodeCount,
                    (Group: group, Type: type, Plan: plan),
                    static (children, state) =>
                        WriteMembers(
                            children,
                            state.Group,
                            state.Type,
                            state.Plan.ExpandOverloads));
            }
        });
        writer.Flush();
    }

    private static List<ApiMember> ApplyLimit(
        IReadOnlyList<ApiMember> selectedMembers,
        bool expandOverloads,
        int? memberLimit)
    {
        var members = selectedMembers.ToList();
        if (!memberLimit.HasValue)
        {
            return members;
        }

        var displayEntries = members
            .GroupBy(static member => member.Kind)
            .OrderBy(static group => GetKindOrder(group.Key))
            .SelectMany(group =>
                !IsOverloadGroupedKind(group.Key)
                    ? group
                        .OrderBy(static member => member.Name, StringComparer.Ordinal)
                        .Select(static member => new[] { member })
                    : expandOverloads
                        ? group
                            .GroupBy(static member => member.Name)
                            .OrderBy(
                                static overloads =>
                                    OperatorNames.FormatDisplayName(
                                        overloads.Key),
                                StringComparer.Ordinal)
                            .SelectMany(overloads => overloads
                                .OrderBy(
                                    static member =>
                                        ApiMemberIdentity
                                            .GetMemberSignatureSortKey(
                                                member),
                                    StringComparer.Ordinal)
                                .Select(static member =>
                                    new[] { member }))
                        : group
                            .GroupBy(static member => member.Name)
                            .OrderBy(
                                static overloads =>
                                    OperatorNames.FormatDisplayName(
                                        overloads.Key),
                                StringComparer.Ordinal)
                            .Select(overloads => overloads
                                .OrderBy(
                                    static member =>
                                        ApiMemberIdentity
                                            .GetMemberSignatureSortKey(
                                                member),
                                    StringComparer.Ordinal)
                                .ToArray()))
            .ToArray();

        return memberLimit.Value < displayEntries.Length
            ? displayEntries
                .Take(memberLimit.Value)
                .SelectMany(static entry => entry)
                .ToList()
            : members;
    }

    private static void WriteMembers(
        StreamingTreeWriter writer,
        MemberGroup group,
        ApiType declaringType,
        bool expandOverloads)
    {
        if (IsOverloadGroupedKind(group.Kind))
        {
            var overloadGroups = group.Members
                .GroupBy(static member => member.Name)
                .OrderBy(
                    static overloads =>
                        OperatorNames.FormatDisplayName(overloads.Key),
                    StringComparer.Ordinal)
                .Select(static overloads => overloads
                    .OrderBy(
                        static member =>
                            ApiMemberIdentity.GetMemberSignatureSortKey(
                                member),
                        StringComparer.Ordinal)
                    .ToArray())
                .ToArray();
            int childCount = expandOverloads
                ? overloadGroups.Sum(static overloads => overloads.Length)
                : overloadGroups.Length;
            int childIndex = 0;

            foreach (ApiMember[] overloads in overloadGroups)
            {
                if (expandOverloads)
                {
                    foreach (ApiMember member in overloads)
                    {
                        writer.WriteNode(
                            FormatMember(member),
                            isLastSibling:
                                ++childIndex == childCount);
                    }
                }
                else
                {
                    string text = overloads.Length == 1
                        ? FormatMember(overloads[0])
                        : $"{OperatorNames.FormatDisplayName(
                            overloads[0].Name)} "
                            + $"({overloads.Length} overloads)";
                    writer.WriteNode(
                        text,
                        isLastSibling:
                            ++childIndex == childCount);
                }
            }

            return;
        }

        ApiMember[] orderedMembers = group.Members
            .OrderBy(static member => member.Name, StringComparer.Ordinal)
            .ToArray();
        for (int index = 0; index < orderedMembers.Length; index++)
        {
            ApiMember member = orderedMembers[index];
            writer.WriteNode(
                member.IsFinalizer
                    ? FormatDestructor(declaringType)
                    : FormatMember(member),
                isLastSibling:
                    index == orderedMembers.Length - 1);
        }
    }

    private static string FormatTitle(
        ApiType type,
        TypeShapePresentationPlan plan)
    {
        string packageInfo =
            plan.PackageName is not null
                && plan.PackageVersion is not null
                ? $" ({plan.PackageName} {plan.PackageVersion})"
                : plan.PackageName is not null
                    ? $" ({plan.PackageName})"
                    : "";
        return CSharpIdentifier.ContainRenderedText(
                MetadataTypeNameFormatter.FormatFullName(type))
            + packageInfo;
    }

    private static string FormatMember(ApiMember member) =>
        CSharpIdentifier.ContainRenderedText(
            member.Signature
                ?? OperatorNames.FormatDisplayName(member.Name));

    private static string FormatDestructor(ApiType type) =>
        CSharpIdentifier.ContainRenderedText(
            $"~{CSharpFormatter.FormatDeclarationLeafMetadataName(type)}()");

    private static string FormatKindLabel(MemberGroup group)
    {
        int memberCount = group.Members.Count;
        int logicalCount = IsOverloadGroupedKind(group.Kind)
            ? group.Members
                .Select(static member => member.Name)
                .Distinct(StringComparer.Ordinal)
                .Count()
            : memberCount;
        if (IsOverloadGroupedKind(group.Kind)
            && memberCount != logicalCount)
        {
            string noun = group.Kind switch
            {
                "constructor" => "Constructors",
                "method" => "Methods",
                "operator" => "Operators",
                "explicit-interface-implementation" =>
                    "Explicit Interface Implementations",
                "extension-method" => "Extension Methods",
                _ => GetKindNoun(group.Kind),
            };
            return $"{noun} ({logicalCount} logical, "
                + $"{memberCount} overloads)";
        }

        return $"{GetKindNoun(group.Kind)} ({memberCount})";
    }

    private static bool IsOverloadGroupedKind(string kind) =>
        kind is "constructor"
            or "method"
            or "operator"
            or "explicit-interface-implementation"
            or "extension-method";

    private static int GetKindOrder(string kind) => kind switch
    {
        "constructor" => 0,
        "finalizer" => 1,
        "field" => 2,
        "property" => 3,
        "method" => 4,
        "operator" => 5,
        "explicit-interface-implementation" => 6,
        "extension-method" => 7,
        "event" => 8,
        _ => 9,
    };

    private static string GetKindNoun(string kind) => kind switch
    {
        "property" => "Properties",
        "method" => "Methods",
        "operator" => "Operators",
        "explicit-interface-implementation" =>
            "Explicit Interface Implementations",
        "extension-method" => "Extension Methods",
        "constructor" => "Constructors",
        "finalizer" => "Finalizer",
        "event" => "Events",
        "field" => "Fields",
        _ => kind + "s",
    };

    private sealed record MemberGroup(
        string Kind,
        IReadOnlyList<ApiMember> Members);
}
