using System.CommandLine;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.CommandLine;

public static class InspectionGraphCommandDefinitions
{
    public static Command CreateGraphCommand(SharedOptions opts)
    {
        var command = new Command(
            InspectionGraphCommand.Name,
            "Inspect typed relationships across an explicit workspace");
        var integrations = CreateIntegrationsCommand(opts);
        var libraries = CreateLibrariesCommand(opts);
        command.Subcommands.Add(integrations);
        command.Subcommands.Add(libraries);
        command.SetAction(_ =>
        {
            HelpWriter.WriteHelp(command);
            return 0;
        });
        return command;
    }

    static Command CreateLibrariesCommand(SharedOptions opts)
    {
        var command = new Command(
            LibraryCallUseCommand.Name,
            "Show exact direct call use and typed summaries between two local libraries");
        var libraryOption = new Option<string[]>("--library")
        {
            Description =
                "Local managed library in the induced pair. Specify exactly twice.",
            AllowMultipleArgumentsPerToken = false,
        };
        command.Options.Add(libraryOption);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        opts.AddSectionOptionsTo(command);
        opts.AddCountOptionTo(command);
        command.Options.Add(opts.RowWhere);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!LibraryCallUseQueryOptions.TryParse(
                    parseResult.GetValue(opts.RowWhere) ?? [],
                    out LibraryCallUseQueryOptions query,
                    out OptionError error))
            {
                CommandError.Write(error);
                return 1;
            }

            string[] libraries =
                parseResult.GetValue(libraryOption) ?? [];

            return await LibraryCallUseCommand.ExecuteAsync(
                new LibraryCallUseOptions
                {
                    Libraries = libraries,
                    Cluster = query.Cluster,
                    Format = opts.ResolveFormat(parseResult),
                    Count = parseResult.GetValue(opts.Count),
                    Rows = opts.ParseRows(parseResult),
                    NoHeader =
                        parseResult.GetValue(opts.NoHeaders),
                    Verbose =
                        parseResult.GetValue(opts.Verbose),
                    Columns =
                        opts.ParseColumns(parseResult),
                    Fields =
                        opts.ParseFields(parseResult),
                    Discover =
                        opts.ParseDiscover(parseResult),
                    Select =
                        opts.ParseSelect(parseResult),
                    SelectDefault =
                        opts.ParseSelectDefault(parseResult),
                    Schema =
                        opts.ParseSchema(parseResult),
                    Tree =
                        opts.ParseTree(parseResult),
                },
                cancellationToken);
        });

        return command;
    }

    static Command CreateIntegrationsCommand(SharedOptions opts)
    {
        var command = new Command(
            InspectionGraphCommand.IntegrationsName,
            "Induce Integration relationships over an explicit package set");
        var packageOption = new Option<string[]>("--package")
        {
            Description =
                "Package in the induced set (name or name@version). Repeat for each package.",
            AllowMultipleArgumentsPerToken = false,
        };
        var tfmOption = new Option<string?>("--tfm")
        {
            Description =
                "Shared target framework for the package set (for example net10.0)",
        };
        var relationshipOption = new Option<string[]>("--relationship")
        {
            Description =
                "Exact relationship id. Repeat to override the default Integration family.",
            AllowMultipleArgumentsPerToken = false,
        };
        CliOptionValueValidation.AcceptOnlyFromAmong(
            relationshipOption,
            StringComparer.Ordinal,
            [.. InspectionGraphCommand.SupportedRelationshipIds]);
        var prereleaseOption = new Option<bool>("--preview")
        {
            Description =
                "Allow prerelease versions when an unversioned package floats",
        };
        prereleaseOption.Aliases.Add("--prerelease");

        command.Options.Add(packageOption);
        command.Options.Add(tfmOption);
        command.Options.Add(relationshipOption);
        command.Options.Add(prereleaseOption);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        command.Options.Add(opts.Mermaid);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(
            command,
            validateLegacyRowWindow: static _ => false);
        command.Options.Add(opts.Tree);
        opts.AddCountOptionTo(command);
        opts.AddNuGetOptionsTo(command);

        command.Validators.Add(result =>
        {
            string? missingRelationshipValue =
                result.GetResult(relationshipOption)
                    is { Tokens.Count: > 0 } relationshipResult
                    ? relationshipResult.Tokens
                        .Select(static token => token.Value)
                        .FirstOrDefault(static value =>
                            value.StartsWith(
                                "-",
                                StringComparison.Ordinal))
                    : null;
            if (missingRelationshipValue is not null)
            {
                result.AddError(
                    $"--relationship requires a relationship id before '{missingRelationshipValue}'.");
            }
            if (result.GetValue(opts.Tree)
                && result.GetValue(opts.Mermaid))
            {
                result.AddError(
                    "--tree and --mermaid are alternate graph renderings; choose one.");
            }
            if (result.GetValue(opts.Tree)
                && (result.GetValue(opts.Json)
                    || result.GetValue(opts.Markdown)
                    || result.GetValue(opts.PlainText)
                    || result.GetValue(opts.Table)
                    || result.GetValue(opts.Tsv)
                    || result.GetValue(opts.Jsonl)
                    || result.GetResult(opts.Verbosity)
                        is { Implicit: false }))
            {
                result.AddError(
                    "--tree is a standalone graph rendering and cannot combine with another output format.");
            }
        });

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "Integration graph",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

            string[] packages =
                parseResult.GetValue(packageOption) ?? [];
            string? tfm = parseResult.GetValue(tfmOption);
            if (packages.Length == 0)
            {
                CommandError.Write("At least one --package is required.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect graph integrations --help' for usage.");
                return 1;
            }
            if (string.IsNullOrWhiteSpace(tfm))
            {
                CommandError.Write("A shared --tfm is required.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect graph integrations --help' for usage.");
                return 1;
            }

            OutputFormat format = opts.ResolveFormat(parseResult);
            return await InspectionGraphCommand.ExecuteAsync(
                new InspectionGraphOptions
                {
                    Packages = packages,
                    Tfm = tfm,
                    Relationships =
                        parseResult.GetValue(relationshipOption) ?? [],
                    IncludePrerelease =
                        parseResult.GetValue(prereleaseOption),
                    Format = format,
                    EmbeddedMermaid =
                        opts.IsEmbeddedMermaid(parseResult),
                    Tree = parseResult.GetValue(opts.Tree),
                    Count = parseResult.GetValue(opts.Count),
                    RowSelection = rowSelection,
                    Rows = rowSelection is null
                        ? opts.ParseRows(parseResult)
                        : null,
                    NoHeader = parseResult.GetValue(opts.NoHeaders),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    SourceOptions =
                        opts.ParseNuGetSourceOptions(parseResult),
                },
                cancellationToken);
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
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: static _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return command;
    }
}
