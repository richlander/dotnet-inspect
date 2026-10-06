namespace DotnetInspector.Sections;

public enum TypeDocumentHierarchyTopology
{
    TypeCategoriesAndMemberGroups,
}

public abstract record TypeDocumentHierarchyNode
{
    private TypeDocumentHierarchyNode()
    {
    }

    public sealed record Category(
        MemberGroupCategory Value,
        int LogicalCount,
        int ExactMemberCount)
        : TypeDocumentHierarchyNode;

    public sealed record Member(TypeMemberGroupShape Value)
        : TypeDocumentHierarchyNode;
}

public static class TypeDocumentHierarchyProjection
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
        TypeDocumentInspectionContent document,
        InspectionHierarchyRequest<TypeDocumentHierarchyTopology> request,
        IInspectionHierarchySink<TypeDocumentHierarchyNode> sink)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sink);
        ValidateRequest(request);

        TypeMemberGroupPopulationResult population =
            document.Declarations
                is TypeDocumentDeclarations.Available available
                ? available.Population
                : throw new InvalidOperationException(
                    "The Type document declarations are unavailable.");
        TypeMemberGroupRowsOutcome.Read rows =
            population.Rows is TypeMemberGroupRowsOutcome.Read read
                ? read
                : throw new InvalidOperationException(
                    "The Type document member-group Rows are unavailable.");
        if (rows.Continuation is not null)
        {
            throw new InvalidOperationException(
                "A Type document hierarchy requires complete member-group Rows.");
        }

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
                new TypeDocumentHierarchyNode.Category(
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
                            new TypeDocumentHierarchyNode.Member(member),
                            isLastSibling: ++memberIndex == logicalCount);
                    }
                });
        }
    }

    public static void ValidateRequest(
        InspectionHierarchyRequest<TypeDocumentHierarchyTopology>
            request) =>
        ValidateRequestShape(request, nameof(request));

    public static void ValidateRequest(
        InspectionHierarchyRequest<TypeDocumentHierarchyTopology> request,
        TypeMemberGroupPopulationRequest? declarations,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        ValidateRequestShape(request, parameterName);
        if (declarations?.Rows is not { } rows)
        {
            throw new ArgumentException(
                "A Type document hierarchy requires a member-group Rows request.",
                parameterName);
        }
        if (!rows.IncludeExactMemberCount)
        {
            throw new ArgumentException(
                "A compact Type document hierarchy requires exact-Member Counts for every member-group Row.",
                parameterName);
        }
    }

    private static void ValidateRequestShape(
        InspectionHierarchyRequest<TypeDocumentHierarchyTopology> request,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Topology
            is not TypeDocumentHierarchyTopology
                .TypeCategoriesAndMemberGroups)
        {
            throw new ArgumentException(
                "The Type document does not admit the requested hierarchy topology.",
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
                "The compact Type document hierarchy requires category Rows by Name, MemberGroup Rows by Name, and exact-Member Count.",
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
            if (row.ExactMemberCount is not { } exactMemberCount)
            {
                throw new InvalidOperationException(
                    "A Type document hierarchy requires exact-member Counts for every member group.");
            }

            count += exactMemberCount;
        }

        return count;
    }
}
