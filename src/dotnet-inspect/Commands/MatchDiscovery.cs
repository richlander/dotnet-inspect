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

        LoadedSide? seed = null;
        try
        {
            (seed, int? seedError) = await LoadSideAsync(options, options.AssemblyPath);
            if (seedError.HasValue)
                return seedError.Value;

            return await ExecuteAsync(options, seed!, seed!);
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

            // One image, guaranteed by the gate above, so one group serves both sides.
            using AssemblyContextGroup group = CreateGroup(workspace, seedImage);

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

            // A package is extracted to a temporary directory that this command deletes as it
            // exits, so that extraction path addresses nothing by the time the caller could type
            // it. Disclose the package and the library within it, which is what actually replays.
            (string? candidatePackage, string candidateLibrary) =
                ReplayableCandidateAddress(options.PackagePath, seed.TempDir, candidateImage);

            var view = MatchDiscoveryFormatter.BuildView(
                new MatchDiscoveryRequest(
                    resolvedSeed.Display!,
                    scopeDisplay!,
                    tokensIndexCallerImage ? null : candidateLibrary,
                    limits,
                    options.Top,
                    tokensIndexCallerImage ? null : candidatePackage),
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
    /// A package is extracted to a temporary directory that <see cref="LoadedSide.Dispose"/>
    /// deletes, so disclosing the extraction path handed back an address that had already stopped
    /// existing: discovery reported success and the command it printed failed with "File not
    /// found". When the candidate image came out of that extraction, the replayable address is the
    /// package plus the library's name within it. Every other candidate image is a path the caller
    /// supplied and is disclosed unchanged.
    /// </para>
    /// </summary>
    internal static (string? Package, string Library) ReplayableCandidateAddress(
        string? packagePath,
        string? extractionDirectory,
        string candidateImage)
        => packagePath is not null
            && extractionDirectory is not null
            && candidateImage.StartsWith(
                Path.GetFullPath(extractionDirectory), StringComparison.Ordinal)
                ? (packagePath, Path.GetFileName(candidateImage))
                : (null, candidateImage);

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
            return new ResolvedSeed(null, null, null, null, resolved.Error);

        // The declaring type comes from the resolution that already happened. Re-deriving it here
        // meant a second token scan that could disagree with the first, and for a raw token that
        // disagreement bound the seed to a foreign type in another image and scoped the whole run
        // to it, reporting Completed.
        return new ResolvedSeed(
            resolved.Token,
            resolved.Display,
            resolved.OriginAssemblyPath,
            resolved.DeclaringType,
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
        string? Error);

    sealed class LoadedSide(ApiSurface api, string apiDllPath, string? tempDir) : IDisposable
    {
        internal ApiSurface Api { get; } = api;
        internal string ApiDllPath { get; } = apiDllPath;

        /// <summary>
        /// The extraction root when this side came from a package, so a disclosure can tell that
        /// an image path is ephemeral rather than replayable. Null for a directly named library.
        /// </summary>
        internal string? TempDir { get; } = tempDir;

        public void Dispose() => TryDeleteTempDir(TempDir);
    }
}
