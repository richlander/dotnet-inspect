using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Services;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspect.Cli.CommandLine;

public static class PackageChangesCommandDefinitions
{
    public static Command CreatePackageChangesCommand(
        SharedOptions opts,
        Command packageCommand,
        Argument<string[]> inheritedPackageArgument)
    {
        var command = new Command(
            PackageChangesCommand.Name,
            "Report bounded recent package activity");
        var ecosystemOption = new Option<string?>("--ecosystem")
        {
            Description =
                "Ecosystem short name or canonical ID whose exact package set defines the population",
            Arity = ArgumentArity.ExactlyOne,
        };
        var fromOption = new Option<string?>("--from")
        {
            Description =
                "Exclusive lower bound as an ISO 8601 timestamp with an explicit UTC offset (use with --through)",
            Arity = ArgumentArity.ExactlyOne,
        };
        var throughOption = new Option<string?>("--through")
        {
            Description =
                "Inclusive upper bound as an ISO 8601 timestamp with an explicit UTC offset (use with --from)",
            Arity = ArgumentArity.ExactlyOne,
        };
        var securityOnlyOption = new Option<bool>("--security-only")
        {
            Description =
                "Return only current-affected or evidenced security-release activity",
        };
        var compactOption = new Option<bool>("--compact")
        {
            Description = "Output minified JSON (use with --json or --envelope)",
        };

        command.Options.Add(ecosystemOption);
        command.Options.Add(fromOption);
        command.Options.Add(throughOption);
        command.Options.Add(securityOnlyOption);
        command.Options.Add(compactOption);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        command.Options.Add(opts.Limit);
        command.Options.Add(opts.Verbose);
        command.Options.Add(opts.Table);
        command.Options.Add(opts.Tsv);
        command.Options.Add(opts.Jsonl);
        command.Options.Add(opts.NoHeaders);
        command.Options.Add(opts.Discover);
        command.Options.Add(opts.Select);
        command.Options.Add(opts.Columns);
        command.Options.Add(opts.Fields);
        command.Options.Add(opts.Schema);
        command.Options.Add(opts.Tree);
        command.Options.Add(opts.Count);
        command.Options.Add(opts.Rows);
        command.Options.Add(opts.Head);
        command.Options.Add(opts.Tail);
        command.Options.Add(opts.Lines);
        command.Options.Add(opts.TailLines);
        command.Options.Add(opts.Tips);
        command.Options.Add(opts.Verbosity);
        opts.AddEnvelopeOptionTo(
            command,
            opts.Discover,
            opts.Select,
            opts.Schema,
            opts.Count,
            opts.Rows,
            opts.Head,
            opts.Tail,
            opts.Lines,
            opts.TailLines,
            opts.Verbosity);

        command.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValue(ecosystemOption)))
            {
                result.AddError(
                    "package activity requires --ecosystem <name>, for example "
                    + "'package activity --ecosystem aspire'.");
            }

            bool fromExplicit = IsExplicit(result, fromOption);
            bool throughExplicit = IsExplicit(result, throughOption);
            DateTimeOffset from = default;
            DateTimeOffset through = default;
            if (fromExplicit != throughExplicit)
            {
                result.AddError(
                    "--from and --through must be specified together.");
            }
            else if (fromExplicit
                && (!TryParseTimestamp(
                        result.GetValue(fromOption),
                        out from)
                    || !TryParseTimestamp(
                        result.GetValue(throughOption),
                        out through)))
            {
                result.AddError(
                    "--from and --through must be ISO 8601 timestamps with an explicit UTC offset.");
            }
            else if (fromExplicit)
            {
                try
                {
                    _ = new NuGetCatalogRequest(from, through);
                }
                catch (ArgumentException exception)
                {
                    result.AddError(exception.Message);
                }
            }

            bool renderedLineSelection =
                IsExplicit(result, opts.Lines)
                || IsExplicit(result, opts.TailLines);
            if (!renderedLineSelection
                && result.GetValue(opts.Limit) is int maximumRows
                && maximumRows is < 1
                    or > EcosystemChangeReportRequest
                        .DefaultMaximumCandidateEvents)
            {
                result.AddError(
                    $"-n must be between 1 and "
                    + $"{EcosystemChangeReportRequest.DefaultMaximumCandidateEvents} "
                    + "for package activity.");
            }
            if (IsExplicit(result, compactOption)
                && !result.GetValue(opts.Json)
                && !result.GetValue(opts.Envelope))
            {
                result.AddError(
                    "--compact requires package activity --json or --envelope.");
            }

            foreach (Option option in new Option[]
            {
                opts.Table,
                opts.Tsv,
                opts.Jsonl,
                opts.NoHeaders,
                opts.Discover,
                opts.Select,
                opts.Columns,
                opts.Fields,
                opts.Schema,
                opts.Tree,
                opts.Count,
                opts.Rows,
                opts.Tips,
                opts.Verbosity,
            })
            {
                if (IsExplicit(result, option))
                {
                    result.AddError(
                        $"{option.Name} is not supported with package activity.");
                }
            }
            if (!renderedLineSelection
                && IsExplicit(result, opts.Tail))
            {
                result.AddError(
                    "--tail is not supported with semantic package activity rows.");
            }

            var acceptedParentOptions = new HashSet<Option>
            {
                opts.Envelope,
                opts.Json,
                opts.Markdown,
                opts.PlainText,
                opts.Limit,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines,
                opts.Verbose,
            };
            Option? unsupportedParentOption =
                packageCommand.Options.FirstOrDefault(
                    option => !acceptedParentOptions.Contains(option)
                        && result.GetResult(option) is { Implicit: false });
            if (unsupportedParentOption is not null)
            {
                result.AddError(
                    $"{unsupportedParentOption.Name} is not available with package activity.");
            }

            if (result.GetValue(inheritedPackageArgument) is { Length: > 0 })
            {
                result.AddError(
                    "A package inspection target is not available with package activity; "
                    + "place 'activity' immediately after 'package'.");
            }
        });

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            DateTimeOffset? from =
                TryParseTimestamp(
                    parseResult.GetValue(fromOption),
                    out DateTimeOffset parsedFrom)
                    ? parsedFrom
                    : null;
            DateTimeOffset? through =
                TryParseTimestamp(
                    parseResult.GetValue(throughOption),
                    out DateTimeOffset parsedThrough)
                    ? parsedThrough
                    : null;
            bool envelopeOutput = parseResult.GetValue(opts.Envelope);
            var options = new PackageChangesOptions
            {
                Ecosystem = parseResult.GetValue(ecosystemOption)!,
                FromExclusive = from,
                ThroughInclusive = through,
                SecurityOnly = parseResult.GetValue(securityOnlyOption),
                MaximumRows =
                    UsesRenderedLineSelection(parseResult, opts)
                        ? EcosystemChangeReportRequest.DefaultMaximumRows
                        : parseResult.GetValue(opts.Limit)
                            ?? EcosystemChangeReportRequest.DefaultMaximumRows,
                Format =
                    envelopeOutput
                        ? OutputFormat.Json
                        : opts.ResolveFormat(parseResult),
                EnvelopeOutput = envelopeOutput,
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(opts.Verbose),
            };
            return await PackageChangesCommand.ExecuteAsync(
                options,
                new CommandContext(options.Verbose),
                cancellationToken).ConfigureAwait(false);
        });

        CliRowSelectionCommandRegistry.Register(
            command,
            new(
                opts.Limit,
                opts.Rows,
                top: null,
                orderBy: null,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Lines,
            isActive: static _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return command;
    }

    private static bool UsesRenderedLineSelection(
        ParseResult parseResult,
        SharedOptions opts) =>
        parseResult.GetResult(opts.Lines) is { Implicit: false }
        || parseResult.GetResult(opts.TailLines) is { Implicit: false };

    private static bool IsExplicit(
        CommandResult result,
        Option option) =>
        result.GetResult(option) is { Implicit: false };

    internal static bool TryParseTimestamp(
        string? text,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        int timeSeparator = text.IndexOf('T');
        bool hasExplicitOffset =
            timeSeparator >= 0
            && (text.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
                || text.LastIndexOf('+') > timeSeparator
                || text.LastIndexOf('-') > timeSeparator);
        if (!hasExplicitOffset
            || !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTimeOffset parsed))
        {
            return false;
        }

        timestamp = parsed.ToUniversalTime();
        return true;
    }
}
