using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;
using ILInspector.Text;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the hidden catch-all router. It captures raw bare-mode tokens, rewrites
/// to a real command, and lets that command own parsing and option semantics.
/// </summary>
public static class RouterCommandDefinition
{
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

            if (ContainsHelpOption(tokens) && !tokens[0].StartsWith('-'))
            {
                CommandError.WriteNote($"interpreting bare token '{tokens[0]}' as a package or platform target.");
                CommandError.WriteLine("      Use 'dotnet-inspect --help' to list commands, or 'dotnet-inspect package --help' for package help.");
                CommandError.WriteBlankLine();
            }

            RequestTelemetry.Breadcrumb("router-hit", string.Join(' ', tokens));
            var rewritten = await RouterTokenRewriter.RewriteAsync(tokens, sourceOptions);
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
            return await CommandLineBuilder.InvokeAsync(rootCommand.Parse(rewritten));
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
        "skill"
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
        public static async Task<string[]> RewriteAsync(
            string[] tokens,
            NuGetSourceOptions sourceOptions)
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

            if (ContainsOption(tokens, "--member") || ContainsOption(tokens, "-m"))
                return ["member", target, .. tail];

            if (ContainsOption(tokens, "--library"))
                return ["package", .. tokens];

            if (tokens.Length >= 2
                && !tokens[1].StartsWith('-')
                && !CommandLineHelpers.LooksLikeVersionNumber(tokens[1]))
            {
                return ["type", tokens[1], "--package", target, .. tokens[2..]];
            }

            var hasVersionQuery = ContainsOption(tokens, "--version")
                || ContainsOption(tokens, "--latest-version")
                || ContainsOption(tokens, "--versions")
                || ContainsOption(tokens, "--versions-with-feed");
            if (hasVersionQuery || target.Contains('@'))
                return ["package", .. tokens];

            var sourceKeys = NuGetSourceResolver.ResolveSourceKeys(sourceOptions);
            var context = new CommandContext(verbose: false);
            if (PlatformResolver.IsPlatformCandidate(target))
            {
                var (resolvedPath, _, _, resolvedError) = await PlatformResolver.ResolveAssemblyAsync(
                    target,
                    context.HttpClient,
                    context.Logger.Log);
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
            string? platformLookupFailure = null;
            var memberSplit = SharedParsers.TrySplitQualifiedTypeMember(
                target,
                sourceKeys,
                allowPlatformPrefixFallback,
                message => platformLookupFailure = message);
            if (platformLookupFailure is not null)
            {
                CommandError.Write(platformLookupFailure);
                return tokens;
            }
            if (memberSplit != null)
            {
                var probe = memberSplit.Value.Probe;
                if (probe.Kind == SourceResolver.LocalSourceKind.Platform
                    && !await IsExactPlatformTypeAsync(probe, context))
                {
                    memberSplit = null;
                }
            }

            if (memberSplit != null)
            {
                var probe = memberSplit.Value.Probe;
                RequestTelemetry.Breadcrumb(
                    "qualified-member",
                    $"{target} -> source={probe.SourceName}; type={probe.Remainder}; member={memberSplit.Value.MemberName}");

                return probe.Kind == SourceResolver.LocalSourceKind.Platform
                    ? ["member", probe.Remainder, "--platform", probe.SourceName, "-m", memberSplit.Value.MemberName, .. tail]
                    : ["member", probe.Remainder, "--package", probe.SourceName, "-m", memberSplit.Value.MemberName, .. tail];
            }

            var memberFind = await TypeFindIfMissResolver.ResolvePlatformMemberAsync(
                target,
                includeAll: false,
                sourceOptions,
                context.HttpClient,
                context.Logger);
            if (memberFind.Status == TypeFindIfMissStatus.Found)
            {
                var match = memberFind.TypeResolution.Match!;
                return ["member", match.FullName, "--platform", match.Library, .. FrameworkArgs(match.Source), "-m", memberFind.MemberSelector, .. tail];
            }

            var typeProbe = SourceResolver.TryResolveQualifiedTypeName(
                target,
                sourceKeys,
                allowPlatformPrefixFallback,
                message => platformLookupFailure = message);
            if (platformLookupFailure is not null)
            {
                CommandError.Write(platformLookupFailure);
                return tokens;
            }
            if (typeProbe != null)
            {
                if (typeProbe.Kind == SourceResolver.LocalSourceKind.Platform
                    && !await IsExactPlatformTypeAsync(typeProbe, context))
                {
                    return ["type", target, .. tail];
                }

                RequestTelemetry.Breadcrumb(
                    "qualified-type",
                    $"{target} -> source={typeProbe.SourceName}; type={typeProbe.Remainder}");

                return typeProbe.Kind == SourceResolver.LocalSourceKind.Platform
                    ? ["type", typeProbe.Remainder, "--platform", typeProbe.SourceName, .. tail]
                    : ["type", typeProbe.Remainder, "--package", typeProbe.SourceName, .. tail];
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
                return ["type", match.FullName, "--platform", match.Library, .. FrameworkArgs(match.Source), .. tail];
            }

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
                                   || token.StartsWith(option + "=", StringComparison.Ordinal));

        private static string[] FrameworkArgs(string source)
            => string.IsNullOrWhiteSpace(source) ? [] : ["--framework", source];

        private static async Task<bool> PackageExistsAsync(
            string packageName,
            NuGetSourceOptions sourceOptions,
            CommandContext context)
        {
            if (PackageExtractor.TryGetLatestCachedCandidateVersion(
                    packageName,
                    NuGetSourceResolver.ResolveSourceKeys(
                        sourceOptions),
                    includePrerelease: true) is not null)
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
    }
}
