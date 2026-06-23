using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the hidden catch-all router. It captures raw bare-mode tokens, rewrites
/// to a real command, and lets that command own parsing and option semantics.
/// </summary>
public static class RouterCommandDefinition
{
    public static Command Create(RootCommand rootCommand)
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

            if (TryGetCommandTypoSuggestion(tokens[0]) is { } suggestion)
            {
                Console.Error.WriteLine($"Error: Unknown command '{tokens[0]}'.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Did you mean:");
                Console.Error.WriteLine($"  {suggestion}");
                return 1;
            }

            RequestTelemetry.Breadcrumb("router-hit", string.Join(' ', tokens));
            var rewritten = await RouterTokenRewriter.RewriteAsync(tokens);
            RequestTelemetry.Breadcrumb(
                "router-rewrite",
                $"{string.Join(' ', tokens)} -> {string.Join(' ', rewritten)}");

            if (rewritten.Length == tokens.Length && rewritten.SequenceEqual(tokens))
            {
                Console.Error.WriteLine($"Error: Could not route '{tokens[0]}'.");
                return 1;
            }

            return await rootCommand.Parse(rewritten).InvokeAsync();
        });

        return routerCommand;
    }

    private static readonly string[] CommandSuggestionNames =
    [
        PackageCommand.Name,
        ProjectCommand.Name,
        "library",
        TypeCommand.Name,
        MemberCommand.Name,
        DiffCommand.Name,
        FindCommand.Name,
        SourceCommand.Name,
        "extensions",
        "implements",
        "depends",
        "cache",
        "completion",
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

    private static class RouterTokenRewriter
    {
        public static async Task<string[]> RewriteAsync(string[] tokens)
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
                || ContainsOption(tokens, "--versions");
            if (hasVersionQuery || target.Contains('@'))
                return ["package", .. tokens];

            var context = new CommandContext(verbose: false);
            if (PlatformResolver.IsPlatformCandidate(target))
            {
                var (resolvedPath, _, _, resolvedError) = await PlatformResolver.ResolveAssemblyAsync(
                    target,
                    context.HttpClient,
                    context.Logger.Log);
                if (resolvedPath != null && resolvedError == null)
                {
                    if (target.Count(c => c == '.') >= 2 && PlatformResolver.IsFacadeOnlyAssembly(resolvedPath))
                        return ["type", target, .. tail];

                    return ["library", target, .. tail];
                }
            }

            var allowPlatformPrefixFallback = PlatformResolver.IsPlatformCandidate(target);
            var memberSplit = SharedParsers.TrySplitQualifiedTypeMember(target, allowPlatformPrefixFallback);
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
                sourceOptions: NuGetSourceOptions.Default,
                context.HttpClient,
                context.Logger);
            if (memberFind.Status == TypeFindIfMissStatus.Found)
            {
                var match = memberFind.TypeResolution.Match!;
                var memberSelector = memberFind.OverloadIndex.HasValue
                    ? $"{memberFind.MemberName}:{memberFind.OverloadIndex.Value}"
                    : memberFind.MemberName;
                return ["member", match.FullName, "--platform", match.Library, .. FrameworkArgs(match.Source), "-m", memberSelector, .. tail];
            }

            var typeProbe = SourceResolver.TryResolveQualifiedTypeName(target, allowPlatformPrefixFallback);
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
                sourceOptions: NuGetSourceOptions.Default,
                context.HttpClient,
                context.Logger);
            if (typeFind.Status == TypeFindIfMissStatus.Found)
            {
                var match = typeFind.Match!;
                Console.Error.WriteLine($"Note: Type '{target}' resolved via platform find to {match.FullName} in {match.Library}.");
                return ["type", match.FullName, "--platform", match.Library, .. FrameworkArgs(match.Source), .. tail];
            }

            if (PlatformResolver.IsPlatformCandidate(target))
            {
                if (await PackageExistsAsync(target, context))
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

        private static async Task<bool> PackageExistsAsync(string packageName, CommandContext context)
        {
            if (NuGetCache.TryGetLatestCachedVersion(packageName) != null)
                return true;

            try
            {
                var versions = await PackageExtractor.GetVersionsAsync(
                    context.HttpClient,
                    packageName,
                    includePrerelease: true,
                    limit: 1,
                    log: context.Logger.Log,
                    sourceOptions: NuGetSourceOptions.Default);
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
