using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;

namespace DotnetInspector;

/// <summary>
/// Builds the System.CommandLine command structure.
/// </summary>
public static class CommandLineBuilder
{
    /// <summary>
    /// Known commands for implicit package command detection.
    /// </summary>
    public static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "package", "assembly", "api", "type", "llmstxt", "help", "--help", "-h", "-?", "--version"
    };

    /// <summary>
    /// Pre-processes args to handle implicit package command.
    /// </summary>
    public static string[] PreprocessArgs(string[] args)
    {
        if (args.Length > 0 && !KnownCommands.Contains(args[0]))
        {
            // Prepend "package" to treat as package command
            return ["package", .. args];
        }
        return args;
    }

    /// <summary>
    /// Creates the root command with all subcommands configured.
    /// </summary>
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("A CLI tool for inspecting .NET assemblies and NuGet packages");

        // Shared options (defined once, reused across commands)
        var jsonOption = new Option<bool>("--json") { Description = "Output as JSON" };
        var markoutOption = new Option<bool>("--markout") { Description = "Output as Markout (default)" };
        var verboseOption = new Option<bool>("--verbose") { Description = "Show progress messages on stderr" };
        var verbosityOption = new Option<string?>("-v") { Description = "Verbosity level: q(uiet), m(inimal), n(ormal), d(etailed)" };
        var includeSectionsOption = new Option<string?>("-s") { Description = "Include only these sections (comma-separated, e.g., -s:1,3)" };
        var excludeSectionsOption = new Option<string?>("-x") { Description = "Exclude these sections (comma-separated, e.g., -x:4)" };
        var limitOption = new Option<int?>("-n") { Description = "Limit number of results" };

        // Package command
        var packageCommand = CreatePackageCommand(jsonOption, markoutOption, verboseOption, verbosityOption, includeSectionsOption, excludeSectionsOption, limitOption);
        rootCommand.Subcommands.Add(packageCommand);

        // Assembly command
        var assemblyCommand = CreateAssemblyCommand(jsonOption, markoutOption, verboseOption, verbosityOption, includeSectionsOption, excludeSectionsOption);
        rootCommand.Subcommands.Add(assemblyCommand);

        // API command
        var apiCommand = CreateApiCommand(jsonOption, markoutOption, verboseOption, verbosityOption, limitOption);
        rootCommand.Subcommands.Add(apiCommand);

        // Type command
        var typeCommand = CreateTypeCommand(jsonOption, verboseOption);
        rootCommand.Subcommands.Add(typeCommand);

        // LLMs.txt command
        var llmsTxtCommand = new Command("llmstxt", "Show usage examples (run this first)");
        llmsTxtCommand.SetAction((parseResult) => LlmsTxtCommand.Execute());
        rootCommand.Subcommands.Add(llmsTxtCommand);

        return rootCommand;
    }

    private static Command CreateTypeCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption)
    {
        var typeCommand = new Command("type", "Show type shape with hierarchy and members (tree view)");

        var typeNameArg = new Argument<string>("type")
        {
            Description = "Type name to inspect"
        };

        var typePackageOption = new Option<string?>("--package") { Description = "Extract from package (name or name@version)" };
        var typeAssemblyOption = new Option<string?>("--assembly") { Description = "Assembly path" };
        var typeTfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM" };
        var typeAllOption = new Option<bool>("--all") { Description = "Include hidden/obsolete members" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };

        typeCommand.Arguments.Add(typeNameArg);
        typeCommand.Options.Add(typePackageOption);
        typeCommand.Options.Add(typeAssemblyOption);
        typeCommand.Options.Add(typeTfmOption);
        typeCommand.Options.Add(typeAllOption);
        typeCommand.Options.Add(jsonOption);
        typeCommand.Options.Add(compactOption);
        typeCommand.Options.Add(verboseOption);

        typeCommand.SetAction(async (parseResult, ct) =>
        {
            var typeName = parseResult.GetValue(typeNameArg);
            var options = new TypeOptions
            {
                PackagePath = parseResult.GetValue(typePackageOption),
                AssemblyPath = parseResult.GetValue(typeAssemblyOption),
                Tfm = parseResult.GetValue(typeTfmOption),
                IncludeAll = parseResult.GetValue(typeAllOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(verboseOption)
            };

            return await TypeCommand.ExecuteAsync(typeName!, options);
        });

        return typeCommand;
    }

    private static Command CreatePackageCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption,
        Option<int?> limitOption)
    {
        var packageCommand = new Command("package", "Inspect a NuGet package");

        var packageNameArg = new Argument<string[]>("package")
        {
            Description = "NuGet package name or path to .nupkg file, optionally with version (e.g., System.Text.Json@9.0.0)",
            Arity = ArgumentArity.OneOrMore
        };

        var depsOption = new Option<bool>("--deps") { Description = "Include dependency analysis" };
        var filesOption = new Option<bool>("--files") { Description = "List DLLs in the package" };
        var allFilesOption = new Option<bool>("--all") { Description = "With --files: list all files in entire package" };
        var treeOption = new Option<bool>("--tree") { Description = "With --files: display as tree view" };
        var versionsOption = new Option<bool>("--versions") { Description = "List available versions from nuget.org" };
        var prereleaseOption = new Option<bool>("--preview") { Description = "With --versions: include prerelease versions" };
        prereleaseOption.Aliases.Add("--prerelease");

        packageCommand.Arguments.Add(packageNameArg);
        packageCommand.Options.Add(depsOption);
        packageCommand.Options.Add(filesOption);
        packageCommand.Options.Add(allFilesOption);
        packageCommand.Options.Add(treeOption);
        packageCommand.Options.Add(versionsOption);
        packageCommand.Options.Add(prereleaseOption);
        packageCommand.Options.Add(limitOption);
        packageCommand.Options.Add(jsonOption);
        packageCommand.Options.Add(markoutOption);
        packageCommand.Options.Add(verboseOption);
        packageCommand.Options.Add(verbosityOption);
        packageCommand.Options.Add(includeSectionsOption);
        packageCommand.Options.Add(excludeSectionsOption);

        packageCommand.SetAction(async (parseResult, ct) =>
        {
            var packageArgs = parseResult.GetValue(packageNameArg) ?? [];
            var options = new InspectionOptions
            {
                IncludeDeps = parseResult.GetValue(depsOption),
                ListFiles = parseResult.GetValue(filesOption),
                ListAllFiles = parseResult.GetValue(allFilesOption),
                TreeView = parseResult.GetValue(treeOption),
                ListVersions = parseResult.GetValue(versionsOption),
                IncludePrerelease = parseResult.GetValue(prereleaseOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                IncludeSections = ParseSectionList(parseResult.GetValue(includeSectionsOption)),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption))
            };

            return await PackageCommand.ExecuteAsync(packageArgs, options);
        });

        return packageCommand;
    }

    private static Command CreateAssemblyCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption)
    {
        var assemblyCommand = new Command("assembly", "Inspect a .NET assembly file");

        var assemblyPathArg = new Argument<string?>("path")
        {
            Description = "Path to assembly file",
            Arity = ArgumentArity.ZeroOrOne
        };
        assemblyPathArg.DefaultValueFactory = _ => null;

        var auditOption = new Option<bool>("--audit") { Description = "Include SourceLink and determinism audit" };
        var asmPackageOption = new Option<string?>("--package") { Description = "Extract assembly from package (file, name, or name@version)" };
        var asmTfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM (e.g., net8.0)" };

        assemblyCommand.Arguments.Add(assemblyPathArg);
        assemblyCommand.Options.Add(auditOption);
        assemblyCommand.Options.Add(asmPackageOption);
        assemblyCommand.Options.Add(asmTfmOption);
        assemblyCommand.Options.Add(jsonOption);
        assemblyCommand.Options.Add(markoutOption);
        assemblyCommand.Options.Add(verboseOption);
        assemblyCommand.Options.Add(verbosityOption);
        assemblyCommand.Options.Add(includeSectionsOption);
        assemblyCommand.Options.Add(excludeSectionsOption);

        assemblyCommand.SetAction(async (parseResult, ct) =>
        {
            var assemblyPath = parseResult.GetValue(assemblyPathArg);
            var options = new AssemblyOptions
            {
                IncludeAudit = parseResult.GetValue(auditOption),
                PackagePath = parseResult.GetValue(asmPackageOption),
                Tfm = parseResult.GetValue(asmTfmOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                IncludeSections = ParseSectionList(parseResult.GetValue(includeSectionsOption)),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption))
            };

            return await AssemblyCommand.ExecuteAsync(assemblyPath, options);
        });

        return assemblyCommand;
    }

    private static Command CreateApiCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<int?> limitOption)
    {
        var apiCommand = new Command("api", "Extract public API surface");

        var typeNameArg = new Argument<string?>("type")
        {
            Description = "Type name (full or simple). If omitted, lists all types.",
            Arity = ArgumentArity.ZeroOrOne
        };
        typeNameArg.DefaultValueFactory = _ => null;

        var apiPackageOption = new Option<string?>("--package") { Description = "Extract from package (file, name, or name@version)" };
        var apiAssemblyOption = new Option<string?>("--assembly") { Description = "Assembly path (local file, or relative path within package)" };
        var apiTfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM (e.g., net8.0)" };
        var interfacesOption = new Option<bool>("--interfaces") { Description = "Show implemented interfaces" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete members" };
        var filterOption = new Option<string?>("--filter") { Description = "Filter type names by glob pattern (e.g., *Json*, Progress*)" };
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter to specific member(s)",
            AllowMultipleArgumentsPerToken = true
        };
        memberOption.Aliases.Add("--member");
        var sourceUrlOption = new Option<bool>("--source-url") { Description = "Show SourceLink URL for the type" };
        var docsOption = new Option<bool>("--docs") { Description = "Fetch and display XML doc comments from source" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var signaturesOnlyOption = new Option<bool>("--signatures-only") { Description = "Output only method signatures (no table formatting)" };

        apiCommand.Arguments.Add(typeNameArg);
        apiCommand.Options.Add(apiPackageOption);
        apiCommand.Options.Add(apiAssemblyOption);
        apiCommand.Options.Add(apiTfmOption);
        apiCommand.Options.Add(interfacesOption);
        apiCommand.Options.Add(allOption);
        apiCommand.Options.Add(filterOption);
        apiCommand.Options.Add(memberOption);
        apiCommand.Options.Add(limitOption);
        apiCommand.Options.Add(sourceUrlOption);
        apiCommand.Options.Add(docsOption);
        apiCommand.Options.Add(jsonOption);
        apiCommand.Options.Add(compactOption);
        apiCommand.Options.Add(signaturesOnlyOption);
        apiCommand.Options.Add(markoutOption);
        apiCommand.Options.Add(verboseOption);
        apiCommand.Options.Add(verbosityOption);

        apiCommand.SetAction(async (parseResult, ct) =>
        {
            var typeName = parseResult.GetValue(typeNameArg);
            var members = parseResult.GetValue(memberOption);
            var options = new ApiOptions
            {
                PackagePath = parseResult.GetValue(apiPackageOption),
                AssemblyPath = parseResult.GetValue(apiAssemblyOption),
                Tfm = parseResult.GetValue(apiTfmOption),
                ShowInterfaces = parseResult.GetValue(interfacesOption),
                IncludeAll = parseResult.GetValue(allOption),
                TypeFilter = parseResult.GetValue(filterOption),
                MemberFilter = members?.Length > 0 ? new HashSet<string>(members, StringComparer.OrdinalIgnoreCase) : null,
                Limit = parseResult.GetValue(limitOption),
                ShowSourceUrl = parseResult.GetValue(sourceUrlOption),
                ShowDocs = parseResult.GetValue(docsOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                SignaturesOnly = parseResult.GetValue(signaturesOnlyOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption))
            };

            return await ApiCommand.ExecuteAsync(typeName, options);
        });

        return apiCommand;
    }

    public static Verbosity ParseVerbosity(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Verbosity.Normal;

        var v = value.TrimStart(':').ToLowerInvariant();
        return v switch
        {
            "q" => Verbosity.Quiet,
            "m" => Verbosity.Minimal,
            "n" => Verbosity.Normal,
            "d" => Verbosity.Detailed,
            _ => Verbosity.Normal
        };
    }

    public static HashSet<int>? ParseSectionList(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        var v = value.TrimStart(':');
        var sections = new HashSet<int>();
        foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int section) && section > 0)
            {
                sections.Add(section);
            }
        }
        return sections.Count > 0 ? sections : null;
    }
}
