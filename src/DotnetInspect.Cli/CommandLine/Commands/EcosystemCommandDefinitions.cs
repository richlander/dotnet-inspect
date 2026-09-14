using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.Queries;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspect.Cli.CommandLine;

public static class EcosystemCommandDefinitions
{
    public static Command CreateEcosystemCommand(SharedOptions opts)
    {
        var command = new Command(
            EcosystemCommand.Name,
            "Inspect product-configured knowledge about .NET ecosystems");
        var ecosystemArgument = new Argument<string?>("ecosystem")
        {
            Description =
                "Ecosystem short name or canonical ID (for example aspire or ecosystem.aspire)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        command.Arguments.Add(ecosystemArgument);
        var changesOption = new Option<bool>("--changes")
        {
            Description =
                "Report bounded recent package activity from the nuget.org Catalog",
        };
        var fromOption = new Option<string?>("--from")
        {
            Description =
                "Exclusive lower bound as an ISO 8601 timestamp with an explicit UTC offset (use with --changes and --through)",
            Arity = ArgumentArity.ExactlyOne,
        };
        var throughOption = new Option<string?>("--through")
        {
            Description =
                "Inclusive upper bound as an ISO 8601 timestamp with an explicit UTC offset (use with --changes and --from)",
            Arity = ArgumentArity.ExactlyOne,
        };
        var securityOnlyOption = new Option<bool>("--security-only")
        {
            Description =
                "With --changes, return only current-affected or evidenced security-release activity",
        };
        var compactOption = new Option<bool>("--compact")
        {
            Description = "Output minified JSON (use with --changes --json)",
        };
        command.Options.Add(changesOption);
        command.Options.Add(fromOption);
        command.Options.Add(throughOption);
        command.Options.Add(securityOnlyOption);
        command.Options.Add(compactOption);
        opts.AddJsonOptionTo(command);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        opts.AddSectionOptionsTo(command);
        opts.AddCountOptionTo(command);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        // The lowering bindings require identities for unsupported line modes;
        // leaving these options unattached keeps them outside the public command.
        var linesOption = new Option<bool>("--lines");
        var tailLinesOption = new Option<bool>("--tail-lines");

        command.Validators.Add(result =>
        {
            bool changes = result.GetValue(changesOption);
            bool fromExplicit = IsExplicit(result, fromOption);
            bool throughExplicit = IsExplicit(result, throughOption);
            if (!changes)
            {
                foreach ((Option Option, string Name) reportOption in new[]
                {
                    ((Option)fromOption, "--from"),
                    ((Option)throughOption, "--through"),
                    ((Option)securityOnlyOption, "--security-only"),
                    ((Option)compactOption, "--compact"),
                })
                {
                    if (IsExplicit(result, reportOption.Option))
                    {
                        result.AddError(
                            $"{reportOption.Name} is available only with --changes.");
                    }
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(result.GetValue(ecosystemArgument)))
            {
                result.AddError(
                    "--changes requires a named ecosystem, for example 'ecosystem aspire --changes'.");
            }

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

            if (result.GetValue(opts.Limit) is int maximumRows
                && maximumRows is < 1
                    or > EcosystemChangeReportRequest
                        .DefaultMaximumCandidateEvents)
            {
                result.AddError(
                    $"-n must be between 1 and "
                    + $"{EcosystemChangeReportRequest.DefaultMaximumCandidateEvents} "
                    + "for --changes.");
            }
            if (IsExplicit(result, compactOption)
                && !result.GetValue(opts.Json))
            {
                result.AddError(
                    "--compact requires --changes --json.");
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
                opts.Head,
                opts.Tail,
                opts.Tips,
                opts.Info,
                opts.Verbosity,
            })
            {
                if (IsExplicit(result, option))
                {
                    result.AddError(
                        $"{option.Name} is not supported with --changes.");
                }
            }
        });

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (parseResult.GetValue(changesOption))
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
                var changesOptions = new EcosystemChangesOptions
                {
                    Ecosystem =
                        parseResult.GetValue(ecosystemArgument)!,
                    FromExclusive = from,
                    ThroughInclusive = through,
                    SecurityOnly =
                        parseResult.GetValue(securityOnlyOption),
                    MaximumRows =
                        parseResult.GetValue(opts.Limit)
                        ?? EcosystemChangeReportRequest.DefaultMaximumRows,
                    Format = opts.ResolveFormat(parseResult),
                    CompactJson = parseResult.GetValue(compactOption),
                    Verbose = parseResult.GetValue(opts.Verbose),
                };
                return await EcosystemChangesCommand.ExecuteAsync(
                    changesOptions,
                    new CommandContext(changesOptions.Verbose),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!TryGetRows(parseResult, out RowWindow? rows))
                return 1;

            return EcosystemCommand.Execute(new EcosystemOptions
            {
                Ecosystem = parseResult.GetValue(ecosystemArgument),
                Discover = opts.ParseDiscover(parseResult),
                Select = opts.ParseSelect(parseResult),
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Schema = opts.ParseSchema(parseResult),
                Tree = opts.ParseTree(parseResult),
                Count = parseResult.GetValue(opts.Count),
                Rows = rows,
                Format = opts.ResolveFormat(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
            });
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
                linesOption,
                tailLinesOption),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window,
            isActive: result => !result.GetValue(changesOption));

        return command;
    }

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

    private static bool TryGetRows(
        ParseResult parseResult,
        out RowWindow? rows)
    {
        if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Ecosystem",
                out RowSelectionIntent<string>? intent,
                out string? error))
        {
            CommandError.Write(error!);
            rows = null;
            return false;
        }

        if (intent is null || intent.Operations.Count == 0)
        {
            rows = null;
            return true;
        }

        if (intent.Operations.Count != 1)
        {
            throw new InvalidOperationException(
                "Ecosystem row selection must lower to exactly one operation.");
        }

        RowSelectionIntentOperation<string> operation =
            intent.Operations[0];
        rows = operation.Kind switch
        {
            RowSelectionStageKind.Head => RowWindow.Head(operation.Count),
            RowSelectionStageKind.Tail => RowWindow.Tail(operation.Count),
            RowSelectionStageKind.Window when operation.Start is int start =>
                RowWindow.Range(start, operation.End),
            _ => throw new InvalidOperationException(
                $"Unsupported ecosystem row-selection operation '{operation.Kind}'."),
        };
        return true;
    }
}
