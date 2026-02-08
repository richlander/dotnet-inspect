using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays the public API shape of a specific type.
/// Uses hybrid Markout serializer + imperative rendering.
/// </summary>
public class Api2Command
{
    public static async Task<int> ExecuteAsync(string? typeName, ApiOptions options)
    {
        if (options.MemberFilter?.Count > 0 && string.IsNullOrEmpty(typeName))
        {
            Console.Error.WriteLine("Error: --member requires a type argument.");
            Console.Error.WriteLine("Usage: dotnet-inspect api <type> --package <pkg> --member <name>");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            string searchPath;
            string? runtimeAssemblyPath = null;
            string? packageName = null;
            string? packageVersion = null;

            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                var extracted = await Packages.PackageExtractor.ExtractPackageAsync(context.HttpClient, options.PackagePath, context.Logger.Log, "inspect-api", options.SourceOptions);
                if (extracted == null)
                    return 1;
                (searchPath, tempDir, packageName, packageVersion) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);

                if (!string.IsNullOrEmpty(options.Tfm))
                {
                    var tfmAssembly = ApiCommand.FindAssemblyByTfm(searchPath, options.Tfm);
                    if (tfmAssembly == null)
                    {
                        Console.Error.WriteLine($"Error: No assembly found for TFM '{options.Tfm}'.");
                        return 1;
                    }
                    searchPath = tfmAssembly;
                    logger.Log($"Using TFM: {options.Tfm}");
                }
                else if (!string.IsNullOrEmpty(options.AssemblyPath))
                {
                    var targetPath = Path.Combine(searchPath, options.AssemblyPath.Replace('\\', '/'));
                    if (!File.Exists(targetPath))
                    {
                        Console.Error.WriteLine($"Error: Assembly '{options.AssemblyPath}' not found in package.");
                        return 1;
                    }
                    searchPath = targetPath;
                }
            }
            else if (!string.IsNullOrEmpty(options.AssemblyPath))
            {
                if (!File.Exists(options.AssemblyPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {options.AssemblyPath}");
                    return 1;
                }
                searchPath = options.AssemblyPath;
            }
            else if (!string.IsNullOrEmpty(options.PlatformAssembly))
            {
                var (assemblyPath, framework, version, error) = Inspectors.PlatformResolver.ResolveAssembly(
                    options.PlatformAssembly,
                    options.PlatformFramework,
                    packsDirectory: null,
                    useRuntimeAssemblies: false);

                if (error != null)
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return 1;
                }

                searchPath = assemblyPath!;
                logger.Log($"Using platform ref assembly: {framework} {version}");

                var (runtimePath, _, _, runtimeError) = Inspectors.PlatformResolver.ResolveAssembly(
                    options.PlatformAssembly,
                    options.PlatformFramework,
                    packsDirectory: null,
                    useRuntimeAssemblies: true);

                if (runtimeError == null && runtimePath != null)
                {
                    runtimeAssemblyPath = runtimePath;
                    logger.Log($"Using runtime assembly for PDB lookup: {runtimePath}");
                }
            }
            else
            {
                Console.Error.WriteLine("Error: Must specify --package, --assembly, or --platform.");
                Console.Error.WriteLine("Run 'dotnet-inspect api --help' for usage.");
                return 1;
            }

            string? selectedTfm = null;
            if (string.IsNullOrEmpty(typeName))
            {
                // No type specified - list all types
                if (Directory.Exists(searchPath))
                {
                    var dlls = GetPackageDlls(searchPath);
                    if (dlls.Count > 1)
                    {
                        var (selectedPath, tfm) = SelectHighestTfmAssembly(dlls, searchPath);
                        if (selectedPath != null)
                        {
                            searchPath = selectedPath;
                            selectedTfm = tfm;
                            logger.Log($"Auto-selected TFM: {tfm}");
                        }
                        else
                        {
                            Console.Error.WriteLine("Error: Multiple assemblies found. Please specify one with --assembly or --tfm.");
                            return 1;
                        }
                    }
                }

                var (api, apiDllPath) = ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from assembly.");
                    return 1;
                }

