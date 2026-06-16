using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;
using MarkdownTable.Formatting;
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

        if (await TryExecuteFindIfMissAsync(options) is { } findIfMissExitCode)
            return findIfMissExitCode;

        var (source, sourceError) = await ApiCommand.ResolveSourceAsync(apiOptions);
        if (sourceError.HasValue)
        {
            NamespacePrefixHints.WriteIfLikelyNamespacePrefix(options.PackagePath ?? options.TypeName ?? "");
            return sourceError.Value;
        }

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
            // IL offset mode: resolve method token + offset directly, skip API extraction
            if (!string.IsNullOrEmpty(options.ILOffset))
            {
                return await ExecuteILOffsetAsync(source, options, logger);
            }

            // Extract all types
            var loaded = ApiServices.LoadFullApi(
                searchPath, runtimeAssemblyPath, options.PackagePath, packageName,
                source.ApiSource, source.ApiVersion, selectedTfm, logger, options.IncludeAll);
            if (loaded == null)
            {
                Console.Error.WriteLine("Error: Could not extract API from library.");
                return 1;
            }

            var api = loaded.Api;
            var apiDllPath = loaded.ApiDllPath;

            var dllPath = loaded.PdbLookupPath;

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

        // Resolve source for each type, following type forwarders as needed
        var rows = new List<SourceFileRow>();
        var verifiedRows = new List<VerifiedSourceFileRow>();
        var implServices = new Dictionary<string, SourceLinkService>();

        try
        {

        foreach (var type in typeList)
        {
            var typeDisplayName = ApiOutputFormatter.FormatGenericFullName(type);
            var sourceInfo = service.ResolveTypeSource(type.FullName);

            // Follow type forwarder if source not found in the original assembly
            if (sourceInfo == null)
            {
                var implPath = service.ResolveImplementationAssemblyPath(type.FullName);
                if (implPath != null && !implServices.ContainsKey(implPath))
                {
                    var implSvc = SourceLinkService.Open(implPath, logger.Log);
                    await SourceEnricher.AcquirePdbAsync(implSvc.Context, httpClient,
                        null, null, isPlatformAssembly: true, logger.Log);
                    implServices[implPath] = implSvc;
                }

                if (implPath != null && implServices.TryGetValue(implPath, out var cachedSvc)
                    && cachedSvc.HasPdb && cachedSvc.HasSourceLink)
                {
                    sourceInfo = cachedSvc.ResolveTypeSource(type.FullName);
                }
            }

            if (sourceInfo == null)
            {
                rows.Add(new SourceFileRow(typeDisplayName, null));
                if (options.Verify)
                    verifiedRows.Add(new VerifiedSourceFileRow(typeDisplayName, null, "—"));
                continue;
            }

            var url = options.BrowsableUrls
                ? sourceInfo.GitHubBrowseUrl
                : sourceInfo.SourceUrl;

            rows.Add(new SourceFileRow(typeDisplayName, url));
            if (options.Verify)
                verifiedRows.Add(new VerifiedSourceFileRow(typeDisplayName, url, "pending"));

            foreach (var partial in sourceInfo.AdditionalSourceFiles)
            {
                var partialUrl = options.BrowsableUrls
                    ? partial.GitHubBrowseUrl
                    : partial.SourceUrl;
                rows.Add(new SourceFileRow(typeDisplayName, partialUrl));
                if (options.Verify)
                    verifiedRows.Add(new VerifiedSourceFileRow(typeDisplayName, partialUrl, "pending"));
            }
        }

        // Cat mode: fetch and print source file contents
        if (options.Cat)
        {
            return await CatUrlsAsync(rows.Where(r => r.Url != null).Select(r => r.Url!), httpClient);
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
            WriteOutput(view, options);
        }

        if (!options.FormatExplicitlySet && (!options.IsRawOutput || options.IsDefaultInvocation))
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
                    new Tip("library", $"{sourceFlag} -S \"SourceLink Availability\"", "full source accessibility audit"),
                    new Tip(Name, $"{sourceFlag} -v:d", "include docs and samples"));
            }
        }

        return 0;
        }
        finally
        {
            foreach (var svc in implServices.Values)
                svc.Dispose();
        }
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

        // Follow type forwarders to the implementation assembly for source resolution.
        // E.g., List`1 in System.Collections is forwarded to System.Private.CoreLib at runtime.
        SourceLinkService? implService = null;
        if (sourceInfo == null)
        {
            implService = service.OpenImplementation(lookupResult.Match);
            if (implService != null)
            {
                logger.Log($"Following type forwarder to {Path.GetFileNameWithoutExtension(implService.Context.AssemblyPath)}");
                await SourceEnricher.AcquirePdbAsync(implService.Context, httpClient,
                    null, null, isPlatformAssembly: true, logger.Log);

                if (implService.HasPdb && implService.HasSourceLink)
                    sourceInfo = implService.ResolveTypeSource(lookupResult.Match);
            }
        }

        var effectiveService = implService != null && sourceInfo != null ? implService : service;

        try
        {

        // Member-level resolution: get line numbers from PDB sequence points
        int? startLine = null;
        int? endLine = null;
        if (!string.IsNullOrEmpty(options.MemberName) && sourceInfo != null)
        {
            var methodInfo = effectiveService.ResolveMethodSource(
                lookupResult.Match, options.MemberName,
                options.OverloadIndex ?? 0, publicOnly: false);

            if (methodInfo != null)
            {
                startLine = methodInfo.StartLine;
                endLine = methodInfo.EndLine;
            }
            else
            {
                Console.Error.WriteLine($"Warning: Could not resolve source location for member '{options.MemberName}'.");
            }
        }

        // Primary source URL for the Source field
        string? primaryUrl = null;
        if (sourceInfo != null)
        {
            primaryUrl = options.BrowsableUrls
                ? sourceInfo.GitHubBrowseUrl
                : sourceInfo.SourceUrl;

            // Append line fragment for member-level resolution
            if (primaryUrl != null && startLine.HasValue)
            {
                var lineFragment = endLine.HasValue && endLine.Value > startLine.Value
                    ? $"#L{startLine.Value}-L{endLine.Value}"
                    : $"#L{startLine.Value}";
                primaryUrl += lineFragment;
            }
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

        // Cat mode: fetch and print source file contents
        if (options.Cat)
        {
            var urls = new List<string>();
            if (primaryUrl != null) urls.Add(primaryUrl);
            urls.AddRange(additionalRows.Select(r => r.Url).Where(u => !string.IsNullOrEmpty(u))!);
            return await CatUrlsAsync(urls, httpClient);
        }

        // Verify URLs if requested
        if (options.Verify)
            await VerifyUrlsAsync(verifiedRows, httpClient, logger);

        // Docs enrichment at Normal+ verbosity
        List<MemberDocRow>? memberDocs = null;
        if (options.Verbosity >= Verbosity.Detailed && sourceInfo?.SourceUrl != null)
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
        // default (no -v) = table, -v = markdown
        if (options.IsDefaultInvocation || (options.OneLine && !options.JsonOutput))
        {
            // Oneline: URL-only table (no Type column, type is already known)
            var urlRows = new List<SourceUrlRow>();
            if (primaryUrl != null)
                urlRows.Add(new SourceUrlRow(primaryUrl));
            urlRows.AddRange(additionalRows);

            if (urlRows.Count == 0)
            {
                var sourceFlag = !string.IsNullOrEmpty(options.PlatformAssembly) ? $"--platform {options.PlatformAssembly}"
                    : !string.IsNullOrEmpty(options.PackagePath) ? $"{packageName ?? options.PackagePath}"
                    : !string.IsNullOrEmpty(options.AssemblyPath) ? $"--library {options.AssemblyPath}"
                    : "";
                var simpleName = apiType.FullName.Contains('.')
                    ? apiType.FullName[(apiType.FullName.LastIndexOf('.') + 1)..] : apiType.FullName;

                Console.Error.WriteLine($"No source URLs found for '{lookupResult.Match}'.");
                Console.Error.WriteLine("The PDB may not contain SourceLink document mappings.");
                Console.Error.WriteLine($"Try: source {sourceFlag} {simpleName} -v:q");
                return 0;
            }

            var oneLineView = new SourceDetailOneLineView
            {
                SourceFiles = options.Verify ? null : urlRows,
                VerifiedSourceFiles = options.Verify ? (verifiedRows.Count > 0 ? verifiedRows : null) : null
            };
            WriteOneLine(oneLineView, options);
        }
        else
        {
            // Markdown: heading + inline fields + sections
            // -v:q = heading + core fields (Kind, Library, Source or Files count)
            // -v:m = + extended metadata (Package, Version, Repo, Commit, PDB, Resolution) + partials
            // -v:n = + Documentation
            // -v:d = + Samples
            bool showExtended = options.Verbosity >= Verbosity.Minimal;
            int totalFiles = 1 + additionalRows.Count;
            bool singleFile = totalFiles == 1;
            string title = apiType.FullName;
            var view = new SourceDetailView
            {
                Title = title,
                Description = showExtended ? apiType.Documentation.Summary : null,
                Kind = apiType.Kind,
                Assembly = Path.GetFileNameWithoutExtension(effectiveService.Context.AssemblyPath),
                Source = singleFile ? primaryUrl : null,
                Files = singleFile ? null : totalFiles,
                Package = showExtended ? packageName : null,
                Version = showExtended ? packageVersion : null,
                Repository = showExtended ? effectiveService.RepositoryUrl : null,
                Commit = showExtended ? effectiveService.CommitHash : null,
                PdbStatus = showExtended ? DescribePdbStatus(effectiveService.Context) : null,
                Resolution = showExtended ? sourceInfo?.ResolutionMethod.ToString() : null,
                AdditionalSourceFiles = showExtended ? (options.Verify ? null : (additionalRows.Count > 0 ? additionalRows : null)) : null,
                VerifiedSourceFiles = showExtended ? (options.Verify ? (verifiedRows.Count > 0 ? verifiedRows : null) : null) : null,
                MemberDocs = memberDocs,
                Samples = samples
            };

            if (options.JsonOutput)
                WriteJson(view, options.CompactJson);
            else
                WriteMarkdown(view, options);
        }

        if (!options.FormatExplicitlySet && !options.IsRawOutput)
        {
            var sourceFlag = !string.IsNullOrEmpty(options.PlatformAssembly) ? $"--platform {options.PlatformAssembly}"
                : !string.IsNullOrEmpty(options.PackagePath) ? $"--package {packageName ?? options.PackagePath}"
                : !string.IsNullOrEmpty(options.AssemblyPath) ? $"--library {options.AssemblyPath}"
                : "";
            var displayName = FormatFriendlyTypeName(apiType);
            var quotedName = displayName.Contains('<') ? $"'{displayName}'" : displayName;

            List<Tip> tips = [];

            if (options.Verbosity < Verbosity.Detailed)
                tips.Add(new(Name, $"{quotedName} {sourceFlag} -v:d", "include docs and samples"));

            if (!options.Verify)
                tips.Add(new(Name, $"{quotedName} {sourceFlag} --verify", "verify URLs accessible"));

            tips.Add(new(TypeCommand.Name, $"{quotedName} {sourceFlag}", "view type API"));
            tips.Add(new(MemberCommand.Name, $"{quotedName} {sourceFlag}", "view member details"));

            Hints.WriteTips(options.TipLevel, [.. tips]);
        }

        return 0;
        }
        finally
        {
            implService?.Dispose();
        }
    }

    // ===== IL Offset Mode =====

    /// <summary>
    /// Parses an IL offset string in the format "0x6000001+0x5" into method token and IL offset.
    /// </summary>
    internal static bool TryParseILOffset(string value, out int methodToken, out int ilOffset)
    {
        methodToken = 0;
        ilOffset = 0;

        var plusIndex = value.IndexOf('+');
        if (plusIndex < 0)
            return false;

        var tokenPart = value[..plusIndex].Trim();
        var offsetPart = value[(plusIndex + 1)..].Trim();

        if (!TryParseHexOrDecimal(tokenPart, out methodToken))
            return false;

        if (!TryParseHexOrDecimal(offsetPart, out ilOffset))
            return false;

        // Validate the token is in the MethodDef table (table 0x06)
        int table = (methodToken >> 24) & 0xFF;
        if (table != 0x06)
            return false;

        return ilOffset >= 0;
    }

    private static async Task<int?> TryExecuteFindIfMissAsync(SourceOptions options)
    {
        if (options.TypeName != null || options.PackagePath == null || options.AssemblyPath != null || options.PlatformAssembly != null)
            return null;

        var context = new CommandContext(options.Verbose);
        var resolution = await TypeFindIfMissResolver.ResolvePlatformAsync(
            options.PackagePath,
            options.IncludeAll,
            options.NuGetOptions,
            context.HttpClient,
            context.Logger);

        return resolution.Status switch
        {
            TypeFindIfMissStatus.Found => await ExecuteAsync(resolution.ApplyTo(options)),
            TypeFindIfMissStatus.Ambiguous => resolution.WriteAmbiguousError(),
            _ => null
        };
    }

    private static bool TryParseHexOrDecimal(string value, out int result)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out result);
        return int.TryParse(value, out result);
    }

    private static async Task<int> ExecuteILOffsetAsync(
        ApiCommand.SourceResult source, SourceOptions options,
        VerboseLogger logger)
    {
        var dllPath = source.RuntimeAssemblyPath;

        // If no runtime assembly path, try to find a DLL in the search path
        if (dllPath == null && source.SearchPath != null)
        {
            var (_, apiDllPath) = ApiServices.ExtractFullApi(source.SearchPath, logger, options.IncludeAll);
            dllPath = apiDllPath;
        }

        if (dllPath == null)
        {
            Console.Error.WriteLine("Error: No library found.");
            return 1;
        }

        if (!TryParseILOffset(options.ILOffset!, out var methodToken, out var ilOffset))
        {
            Console.Error.WriteLine($"Error: Invalid --il-offset format '{options.ILOffset}'.");
            Console.Error.WriteLine("Expected format: 0x6000001+0x5 (method token + IL offset)");
            return 1;
        }

        using var service = SourceLinkService.Open(dllPath, logger.Log);
        var pdbContext = service.Context;

        if (!pdbContext.HasMetadata)
        {
            Console.Error.WriteLine("Error: No metadata in library.");
            return 1;
        }

        await SourceEnricher.AcquirePdbAsync(pdbContext, source.Context.HttpClient,
            source.PackageName, source.PackageVersion,
            isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log);

        if (!pdbContext.HasPdb)
        {
            WritePdbWarning(pdbContext);
            return 1;
        }

        if (!service.HasSourceLink)
        {
            logger.Log("Warning: No SourceLink information found. URLs will not be available.");
        }

        var result = service.ResolveByILOffset(methodToken, ilOffset);

        if (result == null)
        {
            Console.Error.WriteLine($"Error: Could not resolve source location for token 0x{methodToken:X}+0x{ilOffset:X}.");
            Console.Error.WriteLine("The method token may be invalid or the PDB may not contain sequence points for this method.");
            return 1;
        }

        // Build URL with line fragment
        string? url = options.BrowsableUrls ? result.GitHubBrowseUrl : result.SourceUrl;
        if (url != null)
            url += $"#L{result.Line}";

        // Output
        if (options.JsonOutput)
        {
            var jsonResult = new ILOffsetResult
            {
                Method = result.MethodName,
                Token = $"0x{methodToken:X}",
                ILOffset = $"0x{ilOffset:X}",
                MatchedOffset = $"0x{result.MatchedOffset:X}",
                File = result.FilePath,
                Line = result.Line,
                Url = url
            };
            string json = options.CompactJson
                ? System.Text.Json.JsonSerializer.Serialize(jsonResult, SourceCompactJsonContext.Default.ILOffsetResult)
                : System.Text.Json.JsonSerializer.Serialize(jsonResult, SourceJsonContext.Default.ILOffsetResult);
            Console.WriteLine(json);
        }
        else
        {
            // Verbosity discipline (mirrors the library-info view):
            //   -v:q  → compact inline line only
            //   -v:m  → "Offset" section only
            //   -v:n+ → "Offset" + "Source" sections
            bool showSections = options.Verbosity >= Verbosity.Minimal;
            bool showSource = options.Verbosity >= Verbosity.Normal;
            string token = $"0x{methodToken:X}";
            string ilOffsetHex = $"0x{ilOffset:X}";
            string? matchedOffset = result.MatchedOffset != ilOffset ? $"0x{result.MatchedOffset:X}" : null;

            var view = new SourceILOffsetView
            {
                Title = result.MethodName ?? token,

                // Compact inline fields (only at -v:q) — token + offsets only;
                // source location is reserved for the "Source" section at -v:n+.
                Token = showSections ? null : token,
                ILOffset = showSections ? null : ilOffsetHex,
                MatchedOffset = showSections ? null : matchedOffset,

                // Offset section (-v:m+)
                Offset = showSections
                    ? new ILOffsetInfoSection { Token = token, ILOffset = ilOffsetHex, MatchedOffset = matchedOffset }
                    : null,

                // Source section (-v:n+)
                Location = showSource ? [new ILOffsetSourceRow(result.FilePath, result.Line, url)] : null,
            };

            if (options.IsDefaultInvocation || (options.OneLine && !options.JsonOutput))
            {
                // Oneline: just the URL (or file:line if no URL)
                Console.WriteLine(url ?? $"{result.FilePath}:{result.Line}");
            }
            else
            {
                WriteMarkdown(view, options);
            }
        }

        return 0;
    }

    // ===== Helpers =====

    private static void WriteOneLine<T>(T view, SourceOptions options) where T : class
    {
        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
            options.Columns, options.Fields,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(view, writer, formatter, SourceViewContext.Default, writerOptions));
    }

    private static void WriteMarkdown<T>(T view, SourceOptions options) where T : class
    {
        var writerOpts = OutputFormatter.CreateProjectedWriterOptions(options.Columns, options.Fields);
        var formatter = options.PlainText ? (IMarkoutFormatter)new PlainTextFormatter() : new MarkdownFormatter();
        if (options.PlainText)
        {
            MarkoutSerializer.Serialize(view, Console.Out, formatter, SourceViewContext.Default, writerOpts);
        }
        else
        {
            OutputFormatter.WriteLimitedMarkdown(Console.Out,
                MarkoutSerializer.Serialize(view, SourceViewContext.Default, writerOpts), options.Rows);
        }
    }

    private static void WriteJson<T>(T view, bool compact) where T : class
    {
        string json = view switch
        {
            SourceListView v => compact
                ? System.Text.Json.JsonSerializer.Serialize(ToListResult(v), SourceCompactJsonContext.Default.SourceListResult)
                : System.Text.Json.JsonSerializer.Serialize(ToListResult(v), SourceJsonContext.Default.SourceListResult),
            SourceDetailView v => compact
                ? System.Text.Json.JsonSerializer.Serialize(ToDetailResult(v), SourceCompactJsonContext.Default.SourceDetailResult)
                : System.Text.Json.JsonSerializer.Serialize(ToDetailResult(v), SourceJsonContext.Default.SourceDetailResult),
            _ => "{}"
        };
        Console.WriteLine(json);
    }

    private static async Task<int> CatUrlsAsync(IEnumerable<string> urls, HttpClient httpClient)
    {
        foreach (var url in urls)
        {
            var hashIndex = url.IndexOf('#');
            var fetchUrl = hashIndex >= 0 ? url[..hashIndex] : url;
            var (startLine, endLine) = hashIndex >= 0 ? ParseLineFragment(url[(hashIndex + 1)..]) : (0, 0);

            try
            {
                using var response = await httpClient.GetAsync(fetchUrl, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    if (startLine > 0)
                    {
                        await WriteExtractedLinesAsync(stream, startLine, endLine > 0 ? endLine : startLine);
                    }
                    else
                    {
                        await WriteAllLinesAsync(stream);
                    }
                }
                else
                {
                    Console.WriteLine($"{url} {(int)response.StatusCode} {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{url} {ex.Message}");
            }
        }
        return 0;
    }

    private static async Task WriteAllLinesAsync(Stream stream)
    {
        var reader = LineReader.Create(stream);
        while (!reader.IsComplete)
        {
            if (!reader.ReadLine(out var line))
            {
                if (!await reader.AdvanceAsync()) break;
                continue;
            }
            Console.WriteLine(LineReader.ToString(line));
        }
    }

    // Streams lines from startLine..endLine with blank-line expansion (up to 3 lines each direction).
    private static async Task WriteExtractedLinesAsync(Stream stream, int startLine, int endLine)
    {
        const int maxExpand = 3;
        var reader = LineReader.Create(stream);
        int currentLine = 1;
        int expandedStart = Math.Max(1, startLine - maxExpand);

        // Skip lines before the expansion zone
        while (currentLine < expandedStart)
        {
            if (!reader.ReadLine(out _))
            {
                if (!await reader.AdvanceAsync()) return;
                continue;
            }
            currentLine++;
        }

        // Read pre-expansion zone, tracking blank-line boundaries
        var preLines = new List<string>(maxExpand);
        while (currentLine < startLine)
        {
            if (!reader.ReadLine(out var line))
            {
                if (!await reader.AdvanceAsync()) break;
                continue;
            }

            if (IsBlank(line))
            {
                preLines.Clear();
            }
            else
            {
                preLines.Add(LineReader.ToString(line));
            }
            currentLine++;
        }

        foreach (var pre in preLines)
        {
            Console.WriteLine(pre);
        }

        // Output requested range
        while (currentLine <= endLine)
        {
            if (!reader.ReadLine(out var line))
            {
                if (!await reader.AdvanceAsync()) break;
                continue;
            }
            Console.WriteLine(LineReader.ToString(line));
            currentLine++;
        }

        // Post-expansion: up to 3 lines, stopping at blank line or EOF
        int expansionCount = 0;
        while (expansionCount < maxExpand && !reader.IsComplete)
        {
            if (!reader.ReadLine(out var line))
            {
                if (!await reader.AdvanceAsync()) break;
                continue;
            }
            if (IsBlank(line)) break;
            Console.WriteLine(LineReader.ToString(line));
            expansionCount++;
        }
    }

    private static bool IsBlank(ReadOnlySpan<byte> line) =>
        line.IsEmpty || line.IndexOfAnyExcept((byte)' ', (byte)'\t') < 0;

    // Parses #L10 or #L10-L20 fragments into 1-based line numbers
    private static (int Start, int End) ParseLineFragment(string fragment)
    {
        // Formats: L10, L10-L20
        if (!fragment.StartsWith('L'))
        {
            return (0, 0);
        }

        var dashIndex = fragment.IndexOf('-');
        if (dashIndex < 0)
        {
            return int.TryParse(fragment[1..], out var single) ? (single, single) : (0, 0);
        }

        var startPart = fragment[1..dashIndex];
        var endPart = fragment[(dashIndex + 1)..];
        // End part may be "L20" or just "20"
        if (endPart.StartsWith('L'))
        {
            endPart = endPart[1..];
        }

        if (int.TryParse(startPart, out var start) && int.TryParse(endPart, out var end))
        {
            return (start, end);
        }

        return (0, 0);
    }

    private static SourceListResult ToListResult(SourceListView v) => new()
    {
        Repository = v.Repository,
        Commit = v.Commit,
        PdbStatus = v.PdbStatus,
        Package = v.Package,
        Version = v.Version,
        Tfm = v.Tfm,
        Types = v.Types,
        SourceFiles = v.SourceFiles?.Select(r => new SourceFileEntry { Type = r.Type, Url = r.Url }).ToList()
            ?? v.VerifiedSourceFiles?.Select(r => new SourceFileEntry { Type = r.Type, Url = r.Url, Status = r.Status }).ToList()
    };

    private static SourceDetailResult ToDetailResult(SourceDetailView v) => new()
    {
        Type = v.Title,
        Kind = v.Kind,
        Assembly = v.Assembly,
        Package = v.Package,
        Version = v.Version,
        Source = v.Source,
        Files = v.Files,
        Repository = v.Repository,
        Commit = v.Commit,
        PdbStatus = v.PdbStatus,
        Resolution = v.Resolution,
        AdditionalSourceFiles = v.AdditionalSourceFiles?.Select(r => r.Url).ToList()
            ?? v.VerifiedSourceFiles?.Select(r => r.Url).ToList(),
        MemberDocs = v.MemberDocs?.Select(r => new MemberDocEntry { Member = r.Member, Summary = r.Summary }).ToList(),
        Samples = v.Samples?.Select(r => new SampleEntry { Type = r.Type, Name = r.Description, Url = r.Url }).ToList()
    };

    private static void WriteOutput<T>(T view, SourceOptions options) where T : class
    {
        if (options.JsonOutput)
        {
            WriteJson(view, options.CompactJson);
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

    /// <summary>
    /// Formats a type name using C#-style generic syntax for display in hints and tips.
    /// e.g., "Dictionary`2" → "Dictionary&lt;TKey, TValue&gt;" (using actual type parameter names when available).
    /// </summary>
    private static string FormatFriendlyTypeName(ApiType type)
    {
        var name = type.Name;
        return ApiOutputFormatter.FormatGenericTypeName(name, type.TypeParameters);
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
        Console.Error.WriteLine("       Use 'library <target> -S \"SourceLink Availability\"' for full source reachability.");
        Console.Error.WriteLine();
    }
}

// JSON domain models (following established pattern: domain models for JSON, views for Markout)

public class SourceListResult
{
    public string? Repository { get; init; }
    public string? Commit { get; init; }
    public string? PdbStatus { get; init; }
    public string? Package { get; init; }
    public string? Version { get; init; }
    public string? Tfm { get; init; }
    public int? Types { get; init; }
    public List<SourceFileEntry>? SourceFiles { get; init; }
}

public class SourceDetailResult
{
    public string? Type { get; init; }
    public string? Kind { get; init; }
    public string? Assembly { get; init; }
    public string? Package { get; init; }
    public string? Version { get; init; }
    public string? Source { get; init; }
    public int? Files { get; init; }
    public string? Repository { get; init; }
    public string? Commit { get; init; }
    public string? PdbStatus { get; init; }
    public string? Resolution { get; init; }
    public List<string>? AdditionalSourceFiles { get; init; }
    public List<MemberDocEntry>? MemberDocs { get; init; }
    public List<SampleEntry>? Samples { get; init; }
}

public class SourceFileEntry
{
    public string? Type { get; init; }
    public string? Url { get; init; }
    public string? Status { get; init; }
}

public class MemberDocEntry
{
    public string? Member { get; init; }
    public string? Summary { get; init; }
}

public class SampleEntry
{
    public string? Type { get; init; }
    public string? Name { get; init; }
    public string? Url { get; init; }
}

public class ILOffsetResult
{
    public string? Method { get; init; }
    public string? Token { get; init; }
    public string? ILOffset { get; init; }
    public string? MatchedOffset { get; init; }
    public string? File { get; init; }
    public int? Line { get; init; }
    public string? Url { get; init; }
}
