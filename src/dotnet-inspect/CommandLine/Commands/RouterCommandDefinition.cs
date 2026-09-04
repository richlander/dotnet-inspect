using System.CommandLine;
using System.CommandLine.Parsing;
using CSharpText;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Planning;
using DotnetInspector.Services;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using NuGet.Versioning;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the hidden catch-all router. It captures raw bare-mode tokens, rewrites
/// to a real command, and lets that command own parsing and option semantics.
/// </summary>
public static class RouterCommandDefinition
{
    internal const string DeferredTypeOrMemberOptionName =
        "--router-deferred-type-or-member";

    private static readonly string DeferredTypeOrMemberCapability =
        Guid.NewGuid().ToString("N");

    internal static bool IsDeferredTypeOrMemberCapability(string? value) =>
        value == DeferredTypeOrMemberCapability;

    public static Command Create(RootCommand rootCommand, SharedOptions opts)
    {
        var routerCommand = new Command("router", "Auto-route bare input to a real command")
        {
            Hidden = true,
            TreatUnmatchedTokensAsErrors = false
        };

        var tokensArg = new Argument<string[]>("tokens")
        {
            Description = "Raw bare-mode tokens",
            Arity = ArgumentArity.ZeroOrMore,
            CaptureRemainingTokens = true
        };
        routerCommand.Arguments.Add(tokensArg);

        routerCommand.SetAction(async (parseResult, ct) =>
        {
            var tokens = parseResult.GetValue(tokensArg) ?? [];
            if (tokens.Length == 0)
            {
                HelpWriter.WriteHelp(rootCommand);
                return 0;
            }

            // The router intentionally captures all options as raw tokens so the rewritten
            // command remains the authority on their semantics. Parse a package-shaped probe
            // with the shared option instances to obtain the caller's NuGet scope without
            // maintaining a second command-line parser here.
            var sourceParseResult = rootCommand.Parse([PackageCommand.Name, .. tokens]);
            var sourceErrors = GetSourceOptionErrors(sourceParseResult, opts);
            if (sourceErrors.Count > 0)
            {
                foreach (var error in sourceErrors)
                    CommandError.Write(error.Message);
                return 1;
            }

            var sourceOptions = opts.ParseNuGetSourceOptions(sourceParseResult);
            if (TryGetCommandTypoSuggestion(tokens[0]) is { } suggestion)
            {
                CommandError.Write($"Unknown command '{tokens[0]}'.");
                CommandError.WriteBlankLine();
                CommandError.WriteLine("Did you mean:");
                CommandError.WriteLine($"  {suggestion}");
                return 1;
            }

            bool hasSectionRequest =
                sourceParseResult.GetResult(opts.Discover)
                    is { Implicit: false }
                || sourceParseResult.GetResult(opts.Select)
                    is { Implicit: false };
            if (hasSectionRequest)
            {
                List<ParseError> requestErrors =
                    GetStructuralRequestErrors(
                        sourceParseResult,
                        opts);
                if (requestErrors.Count > 0)
                {
                    foreach (ParseError error in requestErrors)
                        CommandError.Write(error.Message);
                    return 1;
                }
            }

            if (ContainsHelpOption(tokens) && !tokens[0].StartsWith('-'))
            {
                CommandError.WriteNote($"interpreting bare token '{tokens[0]}' as a package or platform target.");
                CommandError.WriteLine("      Use 'dotnet-inspect --help' to list commands, or 'dotnet-inspect package --help' for package help.");
                CommandError.WriteBlankLine();
            }

            RequestTelemetry.Breadcrumb("router-hit", string.Join(' ', tokens));
            bool structuralDiscovery =
                opts.IsDiscoveryMode(sourceParseResult)
                && opts.ParseSchema(sourceParseResult);
            string? sourceIdentityTypeTarget =
                RouterTokenRewriter.GetSecondaryPositionalTarget(
                    tokens,
                    rootCommand);
            if (structuralDiscovery
                && RouterTokenRewriter.TryRewriteAcquisitionFree(
                    tokens,
                    rootCommand,
                    structuralSchema: true,
                    out string[] structuralRewrite))
            {
                structuralRewrite =
                    CommandLineBuilder.PreprocessArgs(
                        structuralRewrite,
                        rootCommand);
                RequestTelemetry.Breadcrumb(
                    "router-structural",
                    $"syntax: {string.Join(' ', structuralRewrite)}");
                return await CommandLineBuilder.InvokeWithLineWindowAsync(
                    rootCommand.Parse(structuralRewrite),
                    structuralRewrite);
            }

            if (StructuralViewRegistry.TryClassifyCommandless(
                    tokens,
                    structuralDiscovery,
                    out CommandlessStructuralRoute? structuralRoute))
            {
                string[] structuralTokens =
                    CommandLineBuilder.PreprocessArgs(
                        structuralRoute!.RewrittenTokens,
                        rootCommand);
                RequestTelemetry.Breadcrumb(
                    "router-structural",
                    $"{structuralRoute.Route.Label}: "
                    + string.Join(' ', structuralTokens));
                return await CommandLineBuilder.InvokeWithLineWindowAsync(
                    rootCommand.Parse(structuralTokens),
                    structuralTokens);
            }

            if (structuralDiscovery)
            {
                ParseResult analysisParseResult =
                    rootCommand.Parse(
                        [MemberCommand.Name, .. tokens]);
                OptionError? analysisError =
                    SharedParsers.ParseAnalysisQueryOptions(
                        analysisParseResult,
                        opts,
                        typeScoped: false,
                        typeName: null,
                        out _,
                        out _);
                analysisError ??=
                    MemberOptionsParser.GetMermaidOptionError(
                        analysisParseResult,
                        opts);
                if (analysisError is not null)
                {
                    CommandError.Write(analysisError.Value);
                    return 1;
                }

                StructuralDiscoveryRequest request =
                    StructuralDiscoveryRequest.From(
                        sourceParseResult,
                        opts);
                StructuralCatalogAlternatives alternatives =
                    StructuralViewRegistry
                        .CreateCommandlessAlternatives(
                            tokens,
                            request,
                            sourceIdentityTypeTarget);
                RequestTelemetry.Breadcrumb(
                    "router-structural",
                    "alternatives: "
                    + string.Join(
                        ",",
                        alternatives.Alternatives.Select(
                            alternative =>
                                alternative.Route.Label)));
                return StructuralViewRegistry.Execute(
                    alternatives,
                    request);
            }

            if (hasSectionRequest)
            {
                StructuralDiscoveryRequest commandlessRequest =
                    StructuralDiscoveryRequest.From(
                        sourceParseResult,
                        opts);
                if (StructuralViewRegistry
                    .RejectUniversallyInvalidCommandlessRequest(
                        tokens,
                        commandlessRequest,
                        sourceIdentityTypeTarget))
                {
                    return 1;
                }
            }

            var rewritten = await RouterTokenRewriter.RewriteAsync(
                tokens,
                sourceOptions,
                rootCommand);
            RequestTelemetry.Breadcrumb(
                "router-rewrite",
                $"{string.Join(' ', tokens)} -> {string.Join(' ', rewritten)}");

            if (rewritten.Length == tokens.Length && rewritten.SequenceEqual(tokens))
            {
                CommandError.Write($"Could not route '{tokens[0]}'.");
                return 1;
            }

            // Invoked through the audit choke point, not ParseResult.InvokeAsync: the router
            // captures projection flags as raw tokens, so the outer invocation records nothing
            // and only this rewritten parse can tell whether a projection was honored.
            rewritten = CommandLineBuilder.PreprocessArgs(
                rewritten,
                rootCommand);
            return await CommandLineBuilder.InvokeWithLineWindowAsync(
                rootCommand.Parse(rewritten),
                rewritten);
        });

        return routerCommand;
    }

