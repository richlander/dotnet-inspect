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

            PopulateTypeSections(view, api.Types, options.ShowDocs);
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
                return new TypeSummaryRow(fullName, members, desc);
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
            api, options.Verbosity, options.IncludeSections, options.ExcludeSections);

        return new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            IncludeDescription = options.Verbosity != Verbosity.Quiet
        };
    }

    // ===== Single Type Rendering =====

    public static string RenderTypeMarkdown(ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options)
    {
        var writer = new MarkdownWriter(BuildTypeWriterOptions(type, options));
        RenderTypeMarkdown(writer, type, foundIn, packageName, packageVersion, apiSource, selectedTfm, options);
        return writer.ToString().TrimEnd();
    }

    public static void RenderTypeMarkdown(MarkoutWriter writer, ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options)
    {
        // Build the view model
        var view = BuildApiTypeView(type, foundIn, packageName, packageVersion, apiSource, selectedTfm, options);

        // Populate enum values declaratively for Normal+ enums
        if (type.Kind == "enum" && options.Verbosity >= Verbosity.Normal)
            PopulateEnumValues(view, type, options);

        int truncatedCount = 0;
        string truncatedNoun = "";

        // Populate member sections declaratively (unless quiet or enum)
        bool fullSerializer = options.Verbosity != Verbosity.Quiet;

        if (fullSerializer && view.EnumValues == null && view.EnumValuesWithDocs == null)
        {
            if (options.CtorOnly && options.Verbosity >= Verbosity.Normal
                && type.Members.Any(m => m.Kind == "constructor"))
            {
                PopulateConstructorOverloads(view, type, options);
            }
            else if (options.Verbosity == Verbosity.Minimal)
            {
                (truncatedCount, truncatedNoun) = PopulateMemberSummarySections(view, type, options);
            }
            else
            {
                (truncatedCount, truncatedNoun) = PopulateMemberSections(view, type, options);
            }
        }

        // Serialize the complete view
        new MarkoutContext().Serialize(view, writer);

        // Truncation message
        if (truncatedCount > 0)
            writer.WriteParagraph($"... *and {truncatedCount} more {truncatedNoun}*");
    }

    internal static MarkoutWriterOptions BuildTypeWriterOptions(ApiType type, ApiOptions options)
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(
            type, options.Verbosity, options.IncludeSections, options.ExcludeSections);

        return new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            IncludeDescription = options.Verbosity != Verbosity.Quiet
        };
    }

    // ===== Shape Output (--shape) =====

    public static void WriteShapeOutput(ApiType type, string? foundIn, string? packageName, string? packageVersion, HashSet<string> memberFilter)
    {
        var view = BuildShapeView(type, foundIn, packageName, packageVersion, memberFilter);
        MarkoutSerializer.Serialize(view, Console.Out, TypeViewContext.Default);
    }

    // ===== View Model Factories =====

    internal static ApiTypeView BuildApiTypeView(ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options)
    {
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

        // Type parameters inline (for quiet/minimal only)
        string? typeParamsInline = null;
        if (type.TypeParameters.Count > 0 &&
            (options.Verbosity == Verbosity.Quiet || options.Verbosity == Verbosity.Minimal))
        {
            var paramDescriptions = type.TypeParameters
                .Select(tp => tp.Constraints.Count > 0
                    ? $"{tp.DisplayName} : {tp.ConstraintsSummary}"
                    : tp.DisplayName);
            typeParamsInline = string.Join(", ", paramDescriptions);
        }

        // Description (from docs)
        string? description = null;
        if (options.ShowDocs && type.Documentation.Summary != null)
            description = type.Documentation.Summary;

        // Samples info (only with --docs/--samples)
        string? samplesInfo = null;
        if ((options.ShowDocs || options.ShowSamples) && type.Documentation.Samples.Count > 0)
            samplesInfo = $"{type.Documentation.Samples.Count} available";

        // Type parameters table (Normal+)
        List<TypeParameterRow>? typeParameterRows = null;
        if (type.TypeParameters.Count > 0 && options.Verbosity >= Verbosity.Normal)
        {
            typeParameterRows = type.TypeParameters
                .Select(tp => new TypeParameterRow { Parameter = tp.DisplayName, Constraints = tp.ConstraintsSummary ?? "" })
                .ToList();
        }

        // Interfaces (Detailed+)
        List<InterfaceRow>? interfaceRows = null;
        if (type.Interfaces.Count > 0 && options.Verbosity >= Verbosity.Detailed)
        {
            interfaceRows = type.Interfaces.Order()
                .Select(i => new InterfaceRow { Interface = i })
                .ToList();
        }

        // Baseclass (Detailed+, filtered for trivial bases)
        List<BaseclassRow>? baseclassRows = null;
        if (baseType != null && options.Verbosity >= Verbosity.Detailed)
        {
            baseclassRows = [new BaseclassRow { Type = baseType }];
        }

        // Source files (when SourceLink data is available)
        List<SourceRow>? sourceRows = null;
        if (type.SourceFilePath != null)
        {
            sourceRows = [new SourceRow
            {
                File = Path.GetFileName(type.SourceFilePath),
                Url = type.GitHubBrowseUrl
            }];

            foreach (var f in type.AdditionalSourceFiles)
            {
                sourceRows.Add(new SourceRow
                {
                    File = Path.GetFileName(f.FilePath ?? ""),
                    Url = f.GitHubBrowseUrl
                });
            }
        }

        return new ApiTypeView
        {
            Title = $"{FormatGenericFullName(type)}{packageInfo}",
            Description = description,
            Kind = type.Kind,
            Modifiers = modifiers.Count > 0 ? string.Join(", ", modifiers) : null,
            BaseType = baseType,
            TypeParametersInline = typeParamsInline,
            Assembly = foundIn,
            Package = packageName,
            Version = packageVersion,
            Source = apiSource,
            Tfm = selectedTfm,
            SamplesInfo = samplesInfo,
            // Member stats for quiet verbosity
            Constructors = options.Verbosity == Verbosity.Quiet ? NullIfZero(type.Members.Count(m => m.Kind == "constructor")) : null,
            Fields = options.Verbosity == Verbosity.Quiet ? NullIfZero(type.Members.Count(m => m.Kind == "field" && !m.EnumValue.HasValue)) : null,
            Properties = options.Verbosity == Verbosity.Quiet ? NullIfZero(type.Members.Count(m => m.Kind == "property")) : null,
            Methods = options.Verbosity == Verbosity.Quiet ? NullIfZero(type.Members.Count(m => m.Kind == "method")) : null,
            Events = options.Verbosity == Verbosity.Quiet ? NullIfZero(type.Members.Count(m => m.Kind == "event")) : null,
            TypeParameterRows = typeParameterRows,
            InterfaceRows = interfaceRows,
            BaseclassRows = baseclassRows,
            SourceRows = sourceRows
        };

        static int? NullIfZero(int count) => count > 0 ? count : null;
    }

    internal static TypeShapeView BuildShapeView(ApiType type, string? foundIn, string? packageName, string? packageVersion, HashSet<string> memberFilter)
    {
        List<TreeNode> nodes = [];

        // Inheritance (always show)
        if (!string.IsNullOrEmpty(type.BaseType) && type.BaseType != "Object")
        {
            nodes.Add(new TreeNode("Inherits", new[] { type.BaseType }));
        }

        // Interfaces (always show)
        if (type.Interfaces.Count > 0)
        {
            nodes.Add(new TreeNode("Implements", type.Interfaces));
        }

        // Type parameters with constraints (always show)
        if (type.TypeParameters.Count > 0)
        {
            var typeParamDescriptions = type.TypeParameters
                .Select(tp => tp.Constraints.Count > 0
                    ? $"{tp.DisplayName} : {tp.ConstraintsSummary}"
                    : tp.DisplayName)
                .ToList();
            nodes.Add(new TreeNode("Type Parameters", typeParamDescriptions));
        }

        // Group members by kind
        if (type.Members.Count > 0)
        {
            var members = type.Members.Where(m => !IsCompilerGenerated(m.Name));

            if (memberFilter.Count > 0)
            {
                members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, memberFilter));
            }

            var membersByKind = members
                .GroupBy(m => m.Kind)
                .OrderBy(g => GetTreeKindOrder(g.Key));

            foreach (var group in membersByKind)
            {
                var kindLabel = GetTreeKindLabel(group.Key, group.Count());
                var memberSignatures = group
                    .OrderBy(m => m.Name)
                    .Select(m => m.Signature ?? OperatorNames.FormatDisplayName(m.Name))
                    .ToList();

                nodes.Add(new TreeNode(kindLabel, memberSignatures));
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

    internal static void PopulateEnumValues(ApiTypeView view, ApiType type, ApiOptions options)
    {
        var enumMembers = type.Members
            .Where(m => m.Kind == "field" && m.EnumValue.HasValue && !IsCompilerGenerated(m.Name))
            .OrderBy(m => m.EnumValue)
            .ToList();

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

    internal static (int truncated, string noun) PopulateMemberSections(ApiTypeView view, ApiType type, ApiOptions options)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly);
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

        bool hasDocs = options.ShowDocs && allMembers.Any(m => m.Documentation.Summary != null);
        bool abbreviate = options.Verbosity == Verbosity.Minimal;
        bool showSelect = options.ShowSelect;

        foreach (var group in kindGroups)
        {
            var kind = group.Key;
            var members = group.OrderBy(m => m.Name).ThenBy(m => m.Signature).ToList();

            // Pre-compute overload counts and indices for --select
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
                    select = $"`{(hasOverloads ? $"{m.Name}:{idx}" : m.Name)}`";
                }

                return new MemberRow(select, OperatorNames.FormatDisplayName(m.Name), $"`{sig}`", hasDocs ? (m.Documentation.Summary ?? "") : null);
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
                    { if (hasDocs) view.MethodSelectRowsWithDocs = rows; else view.MethodSelectRows = rows; }
                    else
                    { if (hasDocs) view.MethodRowsWithDocs = rows; else view.MethodRows = rows; }
                    break;
                case "event":
                    if (showSelect)
                    { if (hasDocs) view.EventSelectRowsWithDocs = rows; else view.EventSelectRows = rows; }
                    else
                    { if (hasDocs) view.EventRowsWithDocs = rows; else view.EventRows = rows; }
                    break;
            }
        }

        return (truncated, "members");
    }

    /// <summary>
    /// Populates compact member summary sections for Minimal verbosity.
    /// Groups members by name within each kind, with kind-specific columns
    /// matching the old QuietMemberFormatter design.
    /// </summary>
    internal static (int truncated, string noun) PopulateMemberSummarySections(ApiTypeView view, ApiType type, ApiOptions options)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly);
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
                        view.MethodSummaryRowsWithOverloads = rows;
                    else
                        view.MethodSummaryRows = rows;
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
                    view.EventSummaryRows = rows;
                    break;
                }
            }
        }

        return (truncated, "members");
    }

    internal static void PopulateConstructorOverloads(ApiTypeView view, ApiType type, ApiOptions options)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly);
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
                    .Select(p => new ConstructorParameterRow(p.name, $"`{p.type}`", p.hasDefault ? "optional" : "required"))
                    .ToList();
            }

            return overloadView;
        }).ToList();
    }

    internal static void PopulateIndexSections(ApiTypeView view, ApiType type, List<ApiMember> methods, string dllPath, int overloadIndex)
    {
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);

        foreach (var method in methods)
        {
            // Custom attributes
            var attributes = AttributeReader.GetMethodAttributes(
                peReader, type.FullName, method.Name, overloadIndex, publicOnly: true);
            if (attributes.Count > 0)
            {
                view.MethodAttributeRows = attributes
                    .Select(a => new MethodAttributeRow(a.Name, a.Value ?? ""))
                    .ToList();
            }

            // Lowered C#
            try
            {
                var context = Decompiler.MethodBodyContext.Create(
                    peReader, type.FullName, method.Name, overloadIndex, publicOnly: true);
                if (context != null)
                {
                    var lowered = Decompiler.CSharpEmitter.Emit(context);
                    if (!string.IsNullOrWhiteSpace(lowered))
                        view.LoweredCSharp = new CodeSection("csharp", lowered.TrimEnd());
                }
            }
            catch { }

            // IL disassembly
            var instructions = ILDisassembler.DisassembleMethod(
                peReader, type.FullName, method.Name, overloadIndex, publicOnly: true);
            if (instructions is { Count: > 0 })
            {
                var ilText = string.Join(Environment.NewLine, instructions.Select(i => i.ToString()));
                view.ILCode = new CodeSection("il", ilText);
            }

            // Annotated IL
            try
            {
                var context = Decompiler.MethodBodyContext.Create(
                    peReader, type.FullName, method.Name, overloadIndex, publicOnly: true);
                if (context != null)
                {
                    var annotated = Decompiler.AnnotatedILEmitter.Emit(
                        context, Decompiler.ILAnnotationDepth.Structured);
                    if (!string.IsNullOrWhiteSpace(annotated))
                        view.AnnotatedIL = new CodeSection("il", annotated.TrimEnd());
                }
            }
            catch { }
        }
    }

    // ===== Helper Methods =====

    internal static Dictionary<string, List<ApiMember>> GroupMembersByKind(ApiType type, HashSet<string>? memberFilter = null, bool unsafeOnly = false)
    {
        var members = type.Members
            .Where(m => !IsCompilerGenerated(m.Name))
            .ToList();

        if (memberFilter?.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, memberFilter)).ToList();

        if (unsafeOnly)
            members = members.Where(m => m.IsUnsafe).ToList();

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

    private static string PluralizeKind(string kind) => kind switch
    {
        "property" => "Properties",
        "method" => "Methods",
        "field" => "Fields",
        "event" => "Events",
        "constructor" => "Constructors",
        _ => char.ToUpper(kind[0]) + kind[1..] + "s"
    };

    private static bool IsCompilerGenerated(string name) => MemberFilters.IsCompilerGenerated(name);

    private static readonly string[] MemberKinds = ["constructor", "field", "property", "method", "event"];

    internal static int GetMemberSortOrder(string kind)
    {
        var index = Array.IndexOf(MemberKinds, kind);
        return index >= 0 ? index : MemberKinds.Length;
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
        "property" => 1,
        "method" => 2,
        "event" => 3,
        "field" => 4,
        _ => 5
    };

    private static string GetTreeKindLabel(string kind, int count)
    {
        var plural = kind switch
        {
            "property" => "Properties",
            "method" => "Methods",
            "constructor" => "Constructors",
            "event" => "Events",
            "field" => "Fields",
            _ => kind + "s"
        };
        return $"{plural} ({count})";
    }
}