                if (api.Types.Count == 0 && api.TypeForwarders.Count > 0 && apiDllPath != null)
                {
                    ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);
                }

                if (!string.IsNullOrEmpty(options.PackagePath))
                {
                    var (pkgName, _) = ParsePackageReference(options.PackagePath);
                    api.Name = pkgName;
                }
                else if (apiDllPath != null)
                {
                    api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
                }

                var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                if (pdbLookupPath != null)
                {
                    api.RepositoryUrl = await ApiCommand.ExtractRepositoryUrlAsync(pdbLookupPath, options, logger, context.HttpClient);
                }
                api.Tfm = selectedTfm;

                if ((options.ShowDocs || options.ShowSamples || options.SourceLinkOnly) && pdbLookupPath != null)
                {
                    logger.Log("Enriching types with source info...");
                    foreach (var type in api.Types)
                    {
                        var fullTypeName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
                        await ApiCommand.EnrichTypeWithSourceInfoAsync(type, fullTypeName, pdbLookupPath, options, logger, context.HttpClient);
                    }
                }

                WriteFullApiOutput(api, options, selectedTfm);
            }
            else
            {
                typeName = ApiCommand.ConvertGenericTypeName(typeName);

                var (apiType, foundIn, dllPath, surface) = FindType(typeName, searchPath, logger, options.IncludeAll);
                if (apiType == null || dllPath == null)
                {
                    Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                    return 1;
                }

                if (options.ShowHierarchy && surface != null)
                {
                    Inspectors.ApiSurfaceExtractor.PopulateDerivedTypes(surface, apiType);
                }

                var pdbLookupPath = runtimeAssemblyPath ?? dllPath;
                await ApiCommand.EnrichTypeWithSourceInfoAsync(apiType, typeName, pdbLookupPath, options, logger, context.HttpClient);

                WriteTypeOutput(apiType, foundIn, packageName, packageVersion, options);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    // ===== Full API Surface Rendering =====

    private static void WriteFullApiOutput(ApiSurface api, ApiOptions options, string? selectedTfm = null)
    {
        // Apply type filter
        if (!string.IsNullOrEmpty(options.TypeFilter))
        {
            api.Types = api.Types.Where(t =>
            {
                var fullName = string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Namespace}.{t.Name}";
                return MatchesGlobPattern(fullName, options.TypeFilter) ||
                       MatchesGlobPattern(t.Name, options.TypeFilter);
            }).ToList();
            api.PublicTypeCount = api.Types.Count;
        }

        // Apply sourcelink-only filter
        if (options.SourceLinkOnly)
        {
            api.Types = api.Types.Where(t => !string.IsNullOrEmpty(t.SourceUrl)).ToList();
            api.PublicTypeCount = api.Types.Count;
            api.PublicMethodCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind is "method" or "constructor") ?? 0);
            api.PublicPropertyCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "property") ?? 0);
            api.PublicFieldCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "field") ?? 0);
            api.PublicEventCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "event") ?? 0);
        }

        // Apply unsafe filter
        if (options.UnsafeOnly)
        {
            foreach (var type in api.Types)
            {
                if (type.Members != null)
                    type.Members = type.Members.Where(m => m.IsUnsafe).ToList();
            }
            api.Types = api.Types.Where(t => t.Members?.Count > 0).ToList();
            api.PublicTypeCount = api.Types.Count;
            api.PublicMethodCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind is "method" or "constructor") ?? 0);
            api.PublicPropertyCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "property") ?? 0);
            api.PublicFieldCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "field") ?? 0);
            api.PublicEventCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "event") ?? 0);
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(api, ApiJsonContext.Default.ApiSurface));
        }
        else
        {
            Console.WriteLine(RenderFullApiMarkdown(api, options));
        }
    }

    private static string RenderFullApiMarkdown(ApiSurface api, ApiOptions options)
    {
        var totalCount = api.Types.Count;

        // Pre-truncate types list if --limit (before serialization so section reflects limit)
        int? truncatedCount = null;
        if (options.Limit.HasValue && options.Limit.Value < totalCount)
        {
            truncatedCount = totalCount - options.Limit.Value;
            api.Types = api.Types.Take(options.Limit.Value).ToList();
        }

        // Serializer handles: title + summary fields only (types rendered imperatively per-kind)
        var markoutContext = new MarkoutContext();
        var output = markoutContext.Serialize(api);

        if (options.FieldsOnly)
            return output.TrimEnd();

        // Imperative rendering
        var writer = new MarkoutWriter();

        if (totalCount == 0)
        {
            writer.WriteParagraph("This assembly contains no public types.");

            if (api.TypeForwarders.Count > 0)
            {
                writer.WriteParagraph("Type forwarders could not be resolved. Target assemblies:");

                var byAssembly = api.TypeForwarders
                    .GroupBy(f => f.TargetAssembly)
                    .OrderBy(g => g.Key)
                    .ToList();

                writer.WriteTable(
                    new[] { "Target Assembly", "Types" },
                    byAssembly.Select(g => new[] { g.Key, g.Count().ToString() }));
            }
        }
        else
        {
            if (api.IsTypeForwardingAssembly)
            {
                writer.WriteParagraph("*This is a type-forwarding assembly. Types shown are resolved from target assemblies.*");
            }

            // Per-kind type sections (verbosity-independent)
            RenderTypesPerKind(writer, api.Types, options);

            if (truncatedCount.HasValue)
            {
                writer.WriteParagraph($"*... and {truncatedCount.Value} more types*");
            }
        }

        // Source section (--docs/--samples): simple Resolution + URL from first type
        if ((options.ShowDocs || options.ShowSamples) && options.ShouldRenderSection("Source"))
            RenderAssemblySourceInfo(writer, api, options);

        return JoinSerializerAndImperative(output, writer);
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

    /// <summary>
    /// Converts CLR backtick generic names to C#-style: "Dictionary`2" → "Dictionary&lt;TKey, TValue&gt;".
    /// Falls back to placeholder letters if TypeParameters is not populated.
    /// </summary>
    private static string FormatGenericTypeName(string name, List<TypeParameter>? typeParameters)
    {
        int backtickIndex = name.IndexOf('`');
        if (backtickIndex < 0)
            return name;

        var baseName = name[..backtickIndex];
        if (typeParameters is { Count: > 0 })
            return $"{baseName}<{string.Join(", ", typeParameters.Select(tp => tp.Name))}>";

        // Fallback: use arity to generate T, T1, T2, ...
        if (int.TryParse(name[(backtickIndex + 1)..], out int arity) && arity > 0)
        {
            var names = arity == 1 ? "T" : string.Join(", ", Enumerable.Range(1, arity).Select(i => $"T{i}"));
            return $"{baseName}<{names}>";
        }

        return name;
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

    /// <summary>
    /// Renders per-kind type sections. Verbosity does not affect the types view;
    /// the only variation is whether --docs adds a Description column.
    /// </summary>
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
            if (!options.ShouldRenderSection(sectionName))
                continue;

            writer.WriteHeading(2, sectionName);

            var headers = showDocs
                ? new[] { "Type", "Members", "Description" }
                : new[] { "Type", "Members" };

            var rows = group.Select(t =>
            {
                var displayName = FormatGenericTypeName(t.Name, t.TypeParameters);
                var fullName = string.IsNullOrEmpty(t.Namespace) ? displayName : $"{t.Namespace}.{displayName}";
                var members = (t.Members?.Count ?? 0).ToString();

                if (showDocs)
                {
                    var desc = t.Documentation?.Summary ?? "";
                    desc = desc.Replace("\n", " ").Replace("\r", "");
                    if (desc.Length > 80)
                        desc = desc[..77] + "...";
                    return new[] { fullName, members, desc };
                }

                return new[] { fullName, members };
            });

            writer.WriteTable(headers, rows);
        }
    }

    // ===== Single Type Rendering =====

    private static void WriteTypeOutput(ApiType type, string? foundIn, string? packageName, string? packageVersion, ApiOptions options)
    {
        // Check for member filter miss and warn
        if (options.MemberFilter?.Count > 0 && type.Members != null)
        {
            var matchingMembers = type.Members
                .Where(m => MatchesMemberFilter(m.Name, options.MemberFilter))
                .ToList();

            if (matchingMembers.Count == 0)
            {
                Console.Error.WriteLine($"Warning: No members matched filter '{string.Join(", ", options.MemberFilter)}'");
            }
        }

        if (options.JsonOutput)
        {
            WriteJsonTypeOutput(type, options);
        }
        else
        {
            Console.WriteLine(RenderTypeMarkdown(type, foundIn, packageName, packageVersion, options));
        }
    }

    private static void WriteJsonTypeOutput(ApiType type, ApiOptions options)
    {
        var outputType = type;
        var members = type.Members;

        if (options.MemberFilter?.Count > 0 && members != null)
            members = members.Where(m => MatchesMemberFilter(m.Name, options.MemberFilter)).ToList();

        if (options.UnsafeOnly && members != null)
            members = members.Where(m => m.IsUnsafe).ToList();

        if (options.Limit.HasValue && members != null && members.Count > options.Limit.Value)
            members = members.Take(options.Limit.Value).ToList();

        if (members != type.Members)
        {
            outputType = new ApiType
            {
                Namespace = type.Namespace,
                Name = type.Name,
                Kind = type.Kind,
                IsSealed = type.IsSealed,
                IsAbstract = type.IsAbstract,
                IsStatic = type.IsStatic,
                BaseType = type.BaseType,
                Interfaces = type.Interfaces,
                Members = members,
                SourceFilePath = type.SourceFilePath,
                SourceUrl = type.SourceUrl,
                GitHubBrowseUrl = type.GitHubBrowseUrl,
                SourceLineNumber = type.SourceLineNumber,
                Documentation = type.Documentation
            };
        }

        if (options.CompactJson)
            Console.WriteLine(JsonSerializer.Serialize(outputType, ApiTypeCompactJsonContext.Default.ApiType));
        else
            Console.WriteLine(JsonSerializer.Serialize(outputType, ApiTypeJsonContext.Default.ApiType));
    }

    private static string RenderTypeMarkdown(ApiType type, string? foundIn, string? packageName, string? packageVersion, ApiOptions options)
    {
        // Signatures-only: plain text, no serializer
        if (options.SignaturesOnly)
            return RenderSignaturesOnly(type, options);

        // Build the view model
        var view = BuildApiTypeView(type, foundIn, packageName, packageVersion, options);

        // Populate enum values declaratively for Normal+ non-fields-only enums
        if (type.Kind == "enum" && options.Verbosity >= Verbosity.Normal && !options.FieldsOnly)
            PopulateEnumValues(view, type, options);

        // Serialize title + description + identity fields + enum values
        var markoutContext = new MarkoutContext();
        var output = markoutContext.Serialize(view);

        // In fields-only mode, stop here
        if (options.FieldsOnly)
            return output.TrimEnd();

        // Imperative rendering for everything after serialized output
        var writer = new MarkoutWriter();
        int truncatedCount = 0;
        string truncatedNoun = "";

        if (view.EnumValues == null && view.EnumValuesWithDocs == null)
        {
            if (options.CtorOnly && options.Verbosity >= Verbosity.Normal &&
                type.Members?.Any(m => m.Kind == "constructor") == true)
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

        // Type parameters table (Normal+)
        if (options.Verbosity >= Verbosity.Normal && options.ShouldRenderSection("Type Parameters"))
            RenderTypeParametersTable(writer, type);

        // Type hierarchy table (Detailed or --hierarchy)
        if ((options.Verbosity >= Verbosity.Detailed || options.ShowHierarchy) && options.ShouldRenderSection("Hierarchy"))
            RenderTypeHierarchy(writer, type);

        // Source info section (--docs/--samples)
        if ((options.ShowDocs || options.ShowSamples) && options.ShouldRenderSection("Source"))
            RenderSourceInfo(writer, type, options);

        // Truncation message
        if (truncatedCount > 0)
            writer.WriteParagraph($"*... and {truncatedCount} more {truncatedNoun}*");

        return JoinSerializerAndImperative(output, writer);
    }

    // ===== View Model Factory =====

    private static ApiTypeView BuildApiTypeView(ApiType type, string? foundIn, string? packageName, string? packageVersion, ApiOptions options)
    {
        var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";

        // Build title with package context
        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null ? $" ({packageName})" : "";

        // Build modifiers
        var modifiers = new List<string>();
        if (type.IsStatic) modifiers.Add("static");
        if (type.IsAbstract && type.Kind == "class") modifiers.Add("abstract");
        if (type.IsSealed && type.Kind == "class") modifiers.Add("sealed");

        // Base type (filter out trivial bases)
        string? baseType = null;
        if (!string.IsNullOrEmpty(type.BaseType) && type.BaseType != "System.Object" && type.BaseType != "System.ValueType" && type.BaseType != "System.Enum")
            baseType = type.BaseType;

        // Type parameters inline (for quiet/minimal only)
        string? typeParamsInline = null;
        if (type.TypeParameters is { Count: > 0 } &&
            (options.Verbosity == Verbosity.Quiet || options.Verbosity == Verbosity.Minimal))
        {
            var paramDescriptions = type.TypeParameters
                .Select(tp => tp.Constraints.Count > 0
                    ? $"{tp.DisplayName} : {tp.ConstraintsSummary}"
                    : tp.DisplayName);
            typeParamsInline = string.Join(", ", paramDescriptions);
        }

        // Interfaces
        string? implements = null;
        if (options.ShowInterfaces && type.Interfaces is { Count: > 0 })
            implements = string.Join(", ", type.Interfaces);

        // Description (from docs)
        string? description = null;
        if (options.ShowDocs && type.Documentation?.Summary != null)
            description = type.Documentation.Summary;

        // Samples info (only with --docs/--samples)
        string? samplesInfo = null;
        if ((options.ShowDocs || options.ShowSamples) && type.Documentation?.Samples?.Count > 0)
            samplesInfo = $"{type.Documentation.Samples.Count} available";

        return new ApiTypeView
        {
            Title = $"{fullName}{packageInfo}",
            Description = description,
            Kind = type.Kind,
            Modifiers = modifiers.Count > 0 ? string.Join(", ", modifiers) : null,
            BaseType = baseType,
            TypeParametersInline = typeParamsInline,
            Implements = implements,
            Assembly = foundIn,
            Package = packageName,
            Version = packageVersion,
            SamplesInfo = samplesInfo
        };
    }

    // ===== Member Rendering =====

    /// <summary>
    /// Populates enum value rows on the view model for declarative serialization.
    /// </summary>
    private static void PopulateEnumValues(ApiTypeView view, ApiType type, ApiOptions options)
    {
        var enumMembers = (type.Members ?? [])
            .Where(m => m.Kind == "field" && m.EnumValue.HasValue && !IsCompilerGenerated(m.Name))
            .OrderBy(m => m.EnumValue)
            .ToList();

        if (enumMembers.Count == 0)
            return;

        if (options.Limit.HasValue && options.Limit.Value < enumMembers.Count)
            enumMembers = enumMembers.Take(options.Limit.Value).ToList();

        bool hasAnyDocs = options.ShowDocs && enumMembers.Any(m => m.Documentation?.Summary != null);

        var rows = enumMembers.Select(m => new EnumValueRow
        {
            Name = m.Name,
            Value = m.EnumValue.ToString()!,
            Description = hasAnyDocs ? (m.Documentation?.Summary ?? "") : null
        }).ToList();

        if (hasAnyDocs)
            view.EnumValuesWithDocs = rows;
        else
            view.EnumValues = rows;
    }

    /// <summary>
    /// Writes per-kind member section tables. Returns (truncated, noun) for truncation message.
    /// </summary>
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

        bool hasDocs = options.ShowDocs && allMembers.Any(m => m.Documentation?.Summary != null);

        foreach (var group in kindGroups)
        {
            var kind = group.Key;
            var sectionName = PluralizeKind(kind);
            if (!options.ShouldRenderSection(sectionName))
                continue;

            var members = group.ToList();

            writer.WriteHeading(2, sectionName);

            if (options.Verbosity == Verbosity.Quiet)
                RenderQuietKindTable(writer, kind, members, hasDocs);
            else
                RenderPerMemberKindTable(writer, kind, members, options, hasDocs);
        }

        return (truncated, "members");
    }

    /// <summary>
    /// Quiet: group by unique name within each kind, kind-specific columns.
    /// </summary>
    private static void RenderQuietKindTable(MarkoutWriter writer, string kind, List<ApiMember> members, bool showDocs)
    {
        var byName = members.GroupBy(m => m.Name).OrderBy(g => g.Key).ToList();
        bool hasOverloads = byName.Any(g => g.Count() > 1);

        switch (kind)
        {
            case "constructor":
            case "method":
            {
                var headers = new List<string> { "Name" };
                if (kind == "method") headers.Add("Return Type");
                if (hasOverloads) headers.Add("Overloads");
                if (showDocs) headers.Add("Description");

                var rows = byName.Select(g =>
                {
                    var row = new List<string> { g.Key };
                    if (kind == "method")
                        row.Add(ExtractReturnType(g.First().Signature));
                    if (hasOverloads)
                        row.Add(g.Count().ToString());
                    if (showDocs)
                        row.Add(FirstDocSummary(g));
                    return row.ToArray();
                });
                writer.WriteTable(headers.ToArray(), rows);
                break;
            }
            case "property":
            {
                var headers = new List<string> { "Name", "Return Type", "Accessors" };
                if (showDocs) headers.Add("Description");
                var rows = byName.Select(g =>
                {
                    var m = g.First();
                    var row = new List<string>
                    {
                        g.Key,
                        ExtractReturnType(m.Signature),
                        ExtractAccessors(m.Signature)
                    };
                    if (showDocs) row.Add(FirstDocSummary(g));
                    return row.ToArray();
                });
                writer.WriteTable(headers.ToArray(), rows);
                break;
            }
            case "event":
            {
                var headers = new List<string> { "Name", "Type" };
                if (showDocs) headers.Add("Description");
                var rows = byName.Select(g =>
                {
                    var m = g.First();
                    var row = new List<string> { g.Key, m.ReturnType ?? m.Signature ?? "" };
                    if (showDocs) row.Add(FirstDocSummary(g));
                    return row.ToArray();
                });
                writer.WriteTable(headers.ToArray(), rows);
                break;
            }
            case "field":
            default:
            {
                var headers = new List<string> { "Name", "Return Type" };
                if (showDocs) headers.Add("Description");
                var rows = byName.Select(g =>
                {
                    var m = g.First();
                    var row = new List<string> { g.Key, m.ReturnType ?? "" };
                    if (showDocs) row.Add(FirstDocSummary(g));
                    return row.ToArray();
                });
                writer.WriteTable(headers.ToArray(), rows);
                break;
            }
        }
    }

    /// <summary>
    /// Minimal/Normal/Detailed: one row per member with Name | Signature columns.
    /// Minimal uses abbreviated signatures; Normal/Detailed use full signatures.
    /// </summary>
    private static void RenderPerMemberKindTable(MarkoutWriter writer, string kind, List<ApiMember> members, ApiOptions options, bool showDocs)
    {
        var headers = showDocs
            ? new[] { "Name", "Signature", "Description" }
            : new[] { "Name", "Signature" };

        var rows = members
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Signature)
            .Select(m =>
            {
                var sig = m.Signature ?? m.ReturnType ?? "";
                if (options.Verbosity == Verbosity.Minimal)
                    sig = AbbreviateSignature(sig);
                return showDocs
                    ? new[] { m.Name, $"`{sig}`", m.Documentation?.Summary ?? "" }
                    : new[] { m.Name, $"`{sig}`" };
            });

        writer.WriteTable(headers, rows);
    }

    /// <summary>
    /// Renders a type parameters table (Normal+ verbosity).
    /// </summary>
    private static void RenderTypeParametersTable(MarkoutWriter writer, ApiType type)
    {
        if (type.TypeParameters is not { Count: > 0 })
            return;

        writer.WriteHeading(2, "Type Parameters");
        writer.WriteTable(
            new[] { "Parameter", "Constraints" },
            type.TypeParameters.Select(tp => new[] { tp.DisplayName, tp.ConstraintsSummary ?? "" }));
    }

    /// <summary>
    /// Renders type hierarchy table (Detailed or --hierarchy).
    /// </summary>
    private static void RenderTypeHierarchy(MarkoutWriter writer, ApiType type)
    {
        var hasBase = !string.IsNullOrEmpty(type.BaseType) &&
                      type.BaseType != "System.Object" &&
                      type.BaseType != "System.ValueType" &&
                      type.BaseType != "System.Enum";
        var hasInterfaces = type.Interfaces is { Count: > 0 };
        var hasDerived = type.DerivedTypes is { Count: > 0 };

        if (!hasBase && !hasInterfaces && !hasDerived)
            return;

        var rows = new List<string[]>();
        if (hasBase)
            rows.Add(new[] { "Base", type.BaseType! });
        if (hasInterfaces)
            foreach (var iface in type.Interfaces!)
                rows.Add(new[] { "Implements", iface });
        if (hasDerived)
            foreach (var derived in type.DerivedTypes!)
                rows.Add(new[] { "Derived", derived });

        writer.WriteHeading(2, "Type Hierarchy");
        writer.WriteTable(new[] { "Relationship", "Type" }, rows);
    }

    /// <summary>
    /// Renders source information as a separate section (--docs/--samples).
    /// </summary>
    private static void RenderSourceInfo(MarkoutWriter writer, ApiType type, ApiOptions options)
    {
        var resolution = type.SourceResolution;
        string? primaryUrl = type.GitHubBrowseUrl != null
            ? (options.BrowsableUrls ? ConvertRawToBlobUrl(type.GitHubBrowseUrl) : type.GitHubBrowseUrl)
            : null;

        if (resolution == null && primaryUrl == null)
            return;

        bool isPartial = type.IsPartialType;
        int fileCount = 1 + (type.AdditionalSourceFiles?.Count ?? 0);

        var rows = new List<string[]>();
        if (!string.IsNullOrEmpty(resolution))
            rows.Add(new[] { "Resolution", resolution });
        rows.Add(new[] { "Partial Type", isPartial ? "true" : "false" });
        rows.Add(new[] { "Files", fileCount.ToString() });

        // Primary source file
        if (!string.IsNullOrEmpty(primaryUrl))
        {
            var fileName = Path.GetFileName(type.SourceFilePath) ?? Path.GetFileName(primaryUrl);
            rows.Add(new[] { fileName, primaryUrl });
        }

        // Additional source files for partial types
        if (type.AdditionalSourceFiles != null)
        {
            foreach (var file in type.AdditionalSourceFiles)
            {
                var url = file.GitHubBrowseUrl != null
                    ? (options.BrowsableUrls ? ConvertRawToBlobUrl(file.GitHubBrowseUrl) : file.GitHubBrowseUrl)
                    : file.SourceUrl ?? "";
                var fileName = Path.GetFileName(file.FilePath) ?? Path.GetFileName(url);
                rows.Add(new[] { fileName, url });
            }
        }

        writer.WriteHeading(2, "Source");
        writer.WriteTable(new[] { "Property", "Value" }, rows);
    }

    /// <summary>
    /// Renders a simple Source section for the full-assembly view (Resolution + URL).
    /// </summary>
    private static void RenderAssemblySourceInfo(MarkoutWriter writer, ApiSurface api, ApiOptions options)
    {
        var resolution = api.Types.FirstOrDefault(t => t.SourceResolution != null)?.SourceResolution;
        if (resolution == null && string.IsNullOrEmpty(api.RepositoryUrl))
            return;

        var rows = new List<string[]>();
        if (!string.IsNullOrEmpty(resolution))
            rows.Add(new[] { "Resolution", resolution });
        if (!string.IsNullOrEmpty(api.RepositoryUrl))
            rows.Add(new[] { "Repository", api.RepositoryUrl });

        writer.WriteHeading(2, "Source");
        writer.WriteTable(new[] { "Property", "Value" }, rows);
    }

    /// <summary>
    /// Extracts return type from signature: "int Compare(string strA)" → "int".
    /// For properties: "char Chars { get; }" → "char".
    /// </summary>
    private static string ExtractReturnType(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";

        // Find first space that's not inside generics
        int depth = 0;
        for (int i = 0; i < signature.Length; i++)
        {
            char c = signature[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ' ' && depth == 0)
                return signature[..i];
        }

        return "";
    }

    /// <summary>
    /// Strips parameter names from signature: "int Compare(string strA, int idx)" → "int Compare(string, int)".
    /// Properties/fields/events pass through unchanged.
    /// </summary>
    private static string AbbreviateSignature(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";

        int parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return signature;

        int parenEnd = signature.LastIndexOf(')');
        if (parenEnd < 0)
            return signature;

        string prefix = signature[..(parenStart + 1)];
        string suffix = signature[parenEnd..];

        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return signature;

        // Split parameters respecting generic depth
        var paramTypes = new List<string>();
        int depth = 0;
        int lastSplit = 0;
        for (int i = 0; i < paramSection.Length; i++)
        {
            char c = paramSection[i];
            if (c == '<' || c == '(') depth++;
            else if (c == '>' || c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                paramTypes.Add(ExtractParamType(paramSection[lastSplit..i].Trim()));
                lastSplit = i + 1;
            }
        }
        paramTypes.Add(ExtractParamType(paramSection[lastSplit..].Trim()));

        return prefix + string.Join(", ", paramTypes) + suffix;
    }

    /// <summary>
    /// Extracts just the type portion from "type name" or "type name = default".
    /// Handles keywords like "out", "ref", "in", "params" before the type.
    /// </summary>
    private static string ExtractParamType(string param)
    {
        // Remove default value
        int eqIndex = param.IndexOf('=');
        if (eqIndex >= 0)
            param = param[..eqIndex].Trim();

        // The type is everything except the last word (the parameter name).
        // But we need to handle generic types with spaces inside <>.
        // Find the last space that's not inside generics.
        int depth = 0;
        int lastSpace = -1;
        for (int i = 0; i < param.Length; i++)
        {
            char c = param[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ' ' && depth == 0)
                lastSpace = i;
        }

        if (lastSpace > 0)
            return param[..lastSpace];

        return param;
    }

    /// <summary>
    /// Extracts public accessor names from a property signature.
    /// "char Chars { get; private set; }" → "get" (private accessors filtered out).
    /// "TValue Item { get; set; }" → "get, set".
    /// </summary>
    private static string ExtractAccessors(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";

        int braceStart = signature.IndexOf('{');
        int braceEnd = signature.LastIndexOf('}');
        if (braceStart < 0 || braceEnd <= braceStart)
            return "";

        var accessorBlock = signature[(braceStart + 1)..braceEnd].Trim();
        var accessors = accessorBlock.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => !a.StartsWith("private", StringComparison.Ordinal) &&
                        !a.StartsWith("protected", StringComparison.Ordinal) &&
                        !a.StartsWith("internal", StringComparison.Ordinal))
            .ToList();

        return string.Join(", ", accessors);
    }

    // ===== Signatures-Only Mode =====

    private static string RenderSignaturesOnly(ApiType type, ApiOptions options)
    {
        var members = type.Members?
            .Where(m => !IsCompilerGenerated(m.Name))
            .OrderBy(m => GetMemberSortOrder(m.Kind))
            .ThenBy(m => m.Name)
            .ToList() ?? [];

        if (options.MemberFilter?.Count > 0)
            members = members.Where(m => MatchesMemberFilter(m.Name, options.MemberFilter)).ToList();

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

    // ===== Rendering Helpers =====

    private static void RenderConstructorEmphasis(MarkoutWriter writer, ApiType type, List<ApiMember> constructors)
    {
        writer.WriteHeading(2, $"Constructors ({constructors.Count} overload{(constructors.Count != 1 ? "s" : "")})");

        var sorted = constructors
            .OrderBy(c => CountParameters(c.Signature))
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var ctor = sorted[i];
            var paramCount = CountParameters(ctor.Signature);
            var paramInfo = ExtractParameterInfo(ctor.Signature);

            writer.WriteHeading(3, $"Overload {i + 1}: {paramCount} parameter{(paramCount != 1 ? "s" : "")}");

            writer.WriteCodeBlockStart("csharp");
            writer.WriteParagraph($"new {type.Name}{FormatConstructorCall(ctor.Signature)}");
            writer.WriteCodeBlockEnd();

            if (paramInfo.Count > 0)
            {
                var headers = new[] { "Parameter", "Type", "Notes" };
                var rows = paramInfo.Select(p => new[] { p.name, $"`{p.type}`", p.hasDefault ? "optional" : "required" });
                writer.WriteTable(headers, rows);
            }
        }
    }

    // ===== Helper Methods (delegated to ApiCommand where static, or copied for private ones) =====

    private static Dictionary<string, List<ApiMember>> GroupMembersByKind(ApiType type, HashSet<string>? memberFilter = null, bool unsafeOnly = false)
    {
        var members = type.Members?
            .Where(m => !IsCompilerGenerated(m.Name))
            .ToList() ?? [];

        if (memberFilter?.Count > 0)
            members = members.Where(m => MatchesMemberFilter(m.Name, memberFilter)).ToList();

        if (unsafeOnly)
            members = members.Where(m => m.IsUnsafe).ToList();

        return members
            .GroupBy(m => m.Kind)
            .OrderBy(g => GetMemberSortOrder(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static bool MatchesMemberFilter(string name, HashSet<string> filter)
    {
        foreach (var pattern in filter)
        {
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                if (MatchesGlobPattern(name, pattern))
                    return true;
            }
            else
            {
                if (filter.Contains(name))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the doc summary from the first member in the group that has one.
    /// </summary>
    private static string FirstDocSummary(IGrouping<string, ApiMember> group) =>
        group.Select(m => m.Documentation?.Summary).FirstOrDefault(s => s != null) ?? "";

    private static string PluralizeKind(string kind) => kind switch
    {
        "property" => "Properties",
        "method" => "Methods",
        "field" => "Fields",
        "event" => "Events",
        "constructor" => "Constructors",
        _ => char.ToUpper(kind[0]) + kind[1..] + "s"
    };

    private static bool IsCompilerGenerated(string name)
    {
        return name.StartsWith('<') ||
               name.StartsWith('_') ||
               name.Contains("__BackingField") ||
               name == "value__";
    }

    private static int GetMemberSortOrder(string kind) => kind switch
    {
        "constructor" => 0,
        "field" => 1,
        "property" => 2,
        "method" => 3,
        "event" => 4,
        _ => 5
    };

    private static string ExtractFirstParamType(string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return "";

        var openParen = signature.IndexOf('(');
        if (openParen < 0) return "";

        var closeParen = signature.IndexOf(')', openParen);
        if (closeParen <= openParen + 1) return "";

        var paramsPart = signature.Substring(openParen + 1, closeParen - openParen - 1);
        if (string.IsNullOrWhiteSpace(paramsPart)) return "";

        var firstParam = paramsPart.Split(',')[0].Trim();
        var parts = firstParam.Split(' ');
        var typePart = parts[0];

        var dotIndex = typePart.LastIndexOf('.');
        if (dotIndex >= 0)
            typePart = typePart[(dotIndex + 1)..];

        var genericIndex = typePart.IndexOf('<');
        if (genericIndex >= 0)
            typePart = typePart[..genericIndex];

        return typePart;
    }

    private static int CountParameters(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return 0;

        int parenStart = signature.IndexOf('(');
        int parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart + 1)
            return 0;

        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return 0;

        int count = 1;
        int depth = 0;
        foreach (char c in paramSection)
        {
            if (c == '<' || c == '(')
                depth++;
            else if (c == '>' || c == ')')
                depth--;
            else if (c == ',' && depth == 0)
                count++;
        }
        return count;
    }

    private static List<(string name, string type, bool hasDefault)> ExtractParameterInfo(string? signature)
    {
        var result = new List<(string, string, bool)>();
        if (string.IsNullOrEmpty(signature))
            return result;

        int parenStart = signature.IndexOf('(');
        int parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart + 1)
            return result;

        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return result;

        var params_ = new List<string>();
        int depth = 0;
        int lastSplit = 0;
        for (int i = 0; i < paramSection.Length; i++)
        {
            char c = paramSection[i];
            if (c == '<' || c == '(')
                depth++;
            else if (c == '>' || c == ')')
                depth--;
            else if (c == ',' && depth == 0)
            {
                params_.Add(paramSection[lastSplit..i].Trim());
                lastSplit = i + 1;
            }
        }
        params_.Add(paramSection[lastSplit..].Trim());

        foreach (var p in params_)
        {
            bool hasDefault = p.Contains('=');
            string clean = hasDefault ? p[..p.IndexOf('=')].Trim() : p;

            int lastSpace = clean.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                string type = clean[..lastSpace].Trim();
                string name = clean[(lastSpace + 1)..].Trim();
                result.Add((name, type, hasDefault));
            }
        }

        return result;
    }

    private static string FormatConstructorCall(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "()";

        int parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return "()";

        return signature[parenStart..];
    }

    /// <summary>
    /// Joins serializer output with imperative MarkoutWriter additions,
    /// ensuring exactly one blank line between them.
    /// </summary>
    private static string JoinSerializerAndImperative(string serialized, MarkoutWriter writer)
    {
        var additional = writer.ToString();
        if (additional.Length == 0)
            return serialized.TrimEnd();
        return serialized.TrimEnd() + "\n\n" + additional.TrimEnd();
    }

    private static string ConvertRawToBlobUrl(string url)
    {
        return url.Replace("/raw/", "/blob/");
    }

    private static bool MatchesGlobPattern(string text, string pattern)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    // ===== Extraction Logic Delegation =====
    // These delegate to ApiCommand's internal static methods

    private static (ApiSurface? api, string? dllPath) ExtractFullApi(string searchPath, VerboseLogger logger, bool includeAll)
    {
        // Reuse ApiCommand's ExtractFullApi
        return ApiCommand.ExtractFullApi(searchPath, logger, includeAll);
    }

    private static (ApiType? type, string? assembly, string? dllPath, ApiSurface? surface) FindType(string typeName, string searchPath, VerboseLogger logger, bool includeAll)
    {
        return ApiCommand.FindType(typeName, searchPath, logger, includeAll);
    }

    private static void ResolveForwardedTypes(ApiSurface api, string dllPath, VerboseLogger logger, bool includeAll)
    {
        ApiCommand.ResolveForwardedTypes(api, dllPath, logger, includeAll);
    }

    private static (string? name, string? version) ParsePackageReference(string packageSource)
    {
        return ApiCommand.ParsePackageReference(packageSource);
    }

    private static List<string> GetPackageDlls(string extractPath)
    {
        return ApiCommand.GetPackageDlls(extractPath);
    }

    private static (string? path, string? tfm) SelectHighestTfmAssembly(List<string> dlls, string extractPath)
    {
        return ApiCommand.SelectHighestTfmAssembly(dlls, extractPath);
    }
}
