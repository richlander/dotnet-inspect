using System.CommandLine;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

public static class InspectionGraphCommandDefinitions
{
    public static Command CreateGraphCommand(SharedOptions opts)
    {
        var command = new Command(
            InspectionGraphCommand.Name,
            "Inspect typed relationships across an explicit workspace");
        var integrations = CreateIntegrationsCommand(opts);
        command.Subcommands.Add(integrations);
        command.SetAction(_ =>
        {
            HelpWriter.WriteHelp(command);
            return 0;
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
        relationshipOption.AcceptOnlyFromAmong(
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
        opts.AddOutputOptionsTo(command);
        command.Options.Add(opts.Tree);
        opts.AddCountOptionTo(command);
        opts.AddNuGetOptionsTo(command);

        command.Validators.Add(result =>
        {
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
                    Rows = opts.ParseRows(parseResult),
                    NoHeader = parseResult.GetValue(opts.NoHeaders),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    SourceOptions =
                        opts.ParseNuGetSourceOptions(parseResult),
                },
                cancellationToken);
        });

        return command;
    }
}
