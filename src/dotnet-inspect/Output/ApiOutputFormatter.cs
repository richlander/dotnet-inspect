using System.Reflection.PortableExecutable;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats API command output for display.
/// </summary>
public static class ApiOutputFormatter
{
    // ===== Full API View Model Factory =====

    internal static (CliApiSurface view, int truncatedCount) BuildFullApiView(ApiSurface api, ApiOptions options)
    {
        var totalCount = api.Types.Count;

        // Pre-truncate types list if --limit
        int truncatedCount = 0;
        if (options.Limit.HasValue && options.Limit.Value < totalCount)
        {
            truncatedCount = totalCount - options.Limit.Value;
            api.Types = api.Types.Take(options.Limit.Value).ToList();
        }

        var view = new CliApiSurface
        {
            Name = api.Name,
            Library = api.Library,
            Types = api.PublicTypeCount,
            Methods = api.PublicMethodCount,
            Properties = api.PublicPropertyCount,
            Source = api.Source,
            Version = api.Version,
            Tfm = api.Tfm
        };

        if (totalCount == 0)
        {
            if (api.TypeForwarders.Count > 0)
            {
                view.Description = "This library contains no public types. Type forwarders could not be resolved.";
                view.TypeForwarders = api.TypeForwarders
                    .GroupBy(f => f.TargetAssembly)
                    .OrderBy(g => g.Key)
                    .Select(g => new ForwarderSummaryRow(g.Key, g.Count().ToString()))
                    .ToList();
            }
            else
            {
                view.Description = "This library contains no public types.";
            }
        }
        else if (options.Verbosity != Verbosity.Quiet)
        {
            if (api.IsTypeForwardingAssembly)
                view.Description = "*This is a type-forwarding library. Types shown are resolved from target libraries.*";

            var showDocs = options.ShowDocs
                || options.Columns?.Any(c => c.Equals("Description", StringComparison.OrdinalIgnoreCase)
                    || c.Equals("Kind", StringComparison.OrdinalIgnoreCase)) == true;
            PopulateTypeSections(view, api.Types, showDocs);
        }

        return (view, truncatedCount);
    }

    private static void PopulateTypeSections(CliApiSurface view, List<ApiType> types, bool showDocs)
    {
        var byKind = types
            .GroupBy(t => t.Kind)
            .OrderBy(g => GetTypeKindSortOrder(g.Key))
            .ToList();

        foreach (var group in byKind)
        {
            var rows = group.Select(t =>
            {
                var fullName = FormatGenericFullName(t);
                var members = t.Members.Count.ToString();
                string? desc = null;
                if (showDocs)
                {
                    desc = t.Documentation.Summary ?? "";
                    desc = desc.ReplaceLineEndings(" ");
                    if (desc.Length > 80) desc = desc[..77] + "...";
                }
                return new TypeSummaryRow(group.Key, fullName, members, desc);
            }).ToList();

            switch (group.Key)
            {
                case "class":
                    if (showDocs) view.ClassesWithDocs = rows; else view.Classes = rows;
                    break;
                case "struct":
                    if (showDocs) view.StructsWithDocs = rows; else view.Structs = rows;
                    break;
                case "interface":
                    if (showDocs) view.InterfacesWithDocs = rows; else view.Interfaces = rows;
                    break;
                case "enum":
                    if (showDocs) view.EnumsWithDocs = rows; else view.Enums = rows;
                    break;
                case "delegate":
                    if (showDocs) view.DelegatesWithDocs = rows; else view.Delegates = rows;
                    break;
            }
        }
    }

    internal static MarkoutWriterOptions BuildWriterOptions(ApiSurface api, ApiOptions options)
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(
            api, options.Verbosity, options.IncludeSections);

