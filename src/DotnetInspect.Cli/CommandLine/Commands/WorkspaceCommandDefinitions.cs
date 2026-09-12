using System.CommandLine;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

public static class WorkspaceCommandDefinitions
{
    public static Command CreateWorkspaceCommand(SharedOptions opts)
    {
        var command = new Command(
            WorkspaceCommand.Name,
            "Show an inspection Workspace and optionally evaluate one exact Navigation occurrence");
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
        var rootRequestOption = new Option<string?>("--root-request")
        {
            Description =
                "Reopen the exact package Root named by a reopening token from the Root column of 'find --literal'",
            Arity = ArgumentArity.ExactlyOne,
        };
        var activePackageOption = new Option<int?>("--active-package")
        {
            Description =
                "Evaluate one exact committed Package occurrence by its one-based Workspace order",
        };
        var libraryOption = new Option<string?>("--library")
        {
            Description =
                "Exact Library asset id from Navigation output",
        };
        var allLibrariesOption = new Option<bool>("--all-libraries")
        {
            Description =
                "Use the aggregate Library subject as a Type destination source",
        };
        var typeOption = new Option<string?>("--type")
        {
            Description =
                "Exact returned metadata Type full name for an atomic destination",
        };
        var memberOption = new Option<string?>("--member")
        {
            Description =
                "Exact returned Member stable selector for an atomic destination",
        };
        var lensOption = new Option<string?>("--lens")
        {
            Description =
                "Exact destination view-facet id, such as type.compare or member.compare",
        };

        command.Options.Add(packageOption);
        command.Options.Add(tfmOption);
        command.Options.Add(prereleaseOption);
        command.Options.Add(rootRequestOption);
        command.Options.Add(activePackageOption);
        command.Options.Add(libraryOption);
        command.Options.Add(allLibrariesOption);
        command.Options.Add(typeOption);
        command.Options.Add(memberOption);
        command.Options.Add(lensOption);
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
            string? rootRequest = parseResult.GetValue(rootRequestOption);
            int? activePackage =
                parseResult.GetValue(activePackageOption);
            string? library = parseResult.GetValue(libraryOption);
            bool allLibraries =
                parseResult.GetValue(allLibrariesOption);
            string? type = parseResult.GetValue(typeOption);
            string? member = parseResult.GetValue(memberOption);
            string? lens = parseResult.GetValue(lensOption);
            if (rootRequest is not null
                && (packages.Length > 0
                    || !string.IsNullOrWhiteSpace(tfm)))
            {
                CommandError.Write(
                    "--root-request opens the exact Root its token names and cannot be combined with --package or --tfm.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect workspace --help' for usage.");
                return 1;
            }

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
                    RootRequest = rootRequest,
                    ActivePackage = activePackage,
                    Library = library,
                    AllLibraries = allLibraries,
                    Type = type,
                    Member = member,
                    Lens = lens,
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
