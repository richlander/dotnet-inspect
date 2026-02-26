using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects type members (docs on by default).
/// </summary>
public static class MemberCommand
{
    public const string Name = "member";

    public static async Task<int> ExecuteAsync(MemberOptions options)
    {
        // Validate that member command has a type argument
        if (options.MemberFilter.Count > 0 && string.IsNullOrEmpty(options.TypeName))
        {
            Console.Error.WriteLine("Error: --member requires a type argument.");
            Console.Error.WriteLine("Usage: dotnet-inspect member <type> --package <pkg> -m <name>");
            Console.Error.WriteLine("   or: dotnet-inspect member -m Type.Member --package <pkg>");
            return 1;
        }

        // Shared preamble: section validation, discovery, verbosity promotion
        var (preamble, error) = ApiCommand.RunPreamble(options);
        if (error.HasValue) return error.Value;

        options = (MemberOptions)preamble.Options;
        var discoverSections = preamble.DiscoverSections;
        var memberPipeline = preamble.MemberPipeline;

        // Shared source resolution
        var (source, sourceError) = await ApiCommand.ResolveSourceAsync(options);
        if (sourceError.HasValue) return sourceError.Value;

        var searchPath = source.SearchPath;
        var runtimeAssemblyPath = source.RuntimeAssemblyPath;
        var packageName = source.PackageName;
        var packageVersion = source.PackageVersion;
        var apiSource = source.ApiSource;
        var selectedTfm = source.SelectedTfm;
        var tempDir = source.TempDir;
        var typeName = source.TypeName;
        var context = source.Context;
        var logger = context.Logger;

        try
        {
            typeName = GenericTypeNameConverter.Convert(typeName!);

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

            if (lookupResult.Match == null)
            {
                if (lookupResult.Suggestions.Count > 0)
                {
                    Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Did you mean:");
                    foreach (var s in lookupResult.Suggestions)
                        Console.Error.WriteLine($"  {s}");
                }
                else
                {
                    Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                }
                return 1;
            }

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
            MemberOptions effectiveOptions = options;
            if (!options.DocsExplicitlySet && options.Verbosity >= Verbosity.Normal)
                effectiveOptions = options with { ShowDocs = true };

            // --index: select a specific overload and show IL
            if (effectiveOptions.OverloadIndex.HasValue)
            {
                if (effectiveOptions.MemberFilter.Count != 1)
                {
                    Console.Error.WriteLine("Error: --index/Name:N requires exactly one member name.");
                    return 1;
                }

                var memberName = effectiveOptions.MemberFilter.First();
                var overloads = apiType.Members
                    .Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int idx = effectiveOptions.OverloadIndex.Value;
                if (idx < 1 || idx > overloads.Count)
                {
                    Console.Error.WriteLine($"Error: {memberName}:{idx} is out of range. Use {memberName}:1 through {memberName}:{overloads.Count}.");
                    return 1;
                }

                var selected = overloads[idx - 1];
                apiType.Members = [selected];
                var exclude = effectiveOptions.ExcludeSections ?? [];
                exclude.Add("Remote Source");
                effectiveOptions = effectiveOptions with { DllPath = apiDllPath, ExcludeSections = exclude };
            }

            // --params / -of: select overload by parameter type matching
            if (!effectiveOptions.OverloadIndex.HasValue && (effectiveOptions.ParamTypes != null || effectiveOptions.FirstParamType != null))
            {
                if (effectiveOptions.MemberFilter.Count != 1)
                {
                    Console.Error.WriteLine("Error: --params/-of requires exactly one member name via -m.");
                    return 1;
                }

                var memberName = effectiveOptions.MemberFilter.First();
                var overloads = apiType.Members
                    .Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var matches = new List<(ApiMember member, int index)>();
                for (int i = 0; i < overloads.Count; i++)
                {
                    var sig = overloads[i].Signature;
                    if (sig == null) continue;
                    var paramTypes = ApiCommand.ExtractParameterTypes(sig);

                    if (effectiveOptions.ParamTypes != null)
                    {
                        if (ApiCommand.MatchesParamTypes(paramTypes, effectiveOptions.ParamTypes))
                            matches.Add((overloads[i], i));
                    }
                    else if (effectiveOptions.FirstParamType != null)
                    {
                        if (paramTypes.Count > 0 && ApiCommand.SimpleNameMatches(paramTypes[0], effectiveOptions.FirstParamType))
                            matches.Add((overloads[i], i));
                    }
                }

                if (matches.Count == 0)
                {
                    var filter = effectiveOptions.ParamTypes != null
                        ? $"--params \"{string.Join(",", effectiveOptions.ParamTypes)}\""
                        : $"-of \"{effectiveOptions.FirstParamType}\"";
                    Console.Error.WriteLine($"Error: No overload of {memberName} matches {filter}.");
                    return 1;
                }

                if (matches.Count > 1)
                {
                    Console.Error.WriteLine($"Error: Multiple overloads of {memberName} match. Use --params with more specific types to disambiguate:");
                    foreach (var (m, _) in matches)
                        Console.Error.WriteLine($"  {m.Signature}");
                    return 1;
                }

                var (selectedMember, overloadIdx) = matches[0];
                apiType.Members = [selectedMember];
                var excludeSections = effectiveOptions.ExcludeSections ?? [];
                excludeSections.Add("Remote Source");
                effectiveOptions = effectiveOptions with
                {
                    DllPath = apiDllPath,
                    ExcludeSections = excludeSections,
                    OverloadIndex = overloadIdx + 1
                };
            }

            // Enrich with source/doc info
            {
                bool isPlatformLocal = !string.IsNullOrEmpty(effectiveOptions.PlatformAssembly)
                    && effectiveOptions.ShowDocs;
                bool shouldEnrich = effectiveOptions.Verbosity >= Verbosity.Detailed || isPlatformLocal;

                if (shouldEnrich)
                {
                    var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                    if (pdbLookupPath != null)
                        await SourceEnricher.EnrichTypeWithSourceInfoAsync(apiType, typeName, pdbLookupPath, effectiveOptions, logger, context.HttpClient);
                }
            }

            // Resolve method source code for --index view (after PDB acquisition)
            if (effectiveOptions.OverloadIndex.HasValue && apiDllPath != null)
            {
                var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                var methodSource = await ApiCommand.ResolveMethodSourceAsync(
                    pdbLookupPath, apiType.FullName,
                    effectiveOptions.MemberFilter.First(),
                    effectiveOptions.OverloadIndex.Value - 1,
                    effectiveOptions, context.HttpClient, logger);

                if (methodSource != null)
                    effectiveOptions = effectiveOptions with { MethodSource = methodSource };
            }

            if (discoverSections)
            {
                memberPipeline.ListEffectiveSections(apiType);
                return 0;
            }

            ApiCommand.WriteTypeOutput(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, effectiveOptions);

            if (!effectiveOptions.IsRawOutput && effectiveOptions.OverloadIndex == null)
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
                    tips.Add(new(Name, $"{simpleName} {sourceFlag} {memberName}:1", "view member detail (source, IL)"));
                }

                if (overloadGroups.Any(g => g.Count() > 1))
                    tips.Add(new(Name, $"{simpleName} {sourceFlag} --select", "show Name:N overload index"));

                tips.Add(new(TypeCommand.Name, $"{simpleName} {sourceFlag} --shape", "view type shape"));
                tips.Add(new(Name, $"-m {simpleName}.{(exampleGroup?.Key ?? "Method")} {sourceFlag}", "dotted member syntax"));

                if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
                    tips.Add(new(DiffCommand.Name, $"--package {packageName}@<prev>..{packageVersion} -t {simpleName}", "compare API changes"));

                Hints.WriteTips(effectiveOptions.TipLevel, [.. tips]);
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
