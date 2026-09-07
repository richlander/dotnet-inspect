using System.CommandLine;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

public static class WorkspaceCommandDefinitions
{
    public static Command CreateWorkspaceCommand(SharedOptions opts)
    {
        var command = new Command(
            WorkspaceCommand.Name,
            "Show the committed package Roots in an inspection Workspace");
        var packageOption = new Option<string[]>("--package")
        {
            Description =
                "Package in the Workspace (name or name@version). Exact duplicates are shown once.",
            AllowMultipleArgumentsPerToken = false,
        };
        var tfmOption = new Option<string?>("--tfm")
        {
            Description =
                "Shared target framework for the package set (for example net10.0)",
        };
        var prereleaseOption = new Option<bool>("--preview")
        {
            Description =
                "Allow prerelease versions when an unversioned package floats",
        };
        prereleaseOption.Aliases.Add("--prerelease");

        command.Options.Add(packageOption);
        command.Options.Add(tfmOption);
        command.Options.Add(prereleaseOption);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        command.Options.Add(opts.Json);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        opts.AddCountOptionTo(command);
        opts.AddNuGetOptionsTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string[] packages =
                parseResult.GetValue(packageOption) ?? [];
            string? tfm = parseResult.GetValue(tfmOption);
            if (packages.Length > 0
                && string.IsNullOrWhiteSpace(tfm))
            {
                CommandError.Write(
                    "A shared --tfm is required when the Workspace contains packages.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect workspace --help' for usage.");
                return 1;
            }

            return await WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = packages,
                    Tfm = tfm,
                    IncludePrerelease =
                        parseResult.GetValue(prereleaseOption),
                    Format = opts.ResolveFormat(parseResult),
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