        return new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            IncludeDescription = options.Verbosity != Verbosity.Quiet,
            Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields)
        };
    }

    internal static MarkoutWriterOptions BuildTypeWriterOptions(ApiType type, ApiOptions options)
    {
        var effectiveVerbosity = options.Verbosity;

        var pipeline = ApiMemberSectionPipelines.Create(options);
        var selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        var includeSections = pipeline.ComputeIncludeSections(
            type, effectiveVerbosity, options.IncludeSections, selectAll);
        if (ShouldRenderMemberDetailContext(options) && includeSections is { Count: > 0 }
            && !includeSections.Contains(SectionNames.Summary))
            includeSections = [SectionNames.Summary, .. includeSections];

        return new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            IncludeDescription = effectiveVerbosity != Verbosity.Quiet && !ShouldRenderMemberDetailContext(options),
            Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields)
        };
    }

    internal static bool ShouldRenderMemberDetailContext(ApiOptions options) =>
        options is MemberOptions { OverloadIndex: not null }
        && options.IncludeSections is { Count: > 0 }
        && !SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
        && !SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections)
        && !options.Count
        && !options.JsonOutput
        && !options.OneLine;

    internal static bool ShouldRenderMemberGroups(ApiOptions options)
    {
        if (ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(options))
            return false;

        if (SectionRequested(options.IncludeSections, SectionNames.MethodGroups)
            || DiscoveryRequests(options, SectionNames.MethodGroups))
            return true;

        return options.Verbosity == Verbosity.Minimal
            && !SectionRequested(options.IncludeSections, SectionNames.Methods)
            && !DiscoveryRequests(options, SectionNames.Methods);
    }

    internal static bool ShouldRenderMemberRows(ApiOptions options) =>
        options.Verbosity != Verbosity.Minimal
        || ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(options)
        || SectionRequested(options.IncludeSections, SectionNames.Methods)
        || (!SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections)
            && (SectionRequested(options.IncludeSections, SectionNames.Operators)
                || SectionRequested(options.IncludeSections, SectionNames.ExplicitInterfaceImplementations)
                || SectionRequested(options.IncludeSections, SectionNames.ExtensionMethods)))
        || DiscoveryRequests(options, SectionNames.Methods)
        || DiscoveryRequests(options, SectionNames.Operators)
        || DiscoveryRequests(options, SectionNames.ExplicitInterfaceImplementations)
        || DiscoveryRequests(options, SectionNames.ExtensionMethods);

    internal static readonly HashSet<string> SupplementalMemberKinds =
    [
        "operator",
        "explicit-interface-implementation",
        "extension-method"
    ];

    internal static bool ShouldRenderSupplementalMemberRows(ApiOptions options) =>
        options.Verbosity == Verbosity.Minimal
        && !ShouldRenderMemberRows(options)
        && (options.IncludeSections is null
            || SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections));

    internal static bool ShouldRenderSectionedTabularView(ApiType type, ApiOptions options)
    {
        if (options.IncludeSections is { Count: 1 })
            return true;
        if (!ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(options))
            return false;

        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly, options.KindFilter);
        return grouped.Count == 1;
    }

    internal static void SerializeTypeDocument(
        TypeView view,
        EventsView? eventsView,
        MethodGroupsView? methodGroupsView,
        MethodsView? methodsView,
        OperatorsView? operatorsView,
        ExplicitInterfaceImplementationsView? explicitInterfaceImplementationsView,
        ExtensionMethodsView? extensionMethodsView,
        MemberCodeView? memberCodeView,
        MarkoutWriter writer)
    {
        ApiViewContext.Default.Serialize(view, writer);
        if (methodGroupsView is { HasRows: true })
            ApiViewContext.Default.Serialize(methodGroupsView, writer);
        if (methodsView is { HasRows: true })
            ApiViewContext.Default.Serialize(methodsView, writer);
        if (operatorsView is { HasRows: true })
            ApiViewContext.Default.Serialize(operatorsView, writer);
        if (explicitInterfaceImplementationsView is { HasRows: true })
            ApiViewContext.Default.Serialize(explicitInterfaceImplementationsView, writer);
        if (extensionMethodsView is { HasRows: true })
            ApiViewContext.Default.Serialize(extensionMethodsView, writer);
        if (eventsView is { HasRows: true })
            ApiViewContext.Default.Serialize(eventsView, writer);
        if (memberCodeView != null)
            ApiViewContext.Default.Serialize(memberCodeView, writer);
    }

    private static bool SectionRequested(HashSet<string>? sections, string name)
        => sections?.Contains(name) == true;

    private static bool DiscoveryRequests(ApiOptions options, string name)
        => options.Discover is { Length: > 0 } discover
           && discover.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase));

    private static bool ShouldAbbreviateMemberSignatures(ApiOptions options) =>
        options.Verbosity == Verbosity.Minimal
        && options.IncludeSections is null
        && !ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(options)
        && !SectionRequested(options.IncludeSections, SectionNames.Methods)
        && !DiscoveryRequests(options, SectionNames.Methods);

    // ===== Shape Output (--shape) =====

    public static void WriteShapeOutput(ApiType type, string? foundIn, string? packageName, string? packageVersion, HashSet<string> memberFilter, HashSet<string>? kindFilter = null)
    {
        var view = BuildShapeView(type, foundIn, packageName, packageVersion, memberFilter, kindFilter);
        if (view.Members is { Count: > 0 })
        {
            Console.WriteLine(view.FullName);
            var writer = new MarkoutWriter(Console.Out, new MarkdownFormatter());
            writer.WriteTree([.. view.Members]);
        }
        else if (kindFilter?.Count > 0 || memberFilter.Count > 0)
        {
            var filterDesc = kindFilter?.Count > 0
                ? string.Join(", ", kindFilter)
                : string.Join(", ", memberFilter);
            Console.Error.WriteLine($"No matching members for filter: {filterDesc}");
        }
        else
        {
            Console.WriteLine(view.FullName);
        }
    }

    // ===== View Model Factories =====

    internal static TypeView BuildTypeView(ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options)
    {
        bool memberDetail = options is MemberOptions { OverloadIndex: not null } && type.Members.Count == 1;
        bool memberFilterActive = options is MemberOptions { MemberFilter.Count: > 0 };
        var selectedMember = memberDetail ? type.Members[0] : null;

        // Build title with package context
        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null ? $" ({packageName})" : "";

        // Build modifiers
        List<string> modifiers = [];
        if (type.IsStatic) modifiers.Add("static");
        if (type.IsAbstract && type.Kind == "class") modifiers.Add("abstract");
        if (type.IsSealed && type.Kind == "class") modifiers.Add("sealed");

        // Base type (filter out trivial bases)
        string? baseType = null;
        if (!string.IsNullOrEmpty(type.BaseType) && type.BaseType != "System.Object" && type.BaseType != "System.ValueType" && type.BaseType != "System.Enum")
            baseType = type.BaseType;

        // Type parameters inline (Quiet only — at Minimal+ the section replaces this)
        string? typeParamsInline = null;
        if (type.TypeParameters.Count > 0 && options.Verbosity == Verbosity.Quiet)
        {
            var paramDescriptions = type.TypeParameters
                .Select(tp => tp.Constraints.Count > 0
                    ? $"{tp.DisplayName} : {tp.ConstraintsSummary}"
                    : tp.DisplayName);
            typeParamsInline = string.Join(", ", paramDescriptions);
        }

        // Description (from docs) — suppressed at quiet
        string? description = null;
        if (!memberDetail && options.Verbosity != Verbosity.Quiet && options.ShowDocs && type.Documentation.Summary != null)
            description = type.Documentation.Summary;

        // Samples info (only with --docs/--samples)
        string? samplesInfo = null;
        if ((options.ShowDocs || options.ShowSamples) && type.Documentation.Samples.Count > 0)
            samplesInfo = $"{type.Documentation.Samples.Count} available";

        // Type parameters table (pipeline controls visibility via IncludeSections)
        List<TypeParameterRow>? typeParameterRows = null;
        if (!memberFilterActive && type.TypeParameters.Count > 0)
        {
            typeParameterRows = type.TypeParameters
                .Select(tp => new TypeParameterRow { Parameter = tp.DisplayName, Constraints = tp.ConstraintsSummary ?? "" })
                .ToList();
        }

        // Interfaces (pipeline controls visibility via IncludeSections)
        List<InterfaceRow>? interfaceRows = null;
        if (!memberFilterActive && type.Interfaces.Count > 0)
        {
            interfaceRows = type.Interfaces.Order()
                .Select(i => new InterfaceRow { Interface = i })
                .ToList();
        }

        // Baseclass (pipeline controls visibility via IncludeSections; filtered for trivial bases)
        List<BaseclassRow>? baseclassRows = null;
        if (!memberFilterActive && baseType != null)
        {
            baseclassRows = [new BaseclassRow { Type = baseType }];
        }

        bool topFieldsOnly = options.Verbosity == Verbosity.Quiet;
        var title = memberDetail
            ? $"{FormatGenericFullName(type)}.{OperatorNames.FormatDisplayName(selectedMember!.Name)}"
            : $"{FormatGenericFullName(type)}{packageInfo}";

        return new TypeView
        {
            Title = title,
            Description = description,
            Summary = memberDetail
                ? BuildMemberDetailSummary(type, foundIn, packageName, packageVersion, apiSource, selectedTfm)
                : null,
            Kind = topFieldsOnly ? type.Kind : null,
            Modifiers = topFieldsOnly ? (modifiers.Count > 0 ? string.Join(", ", modifiers) : null) : null,
            BaseType = topFieldsOnly ? baseType : null,
            TypeParametersInline = typeParamsInline,
            Assembly = topFieldsOnly ? foundIn : null,
            Package = topFieldsOnly ? packageName : null,
            Version = topFieldsOnly ? packageVersion : null,
            Source = topFieldsOnly ? apiSource : null,
            Tfm = topFieldsOnly ? selectedTfm : null,
            SamplesInfo = topFieldsOnly ? samplesInfo : null,
            // Member stats for quiet verbosity
            Constructors = topFieldsOnly ? NullIfZero(type.Members.Count(m => m.Kind == "constructor")) : null,
            Fields = topFieldsOnly ? NullIfZero(type.Members.Count(m => m.Kind == "field" && !m.EnumValue.HasValue)) : null,
            Properties = topFieldsOnly ? NullIfZero(type.Members.Count(m => m.Kind == "property")) : null,
            Methods = topFieldsOnly ? NullIfZero(type.Members.Count(m => m.Kind == "method")) : null,
            Operators = topFieldsOnly ? NullIfZero(type.Members.Count(m => m.Kind == "operator")) : null,
            ExplicitInterfaceImplementations = topFieldsOnly ? NullIfZero(type.Members.Count(m => m.Kind == "explicit-interface-implementation")) : null,
            ExtensionMethods = topFieldsOnly ? NullIfZero(type.Members.Count(m => m.Kind == "extension-method")) : null,
            Events = topFieldsOnly ? NullIfZero(type.Members.Count(m => m.Kind == "event")) : null,
            TypeParameterRows = typeParameterRows,
            InterfaceRows = interfaceRows,
            BaseclassRows = baseclassRows,
        };

        static int? NullIfZero(int count) => count > 0 ? count : null;
    }

    private static List<MarkoutField> BuildMemberDetailSummary(
        ApiType type, string? foundIn, string? packageName, string? packageVersion,
        string? apiSource, string? selectedTfm)
    {
        List<MarkoutField> fields = [new("Type", FormatGenericFullName(type))];

        if (!string.IsNullOrEmpty(foundIn))
            fields.Add(new("Library", foundIn));
        if (!string.IsNullOrEmpty(packageName))
            fields.Add(new("Package", packageName));
        if (!string.IsNullOrEmpty(packageVersion))
            fields.Add(new("Version", packageVersion));
        if (!string.IsNullOrEmpty(apiSource))
            fields.Add(new("Source", apiSource));
        if (!string.IsNullOrEmpty(selectedTfm))
            fields.Add(new("TFM", selectedTfm));

        return fields;
    }

    internal static TypeShapeView BuildShapeView(ApiType type, string? foundIn, string? packageName, string? packageVersion, HashSet<string> memberFilter, HashSet<string>? kindFilter = null)
    {
        bool hasFilter = memberFilter.Count > 0 || kindFilter?.Count > 0;
        List<TreeNode> nodes = [];

        // Group members by kind
        bool hasMemberNodes = false;
        if (type.Members.Count > 0)
        {
            var members = type.Members.Where(m => !IsCompilerGenerated(m.Name));

            if (memberFilter.Count > 0)
            {
                members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, memberFilter));
            }

            if (kindFilter?.Count > 0)
            {
                members = members.Where(m => kindFilter.Contains(m.Kind));
            }

            var membersByKind = members
                .GroupBy(m => m.Kind)
                .OrderBy(g => GetTreeKindOrder(g.Key))
                .ToList();

            hasMemberNodes = membersByKind.Count > 0;

            foreach (var group in membersByKind)
            {
                var children = BuildShapeMemberNodes(group.Key, group);
                var kindLabel = GetShapeKindLabel(group.Key, group.Count(), children.Count);
                nodes.Add(new TreeNode(kindLabel) { Children = children });
            }
        }

        static List<TreeNode> BuildShapeMemberNodes(string kind, IEnumerable<ApiMember> members)
        {
            if (IsOverloadGroupedKind(kind))
            {
                return members
                    .GroupBy(m => m.Name)
                    .OrderBy(g => OperatorNames.FormatDisplayName(g.Key), StringComparer.Ordinal)
                    .Select(g =>
                    {
                        var ordered = g
                            .OrderBy(GetMemberSignatureSortKey, StringComparer.Ordinal)
                            .ToList();
                        if (ordered.Count == 1)
                            return new TreeNode(ordered[0].Signature ?? OperatorNames.FormatDisplayName(ordered[0].Name));

                        var displayName = OperatorNames.FormatDisplayName(g.Key);
                        return new TreeNode($"{displayName} ({ordered.Count} overloads)");
                    })
                    .ToList();
            }

            return members
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => new TreeNode(m.Signature ?? OperatorNames.FormatDisplayName(m.Name)))
                .ToList();
        }

        static bool IsOverloadGroupedKind(string kind)
            => kind is "constructor" or "method" or "operator" or "explicit-interface-implementation" or "extension-method";

        static string GetShapeKindLabel(string kind, int overloadCount, int logicalCount)
        {
            if (IsOverloadGroupedKind(kind) && overloadCount != logicalCount)
            {
                var noun = kind switch
                {
                    "constructor" => "Constructors",
                    "method" => "Methods",
                    "operator" => "Operators",
                    "explicit-interface-implementation" => "Explicit Interface Implementations",
                    "extension-method" => "Extension Methods",
                    _ => GetTreeKindLabel(kind, overloadCount).Split(' ')[0]
                };
                return $"{noun} ({logicalCount} logical, {overloadCount} overloads)";
            }

            return GetTreeKindLabel(kind, overloadCount);
        }

        // Structural nodes (suppress when a filter is active but matched nothing)
        if (!hasFilter || hasMemberNodes)
        {
            // Inheritance
            if (!string.IsNullOrEmpty(type.BaseType) && type.BaseType != "Object")
            {
                nodes.Insert(0, new TreeNode("Inherits") { Children = [new TreeNode(type.BaseType)] });
            }

            // Interfaces
            if (type.Interfaces.Count > 0)
            {
                var insertAt = nodes.Count > 0 && nodes[0].Text == "Inherits" ? 1 : 0;
                nodes.Insert(insertAt, new TreeNode("Implements") { Children = type.Interfaces.Select(i => new TreeNode(i)).ToList() });
            }

            // Type parameters with constraints
            if (type.TypeParameters.Count > 0)
            {
                var typeParamDescriptions = type.TypeParameters
                    .Select(tp => tp.Constraints.Count > 0
                        ? $"{tp.DisplayName} : {tp.ConstraintsSummary}"
                        : tp.DisplayName)
                    .ToList();
                var insertAt = nodes.FindIndex(n => n.Text != "Inherits" && n.Text != "Implements");
                if (insertAt < 0) insertAt = nodes.Count;
                nodes.Insert(insertAt, new TreeNode("Type Parameters") { Children = typeParamDescriptions.Select(t => new TreeNode(t)).ToList() });
            }
        }

        List<string> modifiers = [];
        if (type.IsStatic) modifiers.Add("static");
        if (type.IsAbstract && type.Kind == "class") modifiers.Add("abstract");
        if (type.IsSealed && type.Kind == "class") modifiers.Add("sealed");

        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null ? $" ({packageName})" : "";

        return new TypeShapeView
        {
            FullName = $"{FormatGenericFullName(type)}{packageInfo}",
            Kind = type.Kind,
            Modifiers = modifiers.Count > 0 ? string.Join(", ", modifiers) : null,
            Assembly = foundIn,
            Package = packageName,
            Version = packageVersion,
            Members = nodes
        };
    }

    // ===== Internal Rendering Methods =====

    internal static void PopulateEnumValues(TypeView view, ApiType type, ApiOptions options)
    {
        var enumMembers = type.Members
            .Where(m => m.Kind == "field" && m.EnumValue.HasValue && !IsCompilerGenerated(m.Name))
            .OrderBy(m => m.EnumValue)
            .ToList();
        if (options.MemberFilter.Count > 0)
            enumMembers = enumMembers
                .Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter))
                .ToList();
        if (options.UnsafeOnly)
            enumMembers = [];
        if (options.KindFilter.Count > 0 && !options.KindFilter.Contains("field"))
            enumMembers = [];

        if (enumMembers.Count == 0)
            return;

        if (options.Limit.HasValue && options.Limit.Value < enumMembers.Count)
            enumMembers = enumMembers.Take(options.Limit.Value).ToList();

        bool hasAnyDocs = options.ShowDocs && enumMembers.Any(m => m.Documentation.Summary != null);

        var rows = enumMembers.Select(m => new EnumValueRow
        {
            Name = m.Name,
            Value = m.EnumValue.ToString()!,
            Description = hasAnyDocs ? (m.Documentation.Summary ?? "") : null
        }).ToList();

        if (hasAnyDocs)
            view.EnumValuesWithDocs = rows;
        else
            view.EnumValues = rows;
    }

    internal static (int truncated, string noun) PopulateMemberSections(
        TypeView view,
        MethodsView methodsView,
        OperatorsView operatorsView,
        ExplicitInterfaceImplementationsView explicitInterfaceImplementationsView,
        ExtensionMethodsView extensionMethodsView,
        EventsView eventsView,
        ApiType type,
        ApiOptions options,
        IReadOnlySet<string>? onlyKinds = null)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly, options.KindFilter);
        if (onlyKinds is { Count: > 0 })
            grouped = grouped
                .Where(kvp => onlyKinds.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        if (grouped.Count == 0) return (0, "");

        // Flatten sorted for --limit application
        var allMembers = grouped
            .SelectMany(g => g.Value)
            .OrderBy(m => GetMemberSortOrder(m.Kind))
            .ThenBy(m => m.Name)
            .ToList();

        int truncated = 0;
        if (options.Limit.HasValue && options.Limit.Value < allMembers.Count)
        {
            truncated = allMembers.Count - options.Limit.Value;
            allMembers = allMembers.Take(options.Limit.Value).ToList();
        }

        // Re-group after truncation
        var kindGroups = allMembers
            .GroupBy(m => m.Kind)
            .OrderBy(g => GetMemberSortOrder(g.Key))
            .ToList();

        bool docsRequested = options.ShowDocs
            || options.Columns?.Any(c => c.Equals("Description", StringComparison.OrdinalIgnoreCase)) == true;
        bool hasDocs = docsRequested && allMembers.Any(m => m.Documentation.Summary != null);
        bool abbreviate = ShouldAbbreviateMemberSignatures(options);
        bool showSelect = options is MemberOptions mo && mo.ShowSelect;

        foreach (var group in kindGroups)
        {
            var kind = group.Key;
            var members = group
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ThenBy(GetMemberSignatureSortKey, StringComparer.Ordinal)
                .ToList();

            // Pre-compute overload counts and indices for --show-index
            var overloadCounts = showSelect
                ? members.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.Count())
                : null;
            var overloadIndices = showSelect ? new Dictionary<string, int>() : null;

            var rows = members.Select(m =>
            {
                var sig = abbreviate
                    ? SignatureParser.AbbreviateSignature(m.Signature ?? m.ReturnType ?? "")
                    : m.Signature ?? m.ReturnType ?? "";

                string? select = null;
                if (showSelect && overloadCounts != null && overloadIndices != null)
                {
                    overloadIndices.TryGetValue(m.Name, out int idx);
                    idx++;
                    overloadIndices[m.Name] = idx;
                    bool hasOverloads = overloadCounts[m.Name] > 1;
                    var selectorName = GetMemberSelectorName(m);
                    select = MarkoutInline.Code(hasOverloads ? $"{selectorName}:{idx}" : selectorName);
                }

                var sigDisplay = m.Accessibility != null ? $"{m.Accessibility} {sig}" : sig;
                string? obsoleteCell = null;
                if (m.IsObsolete)
                {
                    obsoleteCell = string.IsNullOrWhiteSpace(m.ObsoleteMessage)
                        ? "⚠ Obsolete"
                        : $"⚠ Obsolete — {m.ObsoleteMessage}";
                }
                return new MemberRow(select, OperatorNames.FormatDisplayName(m.Name), MarkoutInline.Code(sigDisplay), obsoleteCell, hasDocs ? (m.Documentation.Summary ?? "") : null);
            }).ToList();

            switch (kind)
            {
                case "constructor":
                    if (showSelect)
                    { if (hasDocs) view.ConstructorSelectRowsWithDocs = rows; else view.ConstructorSelectRows = rows; }
                    else
                    { if (hasDocs) view.ConstructorRowsWithDocs = rows; else view.ConstructorRows = rows; }
                    break;
                case "field":
                    if (showSelect)
                    { if (hasDocs) view.FieldSelectRowsWithDocs = rows; else view.FieldSelectRows = rows; }
                    else
                    { if (hasDocs) view.FieldRowsWithDocs = rows; else view.FieldRows = rows; }
                    break;
                case "property":
                    if (showSelect)
                    { if (hasDocs) view.PropertySelectRowsWithDocs = rows; else view.PropertySelectRows = rows; }
                    else
                    { if (hasDocs) view.PropertyRowsWithDocs = rows; else view.PropertyRows = rows; }
                    break;
                case "method":
                    if (showSelect)
                    { if (hasDocs) methodsView.SelectRowsWithDocs = rows; else methodsView.SelectRows = rows; }
                    else
                    { if (hasDocs) methodsView.RowsWithDocs = rows; else methodsView.Rows = rows; }
                    break;
                case "operator":
                    if (showSelect)
                    { if (hasDocs) operatorsView.SelectRowsWithDocs = rows; else operatorsView.SelectRows = rows; }
                    else
                    { if (hasDocs) operatorsView.RowsWithDocs = rows; else operatorsView.Rows = rows; }
                    break;
                case "explicit-interface-implementation":
                    if (showSelect)
                    { if (hasDocs) explicitInterfaceImplementationsView.SelectRowsWithDocs = rows; else explicitInterfaceImplementationsView.SelectRows = rows; }
                    else
                    { if (hasDocs) explicitInterfaceImplementationsView.RowsWithDocs = rows; else explicitInterfaceImplementationsView.Rows = rows; }
                    break;
                case "extension-method":
                    if (showSelect)
                    { if (hasDocs) extensionMethodsView.SelectRowsWithDocs = rows; else extensionMethodsView.SelectRows = rows; }
                    else
                    { if (hasDocs) extensionMethodsView.RowsWithDocs = rows; else extensionMethodsView.Rows = rows; }
                    break;
                case "event":
                    if (showSelect)
                    { if (hasDocs) eventsView.SelectRowsWithDocs = rows; else eventsView.SelectRows = rows; }
                    else
                    { if (hasDocs) eventsView.RowsWithDocs = rows; else eventsView.Rows = rows; }
                    break;
            }
        }

        return (truncated, "members");
    }

    internal static void PopulateMemberSignature(TypeView view, ApiType type, ApiOptions options)
    {
        if (type.Members.Count != 1)
            return;

        var member = type.Members[0];
        var sig = member.Signature ?? member.ReturnType ?? "";
        var sigDisplay = member.Accessibility != null ? $"{member.Accessibility} {sig}" : sig;
        string? obsoleteCell = null;
        if (member.IsObsolete)
        {
            obsoleteCell = string.IsNullOrWhiteSpace(member.ObsoleteMessage)
                ? "⚠ Obsolete"
                : $"⚠ Obsolete — {member.ObsoleteMessage}";
        }

        var docsRequested = options.ShowDocs
            || options.Columns?.Any(c => c.Equals("Description", StringComparison.OrdinalIgnoreCase)) == true;
        var description = docsRequested ? member.Documentation.Summary : null;

        view.SignatureRows =
        [
            new MemberSignatureRow(MarkoutInline.Code(sigDisplay), obsoleteCell, description)
        ];
    }

    /// <summary>
    /// Populates compact member summary sections for Minimal verbosity.
    /// Groups members by name within each kind, with kind-specific columns
    /// matching the old QuietMemberFormatter design.
    /// </summary>
    internal static (int truncated, string noun) PopulateMemberSummarySections(
        TypeView view, MethodGroupsView methodGroupsView, EventsView eventsView,
        ApiType type, ApiOptions options, bool methodGroupsOnly = false)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly, options.KindFilter);
        if (methodGroupsOnly)
            grouped = grouped
                .Where(kvp => kvp.Key == "method")
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        if (grouped.Count == 0) return (0, "");

        // Flatten all unique-name entries across kinds for --limit
        var allEntries = grouped
            .OrderBy(g => GetMemberSortOrder(g.Key))
            .SelectMany(kindGroup =>
                kindGroup.Value
                    .GroupBy(m => m.Name)
                    .OrderBy(g => g.Key)
                    .Select(g => (kind: kindGroup.Key, members: g.ToList()))
            )
            .ToList();

        int truncated = 0;
        if (options.Limit.HasValue && options.Limit.Value < allEntries.Count)
        {
            truncated = allEntries.Count - options.Limit.Value;
            allEntries = allEntries.Take(options.Limit.Value).ToList();
        }

        // Re-group by kind after truncation
        foreach (var kindGroup in allEntries.GroupBy(e => e.kind).OrderBy(g => GetMemberSortOrder(g.Key)))
        {
            var kind = kindGroup.Key;
            var byName = kindGroup.ToList();
            bool hasOverloads = byName.Any(e => e.members.Count > 1);

            switch (kind)
            {
                case "constructor":
                {
                    var rows = byName.Select(e =>
                        new ConstructorSummaryRow(
                            OperatorNames.FormatDisplayName(e.members[0].Name),
                            e.members.Count.ToString())).ToList();
                    if (hasOverloads)
                        view.ConstructorSummaryRowsWithOverloads = rows;
                    else
                        view.ConstructorSummaryRows = rows;
                    break;
                }
                case "method":
                {
                    var rows = byName.Select(e =>
                        new MethodSummaryRow(
                            OperatorNames.FormatDisplayName(e.members[0].Name),
                            SignatureParser.ExtractReturnType(e.members[0].Signature),
                            e.members.Count.ToString())).ToList();
                    if (hasOverloads)
                        methodGroupsView.RowsWithOverloads = rows;
                    else
                        methodGroupsView.Rows = rows;
                    break;
                }
                case "property":
                {
                    var rows = byName.Select(e =>
                    {
                        var m = e.members[0];
                        return new PropertySummaryRow(
                            m.Name,
                            SignatureParser.ExtractReturnType(m.Signature),
                            SignatureParser.ExtractAccessors(m.Signature));
                    }).ToList();
                    view.PropertySummaryRows = rows;
                    break;
                }
                case "field":
                {
                    var rows = byName.Select(e =>
                        new FieldSummaryRow(e.members[0].Name, e.members[0].ReturnType ?? "")).ToList();
                    view.FieldSummaryRows = rows;
                    break;
                }
                case "event":
                {
                    var rows = byName.Select(e =>
                    {
                        var m = e.members[0];
                        return new EventSummaryRow(m.Name, m.ReturnType ?? m.Signature ?? "");
                    }).ToList();
                    eventsView.SummaryRows = rows;
                    break;
                }
            }
        }

        return (truncated, "members");
    }

    internal static void PopulateConstructorOverloads(TypeView view, ApiType type, ApiOptions options)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly, options.KindFilter);
        var constructors = grouped
            .SelectMany(g => g.Value)
            .Where(m => m.Kind == "constructor")
            .ToList();

        if (constructors.Count == 0) return;

        var sorted = constructors
            .OrderBy(c => SignatureParser.CountParameters(c.Signature))
            .ToList();

        view.ConstructorOverloads = sorted.Select((ctor, i) =>
        {
            var paramCount = SignatureParser.CountParameters(ctor.Signature);
            var paramInfo = SignatureParser.ExtractParameterInfo(ctor.Signature);

            var overloadView = new ConstructorOverloadView
            {
                Title = $"Overload {i + 1}: {paramCount} parameter{(paramCount != 1 ? "s" : "")}",
                Signature = new CodeSection("csharp", $"new {type.Name}{SignatureParser.FormatConstructorCall(ctor.Signature)}")
            };

            if (paramInfo.Count > 0)
            {
                overloadView.Parameters = paramInfo
                    .Select(p => new ConstructorParameterRow(p.name, MarkoutInline.Code(p.type), p.hasDefault ? "optional" : "required"))
                    .ToList();
            }

            return overloadView;
        }).ToList();
    }

    internal static void PopulateIndexSections(TypeView view, ApiType type, List<ApiMember> methods, string dllPath, int overloadIndex, IReadOnlySet<string> requestedSections, string? pdbPath = null)
    {
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);

        var memberCode = new MemberCodeView();
        bool hasCode = false;
        bool wantsAttributes = requestedSections.Contains(SectionNames.CustomAttributes);
        bool wantsDecompiledSource = requestedSections.Contains(SectionNames.DecompiledSource);
        bool wantsIL = requestedSections.Contains(SectionNames.IL);
        bool wantsAnnotatedIL = requestedSections.Contains(SectionNames.ILAnnotated);

        foreach (var method in methods)
        {
            var lookupType = method.DeclaringType ?? type.FullName;
            var lookupOverloadIndex = method.DeclaringOverloadIndex is { } declaringIndex
                ? declaringIndex - 1
                : overloadIndex;
            var publicOnly = method.Kind != "explicit-interface-implementation";

            // Custom attributes
            if (wantsAttributes)
            {
                var attributes = AttributeReader.GetMethodAttributes(
                    peReader, lookupType, method.Name, lookupOverloadIndex, publicOnly);
                if (attributes.Count > 0)
                {
                    view.MethodAttributeRows = attributes
                        .Select(a => new MethodAttributeRow(a.Name, a.Value ?? ""))
                        .ToList();
                }
            }

            // Lowered C#
            if (wantsDecompiledSource)
            {
                try
                {
                    var context = Decompiler.MethodBodyContext.Create(
                        peReader, lookupType, method.Name, lookupOverloadIndex, publicOnly, externalPdbPath: pdbPath);
                    if (context != null)
                    {
                        var lowered = Decompiler.CSharpEmitter.Emit(context);
                        if (!string.IsNullOrWhiteSpace(lowered))
                        {
                            var source = FormatLoweredSourceWithDeclaration(type, method, context, lowered);
                            memberCode.DecompiledSourceCode = new CodeSection("csharp", source);
                            hasCode = true;
                        }
                    }
                }
                catch { }
            }

            // IL disassembly
            if (wantsIL)
            {
                var instructions = ILDisassembler.DisassembleMethod(
                    peReader, lookupType, method.Name, lookupOverloadIndex, publicOnly);
                if (instructions is { Count: > 0 })
                {
                    var ilText = string.Join(Environment.NewLine, instructions.Select(i => i.ToString()));
                    memberCode.ILCode = new CodeSection("il", ilText);
                    hasCode = true;
                }
            }

            // Annotated IL
            if (wantsAnnotatedIL)
            {
                try
                {
                    var context = Decompiler.MethodBodyContext.Create(
                        peReader, lookupType, method.Name, lookupOverloadIndex, publicOnly, externalPdbPath: pdbPath);
                    if (context != null)
                    {
                        var annotated = Decompiler.AnnotatedILEmitter.Emit(
                            context, Decompiler.ILAnnotationDepth.Structured);
                        if (!string.IsNullOrWhiteSpace(annotated))
                        {
                            memberCode.AnnotatedIL = new CodeSection("il", annotated.TrimEnd());
                            hasCode = true;
                        }
                    }
                }
                catch { }
            }
        }

        if (hasCode)
            view.MemberCode = memberCode;
    }

    private static string FormatLoweredSourceWithDeclaration(ApiType type, ApiMember member, Decompiler.MethodBodyContext context, string lowered)
    {
        var declaration = FormatMemberDeclaration(type, member, context);
        var body = lowered.TrimEnd();
        if (string.IsNullOrWhiteSpace(declaration))
            return body;

        var indentedBody = string.Join(
            Environment.NewLine,
            body.ReplaceLineEndings("\n").Split('\n').Select(line => line.Length == 0 ? "" : $"    {line}"));

        return $"{declaration}{Environment.NewLine}{{{Environment.NewLine}{indentedBody}{Environment.NewLine}}}";
    }

    private static string FormatMemberDeclaration(ApiType type, ApiMember member, Decompiler.MethodBodyContext context)
    {
        var signature = member.Signature ?? member.ReturnType ?? "";
        if (string.IsNullOrWhiteSpace(signature))
            return "";

        if (member.Kind == "constructor")
        {
            var typeName = FormatConstructorTypeName(type.Name);
            signature = $"{typeName}{SignatureParser.FormatConstructorCall(signature)}";
        }
        else if (member.Name.StartsWith("op_", StringComparison.Ordinal))
        {
            signature = FormatOperatorSignature(signature, member.Name);
        }
        else if (member.Kind is "method" or "extension-method")
        {
            if (context.GenericContext?.MethodParameters is { Count: > 0 } methodParameters)
                signature = AddMethodGenericParameters(signature, member.Name, methodParameters);
            if (member.IsExtension)
                signature = AddExtensionThisModifier(signature);
        }

        List<string> modifiers = member.Kind == "explicit-interface-implementation"
            ? []
            : [member.Accessibility ?? "public"];
        if (member.IsStatic)
            modifiers.Add("static");

        return modifiers.Count == 0
            ? signature
            : $"{string.Join(" ", modifiers)} {signature}";
    }

    private static string FormatOperatorSignature(string signature, string methodName)
    {
        var parenStart = signature.IndexOf('(');
        if (parenStart <= 0)
            return signature;

        var nameIndex = signature.LastIndexOf(methodName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return signature;

        var returnType = signature[..nameIndex].TrimEnd();
        var parameters = signature[parenStart..];

        if (methodName.StartsWith("op_Checked", StringComparison.Ordinal)
            && TryGetCheckedOperatorSymbol(methodName["op_Checked".Length..]) is { } checkedSymbol)
            return $"{returnType} operator checked {checkedSymbol}{parameters}";

        return methodName switch
        {
            "op_Implicit" => $"implicit operator {returnType}{parameters}",
            "op_Explicit" => $"explicit operator {returnType}{parameters}",
            "op_CheckedExplicit" => $"explicit operator checked {returnType}{parameters}",
            _ => $"{returnType} {OperatorNames.FormatDisplayName(methodName)}{parameters}"
        };
    }

    private static string? TryGetCheckedOperatorSymbol(string suffix) => suffix switch
    {
        "Addition" => "+",
        "Subtraction" => "-",
        "Multiply" => "*",
        "Division" => "/",
        "Increment" => "++",
        "Decrement" => "--",
        "UnaryNegation" => "-",
        _ => null
    };

    private static string FormatConstructorTypeName(string name)
    {
        var arityIndex = name.IndexOf('`');
        return arityIndex < 0 ? name : name[..arityIndex];
    }

    private static string AddMethodGenericParameters(string signature, string methodName, IReadOnlyList<string> methodParameters)
    {
        if (methodParameters.Count == 0 || string.IsNullOrEmpty(methodName))
            return signature;

        var parenStart = signature.IndexOf('(');
        if (parenStart <= 0)
            return signature;

        var nameIndex = signature.LastIndexOf(methodName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return signature;

        var insertAt = nameIndex + methodName.Length;
        if (insertAt < parenStart && signature[insertAt] == '<')
            return signature;

        return signature.Insert(insertAt, $"<{string.Join(", ", methodParameters)}>");
    }

    private static string AddExtensionThisModifier(string signature)
    {
        var parenStart = signature.IndexOf('(');
        var parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart + 1)
            return signature;

        var firstParameterStart = parenStart + 1;
        while (firstParameterStart < signature.Length && char.IsWhiteSpace(signature[firstParameterStart]))
            firstParameterStart++;

        if (signature.AsSpan(firstParameterStart).StartsWith("this ".AsSpan(), StringComparison.Ordinal))
            return signature;

        return signature.Insert(firstParameterStart, "this ");
    }

    // ===== Helper Methods =====

    internal static Dictionary<string, List<ApiMember>> GroupMembersByKind(ApiType type, HashSet<string>? memberFilter = null, bool unsafeOnly = false, HashSet<string>? kindFilter = null)
    {
        var members = type.Members
            .Where(m => !IsCompilerGenerated(m.Name))
            .ToList();

        if (memberFilter?.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, memberFilter)).ToList();

        if (unsafeOnly)
            members = members.Where(m => m.IsUnsafe).ToList();

        if (kindFilter?.Count > 0)
            members = members.Where(m => kindFilter.Contains(m.Kind)).ToList();

        return members
            .GroupBy(m => m.Kind)
            .OrderBy(g => GetMemberSortOrder(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    internal static string FormatGenericTypeName(string name, List<TypeParameter>? typeParameters)
    {
        int backtickIndex = name.IndexOf('`');
        if (backtickIndex < 0)
            return name;

        var baseName = name[..backtickIndex];
        if (typeParameters is { Count: > 0 })
            return $"{baseName}<{string.Join(", ", typeParameters.Select(tp => tp.Name))}>";

        if (int.TryParse(name[(backtickIndex + 1)..], out int arity) && arity > 0)
        {
            var names = arity == 1 ? "T" : string.Join(", ", Enumerable.Range(1, arity).Select(i => $"T{i}"));
            return $"{baseName}<{names}>";
        }

        return name;
    }

    internal static string FormatGenericFullName(ApiType type)
    {
        var displayName = FormatGenericTypeName(type.Name, type.TypeParameters);
        return string.IsNullOrEmpty(type.Namespace) ? displayName : $"{type.Namespace}.{displayName}";
    }

    internal static string GetMemberSignatureSortKey(ApiMember member)
    {
        var signature = member.Signature ?? "";
        if (signature.Length == 0 || member.Name.Length == 0)
            return signature;

        var searchStart = 0;
        while (searchStart < signature.Length)
        {
            var nameIndex = signature.IndexOf(member.Name, searchStart, StringComparison.Ordinal);
            if (nameIndex < 0)
                return signature;

            var genericStart = nameIndex + member.Name.Length;
            if (genericStart < signature.Length && signature[genericStart] == '<')
            {
                var depth = 0;
                for (var i = genericStart; i < signature.Length; i++)
                {
                    if (signature[i] == '<')
                        depth++;
                    else if (signature[i] == '>')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            if (i + 1 < signature.Length && signature[i + 1] == '(')
                                return signature.Remove(genericStart, i - genericStart + 1);
                            break;
                        }
                    }
                }
            }

            searchStart = nameIndex + member.Name.Length;
        }

        return signature;
    }

    private static string GetMemberSelectorName(ApiMember member) => member.Kind switch
    {
        "operator" => $"operator:{member.Name}",
        "explicit-interface-implementation" => $"explicit:{member.Name}",
        "extension-method" => $"extension:{member.Name}",
        _ => member.Name
    };

    private static string PluralizeKind(string kind) => kind switch
    {
        "property" => "Properties",
        "method" => "Methods",
        "operator" => "Operators",
        "explicit-interface-implementation" => "Explicit Interface Implementations",
        "extension-method" => "Extension Methods",
        "field" => "Fields",
        "event" => "Events",
        "constructor" => "Constructors",
        _ => char.ToUpper(kind[0]) + kind[1..] + "s"
    };

    private static bool IsCompilerGenerated(string name) => MemberFilters.IsCompilerGenerated(name);

    private static readonly string[] MemberKinds =
    [
        "constructor",
        "field",
        "property",
        "method",
        "operator",
        "explicit-interface-implementation",
        "extension-method",
        "event"
    ];

    internal static int GetMemberSortOrder(string kind)
    {
        var index = Array.IndexOf(MemberKinds, kind);
        return index >= 0 ? index : MemberKinds.Length;
    }

    // ===== One-Line View Builders =====

    /// <summary>
    /// Builds a unified tabular view for a single type's members.
    /// All member kinds are merged into one table with a Kind column.
    /// </summary>
    internal static (ApiTypeOneLineView view, int truncated) BuildTypeOneLineView(ApiType type, ApiOptions options)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly, options.KindFilter);
        var requestedKinds = GetRequestedMemberKinds(options.IncludeSections);
        if (requestedKinds is { Count: > 0 })
            grouped = grouped
                .Where(kvp => requestedKinds.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        if (grouped.Count == 0) return (new ApiTypeOneLineView(), 0);

        var allEntries = grouped
            .OrderBy(g => GetMemberSortOrder(g.Key))
            .SelectMany(kindGroup =>
                kindGroup.Value
                    .GroupBy(m => m.Name)
                    .OrderBy(g => g.Key)
                    .Select(g => (kind: kindGroup.Key, members: g.ToList()))
            )
            .ToList();

        int truncated = 0;
        if (options.Limit.HasValue && options.Limit.Value < allEntries.Count)
        {
            truncated = allEntries.Count - options.Limit.Value;
            allEntries = allEntries.Take(options.Limit.Value).ToList();
        }

        var rows = allEntries.Select(e =>
        {
            var m = e.members[0];
            var returnType = e.kind switch
            {
                "constructor" => "",
                "event" => m.ReturnType ?? m.Signature ?? "",
                _ => SignatureParser.ExtractReturnType(m.Signature)
            };
            var detail = e.kind switch
            {
                "property" => SignatureParser.ExtractAccessors(m.Signature),
                "constructor" or "method" or "operator" or "explicit-interface-implementation" or "extension-method" when e.members.Count > 1 => e.members.Count.ToString(),
                "constructor" or "method" or "operator" or "explicit-interface-implementation" or "extension-method" when e.members.Count == 1 => SignatureParser.ExtractParamList(m.Signature),
                _ => ""
            };
            var kindLabel = m.Accessibility != null ? $"{m.Accessibility} {e.kind}" : e.kind;
            return new ApiOneLineRow(kindLabel, OperatorNames.FormatDisplayName(m.Name), returnType, detail);
        }).ToList();

        return (new ApiTypeOneLineView { Rows = rows }, truncated);
    }

    /// <summary>
    /// Builds a unified tabular view for a full API surface (all types).
    /// All type kinds are merged into one table with a Kind column.
    /// </summary>
    internal static (ApiSurfaceOneLineView view, int truncated) BuildSurfaceOneLineView(ApiSurface api, ApiOptions options)
    {
        int truncated = 0;
        var types = api.Types;
        var requestedKinds = GetRequestedTypeKinds(options.IncludeSections);
        if (requestedKinds is { Count: > 0 })
            types = types.Where(t => requestedKinds.Contains(t.Kind)).ToList();
        if (options.Limit.HasValue && options.Limit.Value < types.Count)
        {
            truncated = types.Count - options.Limit.Value;
            types = types.Take(options.Limit.Value).ToList();
        }

        bool showDescription = options.ShowDocs
            || options.Columns?.Any(c => c.Equals("Description", StringComparison.OrdinalIgnoreCase)) == true;

        var rows = types
            .OrderBy(t => GetTypeKindSortOrder(t.Kind))
            .ThenBy(t => t.FullName)
            .Select(t =>
            {
                string? desc = null;
                if (showDescription)
                {
                    desc = t.Documentation.Summary;
                    if (desc != null)
                    {
                        desc = desc.ReplaceLineEndings(" ");
                        if (desc.Length > 80) desc = desc[..77] + "...";
                    }
                }
                return new ApiSurfaceOneLineRow(
                    t.Kind,
                    FormatGenericFullName(t),
                    t.Members.Count.ToString(),
                    desc);
            })
            .ToList();

        var view = new ApiSurfaceOneLineView();
        if (showDescription)
            view.RowsWithDescription = rows;
        else
            view.Rows = rows;

        return (view, truncated);
    }

    private static int GetTypeKindSortOrder(string kind) => kind switch
    {
        "class" => 0,
        "struct" => 1,
        "interface" => 2,
        "enum" => 3,
        "delegate" => 4,
        _ => 5
    };

    private static int GetTreeKindOrder(string kind) => kind switch
    {
        "constructor" => 0,
        "field" => 1,
        "property" => 2,
        "method" => 3,
        "operator" => 4,
        "explicit-interface-implementation" => 5,
        "extension-method" => 6,
        "event" => 7,
        _ => 8
    };

    private static string GetTreeKindLabel(string kind, int count)
    {
        var plural = kind switch
        {
            "property" => "Properties",
            "method" => "Methods",
            "operator" => "Operators",
            "explicit-interface-implementation" => "Explicit Interface Implementations",
            "extension-method" => "Extension Methods",
            "constructor" => "Constructors",
            "event" => "Events",
            "field" => "Fields",
            _ => kind + "s"
        };
        return $"{plural} ({count})";
    }

    private static HashSet<string>? GetRequestedMemberKinds(HashSet<string>? includeSections)
    {
        if (includeSections is not { Count: > 0 })
            return null;

        HashSet<string> kinds = [];
        foreach (var section in includeSections)
        {
            switch (section)
            {
                case "Constructors":
                    kinds.Add("constructor");
                    break;
                case "Fields":
                    kinds.Add("field");
                    break;
                case "Properties":
                    kinds.Add("property");
                    break;
                case "Method Groups":
                case "Methods":
                    kinds.Add("method");
                    break;
                case "Operators":
                    kinds.Add("operator");
                    break;
                case "Explicit Interface Implementations":
                    kinds.Add("explicit-interface-implementation");
                    break;
                case "Extension Methods":
                    kinds.Add("extension-method");
                    break;
                case "Events":
                    kinds.Add("event");
                    break;
            }
        }

        return kinds;
    }

    private static HashSet<string>? GetRequestedTypeKinds(HashSet<string>? includeSections)
    {
        if (includeSections is not { Count: > 0 })
            return null;

        HashSet<string> kinds = [];
        foreach (var section in includeSections)
        {
            switch (section)
            {
                case "Classes":
                    kinds.Add("class");
                    break;
                case "Structs":
                    kinds.Add("struct");
                    break;
                case "Interfaces":
                    kinds.Add("interface");
                    break;
                case "Enums":
                    kinds.Add("enum");
                    break;
                case "Delegates":
                    kinds.Add("delegate");
                    break;
            }
        }

        return kinds;
    }
}
