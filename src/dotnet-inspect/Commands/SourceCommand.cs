using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Commands;

/// <summary>
/// Shows SourceLink source file information for types in a package or library.
/// </summary>
public static class SourceCommand
{
    public const string Name = "source";

    public static async Task<int> ExecuteAsync(SourceOptions options)
    {
        // Bridge to ApiCommand's source resolution
        var apiOptions = new ApiOptions
        {
            PackagePath = options.PackagePath,
            AssemblyPath = options.AssemblyPath,
            PlatformAssembly = options.PlatformAssembly,
            PlatformFramework = options.PlatformFramework,
            Tfm = options.Tfm,
            TypeName = options.TypeName,
            Verbose = options.Verbose,
            Verbosity = options.Verbosity,
            SourceOptions = options.NuGetOptions
        };

        var (source, sourceError) = await ApiCommand.ResolveSourceAsync(apiOptions);
        if (sourceError.HasValue) return sourceError.Value;

        var searchPath = source.SearchPath;
        var runtimeAssemblyPath = source.RuntimeAssemblyPath;
        var packageName = source.PackageName;
        var packageVersion = source.PackageVersion;
        var selectedTfm = source.SelectedTfm;
        var tempDir = source.TempDir;
        var typeName = source.TypeName;
        var context = source.Context;
        var logger = context.Logger;

        try
        {
            // Extract all types
            var (api, apiDllPath) = ApiServices.ExtractFullApi(searchPath, logger, options.IncludeAll);
            if (api == null)
            {
                Console.Error.WriteLine("Error: Could not extract API from library.");
                return 1;
            }

            if (apiDllPath != null)
                ApiServices.ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);

            var dllPath = runtimeAssemblyPath ?? apiDllPath;
            if (dllPath == null)
            {
                Console.Error.WriteLine("Error: No library found.");
                return 1;
            }

            // Open SourceLink service and acquire PDB
            using var service = SourceLinkService.Open(dllPath, logger.Log);
            var pdbContext = service.Context;

            if (!pdbContext.HasMetadata)
            {
                Console.Error.WriteLine("Error: No metadata in library.");
                return 1;
            }

            var (pkgName, pkgVersion) = !string.IsNullOrEmpty(options.PackagePath)
                ? PackageExtractor.ParsePackageReference(options.PackagePath)
                : (null, null);

            await SourceEnricher.AcquirePdbAsync(pdbContext, context.HttpClient,
                pkgName, pkgVersion,
                isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log);

            if (!pdbContext.HasPdb)
            {
                WritePdbWarning(pdbContext);
                return 1;
            }

            if (!service.HasSourceLink)
            {
                Console.Error.WriteLine("Warning: No SourceLink information found in PDB.");
                Console.Error.WriteLine("         The library was not built with SourceLink enabled.");
                return 1;
            }

            if (!string.IsNullOrEmpty(typeName))
                return await ExecuteSingleTypeAsync(typeName, api, service, options, packageName, packageVersion, selectedTfm, logger, context.HttpClient);
            else
                return await ExecuteAllTypesAsync(api, service, options, packageName, packageVersion, selectedTfm, logger, context.HttpClient);
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

    private static async Task<int> ExecuteAllTypesAsync(
        ApiSurface api, SourceLinkService service, SourceOptions options,
        string? packageName, string? packageVersion, string? selectedTfm,
        VerboseLogger logger, HttpClient httpClient)
    {
        var types = api.Types.AsEnumerable();

        // Apply type filter
        if (!string.IsNullOrEmpty(options.TypeFilter))
            types = types.Where(t => TypeMatcher.MatchesTypeFilter(t.FullName, options.TypeFilter));

        var typeList = types.ToList();

        // Apply limit
        if (options.Limit.HasValue && typeList.Count > options.Limit.Value)
            typeList = typeList.Take(options.Limit.Value).ToList();

        // Resolve source for each type
        var rows = new List<SourceFileRow>();
        var verifiedRows = new List<VerifiedSourceFileRow>();

        foreach (var type in typeList)
        {
            var sourceInfo = service.ResolveTypeSource(type.FullName);
            if (sourceInfo == null)
            {
                rows.Add(new SourceFileRow(type.FullName, null));
                if (options.Verify)
                    verifiedRows.Add(new VerifiedSourceFileRow(type.FullName, null, "—"));
                continue;
            }

            var url = options.BrowsableUrls
                ? sourceInfo.GitHubBrowseUrl
                : sourceInfo.SourceUrl;

            rows.Add(new SourceFileRow(type.FullName, url));
            if (options.Verify)
                verifiedRows.Add(new VerifiedSourceFileRow(type.FullName, url, "pending"));

            foreach (var partial in sourceInfo.AdditionalSourceFiles)
            {
                var partialUrl = options.BrowsableUrls
                    ? partial.GitHubBrowseUrl
                    : partial.SourceUrl;
                rows.Add(new SourceFileRow(type.FullName, partialUrl));
                if (options.Verify)
                    verifiedRows.Add(new VerifiedSourceFileRow(type.FullName, partialUrl, "pending"));
            }
        }

        // Verify URLs if requested
        if (options.Verify)
            await VerifyUrlsAsync(verifiedRows, httpClient, logger);

        // Dispatch output based on format
        if (options.IsDefaultInvocation || (options.OneLine && !options.JsonOutput))
        {
            // Oneline: just the table, no header fields
            var view = new SourceOneLineView
            {
                SourceFiles = options.Verify ? null : rows,
                VerifiedSourceFiles = options.Verify ? verifiedRows : null
            };
            WriteOneLine(view, options);
        }
        else
        {
            // Markdown: header fields + table (table only at -v:m+)
            bool showTable = options.Verbosity >= Verbosity.Minimal;
            string title = packageName ?? (api.Name ?? "Source Files");
            var view = new SourceListView
            {
                Title = title,
                Repository = service.RepositoryUrl,
                Commit = service.CommitHash,
                PdbStatus = DescribePdbStatus(service.Context),
                Package = packageName,
                Version = packageVersion,
                Tfm = selectedTfm,
                Types = typeList.Count,
                SourceFiles = showTable ? (options.Verify ? null : rows) : null,
                VerifiedSourceFiles = showTable ? (options.Verify ? verifiedRows : null) : null
            };
            WriteMarkdown(view, options);
        }

        if (!options.IsRawOutput || options.IsDefaultInvocation)
        {
            var exampleType = typeList.FirstOrDefault(t => rows.Any(r => r.Type == t.FullName && r.Url != null));
            if (exampleType != null)
            {
                var simpleName = exampleType.FullName.Contains('.')
                    ? exampleType.FullName[(exampleType.FullName.LastIndexOf('.') + 1)..] : exampleType.FullName;
                var sourceFlag = !string.IsNullOrEmpty(options.PlatformAssembly) ? $"--platform {options.PlatformAssembly}"
                    : !string.IsNullOrEmpty(options.PackagePath) ? $"--package {packageName ?? options.PackagePath}"
                    : !string.IsNullOrEmpty(options.AssemblyPath) ? $"--library {options.AssemblyPath}"
                    : "";

                Hints.WriteTips(options.TipLevel,
                    new Tip(Name, $"{simpleName} {sourceFlag}", "view single type source"),
                    new Tip(Name, $"{sourceFlag} --verify", "verify URLs are accessible"),
                    new Tip(Name, $"{sourceFlag} -v:d", "include docs and samples"));
            }
        }

        return 0;
    }

    private static async Task<int> ExecuteSingleTypeAsync(
        string typeName, ApiSurface api, SourceLinkService service, SourceOptions options,
        string? packageName, string? packageVersion, string? selectedTfm,
        VerboseLogger logger, HttpClient httpClient)
    {
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
        var sourceInfo = service.ResolveTypeSource(lookupResult.Match);

        // Primary source URL for the Source field
        string? primaryUrl = null;
        if (sourceInfo != null)
        {
            primaryUrl = options.BrowsableUrls
                ? sourceInfo.GitHubBrowseUrl
                : sourceInfo.SourceUrl;
        }

        // Additional source files (partials) and verify rows
        var additionalRows = new List<SourceUrlRow>();
        var verifiedRows = new List<VerifiedSourceUrlRow>();

        if (sourceInfo != null)
        {
            if (options.Verify && primaryUrl != null)
                verifiedRows.Add(new VerifiedSourceUrlRow(primaryUrl, "pending"));

            foreach (var partial in sourceInfo.AdditionalSourceFiles)
            {
                var partialUrl = options.BrowsableUrls
                    ? partial.GitHubBrowseUrl
                    : partial.SourceUrl;
                additionalRows.Add(new SourceUrlRow(partialUrl ?? ""));
                if (options.Verify && partialUrl != null)
                    verifiedRows.Add(new VerifiedSourceUrlRow(partialUrl, "pending"));
            }
        }

        // Verify URLs if requested
        if (options.Verify)
            await VerifyUrlsAsync(verifiedRows, httpClient, logger);

        // Docs enrichment at Normal+ verbosity
        List<MemberDocRow>? memberDocs = null;
        if (options.Verbosity >= Verbosity.Normal && sourceInfo?.SourceUrl != null)
        {
            var enrichOptions = new ApiOptions
            {
                ShowDocs = true,
                Verbose = options.Verbose,
                Verbosity = options.Verbosity,
                PlatformAssembly = options.PlatformAssembly,
                PackagePath = options.PackagePath
            };
            SourceEnricher.EnrichFromLocalXmlDocs(apiType, service.Context.AssemblyPath, enrichOptions, logger);

            memberDocs = apiType.Members
                .Where(m => !string.IsNullOrEmpty(m.Documentation.Summary))
                .Select(m => new MemberDocRow(m.Name, m.Documentation.Summary))
                .ToList();

            if (memberDocs.Count == 0) memberDocs = null;
        }

        // Samples at Detailed verbosity
        List<SampleRow>? samples = null;
        if (options.Verbosity >= Verbosity.Detailed && sourceInfo?.SourceUrl != null)
        {
            var enrichOptions = new ApiOptions
            {
                ShowDocs = true,
                ShowSamples = true,
                BrowsableUrls = options.BrowsableUrls,
                Verbose = options.Verbose,
                Verbosity = options.Verbosity,
                PlatformAssembly = options.PlatformAssembly,
                PackagePath = options.PackagePath
            };
            await SourceEnricher.EnrichDocsAsync(apiType, lookupResult.Match, service.Context.AssemblyPath, enrichOptions, logger, httpClient);

            if (apiType.Documentation.Samples.Count > 0)
            {
                samples = apiType.Documentation.Samples
                    .Select(s => new SampleRow(
                        apiType.Name,
                        s.Description ?? Path.GetFileName(s.RelativePath),
                        (options.BrowsableUrls
                            ? GitHubUrlResolver.ConvertRawToBlobUrl(s.ResolvedUrl ?? "")
                            : s.ResolvedUrl) ?? ""))
                    .ToList();

                if (samples.Count == 0) samples = null;
            }
        }

        // Dispatch output based on format — match member command pattern:
        // default (no -v) = oneline, -v = markdown
        if (options.IsDefaultInvocation || (options.OneLine && !options.JsonOutput))
        {
            // Oneline: URL-only table (no Type column, type is already known)
            var urlRows = new List<SourceUrlRow>();
            if (primaryUrl != null)
                urlRows.Add(new SourceUrlRow(primaryUrl));
            urlRows.AddRange(additionalRows);

            var oneLineView = new SourceDetailOneLineView
            {
                SourceFiles = options.Verify ? null : (urlRows.Count > 0 ? urlRows : null),
                VerifiedSourceFiles = options.Verify ? (verifiedRows.Count > 0 ? verifiedRows : null) : null
            };
            WriteOneLine(oneLineView, options);
        }
        else
        {
            // Markdown: heading + inline fields + sections
            // -v:q = heading + core fields (Kind, Library, Source)
            // -v:m = + extended metadata (Package, Version, Repo, Commit, PDB, Resolution) + partials
            // -v:n = + Documentation
            // -v:d = + Samples
            bool showExtended = options.Verbosity >= Verbosity.Minimal;
            string title = apiType.FullName;
            var view = new SourceDetailView
            {
                Title = title,
                Description = showExtended ? apiType.Documentation.Summary : null,
                Kind = apiType.Kind,
                Assembly = Path.GetFileNameWithoutExtension(service.Context.AssemblyPath),
                Source = primaryUrl,
                Package = showExtended ? packageName : null,
                Version = showExtended ? packageVersion : null,
                Repository = showExtended ? service.RepositoryUrl : null,
                Commit = showExtended ? service.CommitHash : null,
                PdbStatus = showExtended ? DescribePdbStatus(service.Context) : null,
                Resolution = showExtended ? sourceInfo?.ResolutionMethod.ToString() : null,
                AdditionalSourceFiles = showExtended ? (options.Verify ? null : (additionalRows.Count > 0 ? additionalRows : null)) : null,
                VerifiedSourceFiles = showExtended ? (options.Verify ? (verifiedRows.Count > 0 ? verifiedRows : null) : null) : null,
                MemberDocs = memberDocs,
                Samples = samples
            };
            WriteOutput(view, options);
        }

        if (!options.IsRawOutput)
        {
            var sourceFlag = !string.IsNullOrEmpty(options.PlatformAssembly) ? $"--platform {options.PlatformAssembly}"
                : !string.IsNullOrEmpty(options.PackagePath) ? $"--package {packageName ?? options.PackagePath}"
                : !string.IsNullOrEmpty(options.AssemblyPath) ? $"--library {options.AssemblyPath}"
                : "";
            var simpleName = apiType.FullName.Contains('.')
                ? apiType.FullName[(apiType.FullName.LastIndexOf('.') + 1)..] : apiType.FullName;

            List<Tip> tips = [];

            if (options.Verbosity < Verbosity.Detailed)
                tips.Add(new(Name, $"{simpleName} {sourceFlag} -v:d", "include docs and samples"));

            if (!options.Verify)
                tips.Add(new(Name, $"{simpleName} {sourceFlag} --verify", "verify URLs accessible"));

            tips.Add(new(TypeCommand.Name, $"{simpleName} {sourceFlag}", "view type API"));
            tips.Add(new(MemberCommand.Name, $"{simpleName} {sourceFlag}", "view member details"));

            Hints.WriteTips(options.TipLevel, [.. tips]);
        }

        return 0;
    }

    // ===== Helpers =====

    private static void WriteOneLine<T>(T view, SourceOptions options) where T : class
    {
        var writerOpts = new MarkoutWriterOptions
        {
            Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields)
        };
        new MarkoutContext().Serialize(view, Console.Out, new OneLineFormatter(showHeader: !options.NoHeader), writerOpts);
    }

