using DotnetInspector.Inspectors;
using DotnetInspector.Core;
using ILInspector.Metadata;
using System.Security.Cryptography;
using System.Text;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

using Decompiler = ILInspector.Decompiler;
using Analysis = ILInspector.Analysis;

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
                return new TypeSummaryRow(group.Key, MarkoutInline.Code(fullName), members, desc);
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
        MemberIndexView? memberIndexView,
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
        if (memberIndexView is { HasRows: true })
            ApiViewContext.Default.Serialize(memberIndexView, writer);
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

    public static void WriteShapeOutput(
        ApiType type,
        string? foundIn,
        string? packageName,
        string? packageVersion,
        HashSet<string> memberFilter,
        HashSet<string>? kindFilter = null,
        Verbosity verbosity = Verbosity.Minimal)
    {
        var view = BuildShapeView(type, foundIn, packageName, packageVersion, memberFilter, kindFilter, verbosity);
        if (view.Members is { Count: > 0 })
        {
            // Lead with a declaration-style header when the type carries modifiers
            // (ref/readonly struct, static/sealed/abstract class) so the spelling is
            // not silently dropped (#1066); a plain type keeps its bare name header.
            string header = view.Modifiers is { Length: > 0 } modifiers
                ? $"{modifiers.Replace(", ", " ")} {view.Kind} {view.FullName}"
                : view.FullName;
            Console.WriteLine(header);
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

        var modifiers = BuildTypeModifiers(type);

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

        bool topFieldsOnly = options.Verbosity == Verbosity.Quiet
            || (options is TypeOptions { MarkdownExplicitlySet: true } && !memberDetail);
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
            SourceUrl = SelectSourceUrl(type.SourceUrl, options.BrowsableUrls),
            AdditionalSourceFiles = SelectSourceFiles(type.AdditionalSourceFiles, options.BrowsableUrls),
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

    internal static TypeShapeView BuildShapeView(
        ApiType type,
        string? foundIn,
        string? packageName,
        string? packageVersion,
        HashSet<string> memberFilter,
        HashSet<string>? kindFilter = null,
        Verbosity verbosity = Verbosity.Minimal)
    {
        bool hasFilter = memberFilter.Count > 0 || kindFilter?.Count > 0;
        bool expandOverloads = verbosity >= Verbosity.Normal;
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
                var membersInGroup = group.ToList();
                var children = BuildShapeMemberNodes(group.Key, membersInGroup, expandOverloads);
                var logicalCount = IsOverloadGroupedKind(group.Key)
                    ? membersInGroup.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count()
                    : membersInGroup.Count;
                var kindLabel = GetShapeKindLabel(group.Key, membersInGroup.Count, logicalCount);
                nodes.Add(new TreeNode(kindLabel) { Children = children });
            }
        }

        static List<TreeNode> BuildShapeMemberNodes(string kind, IEnumerable<ApiMember> members, bool expandOverloads)
        {
            if (IsOverloadGroupedKind(kind))
            {
                var groups = members
                    .GroupBy(m => m.Name)
                    .OrderBy(g => OperatorNames.FormatDisplayName(g.Key), StringComparer.Ordinal)
                    .ToList();

                if (expandOverloads)
                {
                    return groups
                        .SelectMany(g => g.OrderBy(GetMemberSignatureSortKey, StringComparer.Ordinal))
                        .Select(m => new TreeNode(m.Signature ?? OperatorNames.FormatDisplayName(m.Name)))
                        .ToList();
                }

                return groups
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

        static string GetShapeKindLabel(string kind, int memberCount, int logicalCount)
        {
            if (IsOverloadGroupedKind(kind) && memberCount != logicalCount)
            {
                var noun = kind switch
                {
                    "constructor" => "Constructors",
                    "method" => "Methods",
                    "operator" => "Operators",
                    "explicit-interface-implementation" => "Explicit Interface Implementations",
                    "extension-method" => "Extension Methods",
                    _ => GetTreeKindLabel(kind, memberCount).Split(' ')[0]
                };
                return $"{noun} ({logicalCount} logical, {memberCount} overloads)";
            }

            return GetTreeKindLabel(kind, memberCount);
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

        var modifiers = BuildTypeModifiers(type);

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

    private static List<string> BuildTypeModifiers(ApiType type)
    {
        List<string> modifiers = [];
        if (type.IsStatic)
        {
            modifiers.Add("static");
        }
        else
        {
            if (type.IsAbstract && type.Kind == "class") modifiers.Add("abstract");
            if (type.IsSealed && type.Kind == "class") modifiers.Add("sealed");
        }

        if (type.IsReadOnly && type.Kind == "struct") modifiers.Add("readonly");
        if (type.IsByRefLike && type.Kind == "struct") modifiers.Add("ref");
        return modifiers;
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
        bool showSelect = false;

        foreach (var group in kindGroups)
        {
            var kind = group.Key;
            var members = group
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ThenBy(GetMemberSignatureSortKey, StringComparer.Ordinal)
                .ToList();

            // Historical Select-column path is disabled; selector rows live in Member Index.
            var overloadCounts = showSelect
                ? members.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.Count())
                : null;
            var overloadIndices = showSelect ? new Dictionary<string, int>() : null;

            var rows = members.Select(m =>
            {
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

                var sigDisplay = FormatMemberDeclaration(type, m, abbreviate: abbreviate);
                return new MemberRow(select, OperatorNames.FormatDisplayName(m.Name), MarkoutInline.Code(sigDisplay), hasDocs ? (m.Documentation.Summary ?? "") : null);
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
        var sigDisplay = FormatMemberDeclaration(type, member, abbreviate: false);

        var docsRequested = options.ShowDocs
            || options.Columns?.Any(c => c.Equals("Description", StringComparison.OrdinalIgnoreCase)) == true;
        var description = docsRequested ? member.Documentation.Summary : null;

        view.SignatureRows =
        [
            new MemberSignatureRow(MarkoutInline.Code(sigDisplay), description)
        ];
    }

    internal static void PopulateMemberIndex(MemberIndexView view, ApiType type, ApiOptions options)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly, options.KindFilter);
        var requestedKinds = GetRequestedMemberKinds(options.IncludeSections);
        if (requestedKinds is { Count: > 0 })
            grouped = grouped
                .Where(kvp => requestedKinds.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var allMembers = grouped
            .SelectMany(g => g.Value)
            .OrderBy(m => GetMemberSortOrder(m.Kind))
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(GetMemberSignatureSortKey, StringComparer.Ordinal)
            .ToList();

        if (options.Limit.HasValue && options.Limit.Value < allMembers.Count)
        {
            allMembers = allMembers.Take(options.Limit.Value).ToList();
        }

        if (allMembers.Count == 0)
            return;

        view.Rows = BuildMemberIndexRows(type, allMembers);
    }

    internal static void PopulateMemberSourceLocations(TypeView view, ApiType type, ApiOptions options)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly, options.KindFilter);
        var members = grouped
            .SelectMany(g => g.Value)
            .Where(ApiMemberSectionDescriptors.IsMethodLike)
            .OrderBy(m => GetMemberSortOrder(m.Kind))
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(GetMemberSignatureSortKey, StringComparer.Ordinal)
            .ToList();

        if (options.Limit.HasValue && options.Limit.Value < members.Count)
            members = members.Take(options.Limit.Value).ToList();

        bool detail = options is MemberOptions { OverloadIndex: not null } && type.Members.Count == 1;
        List<MemberIndexRow> indexRows = detail ? [] : BuildMemberIndexRows(type, members);
        List<MemberSourceLocationRow> rows = [];

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member.SourceFilePath is null && member.SourceUrl is null && member.SourceLineNumber is null)
                continue;

            var signature = FormatMemberDeclaration(type, member, abbreviate: false);
            int? endLine = member.SourceEndLineNumber ?? member.SourceLineNumber;

            rows.Add(new MemberSourceLocationRow(
                detail ? null : indexRows[i].Selector,
                string.IsNullOrWhiteSpace(signature) ? null : MarkoutInline.Code(signature),
                member.SourceFilePath is null ? null : MarkoutInline.Code(member.SourceFilePath),
                member.SourceLineNumber,
                endLine,
                SelectSourceUrl(member.SourceUrl, options.BrowsableUrls)));
        }

        view.SourceLocationRows = rows;
    }

    private static string? SelectSourceUrl(string? url, bool browsableUrls)
        => browsableUrls && url != null
            ? GitHubUrlResolver.ConvertRawToBlobUrl(url)
            : url;

    private static List<PartialSourceFileInfo> SelectSourceFiles(
        List<PartialSourceFileInfo> files,
        bool browsableUrls)
        => browsableUrls
            ? files.Select(file => new PartialSourceFileInfo
            {
                FilePath = file.FilePath,
                SourceUrl = SelectSourceUrl(file.SourceUrl, browsableUrls),
                GitHubBrowseUrl = file.GitHubBrowseUrl
            }).ToList()
            : files;

    internal static List<MemberIndexRow> BuildMemberIndexRows(ApiType type, IReadOnlyList<ApiMember> members)
    {
        var overloadCounts = members
            .GroupBy(m => GetMemberSelectorName(m), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var overloadIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        List<MemberIndexRow> rows = [];

        foreach (var member in members)
        {
            var selectorName = GetMemberSelectorName(member);
            overloadIndices.TryGetValue(selectorName, out var index);
            index++;
            overloadIndices[selectorName] = index;

            var canonicalSignature = GetCanonicalSignature(type, member);
            var digest = GetMemberDigest(canonicalSignature);
            var selector = overloadCounts[selectorName] > 1
                ? $"{selectorName}:{index}"
                : selectorName;
            var stableSelector = $"{selectorName}~{digest}";

            rows.Add(new MemberIndexRow(
                MarkoutInline.Code(selector),
                MarkoutInline.Code(stableSelector),
                MarkoutInline.Code(canonicalSignature),
                digest));
        }

        return rows;
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
                            MemberReturnType(e.members[0]),
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
                            MemberReturnType(m),
                            MemberAccessors(m));
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

    internal static void PopulateIndexSections(TypeView view, ApiType type, List<ApiMember> methods, string dllPath, int? overloadIndex, IReadOnlySet<string> requestedSections, string? pdbPath = null, IReadOnlySet<string>? explicitSections = null, IReadOnlyList<string>? callerScopeAssemblies = null, ApiOptions? options = null)
    {
        var request = new MemberCodeProvider.Request(
            DecompiledSource: requestedSections.Contains(SectionNames.DecompiledSource),
            AnnotatedSource: requestedSections.Contains(SectionNames.AnnotatedSource),
            CostOverlay: requestedSections.Contains(SectionNames.CostOverlay),
            SemanticsOverlay: requestedSections.Contains(SectionNames.SemanticsOverlay),
            IL: requestedSections.Contains(SectionNames.IL),
            Attributes: requestedSections.Contains(SectionNames.CustomAttributes),
            Calls: requestedSections.Contains(SectionNames.Calls),
            Callers: requestedSections.Contains(SectionNames.Callers),
            CallGraph: requestedSections.Contains(SectionNames.CallGraph),
            UnsafeOperations: requestedSections.Contains(SectionNames.UnsafeOperations),
            Facts: requestedSections.Contains(SectionNames.Facts),
            ProjectAssetsPath: options?.ProjectAssetsPath,
            TargetFramework: options?.Tfm);

        // An index-backed section that is explicitly selected (via -S or a category like
        // @Audit) renders an empty-state note instead of vanishing when it yields no rows.
        // Sections merely auto-included by verbosity stay silent when empty.
        bool ExplicitlySelected(string section) =>
            explicitSections is not null && explicitSections.Contains(section);

        var memberCode = new MemberCodeView();
        bool hasCode = false;

        // For sections that require a single selected method (Calls, CallGraph, decompiled source, etc.),
        // filter to that specific overload. Callers can aggregate across all overloads.
        var singleMethod = overloadIndex.HasValue
            ? methods.Count == 1
                ? methods[0]
                : overloadIndex.Value < methods.Count
                    ? methods[overloadIndex.Value]
                    : null
            : null;
        var singleMethodList = singleMethod != null ? new List<ApiMember> { singleMethod } : new List<ApiMember>();

        if (request.Calls && singleMethodList is [{ MetadataToken: { } token } callsMethod])
        {
            RequestTelemetry.Breadcrumb("il-analysis.calls", callsMethod.Name);
            var index = Analysis.LibraryBodyIndex.Open(dllPath);
            var rows = index.DirectCalls
                .Where(call => call.Caller.MetadataToken == token)
                .OrderBy(call => call.ILOffset)
                .Select(call => new CallSiteRow(
                    MarkoutInline.Code($"IL_{call.ILOffset:X4}"),
                    string.IsNullOrEmpty(call.Opcode) ? FormatOpcode(call.Kind) : call.Opcode,
                    FormatCallsiteKind(call.Kind),
                    MarkoutInline.Code(FormatCallee(call.Callee)),
                    MarkoutInline.Code($"0x{call.OperandToken:X8}"),
                    call.ReturnAddress is { } returnAddress
                        ? MarkoutInline.Code($"IL_{returnAddress:X4}")
                        : null))
                .ToList();
            if (rows.Count > 0 || ExplicitlySelected(SectionNames.Calls))
            {
                memberCode.CallRows = rows;
                hasCode = true;
            }
        }

        if (requestedSections.Contains(SectionNames.ExceptionRegions) && singleMethodList is [{ MetadataToken: { } exceptionToken } exceptionMethod])
        {
            RequestTelemetry.Breadcrumb("il-analysis.exception-regions", exceptionMethod.Name);
            using var pdbContext = PdbContext.Open(dllPath);
            var regions = pdbContext.ResolveExceptionRegions(exceptionToken, out var error)
                .Select(region => new ExceptionRegionRow(
                    region.Region,
                    region.Clause,
                    FormatILRange(region.TryStart, region.TryEnd),
                    FormatILRange(region.HandlerStart, region.HandlerEnd),
                    region.FilterStart is { } filterStart && region.FilterEnd is { } filterEnd
                        ? FormatILRange(filterStart, filterEnd)
                        : null,
                    region.CaughtType))
                .ToList();
            if (regions.Count > 0 || ExplicitlySelected(SectionNames.ExceptionRegions))
            {
                memberCode.ExceptionRegionRows = regions;
                hasCode = true;
            }
        }

        if (request.Callers && methods.Count > 0)
        {
            RequestTelemetry.Breadcrumb("il-analysis.callers", $"{methods.Count} member(s)");
            var index = Analysis.LibraryBodyIndex.Open(dllPath);
            var ownSource = Path.GetFileNameWithoutExtension(dllPath);
            var rows = new List<CallerSiteRow>();

            // Collect callers for each method (all overloads if multiple methods selected)
            foreach (var method in methods.Where(m => m.MetadataToken.HasValue))
            {
                var targetToken = method.MetadataToken!.Value;

                // Match call edges whose callee resolves to the selected member. The
                // operand-token equality catches direct same-assembly references
                // (including abstract/interface members that have no body and so are
                // absent from index.Methods); the structural pattern (built from the
                // selected member's own identity) adds MemberRef-form references.
                var selected = index.Methods.FirstOrDefault(m => m.MetadataToken == targetToken);
                var pattern = selected is { } identity
                    ? Analysis.MemberPattern.Method(identity.DeclaringType, identity.Name, identity.ParameterTypes)
                    : null;

                // Same-assembly callers
                rows.AddRange(index.DirectCalls
                    .Where(call => call.CalleeDefinitionToken == targetToken || (pattern is not null && pattern.Matches(call.Callee)))
                    .Select(call => CreateCallerRow(ownSource, call)));

                // Cross-assembly scope: scan each additional assembly for inbound callers using the
                // structural pattern only (operand tokens are assembly-local), attributing each hit
                // to its source assembly. Results stay in a single Callers table with a Source column.
                if (pattern is not null && callerScopeAssemblies is { Count: > 0 })
                {
                    foreach (var scopePath in callerScopeAssemblies)
                    {
                        Analysis.LibraryBodyIndex scopeIndex;
                        try
                        {
                            scopeIndex = Analysis.LibraryBodyIndex.Open(scopePath);
                        }
                        catch
                        {
                            continue;
                        }

                        // Cross-assembly matching must normalize generic member identity: a
                        // constructed call site (List<int>.Add / Id<int>) matches the open
                        // target definition (List<T>.Add / Id<T>) it could never match exactly
                        // (#1339). Same-assembly matching above stays exact, token-driven.
                        var scopeSource = Path.GetFileNameWithoutExtension(scopePath);
                        rows.AddRange(scopeIndex.DirectCalls
                            .Where(call => pattern.MatchesCrossAssembly(call.Callee))
                            .Select(call => CreateCallerRow(scopeSource, call)));
                    }
                }
            }

            // Deduplicate and sort
            rows = rows
                .GroupBy(row => (row.Source, row.Caller, row.ILOffset, row.OperandToken))
                .Select(g => g.First())
                .OrderBy(row => row.Source, StringComparer.Ordinal)
                .ThenBy(row => row.Caller, StringComparer.Ordinal)
                .ThenBy(row => row.ILOffset, StringComparer.Ordinal)
                .ToList();

            if (rows.Count > 0 || ExplicitlySelected(SectionNames.Callers))
            {
                memberCode.CallerRows = rows;
                hasCode = true;
            }
        }

        if (request.CallGraph && singleMethodList is [{ MetadataToken: { } graphToken } graphMethod])
        {
            RequestTelemetry.Breadcrumb("il-analysis.call-graph", graphMethod.Name);
            var index = Analysis.LibraryBodyIndex.Open(dllPath);
            var root = ToCallGraphNode(index.BuildCallTree(graphToken), GetRequestedCallGraphFields(options));
            if (root.Children is { Count: > 0 })
            {
                memberCode.CallGraphNodes = [root];
                hasCode = true;
            }
            else if (ExplicitlySelected(SectionNames.CallGraph))
            {
                // No outbound calls: render the empty-state note instead of a lone root node.
                memberCode.CallGraphNodes = [];
                hasCode = true;
            }
        }

        if (requestedSections.Contains(SectionNames.CallerGraph) && singleMethodList is [{ MetadataToken: { } callerGraphToken } callerGraphMethod])
        {
            RequestTelemetry.Breadcrumb("il-analysis.caller-graph", callerGraphMethod.Name);
            var index = Analysis.LibraryBodyIndex.Open(dllPath);
            // Extend the reverse graph across the caller scope (--bin/--project/--caller-package)
            // so a dependency member surfaces the product entry points and callers that reach it.
            var scopeIndexes = new List<Analysis.LibraryBodyIndex>();
            if (callerScopeAssemblies is { Count: > 0 })
            {
                foreach (var scopePath in callerScopeAssemblies)
                {
                    try
                    {
                        scopeIndexes.Add(Analysis.LibraryBodyIndex.Open(scopePath));
                    }
                    catch
                    {
                        // Best-effort: an unreadable scope assembly just doesn't contribute callers.
                    }
                }
            }
            var callerTree = scopeIndexes.Count > 0
                ? index.BuildCallerTree(callerGraphToken, scopeIndexes)
                : index.BuildCallerTree(callerGraphToken);
            var root = ToCallGraphNode(callerTree, GetRequestedCallGraphFields(options));
            if (root.Children is { Count: > 0 } || ExplicitlySelected(SectionNames.CallerGraph))
            {
                memberCode.CallerGraphNodes = [root];
                hasCode = true;
            }
        }

        if (request.UnsafeOperations && singleMethodList is [{ MetadataToken: { } unsafeToken } unsafeMethod])
        {
            RequestTelemetry.Breadcrumb("il-analysis.unsafe", unsafeMethod.Name);
            var index = Analysis.LibraryBodyIndex.Open(dllPath);
            var evidence = index.UnsafeEvidence
                .Where(evidence => evidence.Member.MetadataToken == unsafeToken)
                .OrderBy(evidence => evidence.ILOffset ?? -1)
                .ThenBy(evidence => evidence.Reason, StringComparer.Ordinal)
                .ThenBy(evidence => evidence.Detail, StringComparer.Ordinal)
                .ToList();

            var apiMember = evidence
                .Where(IsUnsafeApiMemberEvidence)
                .Select(evidence => evidence.Detail)
                .Distinct(StringComparer.Ordinal)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(apiMember))
                AddOrReplaceSummaryField(view, "Member", apiMember);

            if (request.UnsafeOperations)
            {
                var rows = evidence
                    .Where(evidence => !IsUnsafeApiMemberEvidence(evidence))
                    .Select(evidence => new UnsafeOperationRow(
                        evidence.Reason,
                        MarkoutInline.Code(evidence.Detail),
                        evidence.Kind,
                        evidence.ILOffset is { } offset ? MarkoutInline.Code($"IL_{offset:X4}") : null,
                        evidence.OperandToken is { } token ? MarkoutInline.Code($"0x{token:X8}") : null))
                    .ToList();
                // When the member's unsafe nature is captured as an API-member in the Summary,
                // an empty operations table would be misleading, so suppress the empty-state note
                // in that case. Show it only when explicitly selected and nothing was reported.
                if (rows.Count > 0
                    || (ExplicitlySelected(SectionNames.UnsafeOperations) && string.IsNullOrEmpty(apiMember)))
                {
                    memberCode.UnsafeOperationRows = rows;
                    hasCode = true;
                }
            }
        }

        if (requestedSections.Overlaps(SemanticFactSections) && singleMethodList is [{ MetadataToken: { } semanticToken } semanticMethod])
        {
            RequestTelemetry.Breadcrumb("il-analysis.semantic-facts", semanticMethod.Name);
            var index = Analysis.LibraryBodyIndex.Open(dllPath);

            if (requestedSections.Contains(SectionNames.AllocationFacts))
            {
                var rows = Analysis.SemanticFactProjection.AllocationFacts(index.GetAllocationOccurrences(), semanticToken)
                    .Select(fact => ToAllocationFactRow(fact, includeMember: false))
                    .ToList();
                if (rows.Count > 0 || ExplicitlySelected(SectionNames.AllocationFacts))
                {
                    memberCode.AllocationFactRows = rows;
                    hasCode = true;
                }
            }

            if (requestedSections.Contains(SectionNames.SafetyFacts))
            {
                var rows = Analysis.SemanticFactProjection.SafetyFacts(index.GetUnsafeEvidenceByMember(), index.GetUnsafetyOccurrences(), semanticToken)
                    .Select(fact => ToSafetyFactRow(fact, includeMember: false))
                    .ToList();
                if (rows.Count > 0 || ExplicitlySelected(SectionNames.SafetyFacts))
                {
                    memberCode.SafetyFactRows = rows;
                    hasCode = true;
                }
            }

            if (requestedSections.Contains(SectionNames.CostFacts))
            {
                var rows = Analysis.SemanticFactProjection.CostFacts(index.GetDirectCallsByCaller(), semanticToken)
                    .Select(fact => ToCostFactRow(fact, includeMember: false))
                    .ToList();
                if (rows.Count > 0 || ExplicitlySelected(SectionNames.CostFacts))
                {
                    memberCode.CostFactRows = rows;
                    hasCode = true;
                }
            }
        }

        if (request.DecompiledSource || request.AnnotatedSource || request.CostOverlay || request.SemanticsOverlay || request.IL || request.Attributes || request.Facts)
            RequestTelemetry.Breadcrumb("method-body-load", singleMethod?.Name ?? type.Name);

        foreach (var (member, code) in MemberCodeProvider.Collect(type, methods, dllPath, overloadIndex, request, pdbPath, options?.IncludeAll ?? false))
        {
            if (code.Attributes is { Count: > 0 } attributes)
            {
                view.MethodAttributeRows = attributes
                    .Select(a => new MethodAttributeRow(a.Name, a.Value ?? ""))
                    .ToList();
            }

            if (code.LoweredBody is { } lowered)
            {
                EmitDecompileBreadcrumb(member.Name, code.DecompileTrace);
                string source;
                try
                {
                    source = FormatSourceWithDeclaration(
                        type,
                        member,
                        code.MethodGenericParameters,
                        lowered,
                        code.RequiresAsyncDeclaration,
                        preferExpressionBodied: true);
                }
                catch (Exception ex)
                {
                    source = $"// {Decompiler.DiagnosticIds.InternalError}: declaration formatting failed: {ex.GetType().Name}: {ex.Message}";
                }
                memberCode.DecompiledSourceCode = new CodeSection("csharp", source);
                hasCode = true;
            }
            else if (code.LoweredDiagnostic is { } loweredDiagnostic)
            {
                EmitDecompileBreadcrumb(member.Name, code.DecompileTrace);
                memberCode.DecompiledSourceCode = new CodeSection("csharp", loweredDiagnostic);
                hasCode = true;
            }

            if (code.AnnotatedBody is { } annotated)
            {
                EmitDecompileBreadcrumb(member.Name, code.DecompileTrace);
                string source;
                try
                {
                    source = FormatSourceWithDeclaration(type, member, code.MethodGenericParameters, annotated);
                }
                catch (Exception ex)
                {
                    source = $"// {Decompiler.DiagnosticIds.InternalError}: declaration formatting failed: {ex.GetType().Name}: {ex.Message}";
                }
                memberCode.AnnotatedSourceCode = new CodeSection("csharp", source);
                hasCode = true;
            }
            else if (code.AnnotatedDiagnostic is { } annotatedDiagnostic)
            {
                EmitDecompileBreadcrumb(member.Name, code.DecompileTrace);
                memberCode.AnnotatedSourceCode = new CodeSection("csharp", annotatedDiagnostic);
                hasCode = true;
            }

            if (code.CostOverlayBody is { } costOverlay)
            {
                EmitDecompileBreadcrumb(member.Name, code.DecompileTrace);
                string source;
                try
                {
                    source = FormatSourceWithDeclaration(
                        type,
                        member,
                        code.MethodGenericParameters,
                        costOverlay,
                        leadingBodyComments: code.CostOverlayHeaderComments);
                }
                catch (Exception ex)
                {
                    source = $"// {Decompiler.DiagnosticIds.InternalError}: declaration formatting failed: {ex.GetType().Name}: {ex.Message}";
                }
                memberCode.CostOverlayCode = new CodeSection("csharp", source);
                hasCode = true;
            }
            else if (code.CostOverlayDiagnostic is { } costDiagnostic)
            {
                EmitDecompileBreadcrumb(member.Name, code.DecompileTrace);
                memberCode.CostOverlayCode = new CodeSection("csharp", costDiagnostic);
                hasCode = true;
            }

            if (code.SemanticsOverlayBody is { } semanticsOverlay)
            {
                EmitDecompileBreadcrumb(member.Name, code.DecompileTrace);
                string source;
                try
                {
                    source = FormatSourceWithDeclaration(type, member, code.MethodGenericParameters, semanticsOverlay);
                }
                catch (Exception ex)
                {
                    source = $"// {Decompiler.DiagnosticIds.InternalError}: declaration formatting failed: {ex.GetType().Name}: {ex.Message}";
                }
                memberCode.SemanticsOverlayCode = new CodeSection("csharp", source);
                hasCode = true;
            }
            else if (code.SemanticsOverlayDiagnostic is { } semanticsDiagnostic)
            {
                EmitDecompileBreadcrumb(member.Name, code.DecompileTrace);
                memberCode.SemanticsOverlayCode = new CodeSection("csharp", semanticsDiagnostic);
                hasCode = true;
            }

            if ((code.ILText ?? code.ILDiagnostic) is { } ilText)
            {
                RequestTelemetry.Breadcrumb("il-render", member.Name);
                memberCode.ILCode = new CodeSection("il", ilText);
                hasCode = true;
            }

            if (request.Facts && code.Facts is { } facts)
            {
                var rows = facts
                    .Select(fact => new FactRow(
                        fact.Member,
                        fact.ILOffset is { } offset ? MarkoutInline.Code($"IL_{offset:X4}") : null,
                        fact.CSharpLine?.ToString(),
                        fact.Anchor,
                        fact.Category,
                        fact.Id,
                        fact.Detail is { } detail ? MarkoutInline.Code(detail) : null,
                        fact.Conditionality))
                    .ToList();
                if (rows.Count > 0 || ExplicitlySelected(SectionNames.Facts))
                {
                    memberCode.FactRows = rows;
                    hasCode = true;
                }
            }
        }

        if (hasCode)
            view.MemberCode = memberCode;
    }

    static void AddOrReplaceSummaryField(TypeView view, string name, string value)
    {
        view.Summary ??= [];
        int index = view.Summary.FindIndex(field => string.Equals(field.Key, name, StringComparison.OrdinalIgnoreCase));
        var field = new MarkoutField(name, value);
        if (index >= 0)
            view.Summary[index] = field;
        else
            view.Summary.Add(field);
    }

    static string FormatCallKind(Analysis.CallKind kind) => kind switch
    {
        Analysis.CallKind.Call => "call",
        Analysis.CallKind.CallVirtual => "callvirt",
        Analysis.CallKind.NewObject => "newobj",
        Analysis.CallKind.LoadFunction => "ldftn",
        Analysis.CallKind.LoadVirtualFunction => "ldvirtftn",
        _ => "calli",
    };

    static string FormatOpcode(Analysis.CallKind kind) => kind switch
    {
        Analysis.CallKind.CallVirtual => "callvirt",
        Analysis.CallKind.NewObject => "newobj",
        Analysis.CallKind.LoadFunction => "ldftn",
        Analysis.CallKind.LoadVirtualFunction => "ldvirtftn",
        Analysis.CallKind.CallIndirect => "calli",
        _ => "call",
    };

    static string FormatCallsiteKind(Analysis.CallKind kind) => kind switch
    {
        Analysis.CallKind.Call => "direct",
        Analysis.CallKind.CallVirtual => "virtual",
        Analysis.CallKind.NewObject => "constructor",
        Analysis.CallKind.LoadFunction => "method pointer",
        Analysis.CallKind.LoadVirtualFunction => "virtual method pointer",
        Analysis.CallKind.CallIndirect => "function pointer",
        _ => "call",
    };

    static string FormatILRange(int start, int end) => $"IL_{start:X4}..IL_{end:X4}";

    static readonly HashSet<string> SemanticFactSections = new(StringComparer.OrdinalIgnoreCase)
    {
        SectionNames.AllocationFacts,
        SectionNames.SafetyFacts,
        SectionNames.CostFacts
    };

    static bool IsUnsafeApiMemberEvidence(Analysis.UnsafeEvidence evidence)
        => evidence is { Reason: "Unsafe API member", Kind: "api" };

    static string FormatCallee(Analysis.MemberRef member)
    {
        if (member.Kind == Analysis.MemberKind.Unsupported)
            return member.DeclaringType.ToDisplayString();

        return FormatMember(member.DeclaringType, member.Name, member.ParameterTypes, member.TypeArguments);
    }

    static TreeNode ToCallGraphNode(Analysis.CallTreeNode node, IReadOnlyList<string>? requestedFields = null)
    {
        var children = node.Children.Select(child => ToCallGraphNode(child, requestedFields)).ToList();
        if (node.Status == Analysis.CallTreeStatus.Truncated)
            children.Add(new TreeNode("… (truncated)"));
        return new TreeNode(FormatCallGraphLabel(node, requestedFields))
        {
            Children = children.Count > 0 ? children : null,
        };
    }

    static IReadOnlyList<string> GetRequestedCallGraphFields(ApiOptions? options)
        => options?.Fields is { Length: > 0 } fields
            ? fields
            : options?.Columns is { Length: > 0 } columns
                ? columns
                : [];

    static string FormatCallGraphLabel(Analysis.CallTreeNode node, IReadOnlyList<string>? requestedFields = null)
    {
        string member = FormatCallee(node.Member);
        var suffixes = new List<string>();

        switch (node.Status)
        {
            case Analysis.CallTreeStatus.External:
                suffixes.Add("external");
                break;
            case Analysis.CallTreeStatus.AlreadyShown:
                suffixes.Add("shown above");
                break;
            case Analysis.CallTreeStatus.DepthLimited:
                suffixes.Add("…");
                break;
        }

        if (requestedFields is { Count: > 0 })
        {
            foreach (var field in requestedFields)
            {
                if (FormatCallGraphAnnotation(node, field) is { } annotation)
                    suffixes.Add(annotation);
            }
        }
        else if (node.Perf is { } perf)
        {
            if (perf.Fanout > 0)
                suffixes.Add($"fanout {perf.Fanout}");
            if (perf.Fanin > 0)
                suffixes.Add($"fanin {perf.Fanin}");
            if (perf.MaxDepth > 1)
                suffixes.Add($"depth {perf.MaxDepth}");
            else if (perf.Fanout == 0 && perf.Fanin == 0 && suffixes.Count == 0)
                suffixes.Add("depth 1");
            if (perf.InLoop)
                suffixes.Add(perf.LoopHint ?? "loop");
            if (!string.IsNullOrEmpty(perf.RootKind))
                suffixes.Add(perf.RootKind);
            if (!string.IsNullOrEmpty(perf.Source))
                suffixes.Add($"from {perf.Source}");
        }

        return suffixes.Count > 0 ? $"{member} ({string.Join(", ", suffixes)})" : member;
    }

    static string? FormatRootAnnotation(Analysis.CallTreePerf perf)
    {
        // The Root field combines the reverse-graph classification (target/entrypoint) with
        // the source assembly for callers pulled in from the --bin/--project/--caller-package
        // scope, so reach evidence can name the caller library when requested.
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(perf.RootKind))
            parts.Add(perf.RootKind);
        if (!string.IsNullOrEmpty(perf.Source))
            parts.Add($"from {perf.Source}");
        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    static string? FormatCallGraphAnnotation(Analysis.CallTreeNode node, string fieldName)
    {
        if (node.Perf is not { } perf)
            return null;

        var normalized = NormalizeCallGraphField(fieldName);
        var signals = perf.SignalsOrNone;
        return normalized switch
        {
            "fanin" or "fanincount" => $"fanin {perf.Fanin}",
            "fanout" or "fanoutcount" => $"fanout {perf.Fanout}",
            "depth" or "maxdepth" => $"depth {perf.MaxDepth}",
            "loop" or "inloop" or "looping" => perf.InLoop ? (perf.LoopHint ?? "loop") : null,
            "root" or "rootkind" or "classification" => FormatRootAnnotation(perf),
            "source" or "assembly" => perf.Source is { } source ? $"from {source}" : null,
            "alloc" or "allocs" or "allocations" => signals.Allocations > 0 ? $"alloc {signals.Allocations}" : null,
            "copy" or "copies" => signals.Copies > 0 ? $"copy {signals.Copies}" : null,
            "unsafe" => signals.Unsafe ? "unsafe" : null,
            "reflection" or "reflect" => signals.Reflection > 0 ? $"reflection {signals.Reflection}" : null,
            "throw" or "throws" or "throwsites" => signals.Throws > 0 ? $"throw {signals.Throws}" : null,
            "catch" or "catches" => signals.Catches > 0 ? $"catch {signals.Catches}" : null,
            "finally" or "finallys" => signals.Finallys > 0 ? $"finally {signals.Finallys}" : null,
            "exceptions" or "exceptiontypes" or "constructedexceptions" => signals.ExceptionTypes.Length > 0
                ? "exceptions " + string.Join(",", signals.ExceptionTypes)
                : null,
            "evidenceil" or "evidence" or "il" => FormatEvidenceIL(signals),
            _ => null,
        };
    }

    // Compact IL receipts for the projected signals: the offsets of the
    // signal-bearing instructions (newobj/newarr/throw/ldftn/reflection calls).
    static string? FormatEvidenceIL(Analysis.MethodSignals signals)
    {
        var offsets = signals.Evidence;
        if (offsets.Length == 0)
            return null;
        return "il " + string.Join(",", offsets.Select(offset => $"IL_{offset:X4}"));
    }

    static string NormalizeCallGraphField(string fieldName)
    {
        var builder = new StringBuilder();
        foreach (var ch in fieldName)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }

    internal static void PopulateUnsafeMembers(TypeView view, ApiType type, string dllPath)
    {
        var index = Analysis.LibraryBodyIndex.Open(dllPath);
        var rows = index.UnsafeEvidence
            .Where(evidence => SameType(evidence.Member.DeclaringType, type))
            .OrderBy(evidence => evidence.Member.Name, StringComparer.Ordinal)
            .ThenBy(evidence => evidence.ILOffset ?? -1)
            .ThenBy(evidence => evidence.Reason, StringComparer.Ordinal)
            .ThenBy(evidence => evidence.Detail, StringComparer.Ordinal)
            .Select(evidence => ToUnsafeMemberRow(evidence, includeDeclaringType: false))
            .ToList();
        if (rows.Count > 0)
            view.UnsafeMemberRows = rows;
    }

    internal static void PopulateTypeExceptionRegions(
        TypeView view,
        ApiType type,
        string dllPath,
        IReadOnlySet<string>? explicitSections = null)
    {
        using var pdbContext = PdbContext.Open(dllPath);
        var rows = type.Members
            .Where(member => member.MetadataToken is not null && ApiMemberSectionDescriptors.IsMethodLike(member))
            .OrderBy(member => GetMemberSortOrder(member.Kind))
            .ThenBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(GetMemberSignatureSortKey, StringComparer.Ordinal)
            .SelectMany(member => pdbContext.ResolveExceptionRegions(member.MetadataToken!.Value, out _)
                .Select(region => new TypeExceptionRegionRow(
                    MarkoutInline.Code(GetMemberDisplaySignature(type, member)),
                    region.Region,
                    region.Clause,
                    FormatILRange(region.TryStart, region.TryEnd),
                    FormatILRange(region.HandlerStart, region.HandlerEnd),
                    region.FilterStart is { } filterStart && region.FilterEnd is { } filterEnd
                        ? FormatILRange(filterStart, filterEnd)
                        : null,
                    region.CaughtType)))
            .ToList();

        if (rows.Count > 0 || explicitSections is not null && explicitSections.Contains(SectionNames.ExceptionRegions))
            view.ExceptionRegionRows = rows;
    }

    internal static void PopulateCalledTypes(
        TypeView view,
        ApiType type,
        string dllPath,
        IReadOnlySet<string>? explicitSections = null)
    {
        var index = Analysis.LibraryBodyIndex.Open(dllPath);
        var rows = index.CalledTypes(method => SameType(method.DeclaringType, type))
            .Select(summary => new CalledTypeRow(
                MarkoutInline.Code(summary.Type.ToQualifiedDisplayString()),
                string.IsNullOrEmpty(summary.Assembly) ? null : summary.Assembly,
                summary.Calls,
                summary.Members,
                string.Join(", ", summary.CallKinds.Select(FormatCallsiteKind))))
            .ToList();

        if (rows.Count > 0 || explicitSections is not null && explicitSections.Contains(SectionNames.CalledTypes))
            view.CalledTypeRows = rows;
    }

    internal static void PopulateTypeSemanticFacts(
        TypeView view,
        ApiType type,
        string dllPath,
        IReadOnlySet<string>? requestedSections,
        IReadOnlySet<string>? explicitSections = null)
    {
        var index = Analysis.LibraryBodyIndex.Open(dllPath);
        var methodTokens = type.Members
            .Where(member => member.MetadataToken is not null && ApiMemberSectionDescriptors.IsMethodLike(member))
            .Select(member => member.MetadataToken!.Value)
            .ToArray();

        if (requestedSections?.Contains(SectionNames.AllocationFacts) == true)
        {
            var allocations = index.GetAllocationOccurrences();
            var rows = methodTokens
                .SelectMany(token => Analysis.SemanticFactProjection.AllocationFacts(allocations, token))
                .Select(fact => ToAllocationFactRow(fact, includeMember: true))
                .ToList();
            if (rows.Count > 0 || explicitSections is not null && explicitSections.Contains(SectionNames.AllocationFacts))
                view.AllocationFactRows = rows;
        }

        if (requestedSections?.Contains(SectionNames.SafetyFacts) == true)
        {
            var unsafeEvidence = index.GetUnsafeEvidenceByMember();
            var unsafety = index.GetUnsafetyOccurrences();
            var rows = methodTokens
                .SelectMany(token => Analysis.SemanticFactProjection.SafetyFacts(unsafeEvidence, unsafety, token))
                .Select(fact => ToSafetyFactRow(fact, includeMember: true))
                .ToList();
            if (rows.Count > 0 || explicitSections is not null && explicitSections.Contains(SectionNames.SafetyFacts))
                view.SafetyFactRows = rows;
        }

        if (requestedSections?.Contains(SectionNames.CostFacts) == true)
        {
            var directCalls = index.GetDirectCallsByCaller();
            var rows = methodTokens
                .SelectMany(token => Analysis.SemanticFactProjection.CostFacts(directCalls, token))
                .Select(fact => ToCostFactRow(fact, includeMember: true))
                .ToList();
            if (rows.Count > 0 || explicitSections is not null && explicitSections.Contains(SectionNames.CostFacts))
                view.CostFactRows = rows;
        }
    }

    internal static void PopulateOptimizationOpportunities(
        TypeView view,
        ApiType type,
        string dllPath,
        IReadOnlySet<string>? explicitSections = null,
        PerformanceTriageOptions? options = null,
        bool restrictToModelMembers = false)
    {
        var index = Analysis.LibraryBodyIndex.Open(dllPath);
        HashSet<int>? memberTokens = restrictToModelMembers
            ? type.Members.Where(m => m.MetadataToken is not null).Select(m => m.MetadataToken!.Value).ToHashSet()
            : null;
        var rows = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                index.OptimizationOpportunities
                    .Where(opportunity => SameType(opportunity.Method.DeclaringType, type))
                    .Where(opportunity => !LibraryMetadataService.IsGeneratedMethod(opportunity.Method, index.GeneratedFrameworkTypeNames))
                    .Where(opportunity => memberTokens is null || memberTokens.Contains(opportunity.Method.MetadataToken)),
                options)
            .Select(opportunity => new OptimizationOpportunityRow(
                MarkoutInline.Code(FormatMember(null, opportunity.Method.Name, opportunity.Method.ParameterTypes, [])),
                opportunity.RootReach.ToString(),
                opportunity.Shape,
                opportunity.Evidence,
                opportunity.SafeFixDirection,
                opportunity.Confidence,
                opportunity.InLoop ? "loop" : "",
                opportunity.RuntimeAllocationType is { Length: > 0 } allocation ? MarkoutInline.Code(allocation) : null,
                opportunity.PathContext,
                opportunity.PathConfidence,
                opportunity.ILOffset is { } offset ? MarkoutInline.Code($"IL_{offset:X4}") : null))
            .ToList();

        if (rows.Count > 0 || explicitSections is not null && explicitSections.Contains(SectionNames.PerformanceTriage))
            view.OptimizationOpportunityRows = rows;
    }

    /// <summary>
    /// Maps each API-surface member of <paramref name="type"/> to its drill selectors,
    /// keyed by metadata token: a round-tripping <c>Stable</c> selector (<c>Name~digest</c>),
    /// the member <c>Visibility</c>, and a <c>Selector</c> (<c>Name</c>, or <c>Name:N</c>
    /// where overloads exist). Reuses the exact Member Index canonical-signature/digest
    /// path so both type- and library-scope Top Leverage emit selectors that resolve via
    /// <c>member Name~digest</c> / <c>member Name:N</c>.
    /// </summary>
    internal static Dictionary<int, (string? Stable, string Visibility, string Selector)> BuildMemberDrillMap(ApiType type)
    {
        // Number overloads in the same order the Member Index uses, so the emitted
        // Name:N selector matches `member Name:N` resolution (the digest is order-free).
        var ordered = type.Members
            .OrderBy(m => GetMemberSortOrder(m.Kind))
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(GetMemberSignatureSortKey, StringComparer.Ordinal)
            .ToList();
        var overloadCounts = ordered
            .GroupBy(GetMemberSelectorName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        // Property/field/event canonical signatures omit parameters, so overloaded indexers
        // (all named Item) collapse to one digest. Count canonical signatures to detect such
        // collisions and suppress the ambiguous Stable selector (the Name:N selector still
        // disambiguates), so an emitted Name~digest always round-trips.
        var canonicalCounts = ordered
            .GroupBy(m => GetCanonicalSignature(type, m), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var overloadIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var map = new Dictionary<int, (string? Stable, string Visibility, string Selector)>();

        foreach (var member in ordered)
        {
            var selectorName = GetMemberSelectorName(member);
            // Advance the overload index for every member (matching BuildMemberIndexRows)
            // so Name:N stays stable regardless of which members carry tokens.
            overloadIndices.TryGetValue(selectorName, out var index);
            index++;
            overloadIndices[selectorName] = index;

            var canonical = GetCanonicalSignature(type, member);
            var selector = overloadCounts[selectorName] > 1 ? $"{selectorName}:{index}" : selectorName;
            string? stable = canonicalCounts[canonical] > 1 ? null : $"{selectorName}~{GetMemberDigest(canonical)}";
            var drill = (stable, member.Accessibility ?? "public", selector);

            // Register under the member's own token and, for properties, both accessor
            // MethodDef tokens, so accessor-level leverage rows (get_X/set_X) resolve to
            // the owning property's selector.
            Register(member.MetadataToken, drill);
            Register(member.GetterToken, drill);
            Register(member.SetterToken, drill);
        }

        return map;

        void Register(int? token, (string? Stable, string Visibility, string Selector) drill)
        {
            if (token is { } resolved && !map.ContainsKey(resolved))
                map[resolved] = drill;
        }
    }

    internal static void PopulateTopLeverage(TypeView view, ApiType type, string dllPath, bool restrictToModelMembers = false)
    {
        var index = Analysis.LibraryBodyIndex.Open(dllPath);
        var drillByToken = BuildMemberDrillMap(type);

        // Rank every method declared on this type; fanin is still measured across all
        // callers in the assembly. The full ranked set is emitted and the generic row
        // limiter (`-n`/`--rows`) trims the rendered table. In member-detail/overload
        // contexts `type.Members` is narrowed to the selected member(s), so restrict the
        // ranked rows to those tokens (mirrors PopulateOptimizationOpportunities).
        var rows = index.TopLeverage(count: int.MaxValue, scope: method => SameType(method.DeclaringType, type))
            .Where(entry => !restrictToModelMembers || drillByToken.ContainsKey(entry.Method.MetadataToken))
            .Select(entry =>
            {
                drillByToken.TryGetValue(entry.Method.MetadataToken, out var drill);
                bool generated = LibraryMetadataService.IsGeneratedMethod(entry.Method, index.GeneratedFrameworkTypeNames);
                return new TopLeverageRow(
                    MarkoutInline.Code(FormatMember(null, entry.Method.Name, entry.Method.ParameterTypes, [])),
                    entry.DirectCallerCount.ToString(),
                    entry.RootReach.ToString(),
                    entry.Fanout.ToString(),
                    entry.MaxDepth.ToString(),
                    entry.LoopCallCount.ToString(),
                    drill.Visibility,
                    generated ? "generated" : null,
                    drill.Stable is { } stable ? MarkoutInline.Code(stable) : null,
                    drill.Selector is { } selector ? MarkoutInline.Code(selector) : null);
            })
            .ToList();
        if (rows.Count > 0)
            view.TopLeverageRows = rows;
    }

    internal static UnsafeMemberRow ToUnsafeMemberRow(Analysis.UnsafeEvidence evidence, bool includeDeclaringType)
    {
        string member = includeDeclaringType
            ? FormatMethod(evidence.Member)
            : FormatMember(null, evidence.Member.Name, evidence.Member.ParameterTypes, []);
        return new UnsafeMemberRow(
            MarkoutInline.Code(member),
            evidence.Reason,
            MarkoutInline.Code(evidence.Detail),
            evidence.Kind,
            evidence.ILOffset is { } offset ? MarkoutInline.Code($"IL_{offset:X4}") : null,
            evidence.OperandToken is { } token ? MarkoutInline.Code($"0x{token:X8}") : null);
    }

    static AllocationFactRow ToAllocationFactRow(Analysis.AllocationFact fact, bool includeMember)
        => new(
            includeMember ? MarkoutInline.Code(FormatMethod(fact.Method)) : null,
            MarkoutInline.Code($"IL_{fact.ILOffset:X4}"),
            fact.AllocationKind,
            fact.AllocatedType is { } allocated ? MarkoutInline.Code(allocated) : null,
            fact.CountedAsHeap ? "Yes" : "No",
            fact.Frequency,
            fact.Escape,
            fact.InLoop ? "Yes" : "No",
            fact.Evidence);

    static SafetyFactRow ToSafetyFactRow(Analysis.SafetyFact fact, bool includeMember)
        => new(
            includeMember ? MarkoutInline.Code(FormatMethod(fact.Method)) : null,
            fact.ILOffset is { } offset ? MarkoutInline.Code($"IL_{offset:X4}") : null,
            fact.SafetyKind,
            MarkoutInline.Code(fact.Operation),
            fact.Requirement,
            fact.Evidence);

    static CostFactRow ToCostFactRow(Analysis.CostFact fact, bool includeMember)
        => new(
            includeMember ? MarkoutInline.Code(FormatMethod(fact.Method)) : null,
            MarkoutInline.Code($"IL_{fact.ILOffset:X4}"),
            fact.CostKind,
            MarkoutInline.Code(fact.Operation),
            fact.InLoop ? "Yes" : "No",
            fact.Evidence);

    static string FormatMethod(Analysis.MethodIdentity method)
        => FormatMember(method.DeclaringType, method.Name, method.ParameterTypes, []);

    static CallerSiteRow CreateCallerRow(string source, Analysis.DirectCall call)
        => new(
            source,
            MarkoutInline.Code(FormatMethod(call.Caller)),
            MarkoutInline.Code($"IL_{call.ILOffset:X4}"),
            string.IsNullOrEmpty(call.Opcode) ? FormatOpcode(call.Kind) : call.Opcode,
            FormatCallsiteKind(call.Kind),
            MarkoutInline.Code($"0x{call.OperandToken:X8}"),
            call.ReturnAddress is { } returnAddress
                ? MarkoutInline.Code($"IL_{returnAddress:X4}")
                : null);

    static string FormatMember(Analysis.TypeRef? declaringType, string name, IEnumerable<Analysis.TypeRef> parameterTypes, IEnumerable<Analysis.TypeRef> typeArguments)
    {
        var typeArgs = typeArguments.ToList();
        if (typeArgs.Count > 0)
            name += $"<{string.Join(", ", typeArgs.Select(t => t.ToQualifiedDisplayString()))}>";
        string signature = $"{name}({string.Join(", ", parameterTypes.Select(p => p.ToQualifiedDisplayString()))})";
        return declaringType is null ? signature : $"{declaringType.ToQualifiedDisplayString()}.{signature}";
    }

    static bool SameType(Analysis.TypeRef typeRef, ApiType type)
        => typeRef.Kind == Analysis.TypeRefKind.Definition
           && string.Equals(typeRef.Namespace, type.Namespace ?? "", StringComparison.Ordinal)
           // Nested types use the metadata '+' separator in the analysis TypeRef
           // (Outer+Inner) but the '.' separator in the API surface (Outer.Inner).
           && string.Equals(typeRef.Name.Replace('+', '.'), type.Name, StringComparison.Ordinal);

    /// <summary>
    /// Converts the decompiler's telemetry-free <see cref="Decompiler.DecompilerTrace"/>
    /// shape into a request-trace breadcrumb: a <c>decompile.method</c> stage on
    /// success or <c>decompile.fallback</c> on failure, with the fidelity outcome,
    /// the symbol source used, and (on failure) the leading diagnostic id. Falls
    /// back to a bare crumb when no trace is available.
    /// </summary>
    private static void EmitDecompileBreadcrumb(string member, Decompiler.DecompilerTrace? trace)
    {
        if (trace is null)
        {
            RequestTelemetry.Breadcrumb("decompile.method", member);
            return;
        }

        var symbols = trace.Symbols switch
        {
            Decompiler.DecompilerSymbolSource.Embedded => "pdb:embedded",
            Decompiler.DecompilerSymbolSource.Sidecar => "pdb:sidecar",
            Decompiler.DecompilerSymbolSource.External => "pdb:external",
            _ => "pdb:none",
        };

        var stage = trace.Succeeded ? "decompile.method" : "decompile.fallback";
        var detail = $"{member} ({trace.Fidelity}, {symbols})";
        if (!trace.Succeeded && trace.Diagnostics.Count > 0)
            detail += $" [{trace.Diagnostics[0].Id}]";

        RequestTelemetry.Breadcrumb(stage, detail);
    }

    private static string FormatSourceWithDeclaration(
        ApiType type,
        ApiMember member,
        IReadOnlyList<string>? methodGenericParameters,
        string lowered,
        bool requiresAsyncDeclaration = false,
        bool preferExpressionBodied = false,
        IReadOnlyList<string>? leadingBodyComments = null)
    {
        var declaration = FormatMemberDeclaration(
            type,
            member,
            abbreviate: false,
            methodGenericParameters,
            forceAsync: requiresAsyncDeclaration);
        var body = lowered.TrimEnd();

        // A constructor's base/this initializer is surfaced by the renderer as a
        // leading `: base(...)` / `: this(...)` line (it is invalid as a body
        // statement). Lift it onto the declaration line so the output is valid C#.
        var bodyLines = body.ReplaceLineEndings("\n").Split('\n');
        if (bodyLines.Length > 0 && bodyLines[0].TrimStart() is { } first
            && (first.StartsWith(": base(", StringComparison.Ordinal)
                || first.StartsWith(": this(", StringComparison.Ordinal)))
        {
            if (!string.IsNullOrWhiteSpace(declaration))
                declaration = $"{declaration} {first.TrimEnd()}";
            body = string.Join("\n", bodyLines.Skip(1)).TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(declaration))
            return body;

        bool hasLeadingComments = leadingBodyComments is { Count: > 0 };
        if (!hasLeadingComments && preferExpressionBodied && Decompiler.CSharpExpressionBody.FromSingleStatement(body) is { } expressionBody)
            return $"{declaration} => {expressionBody};";

        var formattedBodyLines = body.ReplaceLineEndings("\n").Split('\n');
        var lines = hasLeadingComments
            ? leadingBodyComments!.Concat(formattedBodyLines)
            : formattedBodyLines;
        var indentedBody = string.Join(
            Environment.NewLine,
            lines.Select(line => line.Length == 0 ? "" : $"    {line}"));

        return $"{declaration}{Environment.NewLine}{{{Environment.NewLine}{indentedBody}{Environment.NewLine}}}";
    }

    private static string FormatMemberDeclaration(
        ApiType type,
        ApiMember member,
        bool abbreviate,
        IReadOnlyList<string>? methodParameters = null,
        bool forceAsync = false,
        bool forceUnsafe = false)
        => CSharpDeclarationWriter.RenderMemberDeclaration(
            type,
            member,
            new CSharpDeclarationOptions
            {
                AbbreviateSignature = abbreviate,
                ForceAsync = forceAsync,
                ForceUnsafe = forceUnsafe
            },
            methodParameters);

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

    internal static string FormatGenericFullName(ApiType type)
        => MetadataTypeNameFormatter.FormatFullName(type);

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

    internal static string GetMemberDisplaySignature(ApiType type, ApiMember member)
        => member.Signature ?? $"{FormatGenericFullName(type)}.{OperatorNames.FormatDisplayName(member.Name)}";

    private static string GetMemberSelectorName(ApiMember member) => member.Kind switch
    {
        "operator" => $"operator:{member.Name}",
        "explicit-interface-implementation" => $"explicit:{member.Name}",
        "extension-method" => $"extension:{member.Name}",
        _ => member.Name
    };

    internal static string GetMemberDigest(string canonicalSignature)
    {
        var input = Encoding.UTF8.GetBytes("dotnet-inspect.member-index.v1\n" + canonicalSignature);
        var hash = SHA256.HashData(input);
        return Convert.ToHexString(hash).ToLowerInvariant()[..10];
    }

    internal static string GetCanonicalSignature(ApiType type, ApiMember member)
    {
        var declaringType = member.DeclaringType;
        if (string.IsNullOrWhiteSpace(declaringType))
            declaringType = FormatGenericFullName(type);

        var kindCode = member.Kind switch
        {
            "property" => "P",
            "field" => "F",
            "event" => "E",
            _ => "M"
        };

        if (member.Kind is "property" or "field" or "event")
        {
            return $"{kindCode}:{declaringType}.{member.Name}";
        }

        var signature = member.Signature ?? member.ReturnType ?? member.Name;
        var memberName = member.Kind == "constructor"
            ? "#ctor"
            : ExtractMemberNameWithGeneric(signature, member.Name);
        var parameters = ExtractCanonicalParameterList(signature);
        var canonical = $"{kindCode}:{declaringType}.{memberName}{parameters}";
        return canonical;
    }

    private static string ExtractMemberNameWithGeneric(string signature, string memberName)
    {
        var parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return memberName;

        var nameIndex = signature.LastIndexOf(memberName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return memberName;

        var end = nameIndex + memberName.Length;
        if (end < parenStart && signature[end] == '<')
        {
            var depth = 0;
            for (var i = end; i < parenStart; i++)
            {
                if (signature[i] == '<')
                    depth++;
                else if (signature[i] == '>')
                {
                    depth--;
                    if (depth == 0)
                        return NormalizeCanonicalWhitespace(signature[nameIndex..(i + 1)]);
                }
            }
        }

        return memberName;
    }

    private static string ExtractCanonicalParameterList(string signature)
    {
        var abbreviated = SignatureParser.AbbreviateSignature(signature);
        var parenStart = abbreviated.IndexOf('(');
        var parenEnd = abbreviated.LastIndexOf(')');
        if (parenStart < 0 || parenEnd < parenStart)
            return "()";

        var parameters = abbreviated[parenStart..(parenEnd + 1)];
        return NormalizeCanonicalWhitespace(parameters);
    }

    private static string NormalizeCanonicalWhitespace(string value)
        => value.Replace(", ", ",", StringComparison.Ordinal).Trim();

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
                _ => MemberReturnType(m)
            };
            var detail = e.kind switch
            {
                "property" => MemberAccessors(m),
                "constructor" or "method" or "operator" or "explicit-interface-implementation" or "extension-method" when e.members.Count > 1 => e.members.Count.ToString(),
                "constructor" or "method" or "operator" or "explicit-interface-implementation" or "extension-method" when e.members.Count == 1 => MemberParameterTypes(m),
                _ => ""
            };
            var kindLabel = m.Accessibility != null ? $"{m.Accessibility} {e.kind}" : e.kind;
            return new ApiOneLineRow(kindLabel, OperatorNames.FormatDisplayName(m.Name), returnType, detail);
        }).ToList();

        return (new ApiTypeOneLineView { Rows = rows }, truncated);
    }

    private static string MemberReturnType(ApiMember member)
        => member.SignatureModel?.ReturnType
           ?? member.ReturnType
           ?? SignatureParser.ExtractReturnType(member.Signature);

    private static string MemberAccessors(ApiMember member)
        => member.SignatureModel?.PublicAccessorsSummary
           ?? SignatureParser.ExtractAccessors(member.Signature);

    private static string MemberParameterTypes(ApiMember member)
        => member.SignatureModel?.ParameterTypesSummary
           ?? SignatureParser.ExtractParamList(member.Signature);

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
                    MarkoutInline.Code(FormatGenericFullName(t)),
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
