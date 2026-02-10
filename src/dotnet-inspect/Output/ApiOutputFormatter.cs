using System.Text;
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
    // ===== Full API Rendering =====

    public static string RenderFullApiMarkdown(ApiSurface api, ApiOptions options)
    {
        var totalCount = api.Types.Count;

        // Pre-truncate types list if --limit (before serialization so section reflects limit)
        int? truncatedCount = null;
        if (options.Limit.HasValue && options.Limit.Value < totalCount)
        {
            truncatedCount = totalCount - options.Limit.Value;
            api.Types = api.Types.Take(options.Limit.Value).ToList();
        }

        // Compute effective sections via pipeline
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(
            api, options.Verbosity, options.IncludeSections, options.ExcludeSections);

        // Single writer with section filtering
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            IncludeDescription = options.Verbosity != Verbosity.Quiet
        };
        var writer = new MarkoutWriter(writerOptions);

        // Serialize title + summary fields via CLI wrapper
        new MarkoutContext().Serialize(new CliApiSurface(api), writer);

        if (totalCount == 0)
        {
            writer.WriteParagraph("This library contains no public types.");

            if (api.TypeForwarders.Count > 0)
            {
                writer.WriteParagraph("Type forwarders could not be resolved. Target libraries:");

                var byAssembly = api.TypeForwarders
                    .GroupBy(f => f.TargetAssembly)
                    .OrderBy(g => g.Key)
                    .ToList();

                writer.WriteTable(
                    new[] { "Target Library", "Types" },
                    byAssembly.Select(g => new[] { g.Key, g.Count().ToString() }));
            }
        }
        else if (options.Verbosity != Verbosity.Quiet)
        {
            if (api.IsTypeForwardingAssembly)
            {
                writer.WriteParagraph("*This is a type-forwarding library. Types shown are resolved from target libraries.*");
            }

            // Per-kind type sections
            RenderTypesPerKind(writer, api.Types, options);

            if (truncatedCount.HasValue)
            {
                writer.WriteParagraph($"... *and {truncatedCount.Value} more types*");
            }
        }

        return writer.ToString().TrimEnd();
    }

    // ===== Single Type Rendering =====

    public static string RenderTypeMarkdown(ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options)
    {
        // Signatures-only: plain text, no serializer
        if (options.SignaturesOnly)
            return RenderSignaturesOnly(type, options);

        // Build the view model
        var view = BuildApiTypeView(type, foundIn, packageName, packageVersion, apiSource, selectedTfm, options);

        // Populate enum values declaratively for Normal+ enums
        if (type.Kind == "enum" && options.Verbosity >= Verbosity.Normal)
            PopulateEnumValues(view, type, options);

        // Compute effective sections via pipeline
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(
            type, options.Verbosity, options.IncludeSections, options.ExcludeSections);

        // Single writer with section filtering via MarkoutWriterOptions
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            IncludeDescription = options.Verbosity != Verbosity.Quiet
        };
        var writer = new MarkoutWriter(writerOptions);

        // Serialize title + description + identity fields + enum values + type params + interfaces + baseclass
        new MarkoutContext().Serialize(view, writer);

        // Imperative rendering for member tables and source info
        int truncatedCount = 0;
        string truncatedNoun = "";

        if (view.EnumValues == null && view.EnumValuesWithDocs == null)
        {
            if (options.CtorOnly && options.Verbosity >= Verbosity.Normal &&
                type.Members.Any(m => m.Kind == "constructor"))
            {
                // --ctor emphasis mode
                var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly);
                var members = grouped
                    .SelectMany(g => g.Value)
                    .Where(m => m.Kind == "constructor")
                    .ToList();
                RenderConstructorEmphasis(writer, type, members);
            }
            else
            {
                // Per-kind member sections (all verbosity levels)
                (truncatedCount, truncatedNoun) = RenderMembersPerKind(writer, type, options);
            }
        }

        // Truncation message
        if (truncatedCount > 0)
            writer.WriteParagraph($"... *and {truncatedCount} more {truncatedNoun}*");

        return writer.ToString().TrimEnd();
    }

    // ===== Shape Output (--shape) =====

    public static void WriteShapeOutput(ApiType type, string? foundIn, string? packageName, string? packageVersion, HashSet<string> memberFilter)
    {
        var view = BuildShapeView(type, foundIn, packageName, packageVersion, memberFilter);
        MarkoutSerializer.Serialize(view, Console.Out, TypeViewContext.Default);
    }

    // ===== Signatures-Only Mode =====

    public static string RenderSignaturesOnly(ApiType type, ApiOptions options)
    {
        var members = type.Members
            .Where(m => !IsCompilerGenerated(m.Name))
            .OrderBy(m => GetMemberSortOrder(m.Kind))
            .ThenBy(m => m.Name)
            .ToList();

        if (options.MemberFilter.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter)).ToList();

        if (options.UnsafeOnly)
            members = members.Where(m => m.IsUnsafe).ToList();

        var sb = new StringBuilder();
        var displayMembers = members.AsEnumerable();
        if (options.Limit.HasValue && options.Limit.Value < members.Count)
            displayMembers = displayMembers.Take(options.Limit.Value);

        if (type.Kind == "enum")
        {
            var enumMembers = members
                .Where(m => m.Kind == "field" && m.EnumValue.HasValue)
                .OrderBy(m => m.EnumValue);
            foreach (var member in options.Limit.HasValue && options.Limit.Value < members.Count
                ? enumMembers.Take(options.Limit.Value)
                : enumMembers)
            {
                sb.AppendLine($"{member.Name} = {member.EnumValue}");
            }
        }
        else
        {
            foreach (var member in displayMembers)
            {
                sb.AppendLine(member.Signature ?? member.ReturnType ?? "");
            }
        }
        return sb.ToString().TrimEnd();
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
            Title = $"{type.FullName}{packageInfo}",
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
            TypeParameterRows = typeParameterRows,
            InterfaceRows = interfaceRows,
            BaseclassRows = baseclassRows,
            SourceRows = sourceRows
        };
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
                    .Select(m => m.Signature ?? m.Name)
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
            FullName = $"{type.FullName}{packageInfo}",
            Kind = type.Kind,
            Modifiers = modifiers.Count > 0 ? string.Join(", ", modifiers) : null,
            Assembly = foundIn,
            Package = packageName,
            Version = packageVersion,
            Members = nodes
        };
    }

    // ===== Internal Rendering Methods =====

    private static void RenderTypesPerKind(MarkoutWriter writer, List<ApiType> types, ApiOptions options)
    {
        bool showDocs = options.ShowDocs;

        var byKind = types
            .GroupBy(t => t.Kind)
            .OrderBy(g => GetTypeKindSortOrder(g.Key))
            .ToList();

        foreach (var group in byKind)
        {
            var sectionName = PluralizeTypeKind(group.Key);
            writer.WriteHeading(2, sectionName);

            var headers = showDocs
                ? new[] { "Type", "Members", "Description" }
                : new[] { "Type", "Members" };

            var rows = group.Select(t =>
            {
                var displayName = FormatGenericTypeName(t.Name, t.TypeParameters);
                var fullName = string.IsNullOrEmpty(t.Namespace) ? displayName : $"{t.Namespace}.{displayName}";
                var members = t.Members.Count.ToString();

                if (showDocs)
                {
                    var desc = t.Documentation.Summary ?? "";
                    desc = desc.ReplaceLineEndings(" ");
                    if (desc.Length > 80)
                        desc = desc[..77] + "...";
                    return new[] { fullName, members, desc };
                }

                return new[] { fullName, members };
            });

            writer.WriteTable(headers, rows);
        }
    }

    private static void PopulateEnumValues(ApiTypeView view, ApiType type, ApiOptions options)
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

    private static (int truncated, string noun) RenderMembersPerKind(
        MarkoutWriter writer, ApiType type, ApiOptions options)
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
        var formatter = MemberTableFormatter.Create(options.Verbosity);

        foreach (var group in kindGroups)
        {
            var kind = group.Key;
            var sectionName = PluralizeKind(kind);
            var members = group.ToList();

            writer.WriteHeading(2, sectionName);
            writer.WriteTable(
                formatter.GetHeaders(kind, members, hasDocs),
                formatter.FormatRows(kind, members, hasDocs));
        }

        return (truncated, "members");
    }

    private static void RenderConstructorEmphasis(MarkoutWriter writer, ApiType type, List<ApiMember> constructors)
    {
        writer.WriteHeading(2, $"Constructors ({constructors.Count} overload{(constructors.Count != 1 ? "s" : "")})");

        var sorted = constructors
            .OrderBy(c => SignatureParser.CountParameters(c.Signature))
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var ctor = sorted[i];
            var paramCount = SignatureParser.CountParameters(ctor.Signature);
            var paramInfo = SignatureParser.ExtractParameterInfo(ctor.Signature);

            writer.WriteHeading(3, $"Overload {i + 1}: {paramCount} parameter{(paramCount != 1 ? "s" : "")}");

            writer.WriteCodeBlockStart("csharp");
            writer.WriteParagraph($"new {type.Name}{SignatureParser.FormatConstructorCall(ctor.Signature)}");
            writer.WriteCodeBlockEnd();

            if (paramInfo.Count > 0)
            {
                var headers = new[] { "Parameter", "Type", "Notes" };
                var rows = paramInfo.Select(p => new[] { p.name, $"`{p.type}`", p.hasDefault ? "optional" : "required" });
                writer.WriteTable(headers, rows);
            }
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

    private static string PluralizeTypeKind(string kind) => kind switch
    {
        "class" => "Classes",
        "struct" => "Structs",
        "interface" => "Interfaces",
        "enum" => "Enums",
        "delegate" => "Delegates",
        _ => char.ToUpper(kind[0]) + kind[1..] + "s"
    };

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
