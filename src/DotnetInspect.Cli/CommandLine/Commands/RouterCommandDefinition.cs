using System.CommandLine;
using System.CommandLine.Parsing;
using CSharpText;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using NuGet.Versioning;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.CommandLine;

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

    internal static bool IsAcquisitionFreePackageRoute(string[] tokens, RootCommand rootCommand) =>
        RouterTokenRewriter.TryRewriteAcquisitionFree(
            tokens, rootCommand, structuralSchema: false, out string[] rewritten)
        && rewritten.SequenceEqual(new[] { PackageCommand.Name }.Concat(tokens));

    public static Command Create(
        RootCommand rootCommand,
        SharedOptions opts,
        TypeOptionsParser.TypeCommandArgs typeArgs,
        MemberOptionsParser.MemberCommandArgs memberArgs,
        PackageOptionsParser.PackageCommandArgs packageArgs,
        IReadOnlyList<Command> rowSelectionCommands)
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

            WorkspaceSharePacketRecognition recognition =
                WorkspaceSharePacketCodec.Recognize(tokens[0], ct);
            if (recognition is WorkspaceSharePacketRecognition.Invalid invalidPacket)
            {
                CommandError.Write($"Invalid Workspace packet ({invalidPacket.Kind}): {invalidPacket.Message}");
                return 1;
            }
            if (recognition is WorkspaceSharePacketRecognition.Recognized)
            {
                string[] activation =
                    [WorkspaceActivationCommandDefinitions.CommandName, .. tokens];
                return await CommandLineBuilder.InvokeWithLineWindowAsync(
                    rootCommand.Parse(activation), activation);
            }

            if (PackageReferenceAdmission.IsCandidate(tokens[0]))
            {
                string[] referenceRoute = CommandLineBuilder.PreprocessArgs(
                    [PackageCommand.Name, .. tokens], rootCommand);
                RouterDecisionLog.Record("exact-package-reference", "package");
                return await CommandLineBuilder.InvokeWithLineWindowAsync(
                    rootCommand.Parse(referenceRoute), referenceRoute);
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
            if (PackageOptionsParser.GetUnrecognizedOption(
                    sourceParseResult,
                    packageArgs) is { } removedOption
                && ArgumentPreprocessor.GetRemovedPackageOptionError(removedOption)
                    is { } removed)
            {
                CommandError.Write(removed);
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
                    is { Implicit: false }
                || opts.IsQueryHelpMode(sourceParseResult);
            if (hasSectionRequest)
            {
                List<ParseError> requestErrors =
                    GetStructuralRequestErrors(
                        sourceParseResult,
                        opts);
                if (requestErrors.Count > 0)
                {
                    foreach (ParseError error in requestErrors)
                        CommandError.Write(CommandLineBuilder.FormatParseError(error.Message));
                    return 1;
                }
            }

            if (opts.IsQueryHelpMode(sourceParseResult)
                || opts.ParseSelect(sourceParseResult)?.Any(value =>
                    value.StartsWith("Query:", StringComparison.OrdinalIgnoreCase)) == true
                || opts.ParseDiscover(sourceParseResult)?.Any(value =>
                    value.StartsWith("Query:", StringComparison.OrdinalIgnoreCase)) == true)
            {
                if (RouterTokenRewriter.TryRewriteAcquisitionFree(
                        tokens, rootCommand, structuralSchema: true, out string[] queryRewrite))
                {
                    queryRewrite = CommandLineBuilder.PreprocessArgs(queryRewrite, rootCommand);
                    return await CommandLineBuilder.InvokeWithLineWindowAsync(
                        rootCommand.Parse(queryRewrite), queryRewrite);
                }
                CommandError.Write("Query discovery requires an explicit command; use 'library -Q', 'type -Q', 'member -Q', 'package -Q', or 'find -Q'.");
                return 1;
            }

            if (ContainsHelpOption(tokens)
                && !tokens[0].StartsWith('-')
                && !RouterTokenRewriter.HasExplicitApiSource(tokens)
                && !RouterTokenRewriter.ContainsOption(tokens, "--framework"))
            {
                CommandError.WriteNote($"interpreting bare token '{tokens[0]}' as a package or platform target.");
                CommandError.WriteLine("      Use 'dotnet-inspect --help' to list commands, or 'dotnet-inspect package --help' for package help.");
                CommandError.WriteBlankLine();
            }

            RouterDecisionLog.Record("router-hit", string.Join(' ', tokens));
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
                if (structuralRewrite.Contains(
                        DeferredTypeOrMemberOptionName,
                        StringComparer.Ordinal))
                {
                    // Member uses repeatable --project for caller scope; preserve the
                    // Type source option's scalar diagnostic before the deferred rewrite.
                    ParseResult typeInterpretation =
                        rootCommand.Parse([TypeCommand.Name, .. tokens]);
                    if (GetOptionParseError(
                            typeInterpretation,
                            typeArgs.ProjectOption) is { } projectError)
                    {
                        CommandError.Write(
                            CommandLineBuilder.FormatParseError(
                                projectError.Message));
                        return 1;
                    }
                }

                structuralRewrite =
                    CommandLineBuilder.PreprocessArgs(
                        structuralRewrite,
                        rootCommand);
                RouterDecisionLog.Record(
                    "router-structural",
                    $"syntax: {string.Join(' ', structuralRewrite)}");
                return await CommandLineBuilder.InvokeWithLineWindowAsync(
                    rootCommand.Parse(structuralRewrite),
                    structuralRewrite);
            }

            bool hasBareLibraryTarget =
                RouterTokenRewriter.ContainsOption(tokens, "--library")
                && !RouterTokenRewriter.TryGetLibraryValue(
                    tokens,
                    rootCommand,
                    out _);
            if (StructuralViewRegistry.TryClassifyCommandless(
                    tokens,
                    structuralDiscovery,
                    hasBareLibraryTarget,
                    out CommandlessStructuralRoute? structuralRoute))
            {
                if (!structuralDiscovery
                    && TryWriteRowSelectionFailure(
                        tokens,
                        rootCommand,
                        rowSelectionCommands))
                {
                    return 1;
                }

                string[] structuralTokens =
                    CommandLineBuilder.PreprocessArgs(
                        structuralRoute!.RewrittenTokens,
                        rootCommand);
                RouterDecisionLog.Record(
                    "router-structural",
                    $"{structuralRoute.Route.Label}: "
                    + string.Join(' ', structuralTokens));
                return await CommandLineBuilder.InvokeWithLineWindowAsync(
                    rootCommand.Parse(structuralTokens),
                    structuralTokens);
            }

            if (structuralDiscovery)
            {
                ParseResult analysisParseResult = rootCommand.Parse([MemberCommand.Name, .. tokens]);
                OptionError? analysisError = SharedParsers.GetOptionParseError(analysisParseResult);
                analysisError ??= SharedParsers.ParseAnalysisQueryOptions(
                    analysisParseResult, opts, typeScoped: false, typeName: null, out _, out _, out _);
                analysisError ??= MemberOptionsParser.GetMermaidOptionError(analysisParseResult, opts);
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
                var optionErrors = new Dictionary<StructuralRoute, OptionError?>();
                string[] interpretationTokens = tokens;
                foreach (StructuralRoute route in alternatives.Alternatives
                    .Select(alternative => alternative.Route)
                    .Distinct())
                {
                    string command = route.View.DestinationCommand;
                    ParseResult interpretation = rootCommand.Parse([command, .. interpretationTokens]);
                    OptionError? optionError = SharedParsers.GetStructuralParseError(interpretation);
                    if (optionError is null)
                    {
                        string typeTarget = sourceIdentityTypeTarget ?? tokens[0];
                        if (command == TypeCommand.Name)
                        {
                            TypeOptionsParser.TryCreateStructuralPlan(
                                interpretation, opts, typeArgs,
                                out _, out optionError, out _,
                                interpretedTypeTarget: typeTarget,
                                interpretedCatalog: route.Catalog);
                        }
                        else if (command == MemberCommand.Name)
                        {
                            MemberOptionsParser.TryCreateStructuralPlan(
                                interpretation, opts, memberArgs,
                                out _, out optionError, out _,
                                interpretedTypeTarget: typeTarget);
                        }
                        else if (command == PackageCommand.Name)
                        {
                            optionError = PackageOptionsParser.Parse(interpretation, opts, packageArgs) switch
                            {
                                PackageOptionsParser.Success success =>
                                    PackageCommand.GetLibraryInspectionModeError(
                                        success.Options,
                                        allowStaticDiscovery: true),
                                PackageOptionsParser.UnrecognizedOption unknown =>
                                    new OptionError($"Unrecognized option '{unknown.Option}'."),
                                PackageOptionsParser.InvalidArguments invalid =>
                                    new OptionError(invalid.Message),
                                _ => throw new InvalidOperationException("Unexpected package parse result."),
                            };
                        }
                        else if (command == "library")
                        {
                            optionError = LibraryCommand.GetDiscoveryModeError(
                                interpretation.GetValue(opts.Effective),
                                opts.IsDiscoveryMode(interpretation),
                                opts.ParseSchema(interpretation));
                        }
                    }
                    optionErrors.Add(route, optionError);
                }
                alternatives = new StructuralCatalogAlternatives(
                    [.. alternatives.Alternatives.Select(alternative =>
                        optionErrors[alternative.Route] is { } error
                            ? alternative with
                            {
                                CompleteCatalog = false,
                                ResolvedSections = [],
                                Error = error,
                            }
                            : alternative)]);
                RouterDecisionLog.Record(
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

            if (TryWriteRowSelectionFailure(
                    tokens,
                    rootCommand,
                    rowSelectionCommands))
            {
                return 1;
            }

            RouterRewrite? selection = await RouterTokenRewriter.RewriteSelectedAsync(
                tokens,
                sourceOptions,
                rootCommand,
                ct);
            if (selection is null)
                return 1;
            string[] rewritten = selection.Tokens;
            RouterDecisionLog.Record(
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
            ParseResult childParse = rootCommand.Parse(rewritten);
            if (childParse.Errors.Count == 0
                && !RouterTokenRewriter.HasExplicitApiSource(tokens)
                && !RouterTokenRewriter.ContainsOption(tokens, "--framework")
                && !opts.IsDiscoveryMode(sourceParseResult)
                && !CommandLineBuilder.HasParsedOption(childParse, "--workspace")
                && !CommandLineBuilder.HasParsedOption(childParse, "--version"))
            {
                RouterEcosystemExplanation.Write(
                    tokens[0], rewritten[0], selection.SelectedPlatformFramework);
            }
            return await CommandLineBuilder.InvokeWithLineWindowAsync(
                childParse,
                rewritten,
                selection.ResolvedTarget);
        });

        return routerCommand;
    }

    private static bool TryWriteRowSelectionFailure(
        string[] tokens,
        RootCommand rootCommand,
        IReadOnlyList<Command> rowSelectionCommands)
    {
        IReadOnlyList<Command> rowSelectionCandidates =
            GetRowSelectionCandidates(
                tokens,
                rootCommand,
                rowSelectionCommands);
        bool deferLegacyWindow =
            CliRowSelectionRouterPreflight.ShouldDeferLegacyWindow(
                tokens,
                rowSelectionCandidates);

        if (CliRowSelectionRouterPreflight.FindCommonOptionValueError(
                tokens,
                rowSelectionCandidates) is { } optionValueError)
        {
            CommandError.Write(optionValueError);
            return true;
        }

        CliRowSelectionRouteEnvelopeResult rowSelection =
            CliRowSelectionRouterPreflight.Evaluate(
                tokens,
                rowSelectionCandidates,
                deferLegacyWindow);
        if (deferLegacyWindow
            && rowSelection.Outcome
                == CliRowSelectionRouteEnvelopeOutcome.NoRequest)
        {
            return false;
        }
        RouterDecisionLog.Record(
            "router-row-selection",
            rowSelection.Outcome.ToString());
        return CliRowSelectionRouterPreflight.TryWriteFailure(rowSelection);
    }

    internal static IReadOnlyList<Command> GetRowSelectionCandidates(
        string[] tokens,
        RootCommand rootCommand,
        IReadOnlyList<Command> commands)
    {
        if (RouterTokenRewriter.TryRewriteAcquisitionFree(
                tokens,
                rootCommand,
                structuralSchema: false,
                out string[] rewritten)
            && rewritten.Length > 0)
        {
            if (rewritten.Contains(
                    DeferredTypeOrMemberOptionName,
                    StringComparer.Ordinal))
            {
                return
                [
                    .. commands.Where(command =>
                        command.Name is "type" or "member"),
                ];
            }

            if (commands.FirstOrDefault(command =>
                    command.Name.Equals(
                        rewritten[0],
                        StringComparison.Ordinal)) is { } selected)
            {
                return [selected];
            }
        }

        return commands;
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
            opts.QueryHelp,
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

    private static ParseError? GetOptionParseError(
        ParseResult parseResult,
        Option option)
    {
        OptionResult? optionResult = parseResult.GetResult(option);
        return parseResult.Errors.FirstOrDefault(error =>
            IsWithin(error.SymbolResult, optionResult));
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
        FindCommand.Name,
        "extensions",
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

    private sealed record RouterRewrite(
        string[] Tokens,
        RouterResolvedTarget? ResolvedTarget = null,
        string? SelectedPlatformFramework = null);

    private static class RouterTokenRewriter
    {
        public static async Task<RouterRewrite?> RewriteSelectedAsync(
            string[] tokens,
            NuGetSourceOptions sourceOptions,
            RootCommand rootCommand,
            CancellationToken cancellationToken)
        {
            string target = tokens[0].Trim();
            tokens = [target, .. tokens[1..]];
            string[] tail = tokens[1..];
            bool platformOverride = IsStandaloneOverride(tail, "--platform", rootCommand);
            bool packageOverride = IsStandaloneOverride(tail, "--package", rootCommand);
            if (tail.Count(token => token == "--platform"
                    || token.StartsWith("--platform=", StringComparison.Ordinal)) > 1
                || tail.Count(token => token == "--package"
                    || token.StartsWith("--package=", StringComparison.Ordinal)) > 1
                || (platformOverride && target.Contains('@')))
            {
                CommandError.Write("Choose one unambiguous bare target domain or source using --platform or --package.");
                return null;
            }
            if (ContainsOption(tail, "--platform")
                && ContainsOption(tail, "--package"))
            {
                CommandError.Write("Choose only one bare target domain or source: --platform or --package.");
                return null;
            }

            if (platformOverride || packageOverride)
            {
                tail = RemoveStandaloneOverride(tail, platformOverride ? "--platform" : "--package");
                tokens = [target, .. tail];
            }
            RouterTargetDomain domain =
                packageOverride || target.Contains('@') ? RouterTargetDomain.Package
                : platformOverride || ContainsOption(tail, "--framework")
                    ? RouterTargetDomain.Platform
                : RouterTargetAnalysis.Classify(target);

            bool needsPlatformAnalysis =
                domain == RouterTargetDomain.Platform
                && !HasExplicitApiSource(tail)
                && !ContainsOption(tail, "--framework")
                && GetSecondaryPositionalTarget(tokens, rootCommand) is null
                && !ContainsHelpOption(tokens)
                && !ContainsOption(tail, "--versions")
                && !ContainsOption(tail, "--versions-with-feed")
                && !target.Contains('@');
            if (!platformOverride && !packageOverride
                && !needsPlatformAnalysis
                && TryRewriteAcquisitionFree(
                    tokens,
                    rootCommand,
                    structuralSchema: false,
                    out string[] explicitRoute))
            {
                return new RouterRewrite(explicitRoute);
            }

            if (domain == RouterTargetDomain.Package)
                return new RouterRewrite([PackageCommand.Name, .. tokens]);

            if (ContainsOption(tail, "--framework"))
            {
                if (ContainsOption(tail, "--type") || ContainsOption(tail, "-t"))
                    return new RouterRewrite([TypeCommand.Name, target, .. tail]);

                string? framework = GetOptionValue(tail, "--framework");
                if (framework is not null
                    && PlatformResolver.ResolveAssembly(target, framework)
                        .AssemblyPath is not null)
                    return new RouterRewrite(["library", target, .. tail]);

                if (framework is null)
                    return new RouterRewrite([TypeCommand.Name, target, .. tail]);

                var (analyzed, error) =
                    await RouterTargetAnalysis.ResolveFrameworkAsync(
                        target, framework, sourceOptions, cancellationToken);
                if (analyzed is not null)
                    return RewriteResolved(analyzed, tail);
                CommandError.Write(error!);
                return null;
            }

            var context = new CommandContext(verbose: false);
            var (resolvedPath, resolvedFramework, _, assemblyError) =
                await PlatformResolver.ResolveAssemblyAsync(
                    target,
                    context.HttpClient,
                    context.Logger.Log,
                    sourceOptions: sourceOptions);
            if (resolvedPath is not null && assemblyError is null)
            {
                AssemblySurfaceClassificationOutcome classification =
                    PlatformResolver.ClassifyAssemblySurface(resolvedPath);
                if (classification is AssemblySurfaceClassificationOutcome.Rejected rejected)
                {
                    CommandError.Write(
                        $"Could not classify the platform assembly surface ({rejected.Failure.Kind}).");
                    return null;
                }

                return new RouterRewrite(
                    ["library", target, .. tail],
                    SelectedPlatformFramework: resolvedFramework);
            }

            var (locator, route) = await RouterTargetAnalysis.ResolvePlatformAsync(
                target,
                sourceOptions,
                cancellationToken);
            if (route?.Content is PlatformTypeCatalogRouteOutcome.Resolved resolved)
                return RewriteResolved(RouterResolvedTarget.From(resolved), tail);

            if (locator is CliPlatformTypeLocatorOutcome.Completed completed
                && route?.Content is PlatformTypeCatalogRouteOutcome.Missing)
            {
                var namespaceRoute =
                    PlatformTypeLocatorRouting.ResolveNamespace(
                        completed,
                        target,
                        cancellationToken);
                if (namespaceRoute.Content is PlatformNamespaceDiscoveryOutcome.Found found)
                {
                    PlatformNamespaceDiscoveryHit hit = found.Hits[0];
                    return new RouterRewrite([
                        "library", hit.Library, "--namespace", hit.Namespace,
                        "--framework", PlatformFrameworkSpec(hit.Target), .. tail,
                    ]);
                }
            }

            CommandError.Write(RouterTargetAnalysis.Failure(locator, route?.Content, target)
                ?? $"Could not route '{target}' in the selected Platform domain.");
            return null;
        }

        private static RouterRewrite RewriteResolved(
            RouterResolvedTarget analyzed,
            string[] tail)
        {
            string framework = analyzed.Framework;
            string[] source = [
                "--platform",
                analyzed.Assembly.Name,
                "--framework",
                framework,
            ];
            string[] remainder = RemoveOptionWithValue(tail, "--framework", framework);
            string[] rewritten = analyzed.Member is { } member
                ? [MemberCommand.Name, analyzed.TypeName, .. source,
                    "-m", member.RequestedText, .. remainder]
                : [TypeCommand.Name, analyzed.TypeName, .. source, .. remainder];
            return new RouterRewrite(rewritten, analyzed, framework);
        }

        private static bool IsStandaloneOverride(
            string[] tokens,
            string option,
            RootCommand rootCommand)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] != option)
                    continue;
                return i + 1 == tokens.Length
                    || IsKnownOption(rootCommand, tokens[i + 1]);
            }
            return false;
        }

        private static string[] RemoveStandaloneOverride(
            string[] tokens,
            string option) =>
            [.. tokens.Where(token => token != option)];

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
            if (ContainsOption(tokens, "--all-libraries")
                || ContainsOption(tokens, "--namesake-library")
                || ContainsOption(tokens, "--first-library"))
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
            bool hasPackageLibraryValue =
                hasLibraryValue
                && SourceResolver
                    .IsPackageLibraryValue(target, libraryValue);
            bool hasExplicitApiSource =
                ContainsOption(tail, "--package")
                || ContainsOption(tail, "--platform")
                || ContainsOption(tail, "--project")
                || (hasLibraryValue
                    && !hasPackageLibraryValue);
            bool hasVersionQuery =
                ContainsOption(tokens, "--versions")
                || ContainsOption(
                    tokens,
                    "--versions-with-feed");
            bool hasStructuralPackageAssetPath =
                hasPackageLibraryValue
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
                && hasPackageLibraryValue)
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
                && structuralSchema
                && hasExplicitGenericNotation
                && !StructuralViewRegistry
                    .HasExplicitGenericTypeTail(target)
                && !StructuralViewRegistry
                    .HasUnambiguousMemberTail(target))
            {
                return false;
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
                && hasPackageLibraryValue
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
                    (!hasMemberOption
                        && hasExplicitGenericNotation
                        && StructuralViewRegistry
                            .HasExplicitGenericTypeTail(target)
                        && !StructuralViewRegistry
                            .HasGenericTypeAndGenericTailAmbiguity(target)
                        && !StructuralViewRegistry
                            .RequiresGenericTailMemberAlternative(
                                target, tokens)
                        && !StructuralViewRegistry
                            .HasUnambiguousMemberTail(target))
                    || (structuralSchema
                        || ContainsOption(tail, "--mermaid"))
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

            if (ContainsOption(tokens, "--library")
                || ContainsOption(tokens, "--namesake-library")
                || ContainsOption(tokens, "--first-library")
                || ContainsOption(tokens, "--all-libraries"))
            {
                rewritten = [PackageCommand.Name, .. tokens];
                return true;
            }

            if (ContainsOption(tokens, "--versions")
                || ContainsOption(tokens, "--versions-with-feed"))
            {
                ParseResult packageParse = rootCommand.Parse([PackageCommand.Name, .. tokens]);
                if (CommandLineBuilder.HasParsedOption(packageParse, "--versions")
                    || CommandLineBuilder.HasParsedOption(packageParse, "--versions-with-feed"))
                {
                    // A plural package lens owns its positional capacity; an extra
                    // package must not become an implicit type target.
                    rewritten = [PackageCommand.Name, .. tokens];
                    return true;
                }
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

            if (!structuralSchema
                && !ContainsHelpOption(tokens)
                && target.Contains('.')
                && !EcosystemPackCatalog.ShouldSearchPlatform(target))
            {
                rewritten = [PackageCommand.Name, .. tokens];
                return true;
            }

            return false;
        }

        internal static bool ContainsOption(string[] tokens, string option)
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

        private static string PlatformFrameworkSpec(
            PlatformTypeCatalogRouteTarget target) =>
            target.Family switch
            {
                PlatformFamily.DotNetRuntime =>
                    $"runtime@{target.Version}",
                PlatformFamily.AspNetCore =>
                    $"aspnetcore@{target.Version}",
                _ => throw new InvalidOperationException(
                    "Unknown Platform family."),
            };

        internal static bool HasExplicitApiSource(string[] tokens) =>
            ContainsOption(tokens, "--package")
            || ContainsOption(tokens, "--library")
            || ContainsOption(tokens, "--platform")
            || ContainsOption(tokens, "--project");

        internal static bool TryGetLibraryValue(
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
                    return !string.IsNullOrWhiteSpace(value);
                }

                if (tokens[i].Equals(
                        "--library",
                        StringComparison.Ordinal)
                    && i + 1 < tokens.Length
                    && !IsKnownOption(rootCommand, tokens[i + 1]))
                {
                    value = tokens[i + 1];
                    return !string.IsNullOrWhiteSpace(value);
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

        internal static string[] RemoveOptionWithValue(
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

    }
}
