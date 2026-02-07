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

        // Populate the appropriate type summary property for the serializer
        if (api.Types.Count > 0 && !options.FieldsOnly)
        {
            var summaries = api.Types.Select(t =>
            {
                var summary = new ApiTypeSummary
                {
                    Type = string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Namespace}.{t.Name}",
                    Kind = t.Kind,
                    Members = t.Members?.Count ?? 0
                };
                if (options.ShowDocs)
                {
                    var desc = t.Documentation?.Summary ?? "";
                    desc = desc.Replace("\n", " ").Replace("\r", "");
                    if (desc.Length > 80)
                        desc = desc[..77] + "...";
                    summary.Description = desc;
                }
                return summary;
            }).ToList();

            if (options.ShowDocs)
                api.TypeDocSummaries = summaries;
            else
                api.TypeSummaries = summaries;
        }

        // Determine which sections to exclude from the serializer
        var excludeSections = new HashSet<string>();
        if (options.FieldsOnly)
            excludeSections.Add("Types");

        // Serializer handles: title + summary fields + types table
        var markoutContext = new MarkoutContext(new MarkoutWriterOptions
        {
            ExcludeSections = excludeSections.Count > 0 ? excludeSections : null
        });
        var output = markoutContext.Serialize(api);

        if (options.FieldsOnly)
            return output.TrimEnd();

        // Imperative additions for cases the serializer doesn't cover
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

            if (truncatedCount.HasValue)
            {
                writer.WriteParagraph($"*... and {truncatedCount.Value} more types*");
            }
        }

        return JoinSerializerAndImperative(output, writer);
    }

    // ===== Single Type Rendering =====

    private static void WriteTypeOutput(ApiType type, string? foundIn, string? packageName, string? packageVersion, ApiOptions options)
    {
        // Check for member filter miss and warn
        if (options.MemberFilter?.Count > 0 && type.Members != null)
        {
            var matchingMembers = type.Members
                .Where(m => options.MemberFilter.Contains(m.Name))
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
            members = members.Where(m => options.MemberFilter.Contains(m.Name)).ToList();

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

        // Serialize title + description + identity fields
        var markoutContext = new MarkoutContext();
        var output = markoutContext.Serialize(view);

        // In fields-only mode, stop here
        if (options.FieldsOnly)
            return output.TrimEnd();

        // Imperative rendering for the rest
        var writer = new MarkoutWriter();

        // Type parameters table: only at Normal+
        if (options.Verbosity >= Verbosity.Normal && type.TypeParameters is { Count: > 0 })
        {
            RenderTypeParametersTable(writer, type.TypeParameters);
        }

        // Hierarchy table: only at Detailed (or explicit --hierarchy)
        if (options.Verbosity >= Verbosity.Detailed || options.ShowHierarchy)
        {
            RenderTypeHierarchy(writer, type);
        }

        // Members: only at Minimal+
        if (options.Verbosity >= Verbosity.Minimal)
        {
            RenderMembers(writer, type, options);
        }

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

        // Source URL
        string? source = null;
        if (type.GitHubBrowseUrl != null)
            source = options.BrowsableUrls ? ConvertRawToBlobUrl(type.GitHubBrowseUrl) : type.GitHubBrowseUrl;

        // Description (from docs)
        string? description = null;
        if (options.ShowDocs && type.Documentation?.Summary != null)
            description = type.Documentation.Summary;

        // Partial type info
        string? partialInfo = null;
        if (type.IsPartialType && type.AdditionalSourceFiles != null)
            partialInfo = $"{type.AdditionalSourceFiles.Count + 1} source files";

        // Samples info
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
            Source = source,
            SourceResolution = type.SourceResolution,
            PartialInfo = partialInfo,
            SamplesInfo = samplesInfo
        };
    }

    // ===== Member Rendering =====

    private static void RenderMembers(MarkoutWriter writer, ApiType type, ApiOptions options)
    {
        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly);
        if (grouped.Count == 0) return;

        if (options.Verbosity == Verbosity.Minimal)
        {
            RenderMembersMinimal(writer, type, grouped, options);
        }
        else
        {
            RenderMembersNormalOrDetailed(writer, type, grouped, options);
        }
    }

    private static void RenderMembersMinimal(MarkoutWriter writer, ApiType type, Dictionary<string, List<ApiMember>> grouped, ApiOptions options)
    {
        // When using member filter with --docs, show detailed output with docs
        if (options.MemberFilter?.Count > 0 && options.ShowDocs)
        {
            foreach (var (kind, members) in grouped)
            {
                foreach (var member in members.OrderBy(m => m.Signature ?? m.Name))
                {
                    writer.WriteListItem($"`{member.Signature ?? member.Name}`");

                    if (member.Documentation?.Summary != null)
                    {
                        writer.WriteParagraph($"  > {member.Documentation.Summary}");
                    }
                }
            }
            return;
        }

        foreach (var (kind, members) in grouped)
        {
            var byName = members.GroupBy(m => m.Name).OrderBy(g => g.Key).ToList();

            if (kind == "method" || kind == "constructor")
            {
                writer.WriteParagraph($"**{PluralizeKind(kind)}:**");
                foreach (var nameGroup in byName)
                {
                    var overloads = nameGroup.ToList();
                    if (overloads.Count > 1)
                    {
                        var paramHints = overloads
                            .Select(m => ExtractFirstParamType(m.Signature))
                            .Where(p => !string.IsNullOrEmpty(p))
                            .Distinct()
                            .Take(4)
                            .ToList();

                        var hintText = paramHints.Count > 0 ? $" ({string.Join(", ", paramHints)}, ...)" : "";
                        writer.WriteListItem($"**{nameGroup.Key}**: {overloads.Count} overloads{hintText}");
                    }
                    else
                    {
                        writer.WriteListItem($"**{nameGroup.Key}**");
                    }
                }
            }
            else
            {
                var names = byName.Select(g => g.Key);
                writer.WriteField(PluralizeKind(kind), string.Join(", ", names));
            }
        }
    }

    private static void RenderMembersNormalOrDetailed(MarkoutWriter writer, ApiType type, Dictionary<string, List<ApiMember>> grouped, ApiOptions options)
    {
        // Flatten for table display
        var members = grouped
            .SelectMany(g => g.Value)
            .OrderBy(m => GetMemberSortOrder(m.Kind))
            .ThenBy(m => m.Name)
            .ToList();

        // Use enhanced constructor output when --ctor is specified
        if (options.CtorOnly && members.Any(m => m.Kind == "constructor"))
        {
            RenderConstructorEmphasis(writer, type, members.Where(m => m.Kind == "constructor").ToList());
            return;
        }

        // Use specialized enum output for enum types
        if (type.Kind == "enum")
        {
            RenderEnumValues(writer, members, options);
            return;
        }

        bool hasAnyDocs = options.ShowDocs && members.Any(m => m.Documentation?.Summary != null);

        writer.WriteHeading(2, "Members");

        var totalCount = members.Count;
        var displayMembers = options.Limit.HasValue && options.Limit.Value < totalCount
            ? members.Take(options.Limit.Value).ToList()
            : members;

        if (hasAnyDocs)
        {
            var headers = new[] { "Member", "Kind", "Signature", "Description" };
            var rows = displayMembers.Select(member =>
            {
                string sig = member.Signature ?? member.ReturnType ?? "";
                string desc = member.Documentation?.Summary ?? "";
                return new[] { member.Name, member.Kind, $"`{sig}`", desc };
            });
            writer.WriteTable(headers, rows);
        }
        else
        {
            var headers = new[] { "Member", "Kind", "Signature" };
            var rows = displayMembers.Select(member =>
            {
                string sig = member.Signature ?? member.ReturnType ?? "";
                return new[] { member.Name, member.Kind, $"`{sig}`" };
            });
            writer.WriteTable(headers, rows);
        }

        if (options.Limit.HasValue && options.Limit.Value < totalCount)
        {
            var remaining = totalCount - options.Limit.Value;
            writer.WriteParagraph($"*... and {remaining} more members*");
        }
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
            members = members.Where(m => options.MemberFilter.Contains(m.Name)).ToList();

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

    private static void RenderTypeParametersTable(MarkoutWriter writer, List<TypeParameter> typeParameters)
    {
        writer.WriteHeading(2, "Type Parameters");

        var headers = new[] { "Parameter", "Constraints" };
        var rows = typeParameters.Select(param => new[] { param.DisplayName, param.ConstraintsSummary ?? "" });
        writer.WriteTable(headers, rows);
    }

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

        writer.WriteHeading(2, "Type Hierarchy");

        var rows = new List<string[]>();

        if (hasBase)
            rows.Add(new[] { "Base", type.BaseType! });

        if (hasInterfaces)
        {
            foreach (var iface in type.Interfaces!)
                rows.Add(new[] { "Implements", iface });
        }

        if (hasDerived)
        {
            foreach (var derived in type.DerivedTypes!)
                rows.Add(new[] { "Derived", derived });
        }

        writer.WriteTable(new[] { "Relationship", "Type" }, rows);
    }

    private static void RenderEnumValues(MarkoutWriter writer, List<ApiMember> members, ApiOptions options)
    {
        var enumMembers = members
            .Where(m => m.Kind == "field" && m.EnumValue.HasValue)
            .OrderBy(m => m.EnumValue)
            .ToList();

        if (enumMembers.Count == 0)
            return;

        bool hasAnyDocs = options.ShowDocs && enumMembers.Any(m => m.Documentation?.Summary != null);

        var totalCount = enumMembers.Count;
        var displayMembers = options.Limit.HasValue && options.Limit.Value < totalCount
            ? enumMembers.Take(options.Limit.Value).ToList()
            : enumMembers;

        writer.WriteHeading(2, "Values");

        if (hasAnyDocs)
        {
            var headers = new[] { "Name", "Value", "Description" };
            var rows = displayMembers.Select(member =>
            {
                string desc = member.Documentation?.Summary ?? "";
                return new[] { member.Name, member.EnumValue.ToString()!, desc };
            });
            writer.WriteTable(headers, rows);
        }
        else
        {
            var headers = new[] { "Name", "Value" };
            var rows = displayMembers.Select(member => new[] { member.Name, member.EnumValue.ToString()! });
            writer.WriteTable(headers, rows);
        }

        if (options.Limit.HasValue && options.Limit.Value < totalCount)
        {
            var remaining = totalCount - options.Limit.Value;
            writer.WriteParagraph($"*... and {remaining} more values*");
        }
    }

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
            members = members.Where(m => memberFilter.Contains(m.Name)).ToList();

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
