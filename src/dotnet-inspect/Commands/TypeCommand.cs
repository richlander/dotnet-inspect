using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using Markout;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.Commands;

/// <summary>
/// Discovers types in a package or library (terse, no docs by default).
/// </summary>
public static class TypeCommand
{
    public const string Name = "type";

    public static async Task<int> ExecuteAsync(TypeOptions options)
    {
        // Shared preamble: section validation, discovery, verbosity promotion
        var (preamble, error) = ApiCommand.RunPreamble(options);
        if (error.HasValue) return error.Value;

        options = (TypeOptions)preamble.Options;
        var typePipeline = preamble.TypePipeline;
        var memberPipeline = preamble.MemberPipeline;

        // Shared source resolution
        var (source, sourceError) = await ApiCommand.ResolveSourceAsync(options);
        if (sourceError.HasValue) return sourceError.Value;

        var searchPath = source.SearchPath;
        var runtimeAssemblyPath = source.RuntimeAssemblyPath;
        var packageName = source.PackageName;
        var packageVersion = source.PackageVersion;
        var apiSource = source.ApiSource;
        var apiVersion = source.ApiVersion;
        var selectedTfm = source.SelectedTfm;
        var tempDir = source.TempDir;
        var typeName = source.TypeName;
        var context = source.Context;
        var logger = context.Logger;

        try
        {
            if (string.IsNullOrEmpty(typeName))
            {
                // No type specified - list all types
                var (api, apiDllPath) = ApiServices.ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from library.");
                    return 1;
                }

                if (apiDllPath != null)
                    ApiServices.ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);

                if (!string.IsNullOrEmpty(options.PackagePath))
                {
                    var (pkgName, _) = PackageExtractor.ParsePackageReference(options.PackagePath);
                    api.Name = pkgName;
                }
                else if (apiDllPath != null)
                {
                    api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
                }

                var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                api.Tfm = selectedTfm;
                api.Source = apiSource;
                api.Version = apiVersion;
                api.Library = apiDllPath != null ? Path.GetFileName(apiDllPath) : null;

                // --columns Description implicitly enables doc enrichment (local XML only)
                var listOptions = options;
                if (options.Columns?.Any(c => c.Equals("Description", StringComparison.OrdinalIgnoreCase)) == true)
                    listOptions = options with { ShowDocs = true };

                if (pdbLookupPath != null && listOptions.ShowDocs)
                    SourceEnricher.EnrichFromLocalXmlDocs(api.Types, pdbLookupPath, listOptions, logger);

                if (options.EffectiveDiscovery)
                {
                    ApiCommand.ApplySurfaceFilters(api, options, options.TypeFilter);
                    var schema = ApiViewContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema();
                    var effective = typePipeline.GetAvailableSections(api, options.IncludeSections);
                    return DiscoverOutput.ExecuteEffective(options.Discover, effective, schema,
                        tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, markdown: !options.OneLine && !options.JsonOutput,
                        verbosity: (int)options.Verbosity,
                        sectionCostAnnotations: typePipeline.GetCostAnnotations());
                }

                ApiCommand.WriteFullApiOutput(api, options, selectedTfm);

                if (!options.IsRawOutput)
                {
                    var sourceFlag = !string.IsNullOrEmpty(options.PlatformAssembly) ? $"--platform {options.PlatformAssembly}"
                        : !string.IsNullOrEmpty(options.PackagePath) ? $"--package {packageName ?? options.PackagePath}"
                        : !string.IsNullOrEmpty(options.AssemblyPath) ? $"--library {options.AssemblyPath}"
                        : "";

                    // Pick a representative type: prefer the one with most members
                    var exampleType = api.Types
                        .OrderByDescending(t => t.Members.Count)
                        .FirstOrDefault();

                    if (exampleType != null)
                    {
                        var simpleName = exampleType.FullName.Contains('.')
                            ? exampleType.FullName[(exampleType.FullName.LastIndexOf('.') + 1)..] : exampleType.FullName;

                        List<Tip> tips =
                        [
                            new(MemberCommand.Name, $"{simpleName} {sourceFlag}", "inspect type members"),
                            new(Name, $"{sourceFlag} --shape", "view type shape"),
                            new(Name, $"-t \"*Writer*\" {sourceFlag}", "filter types by pattern"),
                        ];

                        Hints.WriteTips(options.TipLevel, [.. tips]);
                    }
                }
            }
            else
            {
                var (api, apiDllPath) = ApiServices.ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from library.");
                    return 1;
                }

