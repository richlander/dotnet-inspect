using DotnetInspector.Inspectors;
using DotnetInspector.Core;
using ILInspector.CSharp;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using System.Collections.Immutable;
using System.Text;
using System.Globalization;
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
    static readonly CSharpFormatter DefaultCSharpFormatter = new();
    static readonly CSharpFormatter AnnotatedCSharpFormatter = new(
        new CSharpFormatOptions { IncludeCustomAttributes = true });
    static readonly CSharpFormatter AbbreviatedCSharpFormatter = new(
        new CSharpFormatOptions { AbbreviateSignature = true });
    static readonly CSharpFormatter CSharpFormatterWithoutObsolete = new(
        new CSharpFormatOptions { IncludeObsoleteAttribute = false });

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

        if (api.InspectionFailures.Count > 0
            && options.Verbosity >= Verbosity.Normal)
        {
            view.InspectionFailures = api.InspectionFailures
                .Select(failure => new ApiInspectionFailureRow(
                    failure.Operation,
                    $"0x{failure.SubjectToken:X8}",
                    failure.Mechanism.ToString(),
                    failure.Kind,
                    failure.Detail))
                .ToList();
        }

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
        && !options.Tabular;

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

        // One source of truth for type-level modifiers, base type, and interface ordering: the
        // presentation-neutral projection in ILInspector.Research. C# constraint spelling for
        // type parameters stays in this layer (ConstraintSummary) because Research is Roslyn-free
        // and does not own C# rendering.
        var projection = ILInspector.Research.ResearchViews.ProjectType(
            type,
            surface: null,
            new ILInspector.Research.ResearchViews.TypeProjectionOptions(Composition: false, RelationshipGraph: false));

        var modifiers = projection.Identity.Modifiers;
        string? baseType = projection.BaseType;

        // Type parameters inline (Quiet only — at Minimal+ the section replaces this)
        string? typeParamsInline = null;
        if (type.TypeParameters.Count > 0 && options.Verbosity == Verbosity.Quiet)
        {
            var paramDescriptions = type.TypeParameters
                .Select(tp => tp.Constraints.Count > 0
                    ? $"{tp.DisplayName} : {ConstraintSummary(type.TypeParameters, tp)}"
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
                .Select(tp => new TypeParameterRow { Parameter = tp.DisplayName, Constraints = ConstraintSummary(type.TypeParameters, tp) })
                .ToList();
        }

        // Interfaces (pipeline controls visibility via IncludeSections)
        List<InterfaceRow>? interfaceRows = null;
        if (!memberFilterActive && type.Interfaces.Count > 0)
        {
            interfaceRows = projection.Interfaces
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
            Finalizer = topFieldsOnly ? NullIfZero(type.Members.Count(m => m.Kind == "finalizer")) : null,
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
                var children = BuildShapeMemberNodes(group.Key, membersInGroup, expandOverloads, type.Name);
                var logicalCount = IsOverloadGroupedKind(group.Key)
                    ? membersInGroup.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count()
                    : membersInGroup.Count;
                var kindLabel = GetShapeKindLabel(group.Key, membersInGroup.Count, logicalCount);
                nodes.Add(new TreeNode(kindLabel) { Children = children });
            }
        }

        static List<TreeNode> BuildShapeMemberNodes(string kind, IEnumerable<ApiMember> members, bool expandOverloads, string declaringTypeName)
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
                .Select(m => new TreeNode(
                    m.IsFinalizer
                        ? ShapeDestructorSpelling(declaringTypeName)
                        : m.Signature ?? OperatorNames.FormatDisplayName(m.Name)))
                .ToList();
        }

        // A finalizer renders as the C# destructor `~Type()` rather than its raw
        // metadata signature (`void Finalize()`).
        static string ShapeDestructorSpelling(string typeName)
        {
            var name = typeName;
            // Isolate the innermost nested-type segment BEFORE stripping generic
            // arity, so a finalizer on a type nested inside a generic outer
            // (e.g. "Outer`1.Nested" or "Outer`1+Nested") spells "~Nested()"
            // rather than "~Outer()".
            int sep = name.LastIndexOfAny(['.', '+']);
            if (sep >= 0)
                name = name[(sep + 1)..];
            int angle = name.IndexOf('<');
            if (angle >= 0)
                name = name[..angle];
            int tick = name.IndexOf('`');
            if (tick >= 0)
                name = name[..tick];
            return $"~{name}()";
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
                        ? $"{tp.DisplayName} : {ConstraintSummary(type.TypeParameters, tp)}"
                        : tp.DisplayName)
                    .ToList();
                var insertAt = nodes.FindIndex(n => n.Text != "Inherits" && n.Text != "Implements");
                if (insertAt < 0) insertAt = nodes.Count;
                nodes.Insert(insertAt, new TreeNode("Type Parameters") { Children = typeParamDescriptions.Select(t => new TreeNode(t)).ToList() });
            }
        }

        var modifiers = ILInspector.Research.ResearchViews.TypeModifiers(type);

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

    // Renders a type parameter's constraint list for display, delegating C# spelling
    // (reserved-keyword type-name escaping, keyword/type-name disambiguation via the
    // metadata-carried constraint kind) to the CSharp layer rather than joining the
    // raw metadata names. Falls back to a token heuristic when structured kinds are
    // unavailable (see CSharpDeclarationWriter.FormatConstraintList).
    private static string ConstraintSummary(IReadOnlyList<TypeParameter> typeParameters, TypeParameter typeParameter)
        => CSharpFormatter.FormatTypeParameterConstraints(typeParameter, typeParameters.Select(p => p.Name));

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

        // Flatten sorted for --limit application. This ordering must match the per-kind
        // display ordering below so that -m N selects the same members that are shown.
        var allMembers = grouped
            .SelectMany(g => g.Value)
            .OrderBy(m => GetMemberSortOrder(m.Kind))
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(GetMemberSignatureSortKey, StringComparer.Ordinal)
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
                var digest = ApiMemberIdentity.GetMemberAnchor(type, m).Fingerprint;
                return new MemberRow(
                    select,
                    OperatorNames.FormatDisplayName(m.Name),
                    MarkoutInline.Code(digest),
                    MarkoutInline.Code(sigDisplay),
                    hasDocs ? (m.Documentation.Summary ?? "") : null);
            }).ToList();

            switch (kind)
            {
                case "constructor":
                    if (showSelect)
                    { if (hasDocs) view.ConstructorSelectRowsWithDocs = rows; else view.ConstructorSelectRows = rows; }
                    else
                    { if (hasDocs) view.ConstructorRowsWithDocs = rows; else view.ConstructorRows = rows; }
                    break;
                case "finalizer":
                    if (showSelect)
                    { if (hasDocs) view.FinalizerSelectRowsWithDocs = rows; else view.FinalizerSelectRows = rows; }
                    else
                    { if (hasDocs) view.FinalizerRowsWithDocs = rows; else view.FinalizerRows = rows; }
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

        var degradedSignatures = allMembers
            .Where(m => m.SignatureDecodeStatus is SignatureDecodeStatus.Degraded)
            .Select(m => FormatMemberDeclaration(type, m, abbreviate: abbreviate))
            .ToList();
        if (degradedSignatures.Count > 0)
            view.DegradedSignatureMembers = (view.DegradedSignatureMembers ?? [])
                .Concat(degradedSignatures)
                .ToList();

        return (truncated, "members");
    }

    internal static void PopulateMemberSignature(TypeView view, ApiType type, ApiOptions options)
    {
        if (type.Members.Count != 1)
            return;

        var member = type.Members[0];
        var sigDisplay = FormatMemberDeclaration(type, member, abbreviate: false);
        var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);

        var docsRequested = options.ShowDocs
            || options.Columns?.Any(c => c.Equals("Description", StringComparison.OrdinalIgnoreCase)) == true;
        var description = docsRequested ? member.Documentation.Summary : null;

        view.SignatureRows =
        [
            new MemberSignatureRow(
                MarkoutInline.Code(sigDisplay),
                MarkoutInline.Code(anchor.Fingerprint),
                MarkoutInline.Code(anchor.CanonicalSignature),
                SignatureDecodeMarker(member),
                description)
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
            // A property/event is located through its accessor's sequence points, so it
            // carries a source location like a method does (issue #3278).
            .Where(ApiMemberSectionDescriptors.IsBodyBacked)
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

            var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
            var selectorIndex = member.SelectorOverloadIndex ?? index;
            var selector = overloadCounts[selectorName] > 1 || member.SelectorOverloadIndex.HasValue
                ? $"{selectorName}:{selectorIndex}"
                : selectorName;

            rows.Add(new MemberIndexRow(
                MarkoutInline.Code(selector),
                MarkoutInline.Code(anchor.Format(MemberAnchorFormat.StableSelector)),
                MarkoutInline.Code(anchor.Format(MemberAnchorFormat.CanonicalSignature)),
                SignatureDecodeMarker(member),
                anchor.Fingerprint));
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
                            e.members.Count.ToString(),
                            SignatureDecodeMarker(e.members))).ToList();
                    if (hasOverloads)
                        view.ConstructorSummaryRowsWithOverloads = rows;
                    else
                        view.ConstructorSummaryRows = rows;
                    break;
                }
                case "finalizer":
                {
                    var rows = byName.Select(e =>
                        new ConstructorSummaryRow(
                            OperatorNames.FormatDisplayName(e.members[0].Name),
                            e.members.Count.ToString(),
                            SignatureDecodeMarker(e.members))).ToList();
                    view.FinalizerSummaryRows = rows;
                    break;
                }
                case "method":
                {
                    var rows = byName.Select(e =>
                        new MethodSummaryRow(
                            OperatorNames.FormatDisplayName(e.members[0].Name),
                            MemberReturnType(e.members[0]),
                            e.members.Count.ToString(),
                            SignatureDecodeMarker(e.members))).ToList();
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
                            MemberAccessors(m),
                            SignatureDecodeMarker(e.members));
                    }).ToList();
                    view.PropertySummaryRows = rows;
                    break;
                }
                case "field":
                {
                    var rows = byName.Select(e =>
                        new FieldSummaryRow(
                            e.members[0].Name,
                            e.members[0].ReturnType ?? "",
                            SignatureDecodeMarker(e.members))).ToList();
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

        // The compact-summary tables no longer carry a Decode column, so surface any
        // signature-decode degradation through the stderr warning instead.
        var degradedSignatures = allEntries
            .SelectMany(e => e.members)
            .Where(m => m.SignatureDecodeStatus is SignatureDecodeStatus.Degraded)
            .Select(m => FormatMemberDeclaration(type, m, abbreviate: false))
            .ToList();
        if (degradedSignatures.Count > 0)
            view.DegradedSignatureMembers = (view.DegradedSignatureMembers ?? [])
                .Concat(degradedSignatures)
                .ToList();

        return (truncated, "members");
    }

    /// <summary>
    /// Emits a stderr warning listing rendered members whose metadata signature blob could
    /// not be fully decoded. The default member tables no longer carry a Decode column, so
    /// this keeps signature-decode failures visible without cluttering successful output.
    /// </summary>
    internal static void WriteSignatureDecodeWarning(TypeView view, TextWriter error)
    {
        if (view.DegradedSignatureMembers is not { Count: > 0 } degraded)
            return;

        error.WriteLine(
            $"Warning: {degraded.Count} member signature(s) could not be fully decoded from " +
            "metadata; the displayed signature(s) may be incomplete or approximate:");
        foreach (var signature in degraded)
            error.WriteLine($"  - {signature}");
    }

    private static string? SignatureDecodeMarker(ApiMember member)
        => member.SignatureDecodeStatus is SignatureDecodeStatus.Degraded
            ? "degraded"
            : null;

    private static string? SignatureDecodeMarker(IEnumerable<ApiMember> members)
        => members.Any(member =>
            member.SignatureDecodeStatus is SignatureDecodeStatus.Degraded)
            ? "degraded"
            : null;

    internal static void PopulateConstructorOverloads(TypeView view, ApiType type, ApiOptions options)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly, options.KindFilter);
        var constructors = grouped
            .SelectMany(g => g.Value)
            .Where(m => m.Kind == "constructor")
            .ToList();

        if (constructors.Count == 0) return;

        var sorted = constructors
            .OrderBy(ConstructorParameterCount)
            .ToList();

        view.ConstructorOverloads = sorted.Select((ctor, i) =>
        {
            var paramCount = ConstructorParameterCount(ctor);
            var paramInfo = ConstructorParameterInfo(ctor);

            var overloadView = new ConstructorOverloadView
            {
                Title = $"Overload {i + 1}: {paramCount} parameter{(paramCount != 1 ? "s" : "")}",
                Signature = new CodeSection("csharp", $"new {type.Name}{ConstructorCall(type, ctor)}")
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

    private static int ConstructorParameterCount(ApiMember constructor)
        => constructor.SignatureModel?.ParameterCount
           ?? SignatureParser.CountParameters(constructor.Signature);

    private static List<(string name, string type, bool hasDefault)> ConstructorParameterInfo(ApiMember constructor)
        => constructor.SignatureModel is { } signature
            ? signature.ParameterInfoSummary
            : SignatureParser.ExtractParameterInfo(constructor.Signature);

    private static string ConstructorCall(ApiType type, ApiMember constructor)
    {
        if (constructor.SignatureModel is { } signature
            && signature.Parameters.All(static parameter => !parameter.HasDefault || !string.IsNullOrWhiteSpace(parameter.DefaultValueText)))
        {
            if (HasKeywordGenericIdentifier(type, signature))
            {
                var declaration = CSharpFormatterWithoutObsolete.FormatMember(
                    type,
                    constructor);
                if (!string.IsNullOrWhiteSpace(declaration))
                    return ConstructorCallFromDeclaration(declaration);
            }

            return CSharpFormatter.FormatParameterList(signature.Parameters);
        }

        // Compatibility-only fallback for legacy signatures without complete
        // structured default-value facts.
        return SignatureParser.FormatConstructorCall(constructor.Signature);
    }

    private static bool HasKeywordGenericIdentifier(ApiType type, ApiSignature signature)
    {
        return type.TypeParameters
            .Concat(signature.TypeParameters)
            .Select(parameter => parameter.Name)
            .Any(name => CSharpFormatter.EscapeIdentifier(name) != name);
    }

    private static string ConstructorCallFromDeclaration(string declaration)
    {
        var parenStart = declaration.IndexOf('(');
        return parenStart < 0 ? "()" : declaration[parenStart..];
    }

    /// <summary>
    /// The method-like members whose IL bodies the index/detail sections analyze. When
    /// the only selected member is a property or event (including an indexer) it has no
    /// method body of its own, so it is resolved to its accessor methods (issue #3265):
    /// get/set for a property or indexer, add/remove for an event, ordered so accessor
    /// ordinal 1 is the getter/adder and ordinal 2 the setter/remover — the order the
    /// overload-index selector (<c>Prop:1</c>/<c>Prop:2</c>) addresses. A field carries
    /// no accessor token and yields no body methods, so its body sections stay N/A.
    /// </summary>
    internal static List<ApiMember> ResolveBodyMethods(ApiType type, IReadOnlySet<string> requestedSections)
    {
        bool includeAbstract = requestedSections.Contains(SectionNames.UnsafeOperations);
        var methods = type.Members
            .Where(m => ApiMemberSectionDescriptors.IsMethodLike(m) && (!m.IsAbstract || includeAbstract))
            .ToList();

        if (methods.Count == 0
            && type.Members is [{ } single]
            && ApiMemberSectionDescriptors.HasAccessorTokens(single))
        {
            methods = AccessorMethods(single, type)
                .Where(m => !m.IsAbstract || includeAbstract)
                .ToList();
        }

        return methods;
    }

    /// <summary>
    /// Synthesizes method members for a property's or event's accessors, keyed by the
    /// accessor's own MethodDef token so body sections address the accessor directly.
    /// The getter/adder is yielded first so the default selection (accessor ordinal 1)
    /// targets it; the setter/remover follows as ordinal 2. Names use the metadata
    /// accessor spelling (<c>get_Name</c>, <c>set_Name</c>, <c>add_Name</c>,
    /// <c>remove_Name</c>) so graph roots and breadcrumbs read as the real method.
    /// </summary>
    internal static IEnumerable<ApiMember> AccessorMethods(ApiMember member, ApiType type)
    {
        var declaringType = string.IsNullOrEmpty(member.DeclaringType) ? type.FullName : member.DeclaringType!;
        switch (member.Kind)
        {
            case "property":
                // A getter returns the property type and takes only the index parameters; a
                // setter (and both event accessors) returns void and takes a trailing `value`.
                if (member.GetterToken is { } getter)
                    yield return Accessor(member, declaringType, $"get_{member.Name}", getter, "get", valueReturning: true);
                if (member.SetterToken is { } setter)
                    yield return Accessor(member, declaringType, $"set_{member.Name}", setter, "set", valueReturning: false);
                break;
            case "event":
                if (member.AdderToken is { } adder)
                    yield return Accessor(member, declaringType, $"add_{member.Name}", adder, "add", valueReturning: false);
                if (member.RemoverToken is { } remover)
                    yield return Accessor(member, declaringType, $"remove_{member.Name}", remover, "remove", valueReturning: false);
                break;
        }
    }

    /// <summary>
    /// Builds a method member for one accessor with a structured signature derived from the
    /// owner property/event, so declaration-rendering sections (Decompiled/Annotated Source,
    /// the overlays) print a real method header (<c>public string get_Name()</c>) rather than
    /// the owner's bare return type. The value type is the property/event type; a value-
    /// returning accessor (a getter) returns it and carries only the index parameters, while a
    /// void accessor (a setter or an event add/remove) appends it as a trailing <c>value</c>.
    /// Modifiers mirror the accessor: virtual/override/sealed/abstract/static come from the
    /// owner (both accessors share the property/event slot), while accessibility uses the
    /// per-accessor entry (a <c>private set</c> stays private) and only falls back to the
    /// owner's when the accessor declares none — events carry no per-accessor entry and so
    /// inherit the event's accessibility.
    /// </summary>
    static ApiMember Accessor(ApiMember owner, string declaringType, string name, int token, string accessorKind, bool valueReturning)
    {
        var ownerModel = owner.SignatureModel;
        var valueType = ownerModel?.ReturnType ?? owner.ReturnType ?? "object";
        var parameters = ownerModel?.Parameters is { Count: > 0 } indexParameters
            ? indexParameters.Select(CloneAccessorParameter).ToList()
            : new List<ApiParameter>();
        string returnType;
        if (valueReturning)
        {
            returnType = valueType;
        }
        else
        {
            returnType = "void";
            parameters.Add(new ApiParameter { Name = "value", Type = valueType });
        }

        var accessorEntry = ownerModel?.Accessors.FirstOrDefault(accessor => accessor.Kind == accessorKind);
        var accessibility = string.IsNullOrEmpty(accessorEntry?.Accessibility)
            ? owner.Accessibility
            : accessorEntry!.Accessibility;

        var renderedParameters = string.Join(", ", parameters.Select(p => $"{p.TypeWithModifier} {p.Name}"));
        return new ApiMember
        {
            Name = name,
            Kind = "method",
            MetadataToken = token,
            DeclaringType = declaringType,
            ReturnType = returnType,
            Signature = $"{returnType} {name}({renderedParameters})",
            SignatureModel = new ApiSignature
            {
                MemberName = name,
                ReturnType = returnType,
                Parameters = parameters,
            },
            IsStatic = owner.IsStatic,
            IsVirtual = owner.IsVirtual,
            IsAbstract = owner.IsAbstract,
            IsOverride = owner.IsOverride,
            IsSealed = owner.IsSealed,
            IsUnsafe = owner.IsUnsafe,
            Accessibility = accessibility,
            Documentation = owner.Documentation,
        };
    }

    static ApiParameter CloneAccessorParameter(ApiParameter parameter) => new()
    {
        Name = parameter.Name,
        Type = parameter.Type,
        CanonicalType = parameter.CanonicalType,
        Modifier = parameter.Modifier,
        HasDefault = parameter.HasDefault,
        DefaultValueText = parameter.DefaultValueText,
        Attributes = [.. parameter.Attributes],
    };

    internal static void PopulateIndexSections(
        TypeView view,
        ApiType type,
        List<ApiMember> methods,
        string dllPath,
        int? overloadIndex,
        IReadOnlySet<string> requestedSections,
        ApiMemberAnalysisInspection analysisInspection,
        string? pdbPath = null,
        IReadOnlySet<string>? explicitSections = null,
        ApiOptions? options = null)
    {
        var request = new MemberCodeProvider.Request(
            DecompiledSource: requestedSections.Contains(SectionNames.DecompiledSource)
                || requestedSections.Contains(SectionNames.SourceDiff),
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
            FidelityCauses: requestedSections.Contains(SectionNames.FidelityCauses),
            AppliedTaste: requestedSections.Contains(SectionNames.AppliedTaste),
            ProjectAssetsPath: options?.ProjectAssetsPath,
            TargetFramework: options?.Tfm,
            CaretFocus: options?.Focus);

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
        // Code and caller sections address a single selected member. When an overload
        // (or property/event accessor, issue #3265) is selected, restrict them to that
        // one method so a read/write property's two accessor bodies don't overwrite each
        // other. Without an explicit selection they still aggregate across all overloads.
        var bodyMethods = overloadIndex.HasValue ? singleMethodList : methods;

        if (request.Calls && singleMethodList is [{ MetadataToken: { } token } callsMethod])
        {
            RequestTelemetry.Breadcrumb("il-analysis.calls", callsMethod.Name);
            var callsByCaller = analysisInspection.BodyIndex.GetDirectCallsByCaller();
            var calls = callsByCaller.TryGetValue(token, out var directCalls)
                ? directCalls
                : ImmutableArray<Analysis.DirectCall>.Empty;
            var rows = calls
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
            var regions = analysisInspection.ResolveExceptionRegions(exceptionToken, out var error)
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

        if (request.Callers && bodyMethods.Count > 0)
        {
            RequestTelemetry.Breadcrumb("il-analysis.callers", $"{bodyMethods.Count} member(s)");
            var rows = new List<CallerSiteRow>();

            // Collect callers for each method (all overloads if multiple methods selected)
            foreach (var method in bodyMethods.Where(m => m.MetadataToken.HasValue))
            {
                var targetToken = method.MetadataToken!.Value;
                rows.AddRange(analysisInspection.CallerEdges(targetToken)
                    .Select(edge => CreateCallerRow(edge.Source, edge.Call)));
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
            var root = ToCallGraphNode(
                analysisInspection.BuildCallTree(graphToken),
                GetRequestedCallGraphFields(options));
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
            var callerTree = analysisInspection.BuildCallerTree(callerGraphToken);
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
            var evidence = InspectSafetyFindings(analysisInspection.BodyIndex, unsafeToken)
                .Evidence
                .Select(static finding => finding.Payload)
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
            if (requestedSections.Contains(SectionNames.AllocationFacts))
            {
                var rows = Analysis.SemanticFactProjection.AllocationFacts(
                        analysisInspection.BodyIndex.GetAllocationOccurrences(),
                        semanticToken)
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
                var safety = InspectSafetyFindings(analysisInspection.BodyIndex, semanticToken);
                var rows = Analysis.SemanticFactProjection.SafetyFacts(
                        safety.Evidence,
                        safety.Operations)
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
                var rows = Analysis.SemanticFactProjection.CostFacts(
                        analysisInspection.BodyIndex.GetDirectCallsByCaller(),
                        semanticToken)
                    .Select(fact => ToCostFactRow(fact, includeMember: false))
                    .ToList();
                if (rows.Count > 0 || ExplicitlySelected(SectionNames.CostFacts))
                {
                    memberCode.CostFactRows = rows;
                    hasCode = true;
                }
            }
        }

        if (request.DecompiledSource || request.AnnotatedSource || request.CostOverlay || request.SemanticsOverlay || request.IL || request.Attributes || request.Facts || request.FidelityCauses || request.AppliedTaste)
            RequestTelemetry.Breadcrumb("method-body-load", singleMethod?.Name ?? type.Name);

        foreach (var (member, code) in MemberCodeProvider.Collect(type, bodyMethods, dllPath, overloadIndex, request, pdbPath, options?.IncludeAll ?? false, options?.RenderOptions))
        {
            if (code.Attributes is { Count: > 0 } attributes)
            {
                view.MethodAttributeRows = attributes
                    .Select(a => new MethodAttributeRow(a.Name, a.Value ?? ""))
                    .ToList();
            }

            hasCode |= PopulateCSharpSections(memberCode, type, member, code);

            // The resolved config is consumed whenever a styled projection prints a
            // body -- Decompiled Source or Applied Taste (both render with the
            // config), but never a callers-only aggregation, a fidelity-only
            // projection (style-invariant), or a bodyless method whose printer never
            // ran. Key the warning latch off that produced styled projection so an
            // Applied-Taste-only run still surfaces a bad .dotnet-inspectconfig,
            // while a run that consumes no config stays silent.
            if (code.StyledProjectionProduced)
                options?.RenderConfigWarnings?.EmitOnce();

            if (code.FidelityCauses is not null)
            {
                memberCode.FidelityCauseRows = BuildFidelityCauseRows(code.FidelityCauses);
                hasCode = true;
            }

            if (code.AppliedTaste is not null)
            {
                memberCode.AppliedTasteRows = BuildAppliedTasteRows(code.AppliedTaste);
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

    internal static bool PopulateCSharpSections(
        MemberCodeView memberCode,
        ApiType type,
        ApiMember member,
        MemberCodeProvider.Item code)
    {
        bool hasCode = false;

        if (code.DecompiledResult is { } decompiledResult)
        {
            EmitDecompileBreadcrumb(member.Name, decompiledResult.Trace);
            memberCode.DecompiledSourceCode = FormatCSharpResult(
                type,
                member,
                code.MethodGenericParameters,
                decompiledResult,
                preferExpressionBodied: true,
                requiresAsyncBodyModifier: code.RequiresAsyncBodyModifier);
            hasCode = true;
        }

        if (code.AnnotatedResult is { } annotatedResult)
        {
            EmitDecompileBreadcrumb(member.Name, annotatedResult.Trace);
            memberCode.AnnotatedSourceCode = FormatCSharpResult(
                type,
                member,
                code.MethodGenericParameters,
                annotatedResult,
                requiresAsyncBodyModifier: code.RequiresAsyncBodyModifier,
                includeCustomAttributes: true,
                declarationTrailingComment: BuildTasteAnnotation(annotatedResult.Decisions));
            hasCode = true;
        }

        if (code.CostOverlayResult is { } costOverlayResult)
        {
            EmitDecompileBreadcrumb(member.Name, costOverlayResult.Trace);
            memberCode.CostOverlayCode = FormatCSharpResult(
                type,
                member,
                code.MethodGenericParameters,
                costOverlayResult,
                leadingBodyComments: code.CostOverlayHeaderComments,
                requiresAsyncBodyModifier: code.RequiresAsyncBodyModifier);
            hasCode = true;
        }

        if (code.SemanticsOverlayResult is { } semanticsOverlayResult)
        {
            EmitDecompileBreadcrumb(member.Name, semanticsOverlayResult.Trace);
            memberCode.SemanticsOverlayCode = FormatCSharpResult(
                type,
                member,
                code.MethodGenericParameters,
                semanticsOverlayResult,
                requiresAsyncBodyModifier: code.RequiresAsyncBodyModifier);
            hasCode = true;
        }

        return hasCode;
    }

    internal static List<FidelityCauseRow> BuildFidelityCauseRows(
        FindingInspection<Decompiler.DecompilerFidelityCause> inspection)
        => inspection switch
        {
            FindingInspection<Decompiler.DecompilerFidelityCause>.Complete complete
                when complete.Findings.IsEmpty =>
            [
                new(
                    "Complete",
                    null,
                    null,
                    null,
                    null,
                    null,
                    "No fidelity causes; decompiler fidelity is Full.")
            ],
            FindingInspection<Decompiler.DecompilerFidelityCause>.Complete complete =>
            [
                .. complete.Findings.Select(static finding =>
                {
                    var cause = finding.Payload;
                    return new FidelityCauseRow(
                        "Complete",
                        cause.Code,
                        FormatFidelityLocation(cause.Location),
                        cause.NodeKind,
                        cause.Node,
                        cause.Discriminator,
                        cause.Reason);
                })
            ],
            FindingInspection<Decompiler.DecompilerFidelityCause>.Absent absent =>
            [
                new(
                    "Absent",
                    null,
                    null,
                    null,
                    null,
                    null,
                    absent.Detail ?? "Method has no decompiler IR body.")
            ],
            FindingInspection<Decompiler.DecompilerFidelityCause>.Failed failed =>
            [
                new(
                    "Failed",
                    null,
                    null,
                    null,
                    null,
                    null,
                    failed.Error.Reason)
            ],
        };

    // The inline taste annotation for the Annotated view: one trailing side
    // comment on the member's signature, in the same shape the fact overlay uses
    // for analysis ("// alloc.new(object; path=branch)"), so a reader scans style
    // and analysis the same way. Anchored to the signature rather than to a
    // statement because a style decision carries a subject but no IL offset; the
    // Applied Taste section remains the full account.
    //
    // Nothing is annotated by default: no style decision is recorded unless a
    // knob was requested, so this comment appears exactly when the reader asked
    // for taste. A byte-divergent lens reports its fidelity instead of its
    // subject (the subject is the enclosing method, already on this line) — that
    // is also the only signal explaining why the interleaved IL is absent.
    internal static string? BuildTasteAnnotation(
        IReadOnlyList<Decompiler.DecompilerDecision> decisions)
    {
        var parts = decisions
            // Same exclusion as the Applied Taste rows: the framework-import
            // rewrite is always-on, not a configurable taste choice.
            .Where(static d => d.RuleId != "type-name.framework-imported")
            .Select(static d => d.Category == Decompiler.DecompilerDecisionCategories.StyleLens
                ? $"taste.{TrimLensPrefix(d.RuleId)}(fidelity=byte-divergent)"
                : $"taste.{d.RuleId}({d.Subject})")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (parts.Count == 0)
            return null;

        // A subject is a metadata name, and metadata names are untrusted: the CLR
        // does not require them to be spellable, printable, or even single-line.
        return NeutralizeForSideComment(string.Join("; ", parts));
    }

    // Makes an untrusted metadata string safe to carry in a trailing // comment
    // without losing which name it was.
    //
    // Two separate hazards, so two separate treatments. A C# line terminator ends
    // the comment, which would leave the rest of the annotation as active code in
    // a block a reader may paste or compile; those fold to a space so the comment
    // cannot leave its line. ReplaceLineEndings covers exactly the terminators C#
    // recognizes (CR, LF, CRLF, FF, NEL, LS, PS). Everything else here is a
    // rendering hazard rather than a syntax one — ANSI escapes recolor or rewrite
    // the terminal, and bidi overrides reorder what follows them, so a name can
    // misrepresent itself or its neighbors. Those become visible \uXXXX escapes,
    // which keeps the identity legible instead of dropping characters that are
    // part of the real name.
    private static string NeutralizeForSideComment(string value)
    {
        var folded = value.ReplaceLineEndings(" ");
        if (!folded.Any(IsRenderingHazard))
            return folded;

        var builder = new StringBuilder(folded.Length);
        foreach (var ch in folded)
        {
            if (IsRenderingHazard(ch))
                builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)ch:X4}");
            else
                builder.Append(ch);
        }

        return builder.ToString();

        // Tab is deliberately allowed: it is legal in a comment and renders as
        // space. Vertical tab is not a C# line terminator, so ReplaceLineEndings
        // leaves it, but it does move the cursor down a line in a terminal — it is
        // caught here as the C0 control it is.
        static bool IsRenderingHazard(char ch) =>
            ch != '\t' && (char.IsControl(ch) || IsBidiControl(ch));

        // Exactly Unicode's Bidi_Control set: ALM, LRM/RLM, the LRE/RLE/PDF/LRO/RLO
        // embeddings and overrides, and the LRI/RLI/FSI/PDI isolates. Deliberately
        // narrower than the Cf category — a zero-width joiner or a BOM does not
        // reorder its neighbors, and legitimate identifiers may contain format
        // characters, so escaping all of Cf would corrupt ordinary names.
        static bool IsBidiControl(char ch) =>
            ch is '\u061C' or '\u200E' or '\u200F'
                or >= '\u202A' and <= '\u202E'
                or >= '\u2066' and <= '\u2069';
    }

    static string TrimLensPrefix(string ruleId)
        => ruleId.StartsWith("style-lens.", StringComparison.Ordinal)
            ? ruleId["style-lens.".Length..]
            : ruleId;

    internal static List<AppliedTasteRow> BuildAppliedTasteRows(
        IReadOnlyList<Decompiler.DecompilerDecision> decisions)
        =>
        [
            // Projects the configurable choices the decompiler RECORDED as
            // decisions -- the byte-divergent style lenses, the opt-in chain-wrap,
            // and the byte-preserving this.-qualification knobs (#3156). Only
            // knob-attributed qualification is recorded; a mandatory shadow
            // disambiguation this. never appears here.
            .. decisions
                // The framework-import rewrite (List<T> for the mangled metadata
                // name) is always-on and universally expected, not a configurable
                // taste choice -- keep it off the taste surface.
                .Where(static d => d.RuleId != "type-name.framework-imported")
                .Select(static d => new AppliedTasteRow(
                    d.RuleId,
                    d.Category == Decompiler.DecompilerDecisionCategories.StyleLens
                        ? "byte-divergent"
                        : "byte-preserving",
                    d.Subject,
                    d.Detail))
        ];

    static string FormatFidelityLocation(Decompiler.DecompilerFidelityLocation location)
        => location.Kind switch
        {
            Decompiler.DecompilerFidelityLocationKind.Signature => "signature",
            Decompiler.DecompilerFidelityLocationKind.IlOffset
                when location.ILOffset is { } offset => MarkoutInline.Code($"IL_{offset:X4}"),
            Decompiler.DecompilerFidelityLocationKind.Local
                when location.LocalIndex is { } local => MarkoutInline.Code($"V_{local}"),
            _ => "unknown",
        };

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

    static SafetyFindingCensus InspectSafetyFindings(
        Analysis.LibraryBodyIndex index,
        int methodToken)
    {
        index.GetUnsafeEvidenceByMember().TryGetValue(methodToken, out var evidence);
        index.GetUnsafetyOccurrences().TryGetValue(methodToken, out var operations);
        evidence = evidence.IsDefault ? [] : evidence;
        operations = operations.IsDefault ? [] : operations;

        var method = index.Methods.FirstOrDefault(candidate => candidate.MetadataToken == methodToken)
            ?? operations.FirstOrDefault()?.Method
            ?? evidence.FirstOrDefault()?.Member;
        if (method is null)
            return new([], []);

        var subject = FindingSubject(method);
        return new(
            Analysis.AnalysisFindings.InspectUnsafeEvidence(evidence, subject),
            Analysis.AnalysisFindings.InspectUnsafety(operations, subject));
    }

    static FindingSubject FindingSubject(Analysis.MethodIdentity method)
        => new(
            $"{method.ModuleVersionId:N}:0x{method.MetadataToken:X8}",
            FormatMethod(method));

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

    internal static void PopulateUnsafeMembers(TypeView view, ApiType type, Analysis.LibraryBodyIndex index)
    {
        var rows = index
            .UnsafeEvidence
            .Where(evidence => ApiAnalysisInspection.SameType(evidence.Member.DeclaringType, type))
            .GroupBy(evidence => evidence.Member.MetadataToken)
            .SelectMany(group =>
            {
                var method = group.First().Member;
                return Analysis.AnalysisFindings.InspectUnsafeEvidence(
                    group,
                    FindingSubject(method));
            })
            .Select(static finding => finding.Payload)
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
        IReadOnlyList<ApiAnalysisInspection.MemberExceptionRegion> exceptionRegions,
        IReadOnlySet<string>? explicitSections = null)
    {
        var rows = exceptionRegions
                .OrderBy(item => GetMemberSortOrder(item.Member.Kind))
                .ThenBy(item => item.Member.Name, StringComparer.Ordinal)
                .ThenBy(item => GetMemberSignatureSortKey(item.Member), StringComparer.Ordinal)
                .Select(item => new TypeExceptionRegionRow(
                    MarkoutInline.Code(GetMemberDisplaySignature(type, item.Member)),
                    item.Region.Region,
                    item.Region.Clause,
                    FormatILRange(item.Region.TryStart, item.Region.TryEnd),
                    FormatILRange(item.Region.HandlerStart, item.Region.HandlerEnd),
                    item.Region.FilterStart is { } filterStart && item.Region.FilterEnd is { } filterEnd
                        ? FormatILRange(filterStart, filterEnd)
                        : null,
                    item.Region.CaughtType))
            .ToList();

        if (rows.Count > 0 || explicitSections is not null && explicitSections.Contains(SectionNames.ExceptionRegions))
            view.ExceptionRegionRows = rows;
    }

    internal static void PopulateCalledTypes(
        TypeView view,
        ApiType type,
        Analysis.LibraryBodyIndex index,
        IReadOnlySet<string>? explicitSections = null)
    {
        var rows = index
            .CalledTypes(method => ApiAnalysisInspection.SameType(method.DeclaringType, type))
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
        Analysis.LibraryBodyIndex index,
        IReadOnlySet<string>? requestedSections,
        IReadOnlySet<string>? explicitSections = null)
    {
        var methodTokens = type.Members
            .Where(member => member.MetadataToken is not null && ApiMemberSectionDescriptors.IsMethodLike(member))
            .Select(member => member.MetadataToken!.Value)
            .ToArray();

        if (requestedSections?.Contains(SectionNames.AllocationFacts) == true)
        {
            var allocationOccurrences = index.GetAllocationOccurrences();
            var rows = methodTokens
                .SelectMany(token => Analysis.SemanticFactProjection.AllocationFacts(allocationOccurrences, token))
                .Select(fact => ToAllocationFactRow(fact, includeMember: true))
                .ToList();
            if (rows.Count > 0 || explicitSections is not null && explicitSections.Contains(SectionNames.AllocationFacts))
                view.AllocationFactRows = rows;
        }

        if (requestedSections?.Contains(SectionNames.SafetyFacts) == true)
        {
            var rows = methodTokens
                .SelectMany(token =>
                {
                    var safety = InspectSafetyFindings(index, token);
                    return Analysis.SemanticFactProjection.SafetyFacts(
                        safety.Evidence,
                        safety.Operations);
                })
                .Select(fact => ToSafetyFactRow(fact, includeMember: true))
                .ToList();
            if (rows.Count > 0 || explicitSections is not null && explicitSections.Contains(SectionNames.SafetyFacts))
                view.SafetyFactRows = rows;
        }

        if (requestedSections?.Contains(SectionNames.CostFacts) == true)
        {
            var directCallsByCaller = index.GetDirectCallsByCaller();
            var rows = methodTokens
                .SelectMany(token => Analysis.SemanticFactProjection.CostFacts(directCallsByCaller, token))
                .Select(fact => ToCostFactRow(fact, includeMember: true))
                .ToList();
            if (rows.Count > 0 || explicitSections is not null && explicitSections.Contains(SectionNames.CostFacts))
                view.CostFactRows = rows;
        }
    }

    internal static void PopulateOptimizationOpportunities(
        TypeView view,
        ApiType type,
        Analysis.LibraryBodyIndex index,
        IReadOnlySet<string>? explicitSections = null,
        PerformanceTriageOptions? options = null,
        bool restrictToModelMembers = false)
    {
        HashSet<int>? memberTokens = restrictToModelMembers
            ? type.Members.Where(m => m.MetadataToken is not null).Select(m => m.MetadataToken!.Value).ToHashSet()
            : null;
        var rows = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                LibraryMetadataService.TriageOpportunities(index, options)
                    .Where(opportunity => ApiAnalysisInspection.SameType(opportunity.Method.DeclaringType, type))
                    .Where(opportunity => !LibraryMetadataService.IsGeneratedMethod(opportunity.Method, index.GeneratedFrameworkTypeNames))
                    .Where(opportunity => memberTokens is null || memberTokens.Contains(opportunity.Method.MetadataToken)),
                options)
            .Select(opportunity => new OptimizationOpportunityRow(
                MarkoutInline.Code(FormatMember(null, opportunity.Method.Name, opportunity.Method.ParameterTypes, [])),
                opportunity.CandidateId is null ? null : MarkoutInline.Code(opportunity.CandidateId),
                opportunity.SourceFinding,
                LibraryMetadataService.FormatProvenance(opportunity.Provenance),
                opportunity.RootReach.ToString(),
                opportunity.Shape,
                opportunity.Operation,
                opportunity.OperandToken is { } token ? MarkoutInline.Code($"0x{token:X8}") : null,
                MarkoutInline.Code(opportunity.Evidence),
                opportunity.SafeFixDirection,
                opportunity.Confidence,
                LibraryMetadataService.IteratesInLoop(opportunity) ? "loop" : "",
                LibraryMetadataService.FormatCallerLoop(opportunity.CallerLoop),
                opportunity.CallerLoop?.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                LibraryMetadataService.FormatCallerLoopWitness(opportunity.CallerLoop),
                opportunity.RuntimeAllocationType is { Length: > 0 } allocation ? MarkoutInline.Code(allocation) : null,
                opportunity.PathContext,
                opportunity.PathConfidence,
                opportunity.PostDominance,
                opportunity.ILOffset is { } offset ? MarkoutInline.Code($"IL_{offset:X4}") : null,
                opportunity.Weight,
                opportunity.DirectAllocationSites?.ToString(),
                opportunity.OnceAllocationPaths?.ToString(),
                opportunity.ConditionalAllocationPaths?.ToString(),
                opportunity.RepeatedAllocationPaths?.ToString(),
                opportunity.UnknownAllocationPaths?.ToString(),
                opportunity.CachedAllocationSites?.ToString(),
                opportunity.OpaqueCallPaths?.ToString(),
                opportunity.AllocationCountSaturated ? "yes" : null))
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

    internal static void PopulateTopLeverage(TypeView view, ApiType type, Analysis.LibraryBodyIndex index, bool restrictToModelMembers = false)
    {
        var drillByToken = BuildMemberDrillMap(type);

        // Rank every method declared on this type; fanin is still measured across all
        // callers in the assembly. The full ranked set is emitted and the generic row
        // limiter (`-n`/`--rows`) trims the rendered table. In member-detail/overload
        // contexts `type.Members` is narrowed to the selected member(s), so restrict the
        // ranked rows to those tokens (mirrors PopulateOptimizationOpportunities).
        var rows = index.TopLeverage(count: int.MaxValue, scope: method => ApiAnalysisInspection.SameType(method.DeclaringType, type))
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

    readonly record struct SafetyFindingCensus(
        ImmutableArray<Finding<Analysis.UnsafeEvidence>> Evidence,
        ImmutableArray<Finding<Analysis.UnsafetyOccurrence>> Operations);

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

    private static string DiagnosticComment(Decompiler.DecompilerResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"// {diagnostic}"));

    private static CodeSection FormatCSharpResult(
        ApiType type,
        ApiMember member,
        IReadOnlyList<string>? methodGenericParameters,
        Decompiler.DecompilerResult result,
        bool preferExpressionBodied = false,
        IReadOnlyList<string>? leadingBodyComments = null,
        bool requiresAsyncBodyModifier = false,
        bool includeCustomAttributes = false,
        string? declarationTrailingComment = null)
    {
        if (!result.Succeeded)
            return new CodeSection("csharp", DiagnosticComment(result));

        try
        {
            return new CodeSection(
                "csharp",
                FormatSourceWithDeclaration(
                    type,
                    member,
                    methodGenericParameters,
                    result,
                    preferExpressionBodied,
                    leadingBodyComments,
                    requiresAsyncBodyModifier,
                    includeCustomAttributes,
                    declarationTrailingComment));
        }
        catch (Exception ex)
        {
            return new CodeSection(
                "csharp",
                $"// {Decompiler.DiagnosticIds.InternalError}: declaration formatting failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static string FormatSourceWithDeclaration(
        ApiType type,
        ApiMember member,
        IReadOnlyList<string>? methodGenericParameters,
        Decompiler.DecompilerResult result,
        bool preferExpressionBodied = false,
        IReadOnlyList<string>? leadingBodyComments = null,
        bool requiresAsyncBodyModifier = false,
        bool includeCustomAttributes = false,
        string? declarationTrailingComment = null)
    {
        var lowered = result.Output
            ?? throw new ArgumentException("A successful decompiler result is required.", nameof(result));
        var bodyShape = new CSharpBlockBody(lowered)
        {
            RequiresAsyncModifier = requiresAsyncBodyModifier,
            RequiresUnsafeModifier = result.RequiresUnsafeBodyModifier,
            // Only spell '~Type()' when the destructor pass recovered the
            // canonical try/finally { base.Finalize(); } scaffold (issue #3157).
            // A Finalize override whose body did not match keeps the literal
            // 'void Finalize()' so recompiling this selected-member source does
            // not silently re-inject the compiler's mandatory base.Finalize().
            SuppressDestructorSyntax = member.IsFinalizer && !result.BodyIsDestructor
        };
        var formatter = includeCustomAttributes ? AnnotatedCSharpFormatter : DefaultCSharpFormatter;
        var declaration = formatter.FormatMemberWithBody(
            type,
            member,
            bodyShape,
            methodGenericParameters);
        if (result.ConstructorChain is { } constructorChain
            && !string.IsNullOrWhiteSpace(declaration))
        {
            declaration = $"{declaration} : {constructorChain}";
        }
        var body = lowered.TrimEnd();

        // The trailing side comment rides the signature line, so it has to be
        // appended after every declaration suffix (the constructor chain above)
        // and after the terminating ';' of an expression-bodied render, never
        // inside the expression. A member with no declaration to hang it on
        // (a bare body projection) keeps the signal as a single leading line
        // rather than dropping it.
        string Annotate(string line)
            => declarationTrailingComment is { Length: > 0 } comment
                ? $"{line}  // {comment}"
                : line;

        if (string.IsNullOrWhiteSpace(declaration))
        {
            return declarationTrailingComment is { Length: > 0 } bareComment
                ? $"// {bareComment}{Environment.NewLine}{Decompiler.Annotations.AnnotationCaret.Flatten(body)}"
                : Decompiler.Annotations.AnnotationCaret.Flatten(body);
        }

        bool hasLeadingComments = leadingBodyComments is { Count: > 0 };
        if (!hasLeadingComments && preferExpressionBodied && CSharpExpressionBody.FromSingleStatement(body) is { } expressionBody)
            return Annotate($"{declaration} => {Decompiler.Annotations.AnnotationCaret.Flatten(expressionBody)};");

        // A multi-line single-statement expression body renders expression-bodied
        // too (a raised switch return, issue #3088; a wrapped fluent chain in
        // return or void expression-statement position, issue #3084):
        // `head => <value>` with the continuation lines below. Those lines sit at
        // their body-relative (column-zero) indent, so at this member's
        // column-zero declaration they need no re-indentation. Gate strictly on
        // the printer's typed BodyIsSingleExpressionBody signal; the extraction
        // helper is not sound on the flat string alone.
        if (!hasLeadingComments && preferExpressionBodied && result.BodyIsSingleExpressionBody
            && CSharpExpressionBody.MultilineExpressionBodyLines(body) is { Count: > 0 } multilineExpression)
        {
            var expression = new StringBuilder();
            expression.Append(declaration).Append(" => ")
                .Append(Decompiler.Annotations.AnnotationCaret.Flatten(multilineExpression[0]));
            for (int i = 1; i < multilineExpression.Count; i++)
            {
                expression.Append(Environment.NewLine);
                expression.Append(Decompiler.Annotations.AnnotationCaret.Flatten(multilineExpression[i]));
                if (i == multilineExpression.Count - 1)
                    expression.Append(';');
            }
            return Annotate(expression.ToString());
        }

        var formattedBodyLines = body.ReplaceLineEndings("\n").Split('\n');
        var lines = hasLeadingComments
            ? leadingBodyComments!.Concat(formattedBodyLines)
            : formattedBodyLines;

        // A caret line is pre-positioned at this declaration's column, so it is
        // the one body line that must not be indented. The indent width is the
        // caret renderer's constant rather than a literal: the two have to agree
        // or the carets shear away from the code they point at.
        string bodyIndent = new(' ', Decompiler.Annotations.AnnotationCaret.BodyIndentWidth);
        var indentedBody = string.Join(
            Environment.NewLine,
            lines.Select(line =>
                line.Length == 0 ? ""
                : Decompiler.Annotations.AnnotationCaret.TryHoist(line, out var hoisted) ? hoisted
                : bodyIndent + line));

        return $"{Annotate(declaration)}{Environment.NewLine}{{{Environment.NewLine}{indentedBody}{Environment.NewLine}}}";
    }

    private static string FormatMemberDeclaration(
        ApiType type,
        ApiMember member,
        bool abbreviate,
        IReadOnlyList<string>? methodParameters = null,
        bool forceAsync = false,
        bool forceUnsafe = false)
    {
        var formatter = !forceAsync && !forceUnsafe
            ? abbreviate ? AbbreviatedCSharpFormatter : DefaultCSharpFormatter
            : new CSharpFormatter(new CSharpFormatOptions
            {
                AbbreviateSignature = abbreviate,
                ForceAsync = forceAsync,
                ForceUnsafe = forceUnsafe
            });
        return formatter.FormatMember(type, member, methodParameters);
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

    internal static string FormatGenericFullName(ApiType type)
        => MetadataTypeNameFormatter.FormatFullName(type);

    internal static string GetMemberSignatureSortKey(ApiMember member)
        => ApiMemberIdentity.GetMemberSignatureSortKey(member);

    internal static string GetMemberDisplaySignature(ApiType type, ApiMember member)
        => member.Signature ?? $"{FormatGenericFullName(type)}.{OperatorNames.FormatDisplayName(member.Name)}";

    private static string GetMemberSelectorName(ApiMember member)
        => ApiMemberIdentity.GetMemberSelectorName(member);

    internal static string GetMemberDigest(string canonicalSignature)
        => ApiMemberIdentity.GetMemberDigest(canonicalSignature);

    internal static string GetCanonicalSignature(ApiType type, ApiMember member)
        => ApiMemberIdentity.GetCanonicalSignature(type, member);

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
        "finalizer" => "Finalizer",
        _ => char.ToUpper(kind[0]) + kind[1..] + "s"
    };

    private static bool IsCompilerGenerated(string name) => MemberFilters.IsCompilerGenerated(name);

    private static readonly string[] MemberKinds =
    [
        "constructor",
        "finalizer",
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

    // ===== Table View Builders =====

    /// <summary>
    /// Builds a unified tabular view for a single type's members.
    /// All member kinds are merged into one table with a Kind column.
    /// </summary>
    internal static (ApiTypeTableView view, int truncated) BuildTypeTableView(ApiType type, ApiOptions options)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly, options.KindFilter);
        var requestedKinds = GetRequestedMemberKinds(options.IncludeSections);
        if (requestedKinds is { Count: > 0 })
            grouped = grouped
                .Where(kvp => requestedKinds.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        if (grouped.Count == 0) return (new ApiTypeTableView(), 0);

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
                "constructor" or "finalizer" => "",
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
            return new ApiTableRow(kindLabel, OperatorNames.FormatDisplayName(m.Name), returnType, detail);
        }).ToList();

        return (new ApiTypeTableView { Rows = rows }, truncated);
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
    internal static (ApiSurfaceTableView view, int truncated) BuildSurfaceTableView(ApiSurface api, ApiOptions options)
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
                return new ApiSurfaceTableRow(
                    t.Kind,
                    MarkoutInline.Code(FormatGenericFullName(t)),
                    t.Members.Count.ToString(),
                    desc);
            })
            .ToList();

        var view = new ApiSurfaceTableView();
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
        "finalizer" => 1,
        "field" => 2,
        "property" => 3,
        "method" => 4,
        "operator" => 5,
        "explicit-interface-implementation" => 6,
        "extension-method" => 7,
        "event" => 8,
        _ => 9
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
            "finalizer" => "Finalizer",
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
                case "Finalizer":
                    kinds.Add("finalizer");
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
