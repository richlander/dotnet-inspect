using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Compares two methods from one retained assembly by structural clone equivalence, projecting
/// the result onto the discrete <see cref="ResearchMatchOutcome"/> model (issue #4304).
/// </summary>
/// <remarks>
/// This is deliberately A-vs-A: both selectors resolve against the same opened assembly. Matching
/// the same declared identity across two different assemblies (A-vs-A', e.g. a version bump) is a
/// distinct, not-yet-built cross-module capability; use <c>diff</c> for identity-driven A-vs-B
/// comparisons instead.
/// </remarks>
public static class MatchCommand
{
    public const string Name = "match";

    /// <summary>
    /// Names the discovery-only option a pairwise invocation supplied, or <c>null</c> when it
    /// supplied none. The parse layer rejects a missing second selector before
    /// <see cref="ExecuteAsync"/> runs, so both sites must consult this: otherwise the caller who
    /// wrote one selector and a discovery flag -- the caller who most clearly meant discovery --
    /// is told to supply a second selector instead of to add <c>--similar</c>.
    /// </summary>
    internal static string? DiscoveryOnlyFlag(
        bool assemblyWide,
        int? top,
        int? maximumResults,
        int? maximumMethods)
        => assemblyWide ? "--assembly-wide"
            : top is not null ? "--top"
            : maximumResults is not null ? "--max-results"
            : maximumMethods is not null ? "--max-methods"
            : null;

    internal static void WriteDiscoveryOnlyError(string flag)
        => CommandError.Write($"{flag} applies to discovery; add --similar.");

    public static async Task<int> ExecuteAsync(
        MatchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.Similar)
            return await MatchDiscovery.ExecuteAsync(options);

        // The discovery options share this options object. Pairwise comparison honors none of
        // them, so accepting them would silently ignore a scope or limit the caller asked for.
        string? discoveryOnly = DiscoveryOnlyFlag(
            options.AssemblyWide, options.Top, options.MaximumResults, options.MaximumMethods);

        if (discoveryOnly is not null)
        {
            WriteDiscoveryOnlyError(discoveryOnly);
            return 1;
        }

        if (string.IsNullOrEmpty(options.LeftSelector) || string.IsNullOrEmpty(options.RightSelector))
        {
            CommandError.Write("match requires two method selectors (Type.Member).");
            CommandError.WriteLine("Usage: dotnet-inspect match <Type.MemberA> <Type.MemberB> --package <pkg>");
            return 1;
        }

        if (options.IncludeBody && (options.Tabular || options.Tsv || options.Jsonl))
        {
            CommandError.Write(
                "--body cannot be combined with --table, --tsv, or --jsonl; "
                    + "render Markdown (the default) or --json instead.");
            return 1;
        }

        var (source, sourceError) = await ApiSourceResolver.ResolveAsync(options with { TypeName = null });
        if (sourceError.HasValue)
            return sourceError.Value;

        var logger = source.Context.Logger;

