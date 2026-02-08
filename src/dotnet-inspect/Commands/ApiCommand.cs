using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays the public API shape of a specific type.
/// Uses hybrid Markout serializer + imperative rendering.
/// </summary>
public class ApiCommand
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
                    var tfmAssembly = TfmSelector.FindAssemblyByTfm(searchPath, options.Tfm);
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
                Console.Error.WriteLine("Error: No package, assembly, or platform specified.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Example: dotnet-inspect api System.Text.Json JsonSerializer");
                return 1;
            }

            string? selectedTfm = null;
            if (string.IsNullOrEmpty(typeName))
            {
                // No type specified - list all types
                if (Directory.Exists(searchPath))
                {
                    var dlls = TfmSelector.GetPackageDlls(searchPath);
                    if (dlls.Count > 1)
                    {
                        var (selectedPath, tfm) = TfmSelector.SelectHighestTfmAssembly(dlls, searchPath);
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

                var (api, apiDllPath) = ApiServices.ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from assembly.");
                    return 1;
                }

                if (api.Types.Count == 0 && api.TypeForwarders.Count > 0 && apiDllPath != null)
                {
                    ApiServices.ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);
                }

                if (!string.IsNullOrEmpty(options.PackagePath))
                {
                    var (pkgName, _) = PackageReferenceParser.ParsePackageReference(options.PackagePath);
                    api.Name = pkgName;
                }
                else if (apiDllPath != null)
                {
                    api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
                }

                var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                if (pdbLookupPath != null)
                {
                    api.RepositoryUrl = await ApiServices.ExtractRepositoryUrlAsync(pdbLookupPath, options, logger, context.HttpClient);
                }
                api.Tfm = selectedTfm;

                if ((options.ShowDocs || options.ShowSamples || options.SourceLinkOnly) && pdbLookupPath != null)
                {
                    logger.Log("Enriching types with source info...");
                    foreach (var type in api.Types)
                    {
                        var fullTypeName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
                        await ApiServices.EnrichTypeWithSourceInfoAsync(type, fullTypeName, pdbLookupPath, options, logger, context.HttpClient);
                    }
                }

                WriteFullApiOutput(api, options, selectedTfm);
            }
            else
            {
                typeName = GenericTypeNameConverter.Convert(typeName);

                var (api, apiDllPath) = ApiServices.ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from assembly.");
                    return 1;
                }

                if (api.Types.Count == 0 && api.TypeForwarders.Count > 0 && apiDllPath != null)
                    ApiServices.ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);

                var allTypeNames = api.Types.Select(t => FullName(t)).ToList();
                var lookupResult = TypeMatcher.Lookup(allTypeNames, typeName);

                if (lookupResult.Match != null)
                {
                    var apiType = api.Types.First(t => FullName(t) == lookupResult.Match);

                    // Check each member filter before producing output
                    if (options.MemberFilter?.Count > 0 && apiType.Members != null)
                    {
                        var memberNames = apiType.Members.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var missedFilters = new List<string>();

                        foreach (var filter in options.MemberFilter)
                        {
                            bool isGlob = filter.Contains('*') || filter.Contains('?');
                            bool anyMatch = isGlob
                                ? memberNames.Any(n => TypeMatcher.MatchesGlob(n, filter))
                                : memberNames.Any(n => string.Equals(n, filter, StringComparison.OrdinalIgnoreCase));

                            if (!anyMatch)
                                missedFilters.Add(filter);
                        }

                        if (missedFilters.Count > 0)
                        {
                            Console.Error.WriteLine($"Error: No members matched filter '{string.Join(", ", missedFilters)}'");
                            var memberResult = TypeMatcher.LookupMembers(memberNames, missedFilters);
                            if (memberResult.Suggestions.Count > 0)
                            {
                                Console.Error.WriteLine();
                                Console.Error.WriteLine("Did you mean:");
                                foreach (var s in memberResult.Suggestions)
                                    Console.Error.WriteLine($"  {s}");
                            }
                            return 1;
                        }
                    }

                    var foundIn = apiDllPath != null ? Path.GetFileNameWithoutExtension(apiDllPath) : null;
                    if (options.ShowDocs || options.ShowSamples || options.SourceLinkOnly)
                    {
                        var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                        if (pdbLookupPath != null)
                            await ApiServices.EnrichTypeWithSourceInfoAsync(apiType, typeName, pdbLookupPath, options, logger, context.HttpClient);
                    }

                    WriteTypeOutput(apiType, foundIn, packageName, packageVersion, options);
                }
                else if (lookupResult.Suggestions.Count > 0)
                {
                    bool isGlob = typeName.Contains('*') || typeName.Contains('?');
                    Console.Error.WriteLine(isGlob
                        ? $"Error: Multiple types match '{typeName}'."
                        : $"Error: Type '{typeName}' not found.");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Did you mean:");
                    foreach (var s in lookupResult.Suggestions)
                        Console.Error.WriteLine($"  {s}");
                    return 1;
                }
                else
                {
                    Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                    return 1;
                }
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
                return TypeMatcher.MatchesGlob(fullName, options.TypeFilter) ||
                       TypeMatcher.MatchesGlob(t.Name, options.TypeFilter);
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

        // Single writer with section filtering
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = options.IncludeSections,
            ExcludeSections = options.ExcludeSections,
            IncludeDescription = options.Verbosity != Verbosity.Quiet
        };
        var writer = new MarkoutWriter(writerOptions);

        // Serialize title + summary fields
        new MarkoutContext().Serialize(api, writer);

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
                writer.WriteParagraph($"... *and {truncatedCount.Value} more types*");
            }
        }

        // Source section (--docs/--samples)
        if (options.ShowDocs || options.ShowSamples)
            RenderAssemblySourceInfo(writer, api, options);

        return writer.ToString().TrimEnd();
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
        if (options.TreeOutput)
        {
            WriteTreeOutput(type, options.MemberFilter);
        }
        else if (options.JsonOutput)
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
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter)).ToList();

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

        // Populate enum values declaratively for Normal+ enums
        if (type.Kind == "enum" && options.Verbosity >= Verbosity.Normal)
            PopulateEnumValues(view, type, options);

        // Single writer with section filtering via MarkoutWriterOptions
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = options.IncludeSections,
            ExcludeSections = options.ExcludeSections,
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

        // Source info section (--docs/--samples)
        if (options.ShowDocs || options.ShowSamples)
            RenderSourceInfo(writer, type, options);

        // Truncation message
        if (truncatedCount > 0)
            writer.WriteParagraph($"... *and {truncatedCount} more {truncatedNoun}*");

        return writer.ToString().TrimEnd();
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

        // Description (from docs)
        string? description = null;
        if (options.ShowDocs && type.Documentation?.Summary != null)
            description = type.Documentation.Summary;

        // Samples info (only with --docs/--samples)
        string? samplesInfo = null;
        if ((options.ShowDocs || options.ShowSamples) && type.Documentation?.Samples?.Count > 0)
            samplesInfo = $"{type.Documentation.Samples.Count} available";

        // Type parameters table (Normal+)
        List<TypeParameterRow>? typeParameterRows = null;
        if (type.TypeParameters is { Count: > 0 } && options.Verbosity >= Verbosity.Normal)
        {
            typeParameterRows = type.TypeParameters
                .Select(tp => new TypeParameterRow { Parameter = tp.DisplayName, Constraints = tp.ConstraintsSummary ?? "" })
                .ToList();
        }

        // Interfaces (Detailed+)
        List<InterfaceRow>? interfaceRows = null;
        if (type.Interfaces is { Count: > 0 } && options.Verbosity >= Verbosity.Detailed)
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

        return new ApiTypeView
        {
            Title = $"{fullName}{packageInfo}",
            Description = description,
            Kind = type.Kind,
            Modifiers = modifiers.Count > 0 ? string.Join(", ", modifiers) : null,
            BaseType = baseType,
            TypeParametersInline = typeParamsInline,
            Assembly = foundIn,
            Package = packageName,
            Version = packageVersion,
            SamplesInfo = samplesInfo,
            TypeParameterRows = typeParameterRows,
            InterfaceRows = interfaceRows,
            BaseclassRows = baseclassRows
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

    /// <summary>
    /// Renders source information as a separate section (--docs/--samples).
    /// </summary>
    private static void RenderSourceInfo(MarkoutWriter writer, ApiType type, ApiOptions options)
    {
        var resolution = type.SourceResolution;
        string? primaryUrl = type.GitHubBrowseUrl != null
            ? (options.BrowsableUrls ? GitHubUrlResolver.ConvertRawToBlobUrl(type.GitHubBrowseUrl) : type.GitHubBrowseUrl)
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
                    ? (options.BrowsableUrls ? GitHubUrlResolver.ConvertRawToBlobUrl(file.GitHubBrowseUrl) : file.GitHubBrowseUrl)
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


    // ===== Signatures-Only Mode =====

    private static string RenderSignaturesOnly(ApiType type, ApiOptions options)
    {
        var members = type.Members?
            .Where(m => !IsCompilerGenerated(m.Name))
            .OrderBy(m => GetMemberSortOrder(m.Kind))
            .ThenBy(m => m.Name)
            .ToList() ?? [];

        if (options.MemberFilter?.Count > 0)
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

    // ===== Rendering Helpers =====

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

    // ===== Helper Methods (delegated to ApiCommand where static, or copied for private ones) =====

    private static Dictionary<string, List<ApiMember>> GroupMembersByKind(ApiType type, HashSet<string>? memberFilter = null, bool unsafeOnly = false)
    {
        var members = type.Members?
            .Where(m => !IsCompilerGenerated(m.Name))
            .ToList() ?? [];

        if (memberFilter?.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, memberFilter)).ToList();

        if (unsafeOnly)
            members = members.Where(m => m.IsUnsafe).ToList();

        return members
            .GroupBy(m => m.Kind)
            .OrderBy(g => GetMemberSortOrder(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());
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

    private static int GetMemberSortOrder(string kind)
    {
        var index = Array.IndexOf(MemberKinds, kind);
        return index >= 0 ? index : MemberKinds.Length;
    }

    private static string FullName(ApiType t) =>
        string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Namespace}.{t.Name}";

    // ===== Tree Output (--tree) =====

    private static void WriteTreeOutput(ApiType type, HashSet<string>? memberFilter)
    {
        var view = BuildTypeView(type, memberFilter);
        MarkoutSerializer.Serialize(view, Console.Out, TypeViewContext.Default);
    }

    private static TypeShapeView BuildTypeView(ApiType type, HashSet<string>? memberFilter)
    {
        var nodes = new List<TreeNode>();

        // Inheritance (always show)
        if (!string.IsNullOrEmpty(type.BaseType) && type.BaseType != "Object")
        {
            nodes.Add(new TreeNode("Inherits", new[] { type.BaseType }));
        }

        // Interfaces (always show)
        if (type.Interfaces is { Count: > 0 })
        {
            nodes.Add(new TreeNode("Implements", type.Interfaces));
        }

        // Type parameters with constraints (always show)
        if (type.TypeParameters is { Count: > 0 })
        {
            var typeParamDescriptions = type.TypeParameters
                .Select(tp => tp.Constraints.Count > 0
                    ? $"{tp.DisplayName} : {tp.ConstraintsSummary}"
                    : tp.DisplayName)
                .ToList();
            nodes.Add(new TreeNode("Type Parameters", typeParamDescriptions));
        }

        // Group members by kind
        if (type.Members is { Count: > 0 })
        {
            var members = type.Members.Where(m => !IsCompilerGenerated(m.Name));

            if (memberFilter?.Count > 0)
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

        return new TypeShapeView
        {
            FullName = type.Namespace != null ? $"{type.Namespace}.{type.Name}" : type.Name,
            Kind = type.Kind,
            Members = nodes
        };
    }

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

/// <summary>
/// View model for type shape output (--tree).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(FullName), DescriptionProperty = nameof(KindDisplay))]
public class TypeShapeView
{
    [MarkoutIgnore]
    public string FullName { get; set; } = "";

    [MarkoutIgnore]
    public string Kind { get; set; } = "";

    [MarkoutIgnore]
    public string KindDisplay => $"*{Kind}*";

    [MarkoutIgnoreInTable]
    public List<TreeNode> Members { get; set; } = [];
}

[MarkoutContext(typeof(TypeShapeView))]
public partial class TypeViewContext : MarkoutSerializerContext
{
}
