using CSharpText;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Metadata;
using InertText;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Commands;

/// <summary>
/// Seeded structural-clone discovery for <c>match --similar</c> (issue #4740): rank a bounded
/// candidate population by structural similarity to one seed method.
/// </summary>
/// <remarks>
/// This is a thin consumer of
/// <see cref="AssemblyContextStructuralCloneRetrievalQuery"/>. It selects exact targets, invokes
/// the query once, and presents the unmodified product result. It does not open PE images,
/// enumerate MethodDefs, reconstruct retrieval features, or reinterpret scores.
/// </remarks>
internal static class MatchDiscovery
{
    internal static async Task<int> ExecuteAsync(MatchOptions options)
    {
        string replayWorkingDirectory = Directory.GetCurrentDirectory();
        if (string.IsNullOrEmpty(options.LeftSelector))
        {
            CommandError.Write("match --similar requires a seed method selector.");
            CommandError.WriteLine(
                "Usage: dotnet-inspect match <Type.Member> [<CandidateType>] --similar --package <pkg>");
            return 1;
        }

        if (options.IncludeImplementation)
        {
            CommandError.Write(
                "--implementation cannot be combined with --similar; it is a pairwise drill-down.",
                ["", "Pick a ranked candidate first, then run:",
                 "  dotnet-inspect match <Type.Member> <Type.Candidate> --implementation"]);
            return 1;
        }

        if (options.AssemblyWide && !string.IsNullOrEmpty(options.RightSelector))
        {
            CommandError.Write(
                $"--assembly-wide searches every method, so the candidate type '{options.RightSelector}' "
                    + "cannot also be given; drop one.");
            return 1;
        }

        if (options.Top is <= 0)
        {
            CommandError.Write("--top must be greater than zero.");
            return 1;
        }

        if (options.MaximumResults is <= 0)
        {
            CommandError.Write("--max-results must be greater than zero.");
            return 1;
        }

        if (options.MaximumMethods is <= 0)
        {
            CommandError.Write("--max-methods must be greater than zero.");
            return 1;
        }

        if (options.SourceOptions is { ConfigFile: not null, ConfigDirectory: not null })
        {
            CommandError.Write(
                "--nugetconfig and --nugetconfig-directory cannot be combined.");
            return 1;
        }

        NuGetSourceOptions sourceOptions =
            options.SourceOptions ?? NuGetSourceOptions.Default;
        if (sourceOptions.ConfigFile is null)
        {
            string configDirectory;
            try
            {
                configDirectory = Path.GetFullPath(
                    sourceOptions.ConfigDirectory ?? replayWorkingDirectory,
                    replayWorkingDirectory);
            }
            catch (Exception ex) when (ex is
                ArgumentException
                or IOException
                or NotSupportedException)
            {
                CommandError.Write(
                    "--nugetconfig-directory must identify a usable directory.");
                return 1;
            }

            if (!Directory.Exists(configDirectory))
            {
                CommandError.Write(
                    $"NuGet config discovery directory not found: '{configDirectory}'.");
                return 1;
            }

            options = options with
            {
                SourceOptions = sourceOptions with
                {
                    ConfigDirectory = configDirectory,
                },
            };
        }

        if (options.PackagePath is not null)
        {
            if (!TryGetReplaySources(
                    options.SourceOptions,
                    out _,
                    out string? replaySourceError,
                    workingDirectory: replayWorkingDirectory))
            {
                CommandError.Write(replaySourceError!);
                return 1;
            }
        }

        LoadedSide? seed = null;
        try
        {
            string? workingDirectoryDependentPackagesRoot =
                GetWorkingDirectoryDependentPackagesRoot(replayWorkingDirectory);
            (seed, int? seedError) = await LoadSideAsync(
                options,
                options.AssemblyPath,
                replayWorkingDirectory);
            if (seedError.HasValue)
                return seedError.Value;

            return await ExecuteAsync(
                options,
                seed!,
                seed!,
                replayWorkingDirectory,
                workingDirectoryDependentPackagesRoot);
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        finally
        {
            seed?.Dispose();
        }
    }

    static async Task<int> ExecuteAsync(
        MatchOptions options,
        LoadedSide seed,
        LoadedSide candidate,
        string replayWorkingDirectory,
        string? workingDirectoryDependentPackagesRoot)
    {
        var resolvedSeed = ResolveSeed(seed, options.LeftSelector!);
        if (resolvedSeed.Error is not null)
        {
            CommandError.Write(resolvedSeed.Error);
            return 1;
        }

        var (population, scopeDisplay, populationImage, scopeError) =
            ResolvePopulation(options, seed, candidate, resolvedSeed);
        if (scopeError is not null)
        {
            CommandError.Write(scopeError);
            return 1;
        }

        var limits = new StructuralCloneRetrievalLimits(
            MaximumMethods: options.MaximumMethods ?? 50_000,
            MaximumResults: options.MaximumResults ?? 100);

        // The population's defining image, not the image the caller named: a facade resolves a
        // forwarded type without defining it, and retrieval reads TypeDefs. Both sides are
        // canonicalized before comparison, because the caller's own --library spelling may be
        // relative while the seed origin is absolute.
        //
        // With one --library there is no second spelling of the caller's own path to reconcile,
        // so these differ only when type forwarding resolves the seed or population to the
        // assembly that defines it.
        string seedImage = resolvedSeed.OriginAssemblyPath!;
        string callerImage = MatchCommand.CanonicalImagePath(candidate.ApiDllPath);
        string candidateImage = populationImage is null
            ? callerImage
            : MatchCommand.CanonicalImagePath(populationImage);
        // Discovery ranks rows of one image against a seed in that same image. When forwarding
        // resolves the seed and the population to different assemblies, nothing downstream can
        // repair it: the ranked tokens address the candidate image, the seed does not exist there,
        // and so the pairwise confirmation the disclosure points at cannot be run. The design has
        // always declared this unsupported; enforce it here rather than leaving every later
        // surface to defend an invariant one gate can hold.
        if (!MatchCommand.SameImage(seedImage, candidateImage))
        {
            CommandError.Write(
                $"Seed '{resolvedSeed.Display}' is defined in {Path.GetFileName(seedImage)}, but "
                    + $"candidate type '{scopeDisplay}' is defined in "
                    + $"{Path.GetFileName(candidateImage)}. Discovery ranks candidates within a "
                    + "single image, because a MethodDef token addresses a row in exactly one "
                    + "image and the seed must be present to confirm a rank pairwise. Name a "
                    + "candidate type defined in "
                    + $"{Path.GetFileName(seedImage)}, or pass --assembly-wide to search it.");
            return 1;
        }

        // Whether the ranked tokens index the image the caller actually named. That, not the
        // seed-to-candidate relation, is what decides if the run has to name an assembly: a
        // forwarded seed and its population can agree with each other and still both sit in an
        // image the caller never typed, and a token addresses a row only in the image owning it.
        bool tokensIndexCallerImage = MatchCommand.SameImage(candidateImage, callerImage);

        // Names are projected from the surface of the image the tokens come from. When the
        // population lives in a forwarded-to assembly, the caller's surface describes the facade,
        // so extract the defining image's surface instead of mislabelling its tokens.
        LoadedSide? populationSide = null;
        if (!tokensIndexCallerImage)
        {
            (populationSide, int? populationError) =
                await LoadSideAsync(
                    ForPhysicalImageLoad(options),
                    candidateImage,
                    replayWorkingDirectory);
            if (populationError.HasValue)
                return populationError.Value;
        }

        try
        {
            // Built from the surface of the image the group opens, never the caller's facade: a
            // MethodDef token is a table row and names nothing outside its own image.
            ApiSurface namesSurface = populationSide?.Api ?? candidate.Api;

            // Extraction and cache paths are implementation details, not stable CLI addresses.
            // Preserve the exact package, asset, source authorization, and TFM needed to select
            // this image again. A source URL that the diagnostic policy would alter cannot be
            // embedded in an executable disclosure; direct the caller to a config-backed source
            // before doing the expensive retrieval instead of printing a command that cannot
            // replay or leaking a credential-bearing value.
            ReplayableCandidateAddress candidateAddress =
                GetReplayableCandidateAddress(
                    seed.ReplayPackage,
                    seed.PackageExtractPath,
                    candidateImage,
                    resolvedSeed.OriginProvenance,
                    workingDirectoryDependentPackageRoot:
                        workingDirectoryDependentPackagesRoot);
            bool disclosePackageReplay =
                !tokensIndexCallerImage || seed.ReplayPackage is not null;
            if (!TryValidateReplayAddress(
                    candidateAddress,
                    out string? replayAddressError))
            {
                CommandError.Write(replayAddressError!);
                return 1;
            }

            MatchDiscoveryReplaySources? replaySources = null;
            bool selectedVersionSourceRestriction =
                seed.ReplayPackage is not null
                && candidateAddress.Package is not null
                && seed.ReplayPackage.Equals(
                    candidateAddress.Package,
                    StringComparison.OrdinalIgnoreCase)
                && seed.PackageReplaySourceUrls is not null
                && !seed.PackageReplayUsesOriginalSources;
            NuGetSourceOptions? replaySourceOptions =
                selectedVersionSourceRestriction
                    ? ReplaySourceOptions(
                        options.SourceOptions,
                        seed.PackageReplaySourceUrls!)
                    : options.SourceOptions;
            if (candidateAddress.Package is not null
                && !TryGetReplaySources(
                    replaySourceOptions,
                    out replaySources,
                    out string? replaySourceError,
                    selectedVersionSourceRestriction,
                    replayWorkingDirectory))
            {
                CommandError.Write(replaySourceError!);
                return 1;
            }

            using var workspace = new InspectionWorkspace();

            // One image, guaranteed by the gate above, so one group serves both sides.
            string? rootPackageDirectory =
                seed.PackageExtractPath is not null
                && TryGetRelativeAsset(
                    seed.PackageExtractPath,
                    seedImage,
                    out _)
                    ? seed.PackageExtractPath
                    : null;
            using AssemblyContextGroup group = CreateGroup(
                workspace,
                seedImage,
                rootPackageDirectory,
                candidateAddress.Tfm,
                options.SourceOptions,
                usePackageSourcePolicy:
                    candidateAddress.Package is not null);

            var input = new AssemblyContextStructuralCloneRetrievalInput(
                group,
                group.Participants[0],
                group,
                group.Participants[0],
                new StructuralCloneQuerySeed.MethodDefinitionToken(resolvedSeed.Token!.Value),
                population!,
                limits);

            AssemblyContextStructuralCloneRetrievalResult result =
                AssemblyContextStructuralCloneRetrievalQuery.Execute(input);

            var view = MatchDiscoveryFormatter.BuildView(
                new MatchDiscoveryRequest(
                    resolvedSeed.Display!,
                    scopeDisplay!,
                    tokensIndexCallerImage ? null : candidateAddress.Library,
                    limits,
                    options.Top,
                    disclosePackageReplay ? candidateAddress.Package : null,
                    disclosePackageReplay ? candidateAddress.Tfm : null,
                    candidateAddress.Library,
                    replaySources,
                    IncludeAll: options.IncludeAll),
                result,
                MatchDiscoveryNames.Build(namesSurface, candidateImage));

            if (options.JsonOutput)
            {
                JsonOutputHelper.Write(
                    view.Document,
                    MatchDiscoveryDocumentJsonContext.Default.MatchDiscoveryDocument,
                    MatchDiscoveryDocumentCompactJsonContext.Default.MatchDiscoveryDocument,
                    options.CompactJson);
            }
            else if (options.Tabular)
            {
                // Table, TSV, and JSONL require exactly one table shape and carry no prose, so
                // this renders only the ranked candidates. Everything the full view would add --
                // the disclosure, which is not optional, plus the identity, receipt, and any
                // blockers -- goes to stderr, which reaches the reader without adding a second row
                // schema to the parsed stream on stdout. The disclosure is read off the view so
                // every rendering carries the one the run actually earned, rather than letting
                // this path keep its own copy that a cross-image run would make false.
                CommandError.WriteNote(view.View.Description!);
                foreach (string line in MatchDiscoveryFormatter.TabularContext(view.View))
                    CommandError.WriteNote(line);

                // `match` does not carry the section-projection options (--select, --columns,
                // --fields, --schema, --tree travel as one bundle), so no projection is passed
                // rather than implying a filter the command cannot accept.
                OutputFormatter.WriteProjectedTable(
                    Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                    columns: null, fields: null,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(
                            MatchDiscoveryFormatter.CandidateTable(view.View),
                            writer, formatter, SearchViewContext.Default, writerOptions),
                    options.Rows);
            }
            else
            {
                OutputFormatter.WriteWindowedMarkdown(Console.Out, options.Rows,
                    opts => MarkoutSerializer.Serialize(view.View, SearchViewContext.Default, opts));
            }

            // A retrieval that could not run is a failure, not an empty result set. Only
            // Completed ranked the population; Unsupported and LimitReached are terminal
            // non-completions that carry blockers, so they must not report success.
            return view.Document.Disposition
                == nameof(StructuralCloneRetrievalDisposition.Completed)
                ? 0
                : 1;
        }
        finally
        {
            populationSide?.Dispose();
        }
    }


    /// <summary>
    /// Chooses an address for the candidate image that the caller can still use after this command
    /// exits.
    /// <para>
    /// Package extraction and cache paths are implementation details rather than caller-owned
    /// addresses. When the candidate image came from a package, the replayable address retains the
    /// exact package version, package-relative asset path, and target framework. Every other
    /// candidate image is a path the caller supplied and is disclosed unchanged.
    /// </para>
    /// </summary>
    internal static ReplayableCandidateAddress GetReplayableCandidateAddress(
        string? packagePath,
        string? packageExtractPath,
        string candidateImage,
        AssemblyResolutionProvenance? candidateProvenance = null,
        IReadOnlyList<string>? packageRoots = null,
        string? workingDirectoryDependentPackageRoot = null)
    {
        if (packagePath is not null
            && packageExtractPath is not null
            && TryGetRelativeAsset(packageExtractPath, candidateImage, out string? packageAsset))
        {
            return PackageCandidateAddress(
                packagePath,
                packageAsset,
                IsWithinRoot(
                    workingDirectoryDependentPackageRoot,
                    candidateImage));
        }

        if (candidateProvenance
                is not AssemblyResolutionProvenance.PackageAsset package)
        {
            return new(null, candidateImage, null);
        }

        if (NuGetCache.TryGetPackageContentIdentity(
                candidateImage,
                out _,
                out _,
                out string appCacheAsset,
                out _))
        {
            return PackageCandidateAddress(
                $"{package.PackageId}@{package.PackageVersion}",
                appCacheAsset);
        }

        IEnumerable<string> roots = packageRoots ?? NuGetCache.GetNuGetPackageRoots();
        foreach (string packagesRoot in roots.OrderByDescending(
            root => Path.GetFullPath(root).Length))
        {
            if (TryGetRelativeAsset(packagesRoot, candidateImage, out string? cacheRelative))
            {
                string[] segments = cacheRelative.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 3)
                {
                    if (!segments[0].Equals(
                            package.PackageId,
                            StringComparison.OrdinalIgnoreCase)
                        || !segments[1].Equals(
                            package.PackageVersion,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string dependencyPackage =
                        $"{package.PackageId}@{package.PackageVersion}";
                    string dependencyAsset = string.Join('/', segments[2..]);
                    return PackageCandidateAddress(
                        dependencyPackage,
                        dependencyAsset,
                        workingDirectoryDependentPackageRoot is not null
                            && SamePath(
                                packagesRoot,
                                workingDirectoryDependentPackageRoot));
                }
            }
        }

        return new(null, candidateImage, null);
    }

    static ReplayableCandidateAddress PackageCandidateAddress(
        string package,
        string packageRelativePath,
        bool workingDirectoryDependentPackageRoot = false)
        => new(
            package,
            packageRelativePath,
            TfmResolver.ExtractTfmFromPath(packageRelativePath),
            workingDirectoryDependentPackageRoot
                ? "match --similar cannot disclose a working-directory-independent "
                    + "package command because the selected image came from a relative "
                    + "NUGET_PACKAGES root. Set NUGET_PACKAGES to an absolute directory "
                    + "and rerun discovery."
                : null);

    static string? GetWorkingDirectoryDependentPackagesRoot(
        string workingDirectory)
    {
        string? value = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            return null;

        string root = Path.GetFullPath(value, workingDirectory);
        string home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)
            && SamePath(root, Path.Combine(home, ".nuget", "packages")))
        {
            return null;
        }

        return root;
    }