    private static List<ParseError> GetSourceOptionErrors(
        ParseResult parseResult,
        SharedOptions opts)
    {
        OptionResult? source = parseResult.GetResult(opts.Source);
        OptionResult? additionalSource = parseResult.GetResult(opts.AddSource);
        OptionResult? config = parseResult.GetResult(opts.NuGetConfig);

        return
        [
            .. parseResult.Errors.Where(error =>
                IsWithin(error.SymbolResult, source)
                || IsWithin(error.SymbolResult, additionalSource)
                || IsWithin(error.SymbolResult, config)),
        ];
    }

    private static List<ParseError> GetStructuralRequestErrors(
        ParseResult parseResult,
        SharedOptions opts)
    {
        Option[] requestOptions =
        [
            opts.Discover,
            opts.Select,
            opts.Tree,
            opts.Json,
            opts.Tsv,
            opts.Jsonl,
            opts.Markdown,
            opts.PlainText,
            opts.Table,
            opts.Verbosity,
            opts.Schema,
            opts.Count,
            opts.Print,
            opts.Value,
            opts.Urls,
            opts.Paths,
            opts.Columns,
            opts.Fields,
        ];

        return
        [
            .. parseResult.Errors.Where(error =>
                requestOptions.Any(option =>
                    IsWithin(
                        error.SymbolResult,
                        parseResult.GetResult(option)))),
        ];
    }

    private static bool IsWithin(SymbolResult? result, SymbolResult? ancestor)
    {
        for (; result != null; result = result.Parent)
        {
            if (ReferenceEquals(result, ancestor))
                return true;
        }

        return false;
    }

    private static readonly string[] CommandSuggestionNames =
    [
        PackageCommand.Name,
        ProjectCommand.Name,
        "library",
        TypeCommand.Name,
        MemberCommand.Name,
        DiffCommand.Name,
        TimelineCommand.Name,
        FindCommand.Name,
        "extensions",
        "implements",
        "depends",
        "cache",
        "skill",
        DemoCommand.Name
    ];