        try
        {
            var loaded = ApiServices.LoadFullApi(
                source.SearchPath, source.RuntimeAssemblyPath, options.PackagePath, source.PackageName,
                source.ApiSource, source.ApiVersion, source.SelectedTfm, logger, options,
                source.PackageExtractPath);
            if (loaded == null)
            {
                CommandError.Write("Could not extract API from library.");
                return 1;
            }

            var left = ResolveSelector(loaded.Api, loaded.ApiDllPath, options.LeftSelector);
            if (left.Error is not null)
            {
                CommandError.Write(left.Error);
                return 1;
            }

            var right = ResolveSelector(loaded.Api, loaded.ApiDllPath, options.RightSelector);
            if (right.Error is not null)
            {
                CommandError.Write(right.Error);
                return 1;
            }

            // Both tokens must index the same physical module. A type resolved through a type
            // forwarder (e.g. a facade assembly forwarding to its implementation assembly) carries
            // metadata tokens that index that *target* assembly, not loaded.ApiDllPath — comparing
            // them against the wrong image would silently reinterpret an unrelated MethodDef row
            // (issue #4304 Slice 3 review). Require both selectors to originate from the same file.
            if (!SameImage(left.OriginAssemblyPath, right.OriginAssemblyPath))
            {
                CommandError.Write(
                    $"'{options.LeftSelector}' and '{options.RightSelector}' resolve to different assemblies "
                        + $"({DistinguishingImageNames(left.OriginAssemblyPath, right.OriginAssemblyPath)}); "
                        + "match compares two methods within one retained assembly.");
                return 1;
            }

            // A raw token names a row number, and a row number absent from the image the caller
            // named is a selector error rather than an internal fault. Without this, the
            // comparison reached analysis's handle validation and surfaced a raw framework
            // resource key ("Arg_ParamName_Name") with no mention of the offending token.
            // Named selectors resolved to a real member, so only raw tokens need asking.
            string? tokenError =
                MethodTokenOutsideImage(left, options.LeftSelector)
                    ?? MethodTokenOutsideImage(right, options.RightSelector);
            if (tokenError is not null)
            {
                CommandError.Write(tokenError);
                return 1;
            }

            ResearchMatchResult result = ResearchMatch.Compare(
                left.OriginAssemblyPath!,
                MetadataTokens.MethodDefinitionHandle(left.Token!.Value),
                MetadataTokens.MethodDefinitionHandle(right.Token!.Value));

            MethodBodyDiffDocument? body = options.IncludeBody
                ? CompareBodies(left, right, result, loaded, source, options, cancellationToken)
                : null;

            if (options.JsonOutput)
            {
                if (body is null)
                {
                    JsonOutputHelper.Write(
                        result.Document,
                        StructuralCloneComparisonDocumentJsonContext.Default.StructuralCloneComparisonDocument,
                        StructuralCloneComparisonDocumentCompactJsonContext.Default.StructuralCloneComparisonDocument,
                        options.CompactJson);
                }
                else
                {
                    var envelope = new MatchBodyDocument(result.Document, body);
                    JsonOutputHelper.Write(
                        envelope,
                        MatchBodyDocumentJsonContext.Default.MatchBodyDocument,
                        MatchBodyDocumentCompactJsonContext.Default.MatchBodyDocument,
                        options.CompactJson);
                }
            }
            else
            {
                WriteMarkoutOutput(left.Display!, right.Display!, result, body, options);
            }

            if (body is { HasFailures: true })
            {
                CommandError.Write(
                    body.Diagnostic?.Detail
                        ?? "Method body comparison did not complete successfully; see its typed outcomes.");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        finally
        {
            if (source.TempDir is not null)
                TryDeleteTempDir(source.TempDir);
        }
    }

    static void TryDeleteTempDir(string tempDir)
    {
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

    internal readonly record struct ResolvedSelector(
        int? Token,
        string? Display,
        string? OriginAssemblyPath,
        string? Error,
        ApiType? DeclaringType = null);

    /// <summary>
    /// Canonicalizes an image path so two spellings of one file compare equal. A selector's origin
    /// is a physical-file identity, and it arrives by two routes: a forwarded type carries the
    /// defining image's recorded path, and a resolved type carries the extraction path. A raw
    /// token contributes no third route — it is anchored to the caller's own --library by
    /// construction. <c>./Foo.dll</c> and its absolute path name one file, so comparing raw
    /// spellings reports one image as two — which rejects a valid pairwise pair and stops
    /// discovery from suppressing its own seed. Lexical only; it deliberately does not resolve
    /// symlinks.
    /// </summary>
    internal static string CanonicalImagePath(string path) => Path.GetFullPath(path);

    /// <summary>
    /// Reports whether two spellings name one image.
    /// <see cref="CanonicalImagePath"/> already reconciles the spelling differences that ordinary
    /// callers produce — a relative path, a <c>./</c> prefix, or a redundant separator — so an
    /// ordinal comparison of canonical paths answers the question these callers actually ask.
    /// <para>
    /// Both origins reaching a comparison derive from one <c>--library</c>, so they differ only
    /// when type forwarding resolves a selector to the assembly that defines it. Case-only
    /// spellings of one file cannot reach here, because nothing in the command constructs a second
    /// spelling of the caller's own path. That is what makes an ordinal comparison sufficient
    /// rather than merely cheap: earlier revisions asked the volume whether two spellings named
    /// one file, and the question is undecidable from path text.
    /// </para>
    /// </summary>
    internal static bool SameImage(string? left, string? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return string.Equals(
            Path.TrimEndingDirectorySeparator(CanonicalImagePath(left)),
            Path.TrimEndingDirectorySeparator(CanonicalImagePath(right)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Reports a selector error when a raw token names no row in the image it resolved against.
    /// Returns null for a named selector, which resolved through a member that exists by
    /// construction, and for a token that does name a row.
    /// </summary>
    static string? MethodTokenOutsideImage(ResolvedSelector resolved, string? spelling)
    {
        if (spelling is null
            || !MatchDiscovery.TryParseMethodToken(spelling, out int token)
            || resolved.OriginAssemblyPath is not string image)
        {
            return null;
        }

        using var source = MetadataSource.OpenWithoutSymbols(image);
        return source.ContainsMethodDefinition(token)
            ? null
            : $"'{spelling}' is not a MethodDef row in {Path.GetFileName(image)}. "
                + "A metadata token addresses a row in one image; use the token exactly as "
                + "`match --similar` printed it, against the assembly that printed it.";
    }

    /// <summary>
    /// Reports whether <paramref name="type"/> owns the metadata rows it projects in
    /// <paramref name="image"/>. A type reached through a type forwarder carries tokens that index
    /// the assembly defining it, so it must not name a row in the image the caller opened. A type
    /// with no recorded source came from that image's own tables.
    /// </summary>
    internal static bool DefinesOwnRows(ApiType type, string image)
        => type.SourceAssemblyPath is null || SameImage(type.SourceAssemblyPath, image);

    /// <summary>
    /// Names two distinct images so the reader can tell them apart. File names alone are the
    /// readable form, but two different directories can hold the same file name, and an error that
    /// prints one name twice explains nothing; fall back to full paths in that case.
    /// </summary>
    static string DistinguishingImageNames(string? left, string? right)
    {
        string leftName = Path.GetFileName(left) ?? "";
        string rightName = Path.GetFileName(right) ?? "";
        return string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase)
            ? $"{CanonicalImagePath(left!)} vs {CanonicalImagePath(right!)}"
            : $"{leftName} vs {rightName}";
    }

    /// <summary>
    /// Resolves a <c>Type.Member</c> selector to the unique <see cref="ApiMember.MetadataToken"/>
    /// it names. Reuses <see cref="ApiTypeLookupService"/>'s type-vs-member boundary detection
    /// rather than re-splitting the string, since that boundary is not knowable syntactically.
    /// </summary>
    internal static ResolvedSelector ResolveSelector(ApiSurface api, string apiDllPath, string selector)
    {
        // A MethodDef token addresses a row directly, so it resolves without a name. This is what
        // makes a discovery row addressable here: an overloaded member and a property with both
        // accessors have no unambiguous Type.Member spelling, but they always have a token, and
        // `match --similar` prints one on every row for exactly this purpose.
        //
        // A raw token indexes exactly one image: the one the caller named with --library. It is
        // never re-attributed to some other image by searching for a row number that matches,
        // because a MethodDef token is a table row index rather than an identity — small, dense,
        // and repeated across every assembly. The merged surface also carries type-forwarded types
        // whose tokens index the assembly that *defines* them, so a scan of api.Types can bind a
        // token to a foreign image and compare a method the caller never named. Type-forwarding
        // resolution states the invariant normatively: forwarding never remaps a terminal token
        // onto the starting facade or authorizes a consumer to interpret it against another image
        // (docs/design/type-forwarding-resolution.md, "Evidence and correspondence stay separate").
        // Discovery keeps its half of the promise by naming the image whose tokens it printed.
        if (MatchDiscovery.TryParseMethodToken(selector, out int methodToken))
        {
            string tokenImage = CanonicalImagePath(apiDllPath);

            // Display only. Restricted to types this image defines, so a forwarded type never
            // lends its name to a row it does not own; an unnamed row still resolves, because the
            // projected surface does not cover every MethodDef.
            ApiType? tokenType = api.Types.FirstOrDefault(
                type => DefinesOwnRows(type, tokenImage)
                    && type.Members.Any(
                        member => MatchDiscovery.MemberTokens(member).Contains(methodToken)));

            return new ResolvedSelector(
                methodToken,
                tokenType is null
                    ? $"MethodDef 0x{methodToken:X8}"
                    : $"{tokenType.FullName} MethodDef 0x{methodToken:X8}",
                tokenImage,
                null,
                tokenType);
        }

        var lookup = ApiTypeLookupService.LookupType(api, selector);
        if (!lookup.Found)
        {
            if (TryGetForwardedTypeFailure(
                    api,
                    selector,
                    out ApiSurfaceInspectionFailure? forwardingFailure,
                    out string? forwardedType))
            {
                string target = forwardingFailure.DependencyAssembly is null
                    ? ""
                    : " Target: "
                        + AssemblyIdentityFormatter.Format(
                            forwardingFailure.DependencyAssembly)
                        + ".";
                return new ResolvedSelector(
                    null,
                    null,
                    null,
                    $"Forwarded type '{forwardedType}' could not be resolved: "
                        + $"{forwardingFailure.Kind}.{target}");
            }

            return new ResolvedSelector(
                null,
                null,
                null,
                $"'{selector}' must name a Type.Member selector (e.g. MyType.MyMethod).");
        }
        if (lookup.ImpliedMember is null)
        {
            return new ResolvedSelector(
                null,
                null,
                null,
                $"'{selector}' must name a Type.Member selector (e.g. MyType.MyMethod).");
        }

        var apiType = lookup.Type!;
        var memberName = lookup.ImpliedMember;

        // A property/event member has no method-def token of its own; its addressable body
        // lives on an accessor. Only a member with exactly one addressable accessor (a
        // get-only property, or an add-only/remove-only event) resolves unambiguously here —
        // a member with both a getter and a setter (or an adder and a remover) would otherwise
        // silently prefer one accessor over the other and compare the wrong body without
        // telling the caller (issue #4304 Slice 3 review).
        static int?[] AccessorTokens(ILInspector.Metadata.ApiMember m)
            => [m.GetterToken, m.SetterToken, m.AdderToken, m.RemoverToken];

        static int? MethodToken(ILInspector.Metadata.ApiMember m)
        {
            if (m.MetadataToken.HasValue)
                return m.MetadataToken;

            var accessors = AccessorTokens(m).Where(t => t.HasValue).ToList();
            return accessors.Count == 1 ? accessors[0] : null;
        }

        var candidates = apiType.Members
            .Where(m => string.Equals(m.Name, memberName, StringComparison.Ordinal)
                && MethodToken(m).HasValue)
            .ToList();

        if (candidates.Count == 0)
        {
            var namedMember = apiType.Members.FirstOrDefault(
                m => string.Equals(m.Name, memberName, StringComparison.Ordinal));
            var reason = namedMember is not null && AccessorTokens(namedMember).Count(t => t.HasValue) > 1
                ? "names more than one addressable accessor (e.g. both a getter and a setter); "
                    + "select the accessor directly (e.g. Type.get_Member) instead"
                : "did not resolve to a member with an addressable metadata token";
            return new ResolvedSelector(
                null,
                null,
                null,
                $"'{selector}' {reason}.");
        }

        if (candidates.Count > 1)
        {
            return new ResolvedSelector(
                null,
                null,
                null,
                $"'{selector}' matches {candidates.Count} overloads; narrow the pattern or use a fully-qualified selector.");
        }

        // The candidate's token indexes the assembly it was extracted from —
        // apiType.SourceAssemblyPath for a type resolved through a type forwarder, otherwise the
        // extraction dll (apiDllPath). Comparing a forwarded type's token against apiDllPath would
        // silently reinterpret an unrelated MethodDef row in the wrong image.
        var originAssemblyPath = CanonicalImagePath(apiType.SourceAssemblyPath ?? apiDllPath);
        return new ResolvedSelector(
            MethodToken(candidates[0]),
            $"{apiType.FullName}.{memberName}",
            originAssemblyPath,
            null,
            apiType);
    }

    static bool TryGetForwardedTypeFailure(
        ApiSurface api,
        string selector,
        [NotNullWhen(true)] out ApiSurfaceInspectionFailure? failure,
        [NotNullWhen(true)] out string? typeName)
    {
        var candidates = api.InspectionFailures
            .Where(candidate =>
                candidate.Operation.Equals(
                    "resolve forwarded type",
                    StringComparison.Ordinal)
                && !candidate.AffectedTypeDefinitions.IsDefaultOrEmpty)
            .SelectMany(candidate =>
                candidate.AffectedTypeDefinitions.Select(
                    affected => (
                        Failure: candidate,
                        Name: affected.ToMetadataFullName())))
            .ToArray();
        if (candidates.Length == 0)
        {
            failure = null;
            typeName = null;
            return false;
        }

        int searchEnd = selector.Length;
        for (int probes = 0; probes < 64 && searchEnd > 0; probes++)
        {
            int dot = FqnParser.LastTopLevelDot(selector[..searchEnd]);
            if (dot <= 0)
                break;

            int typeEnd = dot;
            string member = selector[(dot + 1)..];
            MemberTargetSelector memberSelector =
                MemberTargetSelector.Parse(member);
            if (string.IsNullOrWhiteSpace(memberSelector.Name))
            {
                failure = null;
                typeName = null;
                return false;
            }

            if (typeEnd > 1 && selector[typeEnd - 1] == '.')
            {
                if (!(memberSelector.Name.Equals(
                        ".ctor",
                        StringComparison.OrdinalIgnoreCase)
                    || memberSelector.Name.Equals(
                        "cctor",
                        StringComparison.OrdinalIgnoreCase)
                    || memberSelector.Name.Equals(
                        ".cctor",
                        StringComparison.OrdinalIgnoreCase)))
                {
                    failure = null;
                    typeName = null;
                    return false;
                }

                typeEnd--;
                if (typeEnd > 0 && selector[typeEnd - 1] == '.')
                {
                    failure = null;
                    typeName = null;
                    return false;
                }
            }

            (ApiSurfaceInspectionFailure Failure, string Name)[] matches =
                [.. candidates.Where(candidate =>
                    TypeMatcher.MatchesTypeFilter(
                        candidate.Name,
                        selector[..typeEnd]))];
            if (matches.Length > 0)
            {
                if (matches.Length != 1)
                {
                    failure = null;
                    typeName = null;
                    return false;
                }

                (ApiSurfaceInspectionFailure Failure, string Name) matched =
                    matches[0];
                failure = matched.Failure;
                typeName = matched.Name;
                return true;
            }

            searchEnd = dot;
        }

        failure = null;
        typeName = null;
        return false;
    }

    static MethodBodyDiffDocument CompareBodies(
        ResolvedSelector left,
        ResolvedSelector right,
        ResearchMatchResult structural,
        ApiServices.LoadedApiSurface loaded,
        ApiSourceResult source,
        MatchOptions options,
        CancellationToken cancellationToken)
    {
        string image = left.OriginAssemblyPath!;
        ResolvedAssemblyReference assembly =
            (left.DeclaringType is { } type ? loaded.TryGetSourceAssembly(type) : null)
            ?? ResolvedAssemblyReference.CreateFromPath(
                image, AssemblyResolutionProvenance.Local("match --body"));
        var policy = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(image)
            {
                ProjectAssetsPath = options.ProjectAssetsPath,
                RootPackageDirectory = source.PackageExtractPath,
                TargetFramework = options.Tfm ?? source.SelectedTfm,
                PackageSourceOptions = options.SourceOptions,
                UsePackageSourcePolicy = source.PackageExtractPath is not null,
            });
        using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(assembly, policy);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        // The structural result retains the physical module identity and tokens selected above.
        // Carry those addresses, not names or a new token lookup, into the borrowed context.
        var before = new MetadataMethodAddress(
            structural.Document.Left.ModuleVersionId,
            MetadataTokens.MethodDefinitionHandle(structural.Document.LeftToken));
        var after = new MetadataMethodAddress(
            structural.Document.Right.ModuleVersionId,
            MetadataTokens.MethodDefinitionHandle(structural.Document.RightToken));
        LocalComparisonQueryResult result = DirectMemberComparisonQuery.Execute(
            group,
            new DirectMemberComparisonRequest(
                new(participant, before),
                new(participant, after),
                ResearchProducerCatalog.Kinds),
            cancellationToken);
        return result switch
        {
            LocalComparisonQueryResult.Published published =>
                MethodBodyDiffFormatter.Build(left.Display!, right.Display!, published.Outcome),
            LocalComparisonQueryResult.NonSuccess failure =>
                BodyQueryFailure(left.Display!, right.Display!, failure),
            _ => throw new InvalidOperationException("Unknown local comparison query result."),
        };
    }

    static MethodBodyDiffDocument BodyQueryFailure(
        string before,
        string after,
        LocalComparisonQueryResult.NonSuccess result)
    {
        (string kind, string detail) = result.Failure switch
        {
            LocalComparisonQueryFailure.InvalidDesignation failure =>
                (failure.Kind.ToString(), failure.MetadataFailures.IsDefaultOrEmpty
                    ? $"The selected physical method is unavailable: {failure.Kind}."
                    : string.Join("; ", failure.MetadataFailures.Select(item => item.Detail))),
            LocalComparisonQueryFailure.AccessRejected failure =>
                (failure.Cause.Kind.ToString(), failure.Cause.Detail),
            LocalComparisonQueryFailure.PopulationRejected failure =>
                (failure.Cause.Kind.ToString(), $"Comparison population rejected: {failure.Cause.Kind}."),
            LocalComparisonQueryFailure.AdmissionRejected failure =>
                (failure.Cause.Kind.ToString(), failure.Cause.Summary),
            LocalComparisonQueryFailure.PlanningRejected failure =>
                (failure.Cause.Kind.ToString(), failure.Cause.Summary),
            LocalComparisonQueryFailure.DesignationRejected failure =>
                (failure.Cause.Kind.ToString(), $"Physical pair rejected: {failure.Cause.Kind}."),
            LocalComparisonQueryFailure.DesignationUnavailable failure =>
                ("DesignationUnavailable", string.Join("; ", failure.Cause.Endpoints.Select(
                    endpoint => $"{endpoint.Side}: {endpoint.Kind} ({endpoint.Attempt.Outcome.Kind})"))),
            LocalComparisonQueryFailure.Cancelled failure =>
                ("Cancelled", failure.Cause.Message),
            LocalComparisonQueryFailure.Failed failure =>
                ("Failed", failure.Cause.Message),
            _ => throw new InvalidOperationException("Unknown local comparison query failure."),
        };
        return MethodBodyDiffFormatter.QueryFailure(
            before, after, kind, result.Side?.ToString(), detail);
    }

    static void WriteMarkoutOutput(
        string leftDisplay,
        string rightDisplay,
        ResearchMatchResult result,
        MethodBodyDiffDocument? body,
        MatchOptions options)
    {
        var view = MatchOutputFormatter.BuildView(leftDisplay, rightDisplay, result);

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                options.Rows);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(Console.Out, options.Rows,
                opts =>
                {
                    var text = MarkoutSerializer.Serialize(view, SearchViewContext.Default, opts);
                    if (body is not null)
                    {
                        text = $"{text}\n\n{MethodBodyDiffFormatter.Render(body, opts)}";
                    }

                    return text;
                });
        }
    }
}
