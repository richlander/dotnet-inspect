using System.CommandLine;
using System.CommandLine.Parsing;
using CSharpText;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
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

            if (GetRouteIndependentLimitError(tokens) is { } limitError)
            {
                CommandError.Write(limitError);
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

            if (ContainsHelpOption(tokens) && !tokens[0].StartsWith('-'))
            {
                CommandError.WriteNote($"interpreting bare token '{tokens[0]}' as a package or platform target.");
                CommandError.WriteLine("      Use 'dotnet-inspect --help' to list commands, or 'dotnet-inspect package --help' for package help.");
                CommandError.WriteBlankLine();
            }

            RequestTelemetry.Breadcrumb("router-hit", string.Join(' ', tokens));
            var route = await RouteTokensAsync(
                tokens,
                sourceOptions,
                rootCommand);
            RequestTelemetry.Breadcrumb(
                "router-rewrite",
                $"{string.Join(' ', tokens)} -> {string.Join(' ', route.Arguments)}");

            if (!route.Routed)
            {
                CommandError.Write($"Could not route '{tokens[0]}'.");
                return 1;
            }

            // Invoked through the audit choke point, not ParseResult.InvokeAsync: the router
            // captures projection flags as raw tokens, so the outer invocation records nothing
            // and only this rewritten parse can tell whether a projection was honored.
            return await CommandLineBuilder.InvokeAsync(
                rootCommand.Parse(route.Arguments));
        });

        return routerCommand;
    }

    internal static async Task<(bool Routed, string[] Arguments)>
        RouteTokensAsync(
            string[] tokens,
            NuGetSourceOptions sourceOptions,
            RootCommand rootCommand)
    {
        string[] rewritten = await RouterTokenRewriter.RewriteAsync(
            tokens,
            sourceOptions,
            rootCommand);
        if (rewritten.Length == tokens.Length
            && rewritten.SequenceEqual(tokens))
        {
            return (false, rewritten);
        }

        return (
            true,
            ArgumentPreprocessor.PreprocessRoutedArgs(
                rewritten,
                rootCommand));
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

    internal static string? GetRouteIndependentLimitError(string[] tokens)
    {
        var end = Array.IndexOf(tokens, "--");
        if (end < 0)
            end = tokens.Length;

        bool count = GetBooleanOptionValue(tokens, end, "--count");
        bool hasLimit = ContainsValueOption(tokens, end, "-n");
        bool hasTop = ContainsValueOption(tokens, end, "--top");
        bool hasRows = ContainsValueOption(tokens, end, "--rows");
        bool hasRow = ContainsValueOption(tokens, end, "--row");
        bool hasHead = GetBooleanOptionValue(tokens, end, "--head");
        bool hasTail = GetBooleanOptionValue(tokens, end, "--tail");
        bool hasLines = GetBooleanOptionValue(tokens, end, "--lines");
        bool hasTailLines = GetBooleanOptionValue(tokens, end, "--tail-lines");

        if (hasTop
            && (ContainsValueOption(tokens, end, "-D")
                || ContainsValueOption(tokens, end, "--discover")))
        {
            return SharedOptions.DiscoveryTopConflictError;
        }

        return count
            && (hasLimit
                || hasTop
                || hasRows
                || hasRow
                || hasHead
                || hasTail
                || hasLines
                || hasTailLines)
            ? SharedOptions.CountWindowConflictError
            : null;
    }

    private static bool ContainsValueOption(
        string[] tokens,
        int end,
        string option)
    {
        for (var i = 0; i < end; i++)
        {
            string token = tokens[i];
            if (token.Equals(option, StringComparison.Ordinal)
                || token.StartsWith(option + "=", StringComparison.Ordinal)
                || token.StartsWith(option + ":", StringComparison.Ordinal)
                || option == "-n"
                    && token.StartsWith("-n", StringComparison.Ordinal)
                    && token.Length > 2
                    && int.TryParse(token.AsSpan(2), out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GetBooleanOptionValue(
        string[] tokens,
        int end,
        string option)
    {
        bool enabled = false;
        for (var i = 0; i < end; i++)
        {
            string token = tokens[i];
            if (token.Equals(option, StringComparison.Ordinal))
            {
                enabled = true;
                if (i + 1 < end && bool.TryParse(tokens[i + 1], out bool separated))
                {
                    enabled = separated;
                    i++;
                }
                continue;
            }

            if ((token.StartsWith(option + "=", StringComparison.Ordinal)
                    || token.StartsWith(option + ":", StringComparison.Ordinal))
                && bool.TryParse(token.AsSpan(option.Length + 1), out bool attached))
            {
                enabled = attached;
            }
        }

        return enabled;
    }

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

            if (CommandLineHelpers.TryClassifyAsFilePath(target, out var dllPath, out var nupkgPath))
            {
                if (dllPath != null)
                    return ["library", target, .. tail];
                if (nupkgPath != null)
                    return ["package", target, .. tail];
            }

            var hasExplicitGenericNotation =
                TypeMatcher.HasExplicitGenericNotation(target);
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
                out var libraryValue);
            var hasExplicitApiSource =
                ContainsOption(tail, "--package")
                || ContainsOption(tail, "--platform")
                || ContainsOption(tail, "--project")
                || hasLibraryValue;
            var hasVersionQuery = ContainsOption(tokens, "--version")
                || ContainsOption(tokens, "--latest-version")
                || ContainsOption(tokens, "--versions")
                || ContainsOption(tokens, "--versions-with-feed");
            bool libraryValueIsLimitShorthand =
                hasLibraryValue
                && CommandLineModel.IsLimitShorthand(libraryValue);
            TryFindPositionalIndex(
                tail,
                rootCommand,
                out int deferredTargetIndex);
            if (TryRouteExplicitSourceTarget(
                    target,
                    tail,
                    "--package",
                    hasTypeOption,
                    hasMemberOption,
                    rootCommand,
                    out var explicitSourceRoute)
                || TryRouteExplicitSourceTarget(
                    target,
                    tail,
                    "--platform",
                    hasTypeOption,
                    hasMemberOption,
                    rootCommand,
                    out explicitSourceRoute))
            {
                return explicitSourceRoute;
            }

            if (libraryValueIsLimitShorthand
                && deferredTargetIndex < 0
                && !hasExplicitGenericNotation
                && !hasTypeOption
                && !hasMemberOption
                && !ContainsOption(tail, "--package")
                && !ContainsOption(tail, "--platform")
                && !ContainsOption(tail, "--project"))
            {
                return ["package", .. tokens];
            }

            if (hasTypeOption && hasExplicitApiSource)
            {
                return ["type", target, .. tail];
            }

            if (hasMemberOption
                && (!hasExplicitGenericNotation
                    || (hasExplicitApiSource
                        && trailingSegmentHasGenericNotation
                        && trailingSegmentStart == 0)))
                return ["member", target, .. tail];

            if (hasExplicitApiSource
                && TrySplitOperatorMemberTarget(
                    target,
                    out var operatorType,
                    out var operatorMember))
            {
                return
                [
                    "member",
                    operatorType,
                    "-m",
                    operatorMember,
                    .. tail
                ];
            }

            if (IsExplicitSourceIdentity(target, tail, "--package"))
            {
                return
                [
                    "package",
                    target,
                    .. RemoveOptionWithValue(
                        tail,
                        "--package",
                        target)
                ];
            }

            if (IsExplicitSourceIdentity(target, tail, "--platform"))
                return ["library", target, .. tail];

            if (hasLibraryValue
                && !hasExplicitGenericNotation
                && !ContainsOption(tail, "--package")
                && !ContainsOption(tail, "--platform")
                && !ContainsOption(tail, "--project")
                && IsPackageRelativeLibraryValue(libraryValue))
            {
                return ["package", .. tokens];
            }

            if (hasExplicitApiSource)
            {
                return target.Contains('.')
                    ? RouteDeferredTypeOrMember(target, tail)
                    : ["type", target, .. tail];
            }

            if (ContainsOption(tokens, "--library"))
                return ["package", .. tokens];

            if (tokens.Length >= 2
                && !tokens[1].StartsWith('-')
                && !CommandLineHelpers.LooksLikeVersionNumber(tokens[1]))
            {
                return ["type", tokens[1], "--package", target, .. tokens[2..]];
            }

            if (hasVersionQuery || target.Contains('@'))
                return ["package", .. tokens];

            if (hasExplicitGenericNotation
                && IsStaticSchemaDiscovery(tokens))
            {
                return target.Contains('.')
                    ? RouteDeferredTypeOrMember(target, tail)
                    : ["type", target, .. tail];
            }

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

        private static bool ContainsOption(string[] tokens, string option)
            => tokens.Any(token => token.Equals(option, StringComparison.Ordinal)
                                   || TryGetAttachedOptionValue(
                                       token,
                                       option,
                                       out _));

        private static bool IsStaticSchemaDiscovery(string[] tokens)
            => (ContainsOption(tokens, "--discover")
                || ContainsOption(tokens, "-D"))
               && ContainsOption(tokens, "--schema");

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
            if (!MemberTargetSelector.Parse(memberSelector).Name.StartsWith(
                    "op_",
                    StringComparison.Ordinal))
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
            CommandLineModel.FindOptions(rootCommand, token).Any();

        private static bool IsPackageRelativeLibraryValue(string value)
        {
            if (value.StartsWith('-'))
                return false;

            if (SourceResolver.IsLibrarySelector(value, package: null))
                return true;

            return IsPackageRelativeLibraryPath(value)
                && value.EndsWith(
                    ".dll",
                    StringComparison.OrdinalIgnoreCase)
                && !IsExplicitLibraryPath(value);
        }

        private static bool IsPackageRelativeLibraryPath(string value)
        {
            // A hygienic relative asset path is authoritative even when the target
            // resembles a type; consulting cwd would make routing nondeterministic.
            return PackageCoordinateResolver.IsPackageRelativeAssetPath(value);
        }

        private static bool IsExplicitLibraryPath(string value) =>
            Path.IsPathRooted(value)
            || (value.Length > 0 && value[0] is '/' or '\\')
            || value.StartsWith("./", StringComparison.Ordinal)
            || value.StartsWith(@".\", StringComparison.Ordinal)
            || value.StartsWith("../", StringComparison.Ordinal)
            || value.StartsWith(@"..\", StringComparison.Ordinal)
            || (value.Length >= 2
                && char.IsAsciiLetter(value[0])
                && value[1] == ':');

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

            rewritten = targetToken.Contains('.')
                ? RouteDeferredTypeOrMember(targetToken, sourceTail)
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
                Option[] options =
                [
                    .. CommandLineModel.FindOptions(
                        rootCommand,
                        tokens[i])
                ];
                if (options.Length == 0)
                {
                    if (tokens[i].StartsWith('-'))
                        continue;

                    index = i;
                    return true;
                }

                if (CommandLineModel.HasAttachedValue(tokens[i]))
                    continue;

                string? nextToken = i + 1 < tokens.Length
                    ? tokens[i + 1]
                    : null;
                int remainingValues = nextToken is null
                    ? 0
                    : options.Max(option =>
                        !CommandLineModel.CanConsumeFollowingToken(option, nextToken)
                            ? 0
                            : option.AllowMultipleArgumentsPerToken
                                ? option.Arity.MaximumNumberOfValues
                                : Math.Min(
                                    1,
                                    option.Arity.MaximumNumberOfValues));
                while (remainingValues > 0
                    && i + 1 < tokens.Length
                    && !IsKnownOption(rootCommand, tokens[i + 1]))
                {
                    i++;
                    remainingValues--;
                }
            }

            return true;
        }

        private static bool MemberOptionOwnsTarget(string target)
        {
            if (!TypeMatcher.HasExplicitGenericNotation(target))
                return true;

            return FqnParser.LastTopLevelDot(target) < 0;
        }

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
