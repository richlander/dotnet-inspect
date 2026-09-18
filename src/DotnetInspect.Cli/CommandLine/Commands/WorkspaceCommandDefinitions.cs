using System.CommandLine;
using System.CommandLine.Parsing;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

public static class WorkspaceCommandDefinitions
{
    public static Command CreateWorkspaceCommand(SharedOptions opts)
    {
        var command = new Command(
            WorkspaceCommand.Name,
            "Author or inspect an inspection Workspace and optionally evaluate one exact Navigation occurrence");
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
        var packetOption = new Option<string?>("--packet")
        {
            Description =
                "Use one canonical Workspace packet or exact Inspect Web Workspace URL",
            Arity = ArgumentArity.ExactlyOne,
        };
        var replacePackageOption = new Option<int?>("--replace-package")
        {
            Description =
                "Replace one direct Package by its one-based packet navigation-row order",
        };
        var replacementVersionOption = new Option<string?>("--to-version")
        {
            Description = "Exact destination Version for --replace-package",
        };
        var replacementTfmOption = new Option<string?>("--to-tfm")
        {
            Description = "Destination TFM for --replace-package (isolated context only)",
        };
        var registerLibraryOption =
            new Option<string[]>("--register-library")
            {
                Description =
                    "Register an exact Package Library as package@version/assembly@assembly-version",
                AllowMultipleArgumentsPerToken = false,
            };
        var registerPackagePrefixOption =
            new Option<string[]>("--register-package-prefix")
            {
                Description =
                    "Register an inert literal Package ID prefix",
                AllowMultipleArgumentsPerToken = false,
            };
        var registerEcosystemOption =
            new Option<string[]>("--register-ecosystem")
            {
                Description =
                    "Register a shipped ecosystem by short or canonical ID",
                AllowMultipleArgumentsPerToken = false,
            };
        var kindOption = new Option<string[]>("--kind")
        {
            Description =
                "Select inventory kinds: package, exact-library, package-prefix, or ecosystem",
            AllowMultipleArgumentsPerToken = false,
        };
        CliOptionValueValidation.AcceptOnlyFromAmong(
            kindOption,
            StringComparer.OrdinalIgnoreCase,
            "package",
            "exact-library",
            "package-prefix",
            "ecosystem");
        var rootRequestOption = new Option<string?>("--root-request")
        {
            Description =
                "Reopen the exact package Root named by a reopening token from the Root column of 'package query ... --library-literal'",
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
        var shareOption = WorkspaceShareOption.Create(
            "Emit the complete portable Workspace definition as a canonical packet or URL; coordinate replacement realizes Packages");

        command.Options.Add(packageOption);
        command.Options.Add(tfmOption);
        command.Options.Add(prereleaseOption);
        command.Options.Add(packetOption);
        command.Options.Add(replacePackageOption);
        command.Options.Add(replacementVersionOption);
        command.Options.Add(replacementTfmOption);
        command.Options.Add(registerLibraryOption);
        command.Options.Add(registerPackagePrefixOption);
        command.Options.Add(registerEcosystemOption);
        command.Options.Add(kindOption);
        command.Options.Add(rootRequestOption);
        command.Options.Add(activePackageOption);
        command.Options.Add(libraryOption);
        command.Options.Add(allLibrariesOption);
        command.Options.Add(typeOption);
        command.Options.Add(memberOption);
        command.Options.Add(lensOption);
        command.Options.Add(shareOption);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Envelope);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        opts.AddCountOptionTo(command);
        opts.AddNuGetOptionsTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string[] packages =
                parseResult.GetValue(packageOption) ?? [];
            string? tfm = parseResult.GetValue(tfmOption);
            string? packet = parseResult.GetValue(packetOption);
            WorkspaceRegistrationInput[] orderedRegistrations =
                ParseOrderedRegistrations(
                    parseResult,
                    registerLibraryOption,
                    registerPackagePrefixOption,
                    registerEcosystemOption);
            WorkspaceTopLevelInventoryEntryKind[] inventoryKinds =
            [
                .. (parseResult.GetValue(kindOption) ?? [])
                    .Select(ParseInventoryKind),
            ];
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
                    Packet = packet,
                    ReplacePackage = parseResult.GetValue(replacePackageOption),
                    ReplacementVersion = parseResult.GetValue(replacementVersionOption),
                    ReplacementTfm = parseResult.GetValue(replacementTfmOption),
                    EnvelopeOutput = parseResult.GetValue(opts.Envelope),
                    OrderedRegistrations = orderedRegistrations,
                    RegisteredLibraries =
                        parseResult.GetValue(registerLibraryOption) ?? [],
                    RegisteredPackagePrefixes =
                        parseResult.GetValue(registerPackagePrefixOption) ?? [],
                    RegisteredEcosystems =
                        parseResult.GetValue(registerEcosystemOption) ?? [],
                    InventoryKinds = inventoryKinds,
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
                    ShareFormat =
                        WorkspaceShareOption.Parse(parseResult, shareOption),
                    SourceOptions =
                        opts.ParseNuGetSourceOptions(parseResult),
                },
                cancellationToken);
        });

        return command;
    }

    static WorkspaceRegistrationInput[] ParseOrderedRegistrations(
        ParseResult parseResult,
        Option<string[]> registerLibraryOption,
        Option<string[]> registerPackagePrefixOption,
        Option<string[]> registerEcosystemOption)
    {
        var kinds =
            new Dictionary<string, WorkspaceRegistrationInputKind>(
                StringComparer.Ordinal)
            {
                [registerLibraryOption.Name] =
                    WorkspaceRegistrationInputKind.ExactLibrary,
                [registerPackagePrefixOption.Name] =
                    WorkspaceRegistrationInputKind.PackagePrefix,
                [registerEcosystemOption.Name] =
                    WorkspaceRegistrationInputKind.Ecosystem,
            };
        var registrations = new List<WorkspaceRegistrationInput>();
        for (int index = 0; index < parseResult.Tokens.Count; index++)
        {
            Token token = parseResult.Tokens[index];
            if (token.Type != TokenType.Option
                || !kinds.TryGetValue(
                    token.Value,
                    out WorkspaceRegistrationInputKind kind)
                || index + 1 >= parseResult.Tokens.Count
                || parseResult.Tokens[index + 1].Type == TokenType.Option)
            {
                continue;
            }

            registrations.Add(
                new WorkspaceRegistrationInput(
                    kind,
                    parseResult.Tokens[++index].Value));
        }

        return [.. registrations];
    }

    static WorkspaceTopLevelInventoryEntryKind ParseInventoryKind(
        string value) =>
        value.ToLowerInvariant() switch
        {
            "package" => WorkspaceTopLevelInventoryEntryKind.Package,
            "exact-library" =>
                WorkspaceTopLevelInventoryEntryKind.ExactLibrary,
            "package-prefix" =>
                WorkspaceTopLevelInventoryEntryKind.PackagePrefix,
            "ecosystem" => WorkspaceTopLevelInventoryEntryKind.Ecosystem,
            _ => throw new InvalidOperationException(
                "System.CommandLine admitted an unsupported Workspace inventory kind."),
        };
}