    private static void WriteMarkdown<T>(T view, SourceOptions options) where T : class
    {
        var writerOpts = new MarkoutWriterOptions
        {
            Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields)
        };
        var formatter = options.PlainText ? (IMarkoutFormatter)new PlainTextFormatter() : new MarkdownFormatter();
        new MarkoutContext().Serialize(view, Console.Out, formatter, writerOpts);
    }

    private static void WriteOutput<T>(T view, SourceOptions options) where T : class
    {
        if (options.JsonOutput)
        {
            var json = new MarkoutContext().Serialize(view);
            Console.Write(json);
            return;
        }

        if (options.OneLine)
            WriteOneLine(view, options);
        else
            WriteMarkdown(view, options);
    }

    private static async Task VerifyUrlsAsync<T>(List<T> rows, HttpClient httpClient, VerboseLogger logger)
    {
        var urlItems = rows.Select((row, index) =>
        {
            var url = row switch
            {
                VerifiedSourceFileRow r => r.Url,
                VerifiedSourceUrlRow r => r.Url,
                _ => null
            };
            return (Index: index, Url: url);
        }).Where(x => x.Url != null).ToList();

        var results = new string[rows.Count];
        for (int i = 0; i < results.Length; i++)
            results[i] = "—";

        await Parallel.ForEachAsync(urlItems, new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (item, ct) =>
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, item.Url);
                var response = await httpClient.SendAsync(request, ct);
                results[item.Index] = response.IsSuccessStatusCode ? "✓" : $"✗ {(int)response.StatusCode}";
            }
            catch (Exception ex)
            {
                logger.Log($"Verify failed for {item.Url}: {ex.Message}");
                results[item.Index] = "✗ error";
            }
        });

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is VerifiedSourceFileRow r)
                rows[i] = (T)(object)(r with { Status = results[i] });
            else if (rows[i] is VerifiedSourceUrlRow r2)
                rows[i] = (T)(object)(r2 with { Status = results[i] });
        }
    }

    private static string DescribePdbStatus(PdbContext context)
    {
        if (!context.HasPdb) return "Not available";
        var source = context.PdbLocation ?? "Unknown";
        return context.HasReproducibleFlag ? $"{source} (reproducible)" : source;
    }

    private static void WritePdbWarning(PdbContext pdbContext)
    {
        Console.Error.WriteLine();
        if (pdbContext.WindowsPdbDetected)
        {
            Console.Error.WriteLine("Error: PDB is Windows format (not supported).");
            Console.Error.WriteLine("       Only Portable PDBs are supported.");
        }
        else
        {
            Console.Error.WriteLine("Error: No readable PDB found.");
        }
        Console.Error.WriteLine("       Run 'library --source-link-audit' for more details.");
        Console.Error.WriteLine();
    }
}
