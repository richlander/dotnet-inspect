namespace DotnetInspector.Sections;

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
        InspectionHierarchyRequest request,
        IInspectionHierarchySink<TypeDocumentHierarchyNode> sink)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sink);

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
                                row.Binding.Category == category)
                        .OrderBy(
                            row => row.Binding.Name.ToString(),
                            StringComparer.Ordinal))
                    {
                        children.WriteNode(
                            new TypeDocumentHierarchyNode.Member(member),
                            isLastSibling: ++memberIndex == logicalCount);
                    }
                });
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