    static bool IsWithinRoot(string? root, string candidateImage) =>
        root is not null
        && TryGetRelativeAsset(root, candidateImage, out _);

    static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    static bool TryGetRelativeAsset(
        string root,
        string candidateImage,
        out string relativeAsset)
    {
        string relativePath = Path.GetRelativePath(
            Path.GetFullPath(root),
            Path.GetFullPath(candidateImage));
        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals(".", StringComparison.Ordinal)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            relativeAsset = "";
            return false;
        }

        relativeAsset = relativePath.Replace('\\', '/');
        return true;
    }

    internal static string? GetReplayablePackage(
        string? resolvedPackagePath,
        string? packageName,
        string? packageVersion,
        string? workingDirectory = null)
    {
        if (resolvedPackagePath is null)
            return null;

        workingDirectory ??= Directory.GetCurrentDirectory();
        if (resolvedPackagePath.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase))
        {
            string localPackagePath = Path.GetFullPath(
                resolvedPackagePath,
                workingDirectory);
            if (File.Exists(localPackagePath))
                return localPackagePath;
        }

        if (packageName is null || packageVersion is null)
        {
            return resolvedPackagePath;
        }

        return $"{packageName}@{packageVersion}";
    }

    internal static bool TryGetReplaySources(
        NuGetSourceOptions? sourceOptions,
        out MatchDiscoveryReplaySources? replaySources,
        out string? error,
        bool selectedVersionSourceRestriction = false,
        string? workingDirectory = null)
    {
        if (sourceOptions is null
            || sourceOptions.Sources.Length == 0
                && sourceOptions.AdditionalSources.Length == 0
                && sourceOptions.ConfigFile is null
                && sourceOptions.ConfigDirectory is null)
        {
            replaySources = null;
            error = null;
            return true;
        }

        workingDirectory ??= Directory.GetCurrentDirectory();
        List<string> replaySourcesValues = [];
        List<string> replayAdditionalSourcesValues = [];
        foreach ((string option, string[] values, List<string> replayValues) in new[]
        {
            ("--source", sourceOptions.Sources, replaySourcesValues),
            ("--add-source", sourceOptions.AdditionalSources, replayAdditionalSourcesValues),
        })
        {
            foreach (string value in values)
            {
                string replayValue;
                try
                {
                    replayValue = LocalPackageSourceIdentity.IsLocalSource(value)
                        ? LocalPackageSourceIdentity.Create(
                            value,
                            workingDirectory).CanonicalPath
                        : value;
                }
                catch (Exception ex) when (ex is
                    ArgumentException
                    or IOException
                    or NotSupportedException)
                {
                    replaySources = null;
                    error =
                        "match --similar cannot disclose a replayable package command because "
                            + $"{option} contains a local package source path that cannot be "
                            + "resolved.";
                    return false;
                }

                if (!CanDiscloseSource(replayValue))
                {
                    replaySources = null;
                    error = selectedVersionSourceRestriction
                        ? "match --similar cannot disclose a replayable package command because "
                            + "the source that reported the selected package version contains URL "
                            + "components that must be redacted. Exact replay requires package "
                            + "source mapping that selects that producer through --nugetconfig "
                            + "without printing its URL."
                        : $"match --similar cannot disclose a replayable package command because "
                            + $"{option} contains URL components that must be redacted. Configure "
                            + "that source in a nuget.config file and pass --nugetconfig instead.";
                    return false;
                }

                if (ContainsMarkdownCodeSpanDelimiter(replayValue))
                {
                    replaySources = null;
                    error =
                        "match --similar cannot disclose a replayable package command because "
                            + $"{option} contains text that cannot be emitted losslessly.";
                    return false;
                }

                replayValues.Add(replayValue);
            }
        }

        string? configFile = sourceOptions.ConfigFile is null
            ? null
            : Path.GetFullPath(sourceOptions.ConfigFile, workingDirectory);
        if (configFile is not null
            && (!InertString.IsPermitted(TextPolicy.Field, configFile)
                || ContainsMarkdownCodeSpanDelimiter(configFile)))
        {
            replaySources = null;
            error =
                "match --similar cannot disclose a replayable package command because "
                    + "--nugetconfig contains text that cannot be emitted losslessly. Rename the "
                    + "config path before using it for package-backed discovery.";
            return false;
        }

        string? configDirectory = sourceOptions.ConfigDirectory is null
            ? null
            : Path.GetFullPath(sourceOptions.ConfigDirectory, workingDirectory);
        if (configDirectory is not null
            && (!InertString.IsPermitted(TextPolicy.Field, configDirectory)
                || ContainsMarkdownCodeSpanDelimiter(configDirectory)))
        {
            replaySources = null;
            error =
                "match --similar cannot disclose a replayable package command because "
                    + "--nugetconfig-directory contains text that cannot be emitted losslessly. "
                    + "Use a different config discovery directory for package-backed discovery.";
            return false;
        }

        replaySources = new MatchDiscoveryReplaySources(
            [.. replaySourcesValues],
            [.. replayAdditionalSourcesValues],
            configFile,
            configDirectory);
        error = null;
        return true;
    }

    internal static NuGetSourceOptions? ReplaySourceOptions(
        NuGetSourceOptions? original,
        IReadOnlyList<string> reportingSourceUrls)
    {
        ArgumentNullException.ThrowIfNull(reportingSourceUrls);
        if (reportingSourceUrls.Count == 0)
            return original;

        return new NuGetSourceOptions
        {
            Sources = [.. reportingSourceUrls],
            ConfigFile = original?.ConfigFile,
            ConfigDirectory = original?.ConfigDirectory,
        };
    }

    internal static bool TryValidateReplayAddress(
        ReplayableCandidateAddress address,
        out string? error)
    {
        if (address.Error is not null)
        {
            error = address.Error;
            return false;
        }

        foreach ((string field, string? value) in new[]
        {
            ("package coordinate", address.Package),
            ("library selector", address.Library),
            ("target framework", address.Tfm),
        })
        {
            if (value is not null
                && (ContainsMarkdownCodeSpanDelimiter(value)
                    || !string.Equals(
                        CSharpIdentifier.ContainRenderedText(value),
                        value,
                        StringComparison.Ordinal)))
            {
                error =
                    "match --similar cannot disclose a replayable pairwise command because "
                        + $"the exact {field} contains text that cannot be emitted losslessly.";
                return false;
            }
        }

        error = null;
        return true;
    }

    static bool ContainsMarkdownCodeSpanDelimiter(string value)
        => value.Contains('`');

    static bool CanDiscloseSource(string value)
    {
        string baseline = value;
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            // UrlRedaction and Uri may normalize harmless spelling differences such as host
            // casing, a default port, or an omitted trailing slash. Compare two normalized
            // spellings so only removed or encoded components make the source non-replayable.
            baseline = uri.ToString();
        }

        return string.Equals(
            UrlRedaction.ForDiagnostics(value).ToString(),
            baseline,
            StringComparison.Ordinal);
    }

    internal static MatchOptions ForPhysicalImageLoad(MatchOptions options)
        => options with
        {
            PackagePath = null,
            PackageRangeAddress = null,
        };

    /// <summary>
    /// Resolves the seed to a MethodDef token. A raw <c>0x06......</c> token passes through; any
    /// other spelling resolves through the same <c>Type.Member</c> path pairwise match uses, so
    /// ambiguous overloads report the existing "narrow the pattern" error rather than silently
    /// seeding the wrong body.
    /// </summary>
    static ResolvedSeed ResolveSeed(LoadedSide seed, string selector)
    {
        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(seed.Api, seed.ApiDllPath, selector);
        if (resolved.Error is not null)
            return new ResolvedSeed(
                null,
                null,
                null,
                null,
                null,
                resolved.Error);

        // The declaring type comes from the resolution that already happened. Re-deriving it here
        // meant a second token scan that could disagree with the first, and for a raw token that
        // disagreement bound the seed to a foreign type in another image and scoped the whole run
        // to it, reporting Completed.
        return new ResolvedSeed(
            resolved.Token,
            resolved.Display,
            resolved.OriginAssemblyPath,
            resolved.DeclaringType,
            resolved.DeclaringType is null
                ? null
                : seed.TryGetSourceAssembly(resolved.DeclaringType)
                    ?.Provenance,
            null);
    }

    /// <summary>
    /// Resolves the candidate population, and with it the image the population actually lives in.
    /// A type-forwarding facade resolves the type but does not define it, so the group must be
    /// opened on the defining assembly or retrieval reports CandidateTypeNotFound.
    /// </summary>
    static (StructuralCloneQueryPopulation? Population, string? Display, string? Image, string? Error)
        ResolvePopulation(
            MatchOptions options,
            LoadedSide seed,
            LoadedSide candidate,
            ResolvedSeed resolvedSeed)
    {
        if (options.AssemblyWide)
        {
            // Whole-assembly means the assembly the seed actually lives in. Leaving the image null
            // made the caller's own --library the population, so a seed reached through a type
            // forwarder searched the facade, found no MethodDefs, and completed with an empty
            // ranking at exit 0 — a widening flag returning strictly less than the narrower
            // default it widens.
            return (
                new StructuralCloneQueryPopulation.WholeAssembly(),
                "whole assembly",
                resolvedSeed.OriginAssemblyPath,
                null);
        }

        if (!string.IsNullOrEmpty(options.RightSelector))
        {
            ApiTypeLookupResult lookup =
                ApiTypeLookupService.LookupType(candidate.Api, options.RightSelector);
            if (!lookup.Found)
            {
                return (null, null, null,
                    $"Candidate type '{options.RightSelector}' not found in "
                        + $"{Path.GetFileName(candidate.ApiDllPath)}.");
            }

            if (lookup.ImpliedMember is { } impliedMember)
            {
                // The selector named Type.Member, but the population is a type. Silently widening
                // to the declaring type turns a typo into a completed run over a scope the caller
                // never asked for.
                return (null, null, null,
                    $"Candidate '{options.RightSelector}' names member '{impliedMember}', but a "
                        + "candidate is a type scope. Pass the type alone to rank its members.");
            }

            if (lookup.Type!.DefinitionName is null)
            {
                return (null, null, null,
                    $"Candidate type '{lookup.Type.FullName}' carries no exact metadata definition name.");
            }

            return (
                new StructuralCloneQueryPopulation.Type(lookup.Type.DefinitionName),
                lookup.Type.FullName,
                lookup.Type.SourceAssemblyPath,
                null);
        }

        // Type-scoped retrieval is the normal bounded path, so the seed's declaring type is the
        // default scope. Cross-image discovery resolves that same type name in the candidate
        // image, which is the A-vs-A' shape (the type survived a version bump).
        ApiType? declaring = resolvedSeed.DeclaringType;
        if (declaring is null)
        {
            return (null, null, null,
                "The seed's declaring type could not be determined; name a candidate type "
                    + "explicitly or pass --assembly-wide.");
        }

        if (!ReferenceEquals(seed, candidate))
        {
            ApiTypeLookupResult lookup =
                ApiTypeLookupService.LookupType(candidate.Api, declaring.FullName);
            if (!lookup.Found)
            {
                return (null, null, null,
                    $"The seed's declaring type '{declaring.FullName}' is not present in "
                        + $"{Path.GetFileName(candidate.ApiDllPath)}; name a candidate type "
                        + "explicitly or pass --assembly-wide.");
            }

            declaring = lookup.Type!;
        }

        if (declaring.DefinitionName is null)
        {
            return (null, null, null,
                $"Type '{declaring.FullName}' carries no exact metadata definition name; "
                    + "name a candidate type explicitly or pass --assembly-wide.");
        }

        return (
            new StructuralCloneQueryPopulation.Type(declaring.DefinitionName),
            declaring.FullName,
            declaring.SourceAssemblyPath,
            null);
    }

    static AssemblyContextGroup CreateGroup(
        InspectionWorkspace workspace,
        string path,
        string? rootPackageDirectory,
        string? targetFramework,
        NuGetSourceOptions? sourceOptions,
        bool usePackageSourcePolicy)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Local("match --similar"));
        var policy = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                RootPackageDirectory = rootPackageDirectory,
                TargetFramework = targetFramework,
                PackageSourceOptions = sourceOptions,
                UsePackageSourcePolicy = usePackageSourcePolicy,
            });
        return workspace.CreateAssemblyContextGroup(
            [new AssemblyContextParticipant(assembly, policy)]);
    }

    static async Task<(LoadedSide? Side, int? Error)> LoadSideAsync(
        MatchOptions options,
        string? libraryPath,
        string replayWorkingDirectory)
    {
        MatchOptions sideOptions = options with
        {
            TypeName = null,
            AssemblyPath = libraryPath,
        };

        var (source, sourceError) = await ApiSourceResolver.ResolveAsync(sideOptions);
        if (sourceError.HasValue)
            return (null, sourceError.Value);

        ApiServices.LoadedApiSurface? loaded = ApiServices.LoadFullApi(
            source.SearchPath, source.RuntimeAssemblyPath, sideOptions.PackagePath,
            source.PackageName, source.ApiSource, source.ApiVersion, source.SelectedTfm,
            source.Context.Logger, sideOptions, source.PackageExtractPath,
            usePackageSourcePolicy: true);
        if (loaded is null)
        {
            CommandError.Write("Could not extract API from library.");
            TryDeleteTempDir(source.TempDir);
            return (null, 1);
        }

        return (new LoadedSide(
            loaded,
            source.TempDir,
            GetReplayablePackage(
                source.ResolvedPackagePath,
                source.PackageName,
                source.PackageVersion,
                replayWorkingDirectory),
            source.PackageExtractPath,
            source.PackageReplaySourceUrls,
            source.PackageReplayUsesOriginalSources), null);
    }

    internal static bool TryParseMethodToken(string selector, out int token)
    {
        token = 0;
        if (!selector.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(
                selector.AsSpan(2),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsed))
        {
            return false;
        }

        // 0x06 is the MethodDef table; the query rejects any other table itself, but reporting it
        // here keeps the CLI's own selector grammar honest.
        if ((parsed & unchecked((int)0xFF000000)) != 0x06000000)
            return false;

        token = parsed;
        return true;
    }

    internal static IEnumerable<int> MemberTokens(ApiMember member)
    {
        if (member.MetadataToken.HasValue)
            yield return member.MetadataToken.Value;
        if (member.GetterToken.HasValue)
            yield return member.GetterToken.Value;
        if (member.SetterToken.HasValue)
            yield return member.SetterToken.Value;
        if (member.AdderToken.HasValue)
            yield return member.AdderToken.Value;
        if (member.RemoverToken.HasValue)
            yield return member.RemoverToken.Value;
    }

    static void TryDeleteTempDir(string? tempDir)
    {
        if (tempDir is null)
            return;

        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    readonly record struct ResolvedSeed(
        int? Token,
        string? Display,
        string? OriginAssemblyPath,
        ApiType? DeclaringType,
        AssemblyResolutionProvenance? OriginProvenance,
        string? Error);

    internal readonly record struct ReplayableCandidateAddress(
        string? Package,
        string Library,
        string? Tfm,
        string? Error = null);

    sealed class LoadedSide(
        ApiServices.LoadedApiSurface surface,
        string? tempDir,
        string? replayPackage,
        string? packageExtractPath,
        IReadOnlyList<string>? packageReplaySourceUrls,
        bool packageReplayUsesOriginalSources) : IDisposable
    {
        internal ApiSurface Api => surface.Api;
        internal string ApiDllPath => surface.ApiDllPath;
        internal ResolvedAssemblyReference? TryGetSourceAssembly(
            ApiType type) =>
            surface.TryGetSourceAssembly(type);

        /// <summary>
        /// The temporary package directory, when one must be cleaned up after the command.
        /// </summary>
        internal string? TempDir { get; } = tempDir;
        internal string? ReplayPackage { get; } = replayPackage;
        internal string? PackageExtractPath { get; } = packageExtractPath;
        internal IReadOnlyList<string>? PackageReplaySourceUrls { get; } =
            packageReplaySourceUrls;
        internal bool PackageReplayUsesOriginalSources { get; } =
            packageReplayUsesOriginalSources;

        public void Dispose() => TryDeleteTempDir(TempDir);
    }
}