                if (apiDllPath != null)
                    ApiServices.ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);

                var allTypeNames = api.Types.Select(t => t.FullName).ToList();
                var lookupResult = TypeMatcher.Lookup(allTypeNames, typeName);

                if (lookupResult.Match != null)
                {
                    var apiType = api.Types.First(t => t.FullName == lookupResult.Match);

                    // Check each member filter before producing output
                    if (options.MemberFilter.Count > 0)
                    {
                        var memberNames = apiType.Members.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        List<string> missedFilters = [];

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

                    // Default --docs on for single-type view at Normal+ unless explicitly disabled
                    TypeOptions effectiveOptions = options;
                    if (!options.DocsExplicitlySet && options.Verbosity >= Verbosity.Normal)
                        effectiveOptions = options with { ShowDocs = true };

                    // Default --shape on for single-type view when no explicit format was
                    // chosen and the user is not running a section/projection query. Selection
                    // (-S) and projection (--columns/--fields) produce focused output, not the tree.
                    if (!effectiveOptions.ShapeExplicitlySet && effectiveOptions.IsDefaultInvocation && !effectiveOptions.HasSectionQuery)
                        effectiveOptions = effectiveOptions with { ShapeOutput = true };

                    // Explicit --shape cannot honor a section/projection query; warn rather than
                    // silently dropping the selection.
                    if (effectiveOptions is { ShapeOutput: true, HasSectionQuery: true, Count: false })
                        Console.Error.WriteLine("Warning: --shape does not support -S/--columns/--fields; selection was ignored.");

                    // Enrich with local XML docs only (source info is in the source command)
                    {
                        var dllPath = runtimeAssemblyPath ?? apiDllPath;
                        if (dllPath != null && effectiveOptions.ShowDocs)
                            SourceEnricher.EnrichFromLocalXmlDocs(apiType, dllPath, effectiveOptions, logger);
                    }

                    if (effectiveOptions.EffectiveDiscovery)
                    {
                        return ApiCommand.ExecuteEffectiveDiscovery(apiType, memberPipeline, effectiveOptions);
                    }

                    bool hasProjection = effectiveOptions.Columns is { Length: > 0 } || effectiveOptions.Fields is { Length: > 0 };
                    bool tabularProjection = hasProjection
                        && !effectiveOptions.JsonOutput
                        && !effectiveOptions.Count
                        && effectiveOptions is not TypeOptions { ShapeOutput: true };

                    // Pre-render: validate --columns/--fields names against the section schema
                    // (catches typos) when a specific section is selected, mirroring the package path.
                    if (tabularProjection && effectiveOptions.IncludeSections is { Count: > 0 })
                    {
                        var projSchema = ApiViewContext.Default.GetSchemaInfo<TypeView>()!.ToDocumentSchema();
                        if (!ProjectionDiagnostics.ValidateProjection(projSchema, effectiveOptions.IncludeSections, effectiveOptions.Fields, effectiveOptions.Columns))
                            return 1;
                    }

                    if (tabularProjection)
                    {
                        // Capture output so we can warn when a requested column produced no data
                        // (e.g. Select without --show-index, or a column not shown at this verbosity).
                        var sw = new StringWriter();
                        ApiCommand.WriteTypeOutput(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, effectiveOptions, sw);
                        var rendered = sw.ToString();
                        ProjectionDiagnostics.DiagnoseRendered(effectiveOptions.Fields ?? effectiveOptions.Columns, rendered);
                        Console.Out.Write(rendered);
                    }
                    else
                    {
                        ApiCommand.WriteTypeOutput(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, effectiveOptions);
                    }

                    // Notify when a requested section matched but has no data for this type.
                    // JSON and markdown both honor -S; tabular output falls back to showing all
                    // members and shape replaces selection, so skip those.
                    if (!effectiveOptions.OneLine
                        && effectiveOptions is not TypeOptions { ShapeOutput: true })
                    {
                        ApiCommand.WarnEmptySelectedSections(apiType, effectiveOptions, memberPipeline);
                    }

                    if (!effectiveOptions.IsRawOutput)
                    {
                        var sourceFlag = !string.IsNullOrEmpty(options.PlatformAssembly) ? $"--platform {options.PlatformAssembly}"
                            : !string.IsNullOrEmpty(options.PackagePath) ? $"--package {packageName ?? options.PackagePath}"
                            : !string.IsNullOrEmpty(options.AssemblyPath) ? $"--library {options.AssemblyPath}"
                            : "";

                        var simpleName = apiType.FullName.Contains('.')
                            ? apiType.FullName[(apiType.FullName.LastIndexOf('.') + 1)..] : apiType.FullName;

                        var overloadGroups = apiType.Members
                            .Where(m => m.Kind is "method" or "constructor")
                            .GroupBy(m => m.Name)
                            .OrderByDescending(g => g.Count())
                            .ToList();
                        var exampleGroup = overloadGroups.FirstOrDefault();

                        List<Tip> tips = [];

                        if (exampleGroup != null)
                        {
                            var memberName = exampleGroup.Key == ".ctor" ? ".ctor" : exampleGroup.Key;
                            tips.Add(new(MemberCommand.Name, $"{simpleName} {sourceFlag} {memberName}:1", "view member detail (source, IL)"));
                        }

                        if (overloadGroups.Any(g => g.Count() > 1))
                            tips.Add(new(MemberCommand.Name, $"{simpleName} {sourceFlag} --show-index", "show Name:N overload index"));

                        tips.Add(new(Name, $"{simpleName} {sourceFlag} --shape", "view type shape"));
                        tips.Add(new(MemberCommand.Name, $"-m {simpleName}.{(exampleGroup?.Key ?? "Method")} {sourceFlag}", "dotted member syntax"));

                        if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
                            tips.Add(new(DiffCommand.Name, $"--package {packageName}@<prev>..{packageVersion} -t {simpleName}", "compare API changes"));

                        Hints.WriteTips(effectiveOptions.TipLevel, [.. tips]);
                    }
                }
                else if (lookupResult.Suggestions.Count > 0)
                {
                    bool isGlob = typeName.Contains('*') || typeName.Contains('?');
                    if (isGlob)
                    {
                        // Glob matched multiple types — show types view with filter
                        if (!string.IsNullOrEmpty(options.PackagePath))
                        {
                            var (pkgName, _) = PackageExtractor.ParsePackageReference(options.PackagePath);
                            api.Name = pkgName;
                        }
                        else if (apiDllPath != null)
                        {
                            api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
                        }
                        api.Tfm = selectedTfm;
                        api.Source = apiSource;
                        api.Version = apiVersion;
                        api.Library = apiDllPath != null ? Path.GetFileName(apiDllPath) : null;

                        options = options with
                        {
                            TypeFilter = typeName,
                            Verbosity = options.Verbosity < Verbosity.Minimal ? Verbosity.Minimal : options.Verbosity
                        };

                        ApiCommand.WriteFullApiOutput(api, options, selectedTfm);
                    }
                    else
                    {
                        Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                        Console.Error.WriteLine();
                        Console.Error.WriteLine("Did you mean:");
                        foreach (var s in lookupResult.Suggestions)
                            Console.Error.WriteLine($"  {s}");
                        return 1;
                    }
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
}
