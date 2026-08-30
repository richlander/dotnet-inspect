using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Markout;

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

        // Cross-image discovery reuses the repository's established A-vs-B convention: a range in
        // the source flag (diff spells it --library old.dll..new.dll). Without a range the
        // candidate assembly is the seed assembly, which is the A-vs-A default.
        var (seedLibrary, candidateLibrary, rangeError) =
            ParseLibraryRange(options.AssemblyPath);
        if (rangeError is not null)
        {
            CommandError.Write(rangeError);
            return 1;
        }

        // Same distinction as the library range: "../packages/x.nupkg" is a path, not a range.
        if (options.PackagePath is string package && FindRangeSeparator(package) != -1)
        {
            CommandError.Write(
                "--similar does not accept a package version range; cross-image discovery uses a "
                    + "library range.",
                ["", "  dotnet-inspect match <Type.Member> --similar --library old/Foo.dll..new/Foo.dll"]);
            return 1;
        }

        LoadedSide? seed = null;
        LoadedSide? candidate = null;
        try
        {
            (seed, int? seedError) = await LoadSideAsync(options, seedLibrary);
            if (seedError.HasValue)
                return seedError.Value;

            if (candidateLibrary is null)
            {
                candidate = seed;
            }
            else
            {
                (candidate, int? candidateError) =
                    await LoadSideAsync(options, candidateLibrary);
                if (candidateError.HasValue)
                    return candidateError.Value;
            }

            return await ExecuteAsync(options, seed!, candidate!);
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        finally
        {
            candidate?.Dispose();
            if (!ReferenceEquals(candidate, seed))
                seed?.Dispose();
        }
    }

    static async Task<int> ExecuteAsync(
        MatchOptions options,
        LoadedSide seed,
        LoadedSide candidate)
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
        // forwarded type without defining it, and retrieval reads TypeDefs.
        string seedImage = resolvedSeed.OriginAssemblyPath!;
        string candidateImage = populationImage ?? candidate.ApiDllPath;
        bool sameImage = string.Equals(seedImage, candidateImage, StringComparison.Ordinal);

        // Names are projected from the surface of the image the tokens come from. When the
        // population lives in a forwarded-to assembly, the caller's surface describes the facade,
        // so extract the defining image's surface instead of mislabelling its tokens.
        LoadedSide? populationSide = null;
        if (!string.Equals(candidateImage, candidate.ApiDllPath, StringComparison.Ordinal))
        {
            (populationSide, int? populationError) =
                await LoadSideAsync(options with { PackagePath = null }, candidateImage);
            if (populationError.HasValue)
                return populationError.Value;
        }

        try
        {
            // Built from the surface of the image the group opens, never the caller's facade: a
            // MethodDef token is a table row and names nothing outside its own image.
            ApiSurface namesSurface = populationSide?.Api ?? candidate.Api;

            using var workspace = new InspectionWorkspace();
            using AssemblyContextGroup seedGroup = CreateGroup(workspace, seedImage);
            using AssemblyContextGroup? candidateGroup =
                sameImage ? null : CreateGroup(workspace, candidateImage);
            AssemblyContextGroup effectiveCandidateGroup =
                candidateGroup ?? seedGroup;

            var input = new AssemblyContextStructuralCloneRetrievalInput(
                seedGroup,
                seedGroup.Participants[0],
                effectiveCandidateGroup,
                effectiveCandidateGroup.Participants[0],
                new StructuralCloneQuerySeed.MethodDefinitionToken(resolvedSeed.Token!.Value),
                population!,
                limits);

            AssemblyContextStructuralCloneRetrievalResult result =
                AssemblyContextStructuralCloneRetrievalQuery.Execute(input);

            var view = MatchDiscoveryFormatter.BuildView(
                new MatchDiscoveryRequest(
                    resolvedSeed.Display!,
                    scopeDisplay!,
                    sameImage ? null : candidateImage,
                    limits,
                    options.Top),
                result,
                MatchDiscoveryNames.Build(namesSurface));

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
                // Table, TSV, and JSONL carry rows but no prose, so the Markout description paragraph
                // is dropped. The disclosure is not optional, so it goes to stderr, which reaches the
                // reader without corrupting the parsed stream on stdout.
                CommandError.WriteNote(MatchDiscoveryFormatter.Disclosure);

                OutputFormatter.WriteProjectedTable(
                    Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                    options.Columns, options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(
                            view.View, writer, formatter, SearchViewContext.Default, writerOptions),
                    options.Rows);
            }
            else
            {
                OutputFormatter.WriteWindowedMarkdown(Console.Out, options.Rows,
                    opts => MarkoutSerializer.Serialize(view.View, SearchViewContext.Default, opts));
            }

            // A retrieval that could not run is a failure, not an empty result set.
            return view.Document.Disposition is
                nameof(StructuralCloneRetrievalDisposition.Failed)
                or MatchDiscoveryFormatter.RejectedDisposition
                or MatchDiscoveryFormatter.UnresolvedDisposition
                ? 1
                : 0;
        }
        finally
        {
            populationSide?.Dispose();
        }
    }


    /// <summary>
    /// Resolves the seed to a MethodDef token. A raw <c>0x06......</c> token passes through; any
    /// other spelling resolves through the same <c>Type.Member</c> path pairwise match uses, so
    /// ambiguous overloads report the existing "narrow the pattern" error rather than silently
    /// seeding the wrong body.
    /// </summary>
    static ResolvedSeed ResolveSeed(LoadedSide seed, string selector)
    {
        if (TryParseMethodToken(selector, out int token))
        {
            ApiType? declaring = seed.Api.Types.FirstOrDefault(
                type => type.Members.Any(member => MemberTokens(member).Contains(token)));
            return new ResolvedSeed(
                token,
                declaring is null
                    ? $"MethodDef 0x{token:X8}"
                    : $"{declaring.FullName} MethodDef 0x{token:X8}",
                seed.ApiDllPath,
                declaring,
                null);
        }

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(seed.Api, seed.ApiDllPath, selector);
        if (resolved.Error is not null)
            return new ResolvedSeed(null, null, null, null, resolved.Error);

        ApiTypeLookupResult lookup = ApiTypeLookupService.LookupType(seed.Api, selector);
        return new ResolvedSeed(
            resolved.Token,
            resolved.Display,
            resolved.OriginAssemblyPath,
            lookup.Type,
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
            return (
                new StructuralCloneQueryPopulation.WholeAssembly(),
                "whole assembly",
                null,
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

    static AssemblyContextGroup CreateGroup(InspectionWorkspace workspace, string path)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Local("match --similar"));
        var policy = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        return workspace.CreateAssemblyContextGroup(
            [new AssemblyContextParticipant(assembly, policy)]);
    }

    static async Task<(LoadedSide? Side, int? Error)> LoadSideAsync(
        MatchOptions options,
        string? libraryPath)
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
            source.Context.Logger, sideOptions);
        if (loaded is null)
        {
            CommandError.Write("Could not extract API from library.");
            TryDeleteTempDir(source.TempDir);
            return (null, 1);
        }

        return (new LoadedSide(loaded.Api, loaded.ApiDllPath, source.TempDir), null);
    }

    /// <summary>
    /// Finds the range separator in a source argument, ignoring every <c>..</c> that is a
    /// parent-directory path segment.
    /// </summary>
    /// <remarks>
    /// The two meanings of <c>..</c> are distinguishable by position, not by content. A parent
    /// segment is always bounded by directory separators or by the ends of the argument
    /// (<c>../a.dll</c>, <c>a/../b.dll</c>, <c>a/..</c>); a range separator never is, because a
    /// range joins two file names. Splitting on the first <c>..</c> instead rejects
    /// <c>--library ../a.dll</c>, which pairwise <c>match</c> accepts.
    /// Returns -1 when the argument carries no range, and -2 when it carries more than one, which
    /// is ambiguous rather than a silent left-most win.
    /// </remarks>
    internal static int FindRangeSeparator(string value)
    {
        int found = -1;

        for (int index = value.IndexOf("..", StringComparison.Ordinal);
            index >= 0;
            index = value.IndexOf("..", index + 2, StringComparison.Ordinal))
        {
            bool openBounded = index == 0 || IsDirectorySeparator(value[index - 1]);
            bool closeBounded = index + 2 == value.Length || IsDirectorySeparator(value[index + 2]);
            if (openBounded && closeBounded)
                continue;

            if (found >= 0)
                return -2;

            found = index;
        }

        return found;
    }

    static bool IsDirectorySeparator(char value) => value is '/' or '\\';

    static (string? Seed, string? Candidate, string? Error) ParseLibraryRange(string? library)
    {
        if (string.IsNullOrEmpty(library))
            return (null, null, null);

        int separator = FindRangeSeparator(library);
        if (separator == -1)
            return (library, null, null);

        if (separator == -2)
        {
            return (null, null,
                "Ambiguous library range: more than one '..' separator. "
                    + "Use format: old/Foo.dll..new/Foo.dll");
        }

        if (separator == 0 || separator + 2 >= library.Length)
        {
            return (null, null,
                "Invalid library range. Use format: old/Foo.dll..new/Foo.dll");
        }

        return (library[..separator], library[(separator + 2)..], null);
    }

    static bool TryParseMethodToken(string selector, out int token)
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
        string? Error);

    sealed class LoadedSide(ApiSurface api, string apiDllPath, string? tempDir) : IDisposable
    {
        internal ApiSurface Api { get; } = api;
        internal string ApiDllPath { get; } = apiDllPath;

        public void Dispose() => TryDeleteTempDir(tempDir);
    }
}
