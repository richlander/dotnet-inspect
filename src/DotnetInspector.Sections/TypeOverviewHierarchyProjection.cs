namespace DotnetInspector.Sections;

public enum TypeOverviewHierarchyTopology
{
    TypeCategoriesAndMemberGroups,
}

public abstract record TypeOverviewHierarchyNode
{
    private TypeOverviewHierarchyNode()
    {
    }

    public sealed record Category(
        MemberGroupCategory Value,
        int LogicalCount,
        int ExactMemberCount)
        : TypeOverviewHierarchyNode;

    public sealed record Member(TypeMemberGroupShape Value)
        : TypeOverviewHierarchyNode;
}

public static class TypeOverviewHierarchyProjection
{
    static readonly MemberGroupCategory[] s_categoryOrder =
    [
        MemberGroupCategory.Constructor,
        MemberGroupCategory.Finalizer,
        MemberGroupCategory.Field,
        MemberGroupCategory.Property,
        MemberGroupCategory.Method,
        MemberGroupCategory.Operator,
        MemberGroupCategory.ExplicitInterfaceImplementation,
        MemberGroupCategory.Event,
    ];

    public static void Write(
        TypeOverviewDocument document,
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology> request,
        IInspectionHierarchySink<TypeOverviewHierarchyNode> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        TypeMemberGroupRowsOutcome.Read rows =
            GetValidatedRows(document, request);

        int categoryCount = 0;
        foreach (MemberGroupCategory category in s_categoryOrder)
        {
            if (HasCategory(rows.Items, category))
                categoryCount++;
        }

        int categoryIndex = 0;
        foreach (MemberGroupCategory category in s_categoryOrder)
        {
            int logicalCount = CountCategory(rows.Items, category);
            if (logicalCount == 0)
                continue;

            int exactMemberCount = CountExactMembers(rows.Items, category);
            sink.WriteNode(
                new TypeOverviewHierarchyNode.Category(
                    category,
                    logicalCount,
                    exactMemberCount),
                isLastSibling: ++categoryIndex == categoryCount,
                children =>
                {
                    int memberIndex = 0;
                    foreach (TypeMemberGroupShape member in rows.Items
                        .Where(
                            row =>
                                row.Binding.Category == category))
                    {
                        children.WriteNode(
                            new TypeOverviewHierarchyNode.Member(member),
                            isLastSibling: ++memberIndex == logicalCount);
                    }
                });
        }
    }

    public static void ValidateDocument(
        TypeOverviewDocument document,
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology> request) =>
        _ = GetValidatedRows(document, request);

    private static TypeMemberGroupRowsOutcome.Read GetValidatedRows(
        TypeOverviewDocument document,
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology> request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        TypeMemberGroupRowsOutcome.Read rows =
            document.Members.Rows is TypeMemberGroupRowsOutcome.Read read
                ? read
                : throw new InvalidOperationException(
                    "The Type document member-group Rows are unavailable.");
        if (rows.Continuation is not null)
        {
            throw new InvalidOperationException(
                "A Type overview hierarchy requires complete member-group Rows.");
        }
        if (rows.Items.Any(
                static row => !row.ExactMemberCount.HasValue))
        {
            throw new InvalidOperationException(
                "A Type overview hierarchy requires exact-member Counts for every member group.");
        }

        return rows;
    }

    public static void ValidateRequest(
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology>
            request) =>
        ValidateRequestShape(request, nameof(request));

    public static void ValidateRequest(
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology> request,
        TypeMemberGroupPopulationRequest? members,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        ValidateRequestShape(request, parameterName);
        if (members?.Rows is not { } rows)
        {
            throw new ArgumentException(
                "A Type overview hierarchy requires a member-group Rows request.",
                parameterName);
        }
        if (!rows.IncludeExactMemberCount)
        {
            throw new ArgumentException(
                "A compact Type overview hierarchy requires exact-Member Counts for every member-group Row.",
                parameterName);
        }
    }

    private static void ValidateRequestShape(
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology> request,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Topology
            is not TypeOverviewHierarchyTopology
                .TypeCategoriesAndMemberGroups)
        {
            throw new ArgumentException(
                "The Type overview does not admit the requested hierarchy topology.",
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
                        Spelling:
                            InspectionHierarchyNodeSpelling.Name,
                        Children:
                            InspectionHierarchyPopulationRequest.Count,
                    },
            })
        {
            throw new ArgumentException(
                "The compact Type overview hierarchy requires category Rows by Name, MemberGroup Rows by Name, and exact-Member Count.",
                parameterName);
        }
    }

    static bool HasCategory(
        IReadOnlyList<TypeMemberGroupShape> rows,
        MemberGroupCategory category)
    {
        foreach (TypeMemberGroupShape row in rows)
        {
            if (row.Binding.Category == category)
                return true;
        }

        return false;
    }

    static int CountCategory(
        IReadOnlyList<TypeMemberGroupShape> rows,
        MemberGroupCategory category)
    {
        int count = 0;
        foreach (TypeMemberGroupShape row in rows)
        {
            if (row.Binding.Category == category)
                count++;
        }

        return count;
    }

    static int CountExactMembers(
        IReadOnlyList<TypeMemberGroupShape> rows,
        MemberGroupCategory category)
    {
        int count = 0;
        foreach (TypeMemberGroupShape row in rows)
        {
            if (row.Binding.Category != category)
                continue;
            count += row.ExactMemberCount.GetValueOrDefault();
        }

        return count;
    }
}