    private static string? TryGetCommandTypoSuggestion(string token)
    {
        if (token.Length < 4
            || token.Contains('.')
            || token.Contains('@')
            || token.Contains('/')
            || token.Contains('\\'))
        {
            return null;
        }

        var normalized = token.ToLowerInvariant();
        return CommandSuggestionNames
            .Select(command => new
            {
                Command = command,
                Distance = StringDistance.EditDistance(normalized, command.ToLowerInvariant()),
                Similarity = StringDistance.Similarity(normalized, command.ToLowerInvariant())
            })
            .Where(candidate => candidate.Distance <= 2 && candidate.Similarity >= 0.70)
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Command, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Command)
            .FirstOrDefault();
    }

    private static bool ContainsHelpOption(IEnumerable<string> tokens)
        => tokens.Any(token => token is "--help" or "-h" or "-?");

    private static class RouterTokenRewriter
    {
        private const int MaxTypeMemberBoundaryProbes = 64;

        public static async Task<string[]> RewriteAsync(
            string[] tokens,
            NuGetSourceOptions sourceOptions,
            RootCommand rootCommand)
        {
            var target = tokens[0];
            var tail = tokens[1..];

            if (TryRewriteAcquisitionFree(
                    tokens,
                    rootCommand,
                    structuralSchema: false,
                    out string[] rewritten))
                return rewritten;

            var trailingSegmentStart =
                FqnParser.LastTopLevelDot(target) + 1;
            var trailingSegmentHasGenericNotation =
                TypeMatcher.HasExplicitGenericNotation(
                    target[trailingSegmentStart..]);
            var hasTypeOption = ContainsOption(tokens, "--type")
                || ContainsOption(tokens, "-t");
            var hasMemberOption = ContainsOption(tokens, "--member")
                || ContainsOption(tokens, "-m");
            var hasLibraryValue = TryGetLibraryValue(
                tail,
                rootCommand,
                out _);
            var hasExplicitApiSource =
                ContainsOption(tail, "--package")
                || ContainsOption(tail, "--platform")
                || ContainsOption(tail, "--project")
                || hasLibraryValue;

            var context = new CommandContext(verbose: false);
            if (PlatformResolver.IsPlatformCandidate(target))
            {
                var (resolvedPath, _, _, resolvedError) = await PlatformResolver.ResolveAssemblyAsync(
                    target,
                    context.HttpClient,
                    context.Logger.Log,
                    sourceOptions: sourceOptions);
                if (resolvedPath != null && resolvedError == null)
                {
                    AssemblySurfaceClassificationOutcome classification =
                        PlatformResolver.ClassifyAssemblySurface(resolvedPath);
                    if (classification
                        is AssemblySurfaceClassificationOutcome.Rejected rejected)
                    {
                        CommandError.Write(
                            $"Could not classify the platform assembly surface ({rejected.Failure.Kind}).");
                        return tokens;
                    }

                    bool isFacade =
                        ((AssemblySurfaceClassificationOutcome.Classified)
                            classification).Classification.Kind
                        == AssemblySurfaceKind.Facade;
                    return target.Count(c => c == '.') >= 2 && isFacade
                        ? ["type", target, .. tail]
                        : ["library", target, .. tail];
                }
            }

            var allowPlatformPrefixFallback = PlatformResolver.IsPlatformCandidate(target);

            var frameworkSpec = GetOptionValue(tail, "--framework");
            var exactTypeLookup = LookupExactGenericPlatformType(
                target,
                allowSimpleName: hasTypeOption || hasMemberOption,
                frameworkSpec: frameworkSpec);
            if (exactTypeLookup is PlatformTypeLookupOutcome.Resolved exactType)
            {
                return hasMemberOption
                    && trailingSegmentHasGenericNotation
                    && exactType.Candidate.Type.Segments.Length == 1
                    ? RouteExactGenericPlatformMemberTarget(
                        exactType,
                        target,
                        tail)
                    : RouteExactGenericPlatformType(
                        exactType,
                        target,
                        tail);
            }

            if (LookupExactGenericPlatformMember(target, frameworkSpec)
                is { } exactMember)
            {
                if (exactMember.Lookup
                    is PlatformTypeLookupOutcome.Resolved resolved)
                {
                    return RouteExactGenericPlatformMember(
                        (exactMember.TypeTarget,
                            exactMember.MemberSelector,
                            resolved),
                        tail);
                }

                if (hasExplicitApiSource)
                    return RouteDeferredTypeOrMember(target, tail);

                if (WritePlatformTypeLookupFailure(exactMember.Lookup))
                    return tokens;
            }

            if (hasExplicitApiSource && exactTypeLookup is not null)
                return RouteDeferredTypeOrMember(target, tail);
            if (WritePlatformTypeLookupFailure(exactTypeLookup))
                return tokens;

            string? platformLookupFailure = null;
            var memberSplit = SharedParsers.TrySplitQualifiedTypeMember(
                target,
                sourceOptions,
                allowPlatformPrefixFallback,
                message => platformLookupFailure ??= message);
            if (memberSplit != null)
            {
                var probe = memberSplit.Value.Probe;
                if (probe.Kind == SourceResolver.LocalSourceKind.Platform
                    && !await IsExactPlatformTypeAsync(probe, context))
                {
                    memberSplit = null;
                }
            }

            if (memberSplit is { Probe.Kind: not SourceResolver.LocalSourceKind.Platform })
            {
                var probe = memberSplit.Value.Probe;
                RequestTelemetry.Breadcrumb(
                    "qualified-member",
                    $"{target} -> source={probe.SourceName}; type={probe.Remainder}; member={memberSplit.Value.MemberName}");

                return ["member", probe.Remainder, "--package", probe.SourceName, "-m", memberSplit.Value.MemberName, .. tail];
            }

            // Runtime-catalog fallback can be ambiguous for types owned by another
            // shared framework, so let the all-framework resolvers establish identity first.
            var memberFind = await TypeFindIfMissResolver.ResolvePlatformMemberAsync(
                target,
                includeAll: false,
                sourceOptions,
                context.HttpClient,
                context.Logger);
            if (memberFind.Status == TypeFindIfMissStatus.Found)
            {
                var match = memberFind.TypeResolution.Match!;
                return ["member", match.FullName, "--platform", match.Library, .. FrameworkArgsUnlessSpecified(match.Source, tail), "-m", memberFind.MemberSelector, .. tail];
            }
            if (memberFind.Status == TypeFindIfMissStatus.Ambiguous)
            {
                memberFind.WriteAmbiguousError();
                return tokens;
            }

            if (memberSplit != null)
            {
                var probe = memberSplit.Value.Probe;
                RequestTelemetry.Breadcrumb(
                    "qualified-member",
                    $"{target} -> source={probe.SourceName}; type={probe.Remainder}; member={memberSplit.Value.MemberName}");

                return ["member", probe.Remainder, "--platform", probe.SourceName, "-m", memberSplit.Value.MemberName, .. tail];
            }

            var typeProbe = SourceResolver.TryResolveQualifiedTypeName(
                target,
                sourceOptions,
                allowPlatformPrefixFallback,
                message => platformLookupFailure ??= message);
            bool hasNonExactPlatformProbe = false;
            if (typeProbe != null)
            {
                hasNonExactPlatformProbe =
                    typeProbe.Kind == SourceResolver.LocalSourceKind.Platform
                    && !await IsExactPlatformTypeAsync(typeProbe, context);
                if (!hasNonExactPlatformProbe)
                {
                    RequestTelemetry.Breadcrumb(
                        "qualified-type",
                        $"{target} -> source={typeProbe.SourceName}; type={typeProbe.Remainder}");

                    return typeProbe.Kind == SourceResolver.LocalSourceKind.Platform
                        ? ["type", typeProbe.Remainder, "--platform", typeProbe.SourceName, .. tail]
                        : ["type", typeProbe.Remainder, "--package", typeProbe.SourceName, .. tail];
                }
            }

            var typeFind = await TypeFindIfMissResolver.ResolvePlatformAsync(
                target,
                includeAll: false,
                sourceOptions,
                context.HttpClient,
                context.Logger);
            if (typeFind.Status == TypeFindIfMissStatus.Found)
            {
                var match = typeFind.Match!;
                CommandError.WriteNote($"Type '{target}' resolved via platform find to {match.FullName} in {match.Library}.");
                return ["type", match.FullName, "--platform", match.Library, .. FrameworkArgsUnlessSpecified(match.Source, tail), .. tail];
            }
            if (typeFind.Status == TypeFindIfMissStatus.Ambiguous)
            {
                typeFind.WriteAmbiguousError();
                return tokens;
            }

            if (platformLookupFailure is not null)
            {
                CommandError.Write(platformLookupFailure);
                return tokens;
            }

            if (hasNonExactPlatformProbe)
                return ["type", target, .. tail];

            if (PlatformResolver.IsPlatformCandidate(target))
            {
                if (await PackageExistsAsync(target, sourceOptions, context))
                    return ["package", .. tokens];

                return ["type", target, .. tail];
            }

            return ["package", .. tokens];
        }

        public static bool TryRewriteAcquisitionFree(
            string[] tokens,
            RootCommand rootCommand,
            bool structuralSchema,
            out string[] rewritten)
        {
            rewritten = [];
            if (tokens.Length == 0)
                return false;

            string target = tokens[0];
            string[] tail = tokens[1..];
            if (CommandLineHelpers.IsBooleanOptionEnabled(
                    tokens,
                    "--all-libraries"))
            {
                rewritten = [PackageCommand.Name, .. tokens];
                return true;
            }

            if (CommandLineHelpers.TryClassifyAsFilePath(
                    target,
                    out string? dllPath,
                    out string? nupkgPath))
            {
                if (dllPath is not null)
                {
                    rewritten = ["library", target, .. tail];
                    return true;
                }

                if (nupkgPath is not null)
                {
                    rewritten = [PackageCommand.Name, target, .. tail];
                    return true;
                }
            }

            bool hasExplicitGenericNotation =
                TypeMatcher.HasExplicitGenericNotation(target);
            int trailingSegmentStart =
                FqnParser.LastTopLevelDot(target) + 1;
            bool trailingSegmentHasGenericNotation =
                TypeMatcher.HasExplicitGenericNotation(
                    target[trailingSegmentStart..]);
            bool hasTypeOption =
                ContainsOption(tokens, "--type")
                || ContainsOption(tokens, "-t");
            bool hasMemberOption =
                ContainsOption(tokens, "--member")
                || ContainsOption(tokens, "-m");
            bool hasLibraryValue = TryGetLibraryValue(
                tail,
                rootCommand,
                out string libraryValue);
            bool hasPackageRelativeLibrary =
                hasLibraryValue
                && SourceResolver
                    .IsPackageRelativeLibraryValue(libraryValue);
            bool hasExplicitApiSource =
                ContainsOption(tail, "--package")
                || ContainsOption(tail, "--platform")
                || ContainsOption(tail, "--project")
                || (hasLibraryValue
                    && !hasPackageRelativeLibrary);
            bool hasVersionQuery =
                ContainsOption(tokens, "--version")
                || CommandLineHelpers.IsBooleanOptionEnabled(
                    tokens,
                    "--latest-version")
                || ContainsOption(tokens, "--versions")
                || ContainsOption(
                    tokens,
                    "--versions-with-feed");
            bool hasStructuralPackageAssetPath =
                hasPackageRelativeLibrary
                && (libraryValue.Contains('/')
                    || libraryValue.Contains('\\'));

            if (TryRouteExplicitSourceTarget(
                    target,
                    tail,
                    "--package",
                    hasTypeOption,
                    hasMemberOption,
                    rootCommand,
                    structuralSchema,
                    out rewritten)
                || TryRouteExplicitSourceTarget(
                    target,
                    tail,
                    "--platform",
                    hasTypeOption,
                    hasMemberOption,
                    rootCommand,
                    structuralSchema,
                    out rewritten))
            {
                return true;
            }

            if (hasTypeOption
                && hasPackageRelativeLibrary)
            {
                rewritten = [PackageCommand.Name, .. tokens];
                return true;
            }

            if (hasTypeOption && hasExplicitApiSource)
            {
                rewritten = ["type", target, .. tail];
                return true;
            }

            if (hasMemberOption
                && (structuralSchema
                    || !hasExplicitGenericNotation
                    || (hasExplicitApiSource
                        && trailingSegmentHasGenericNotation
                        && trailingSegmentStart == 0)))
            {
                rewritten = [MemberCommand.Name, target, .. tail];
                return true;
            }

            if (hasExplicitApiSource
                && TrySplitOperatorMemberTarget(
                    target,
                    out string operatorType,
                    out string operatorMember))
            {
                rewritten =
                [
                    MemberCommand.Name,
                    operatorType,
                    "-m",
                    operatorMember,
                    .. tail,
                ];
                return true;
            }

            if (IsExplicitSourceIdentity(
                    target,
                    tail,
                    "--package"))
            {
                rewritten =
                [
                    PackageCommand.Name,
                    target,
                    .. RemoveOptionWithValue(
                        tail,
                        "--package",
                        target),
                ];
                return true;
            }

            if (IsExplicitSourceIdentity(
                    target,
                    tail,
                    "--platform"))
            {
                rewritten = ["library", target, .. tail];
                return true;
            }

            if (hasLibraryValue
                && !hasExplicitGenericNotation
                && !ContainsOption(tail, "--package")
                && !ContainsOption(tail, "--platform")
                && !ContainsOption(tail, "--project")
                && hasPackageRelativeLibrary
                && (!structuralSchema
                    || hasStructuralPackageAssetPath))
            {
                rewritten = [PackageCommand.Name, .. tokens];
                return true;
            }

            if (structuralSchema
                && hasLibraryValue
                && hasExplicitGenericNotation
                && StructuralViewRegistry
                    .HasExplicitGenericTypeTail(target)
                && !StructuralViewRegistry
                    .HasGenericTypeAndGenericTailAmbiguity(target)
                && !StructuralViewRegistry
                    .RequiresGenericTailMemberAlternative(
                        target,
                        tokens)
                && !StructuralViewRegistry
                    .HasUnambiguousMemberTail(target))
            {
                rewritten = ["type", target, .. tail];
                return true;
            }

            if (structuralSchema
                && hasLibraryValue)
            {
                return false;
            }

            if (hasExplicitApiSource)
            {
                rewritten =
                    structuralSchema
                    && hasExplicitGenericNotation
                    && StructuralViewRegistry
                        .HasExplicitGenericTypeTail(target)
                    && !StructuralViewRegistry
                        .HasGenericTypeAndGenericTailAmbiguity(target)
                    && !StructuralViewRegistry
                        .RequiresGenericTailMemberAlternative(
                            target,
                            tokens)
                    && !StructuralViewRegistry
                        .HasUnambiguousMemberTail(target)
                        ? ["type", target, .. tail]
                        : target.Contains('.')
                            ? RouteDeferredTypeOrMember(target, tail)
                            : ["type", target, .. tail];
                return true;
            }

            if (ContainsOption(tokens, "--library"))
            {
                rewritten = [PackageCommand.Name, .. tokens];
                return true;
            }

            if (TryFindPositionalIndex(
                    tail,
                    rootCommand,
                    out int secondTargetIndex)
                && secondTargetIndex >= 0
                && !CommandLineHelpers.LooksLikeVersionNumber(
                    tail[secondTargetIndex]))
            {
                string secondTarget = tail[secondTargetIndex];
                rewritten =
                [
                    "type",
                    secondTarget,
                    "--package",
                    target,
                    .. tail[..secondTargetIndex],
                    .. tail[(secondTargetIndex + 1)..],
                ];
                return true;
            }

            if (hasVersionQuery || target.Contains('@'))
            {
                rewritten = [PackageCommand.Name, .. tokens];
                return true;
            }

            if (hasExplicitGenericNotation
                && structuralSchema)
            {
                if (StructuralViewRegistry
                    .HasUnambiguousMemberTail(target))
                {
                    rewritten =
                        [MemberCommand.Name, target, .. tail];
                    return true;
                }

                rewritten =
                    StructuralViewRegistry
                        .HasExplicitGenericTypeTail(target)
                    && !StructuralViewRegistry
                        .HasGenericTypeAndGenericTailAmbiguity(target)
                    && !StructuralViewRegistry
                        .RequiresGenericTailMemberAlternative(
                            target,
                            tokens)
                    ? ["type", target, .. tail]
                    : target.Contains('.')
                    ? RouteDeferredTypeOrMember(target, tail)
                    : ["type", target, .. tail];
                return true;
            }

            return false;
        }

        private static bool ContainsOption(string[] tokens, string option)
            => tokens.Any(token => token.Equals(option, StringComparison.Ordinal)
                                   || TryGetAttachedOptionValue(
                                       token,
                                       option,
                                       out _));

        private static string? GetOptionValue(
            string[] tokens,
            string option)
        {
            for (var i = 0; i < tokens.Length; i++)
            {
                if (TryGetAttachedOptionValue(
                        tokens[i],
                        option,
                        out var attachedValue))
                {
                    return attachedValue;
                }

                if (tokens[i].Equals(option, StringComparison.Ordinal)
                    && i + 1 < tokens.Length)
                {
                    return tokens[i + 1];
                }
            }

            return null;
        }

        private static bool TrySplitOperatorMemberTarget(
            string target,
            out string typeTarget,
            out string memberSelector)
        {
            var memberBoundary = Math.Max(
                target.LastIndexOf(
                    ".operator",
                    StringComparison.OrdinalIgnoreCase),
                target.LastIndexOf(
                    ".op_",
                    StringComparison.OrdinalIgnoreCase));
            if (memberBoundary <= 0
                || !IsTopLevelDot(target, memberBoundary))
            {
                typeTarget = "";
                memberSelector = "";
                return false;
            }

            memberSelector = target[(memberBoundary + 1)..];
            if (!OperatorNames.IsMetadataOperatorName(
                    MemberTargetSelector
                        .Parse(memberSelector)
                        .Name))
            {
                typeTarget = "";
                memberSelector = "";
                return false;
            }

            typeTarget = target[..memberBoundary];
            return true;
        }

        private static bool IsTopLevelDot(string value, int dotIndex)
        {
            var depth = 0;
            for (var i = 0; i < dotIndex; i++)
            {
                if (value[i] == '<')
                    depth++;
                else if (value[i] == '>')
                    depth--;
            }

            return depth == 0;
        }

        private static string[] RouteDeferredTypeOrMember(
            string target,
            string[] tail) =>
        [
            "member",
            target,
            DeferredTypeOrMemberOptionName,
            DeferredTypeOrMemberCapability,
            .. tail
        ];

        private static async Task<bool> PackageExistsAsync(
            string packageName,
            NuGetSourceOptions sourceOptions,
            CommandContext context)
        {
            if (PackageExtractor.HasCachedCandidateVersion(
                    packageName,
                    SourceResolver.ResolveSourceKeysForProbe(
                        sourceOptions,
                        packageName)))
            {
                return true;
            }

            try
            {
                var versions = await PackageExtractor.GetVersionsAsync(
                    context.HttpClient,
                    packageName,
                    includePrerelease: true,
                    limit: 1,
                    log: context.Logger.Log,
                    sourceOptions);
                return versions is { Count: > 0 };
            }
            catch (Exception ex)
            {
                context.Logger.Log($"Could not query package versions for '{packageName}': {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> IsExactPlatformTypeAsync(
            SourceResolver.LocalProbeResult probe,
            CommandContext context)
        {
            var (assemblyPath, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
                probe.SourceName,
                context.HttpClient,
                context.Logger.Log);
            return error == null
                   && assemblyPath != null
                   && PlatformResolver.HasType(assemblyPath, probe.Remainder);
        }

        private static PlatformTypeLookupOutcome? LookupExactGenericPlatformType(
            string target,
            bool allowSimpleName = false,
            string? frameworkSpec = null)
        {
            if (!target.Contains('`') && !target.Contains('<'))
                return null;

            var normalizedTarget = FqnParser.NormalizeTypeName(target).Replace('+', '.');
            var lookup = frameworkSpec is null
                ? PlatformResolver.LookupType(target)
                : PlatformResolver.LookupTypeInFramework(
                    target,
                    frameworkSpec);
            if (frameworkSpec is null
                && (lookup is not PlatformTypeLookupOutcome.Resolved runtimeResolved
                    || !runtimeResolved.Candidate.Type
                        .ToMetadataFullName()
                        .Replace('+', '.')
                        .Equals(
                            normalizedTarget,
                            StringComparison.OrdinalIgnoreCase)))
            {
                lookup = PlatformResolver.LookupTypeAcrossFrameworks(target);
            }

            if (lookup is not PlatformTypeLookupOutcome.Resolved resolved)
                return lookup is PlatformTypeLookupOutcome.Missing
                    ? null
                    : lookup;

            var normalizedCandidate = resolved.Candidate.Type
                .ToMetadataFullName()
                .Replace('+', '.');
            var exactMatch = normalizedCandidate.Equals(
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase);
            var suffixStart = normalizedCandidate.Length - normalizedTarget.Length;
            var unqualifiedMatch = (allowSimpleName || normalizedTarget.Contains('.'))
                && suffixStart > 0
                && normalizedCandidate[suffixStart - 1] == '.'
                && normalizedCandidate.EndsWith(
                    normalizedTarget,
                    StringComparison.OrdinalIgnoreCase);
            return exactMatch || unqualifiedMatch
                ? resolved
                : null;
        }

        private static (
            string TypeTarget,
            string MemberSelector,
            PlatformTypeLookupOutcome Lookup)?
            LookupExactGenericPlatformMember(
                string target,
                string? frameworkSpec)
        {
            (
                string TypeTarget,
                string MemberSelector,
                PlatformTypeLookupOutcome.Resolved Lookup)? resolved = null;
            var genericDepth = 0;
            var probes = 0;
            for (var i = 0;
                i < target.Length
                && probes < MaxTypeMemberBoundaryProbes;
                i++)
            {
                if (target[i] == '<')
                {
                    genericDepth++;
                    continue;
                }
                if (target[i] == '>')
                {
                    genericDepth--;
                    continue;
                }
                if (target[i] != '.'
                    || genericDepth != 0
                    || i == 0
                    || i == target.Length - 1)
                {
                    continue;
                }

                var typeTarget = target[..i];
                if (!TypeMatcher.HasExplicitGenericNotation(
                        typeTarget))
                {
                    continue;
                }

                probes++;
                var lookup = LookupExactGenericPlatformType(
                    typeTarget,
                    allowSimpleName: true,
                    frameworkSpec: frameworkSpec);
                if (lookup
                    is PlatformTypeLookupOutcome.Resolved exact)
                {
                    resolved = (
                        typeTarget,
                        target[(i + 1)..],
                        exact);
                    continue;
                }

                if (resolved is not null)
                    return resolved;
                if (lookup is not null)
                {
                    return (
                        typeTarget,
                        target[(i + 1)..],
                        lookup);
                }
                return null;
            }

            return resolved;
        }

        private static bool WritePlatformTypeLookupFailure(
            PlatformTypeLookupOutcome? lookup)
        {
            switch (lookup)
            {
                case PlatformTypeLookupOutcome.Ambiguous ambiguous:
                    CommandError.Write(
                        $"Platform type lookup is ambiguous across {ambiguous.Candidates.Length} candidates.");
                    return true;
                case PlatformTypeLookupOutcome.Rejected rejected:
                    CommandError.Write(
                        $"Platform type lookup failed ({rejected.Failure.Kind}).");
                    return true;
                default:
                    return false;
            }
        }

        private static string[] RouteExactGenericPlatformType(
            PlatformTypeLookupOutcome.Resolved resolved,
            string target,
            string[] tail) =>
            !HasExplicitApiSource(tail)
            && TryGetExplicitPlatformSource(resolved, out var assembly, out var framework)
                ? [
                    "type",
                    target,
                    "--platform",
                    assembly,
                    .. FrameworkArgsUnlessSpecified(framework, tail),
                    .. tail
                ]
                : ["type", target, .. tail];

        private static string[] RouteExactGenericPlatformMember(
            (
                string TypeTarget,
                string MemberSelector,
                PlatformTypeLookupOutcome.Resolved Resolved) member,
            string[] tail) =>
            !HasExplicitApiSource(tail)
            && TryGetExplicitPlatformSource(
                member.Resolved,
                out var assembly,
                out var framework)
                ? [
                    "member",
                    member.TypeTarget,
                    "--platform",
                    assembly,
                    .. FrameworkArgsUnlessSpecified(framework, tail),
                    "-m",
                    member.MemberSelector,
                    .. tail
                ]
                : [
                    "member",
                    member.TypeTarget,
                    "-m",
                    member.MemberSelector,
                    .. tail
                ];

        private static string[] RouteExactGenericPlatformMemberTarget(
            PlatformTypeLookupOutcome.Resolved resolved,
            string target,
            string[] tail) =>
            !HasExplicitApiSource(tail)
            && TryGetExplicitPlatformSource(
                resolved,
                out var assembly,
                out var framework)
                ? [
                    "member",
                    target,
                    "--platform",
                    assembly,
                    .. FrameworkArgsUnlessSpecified(framework, tail),
                    .. tail
                ]
                : ["member", target, .. tail];

        private static bool HasExplicitApiSource(string[] tokens) =>
            ContainsOption(tokens, "--package")
            || ContainsOption(tokens, "--library")
            || ContainsOption(tokens, "--platform")
            || ContainsOption(tokens, "--project");

        private static bool TryGetLibraryValue(
            string[] tokens,
            RootCommand rootCommand,
            out string value)
        {
            for (var i = 0; i < tokens.Length; i++)
            {
                if (TryGetAttachedOptionValue(
                        tokens[i],
                        "--library",
                        out value))
                {
                    return true;
                }

                if (tokens[i].Equals(
                        "--library",
                        StringComparison.Ordinal)
                    && i + 1 < tokens.Length
                    && !IsKnownOption(rootCommand, tokens[i + 1]))
                {
                    value = tokens[i + 1];
                    return true;
                }
            }

            value = "";
            return false;
        }

        private static bool IsKnownOption(
            RootCommand rootCommand,
            string token) =>
            FindKnownOption(rootCommand, token) is not null;

        private static Option? FindKnownOption(
            RootCommand rootCommand,
            string token)
        {
            var optionName = GetOptionName(token);
            return rootCommand.Options
                .Concat(rootCommand.Subcommands.SelectMany(
                    static command => command.Options))
                .FirstOrDefault(
                    option => MatchesOption(option, optionName));
        }

        private static bool IsExplicitSourceIdentity(
            string target,
            string[] tokens,
            string option) =>
            GetOptionValue(tokens, option) is { Length: > 0 } source
            && target.Equals(source, StringComparison.OrdinalIgnoreCase);

        private static bool TryRouteExplicitSourceTarget(
            string target,
            string[] tokens,
            string option,
            bool hasTypeOption,
            bool hasMemberOption,
            RootCommand rootCommand,
            bool structuralSchema,
            out string[] rewritten)
        {
            rewritten = [];
            if (!IsExplicitSourceIdentity(target, tokens, option)
                || tokens.Length == 0)
            {
                return false;
            }

            var withoutSourceIdentity =
                RemoveOptionWithValue(tokens, option, target);
            if (!TryFindPositionalIndex(
                    withoutSourceIdentity,
                    rootCommand,
                    out var targetIndex))
                return false;

            if (targetIndex < 0)
            {
                if (hasTypeOption)
                {
                    rewritten =
                    [
                        "type",
                        option,
                        target,
                        .. withoutSourceIdentity
                    ];
                    return true;
                }

                if (hasMemberOption)
                {
                    rewritten =
                    [
                        "member",
                        option,
                        target,
                        .. withoutSourceIdentity
                    ];
                    return true;
                }

                return false;
            }

            var targetToken = withoutSourceIdentity[targetIndex];
            if (NuGetVersion.TryParse(targetToken, out _))
                return false;

            string[] remainingTokens =
            [
                .. withoutSourceIdentity[..targetIndex],
                .. withoutSourceIdentity[(targetIndex + 1)..]
            ];
            string[] sourceTail =
            [
                option,
                target,
                .. remainingTokens
            ];

            if (hasTypeOption)
            {
                rewritten = ["type", targetToken, .. sourceTail];
                return true;
            }

            if (hasMemberOption)
            {
                rewritten = MemberOptionOwnsTarget(targetToken)
                    ? ["member", targetToken, .. sourceTail]
                    : targetToken.Contains('.')
                        ? RouteDeferredTypeOrMember(
                            targetToken,
                            sourceTail)
                        : ["type", targetToken, .. sourceTail];
                return true;
            }

            if (structuralSchema
                && StructuralViewRegistry
                    .HasUnambiguousMemberTail(targetToken))
            {
                rewritten =
                    [MemberCommand.Name, targetToken, .. sourceTail];
                return true;
            }

            bool structurallyProvenGenericType =
                structuralSchema
                && StructuralViewRegistry
                    .HasExplicitGenericTypeTail(targetToken)
                && !StructuralViewRegistry
                    .HasGenericTypeAndGenericTailAmbiguity(targetToken)
                && !StructuralViewRegistry
                    .RequiresGenericTailMemberAlternative(
                        targetToken,
                        tokens)
                && !StructuralViewRegistry
                    .HasUnambiguousMemberTail(targetToken);
            rewritten =
                structurallyProvenGenericType
                    ? ["type", targetToken, .. sourceTail]
                    : targetToken.Contains('.')
                        ? RouteDeferredTypeOrMember(
                            targetToken,
                            sourceTail)
                        : ["type", targetToken, .. sourceTail];
            return true;
        }

        private static bool TryFindPositionalIndex(
            string[] tokens,
            RootCommand rootCommand,
            out int index)
        {
            index = -1;
            for (var i = 0; i < tokens.Length; i++)
            {
                var option = FindKnownOption(rootCommand, tokens[i]);
                if (option is null)
                {
                    if (tokens[i].StartsWith('-'))
                        continue;

                    index = i;
                    return true;
                }

                if (tokens[i].AsSpan().IndexOfAny('=', ':') >= 0)
                    continue;

                if (option.ValueType == typeof(bool))
                {
                    if (i + 1 < tokens.Length
                        && bool.TryParse(tokens[i + 1], out _))
                    {
                        i++;
                    }
                    continue;
                }

                var remainingValues =
                    option.AllowMultipleArgumentsPerToken
                        ? option.Arity.MaximumNumberOfValues
                        : Math.Min(
                            1,
                            option.Arity.MaximumNumberOfValues);
                while (remainingValues > 0
                    && i + 1 < tokens.Length
                    && FindKnownOption(rootCommand, tokens[i + 1])
                        is null)
                {
                    i++;
                    remainingValues--;
                }
            }

            return true;
        }

        internal static string? GetSecondaryPositionalTarget(
            string[] tokens,
            RootCommand rootCommand)
        {
            string[] tail = tokens[1..];
            return TryFindPositionalIndex(
                    tail,
                    rootCommand,
                    out int index)
                && index >= 0
                    ? tail[index]
                    : null;
        }

        private static bool MemberOptionOwnsTarget(string target)
        {
            if (!TypeMatcher.HasExplicitGenericNotation(target))
                return true;

            return FqnParser.LastTopLevelDot(target) < 0;
        }

        private static string GetOptionName(string token)
        {
            var separator = token.AsSpan().IndexOfAny('=', ':');
            return separator < 0
                ? token
                : token[..separator];
        }

        private static bool MatchesOption(
            Option option,
            string optionName) =>
            option.Name.Equals(
                optionName,
                StringComparison.OrdinalIgnoreCase)
            || option.Aliases.Contains(
                optionName,
                StringComparer.OrdinalIgnoreCase);

        private static string[] RemoveOptionWithValue(
            string[] tokens,
            string option,
            string value)
        {
            var rewritten = new List<string>(tokens.Length);
            var removed = false;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (!removed
                    && tokens[i].Equals(option, StringComparison.Ordinal)
                    && i + 1 < tokens.Length
                    && tokens[i + 1].Equals(
                        value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    removed = true;
                    continue;
                }

                if (!removed
                    && TryGetAttachedOptionValue(
                        tokens[i],
                        option,
                        out var attachedValue)
                    && attachedValue.Equals(
                        value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    removed = true;
                    continue;
                }

                rewritten.Add(tokens[i]);
            }

            return [.. rewritten];
        }

        private static bool TryGetAttachedOptionValue(
            string token,
            string option,
            out string value)
        {
            if (token.Length > option.Length
                && token.StartsWith(option, StringComparison.Ordinal)
                && token[option.Length] is '=' or ':')
            {
                value = token[(option.Length + 1)..];
                return true;
            }

            value = "";
            return false;
        }

        private static string[] FrameworkArgsUnlessSpecified(
            string framework,
            string[] tokens) =>
            ContainsOption(tokens, "--framework")
                ? []
                : ["--framework", framework];

        private static bool TryGetExplicitPlatformSource(
            PlatformTypeLookupOutcome.Resolved resolved,
            out string assembly,
            out string framework)
        {
            assembly = resolved.Candidate.Assembly.Identity.Name;
            framework = "";
            if (resolved.Candidate.Assembly.Provenance
                is not AssemblyResolutionProvenance.PlatformAsset platform)
            {
                return false;
            }

            framework = platform.Framework;

            // Runtime reference extraction does not currently materialize nested
            // generic types that are implemented by the core library.
            return !platform.Framework.Equals(
                    "runtime",
                    StringComparison.OrdinalIgnoreCase)
                || resolved.Candidate.Type.Segments.Length == 1;
        }
    }
}
