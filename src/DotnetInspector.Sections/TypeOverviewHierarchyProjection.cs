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

    /// <summary>
    /// A category of MemberGroup rows. <paramref name="LogicalCount"/> is the
    /// number of MemberGroup rows; <paramref name="ExactMemberCount"/> is set
    /// only by the explicit exact-Member Count profile.
    /// </summary>
    public sealed record Category(
        MemberGroupCategory Value,
        int LogicalCount,
        int? ExactMemberCount)
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
        bool countsMembers = RequestsExactMemberCount(request);

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

            int? exactMemberCount =
                countsMembers
                    ? CountExactMembers(rows.Items, category)
                    : null;
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

    public static void ValidateDocument(
        TypeOverviewDocument document,
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology> request,
        TypeMemberGroupPopulationRequest members)
    {
        _ = GetValidatedRows(document, request);
        ArgumentNullException.ThrowIfNull(members);
        ValidateRequest(request, members, nameof(members));
        if (!Matches(document.Members.Binding, members))
        {
            throw new InvalidOperationException(
                "The Type overview hierarchy document population binding does not match the selected presentation plan.");
        }
    }

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
        if (rows.Continuation is not null
            || rows.Items.Length > 0
                && rows.Items.All(
                    static row => row.BaselineOrdinal > 1))
        {
            throw new InvalidOperationException(
                "A Type overview hierarchy requires complete member-group Rows from the population origin.");
        }
        bool countsMembers = RequestsExactMemberCount(request);
        if (rows.Items.Any(
                row => row.ExactMemberCount.HasValue != countsMembers))
        {
            throw new InvalidOperationException(
                countsMembers
                    ? "A Type overview hierarchy with exact-Member Counts requires a Count for every member group."
                    : "A Type overview hierarchy with leaf member groups requires Rows without exact-Member Counts.");
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
        bool countsMembers = RequestsExactMemberCount(request);
        if (countsMembers && !rows.IncludeExactMemberCount)
        {
            throw new ArgumentException(
                "A Type overview hierarchy with exact-Member Counts requires a Count for every member-group Row.",
                parameterName);
        }
        if (!countsMembers && rows.IncludeExactMemberCount)
        {
            throw new ArgumentException(
                "A Type overview hierarchy with leaf member groups does not request exact-Member Counts it cannot present.",
                parameterName);
        }
        if (rows.Continuation is not null)
        {
            throw new ArgumentException(
                "A compact Type overview hierarchy does not admit continued member-group Rows.",
                parameterName);
        }
    }

    private static bool Matches(
        TypeMemberGroupPopulationBinding binding,
        TypeMemberGroupPopulationRequest members) =>
        members.Rows is { } rows
        && binding.Spelling == members.Spelling
        && binding.IncludeHidden == members.IncludeHidden
        && binding.Accessibility == members.Accessibility
        && binding.Receiver == members.Receiver
        && binding.Ordering == rows.Ordering;

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
                            null
                            or InspectionHierarchyPopulationRequest.Count,
                    },
            })
        {
            throw new ArgumentException(
                "The compact Type overview hierarchy requires category Rows by Name and MemberGroup Rows by Name, as leaves or with exact-Member Count.",
                parameterName);
        }
    }

    /// <summary>
    /// The default profile lists MemberGroups as leaves; an explicit Count
    /// terminal beneath them requests exact-Member Counts.
    /// </summary>
    private static bool RequestsExactMemberCount(
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology>
            request) =>
        request.Children
            is InspectionHierarchyPopulationRequest.Rows
            {
                Children:
                    InspectionHierarchyPopulationRequest.Rows
                    {
                        Children:
                            InspectionHierarchyPopulationRequest.Count,
                    },
            };

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
