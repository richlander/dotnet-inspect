using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the top-level audit workflow command.
/// </summary>
public static class AuditCommandDefinitions
{
    public static Command CreateAuditCommand(SharedOptions opts)
    {
        var auditCommand = new Command("audit", "Inspect package or library audit signals");

        var targetArg = new Argument<string?>("target")
        {
            Description = "Package name, platform library, .dll, or .nupkg",
            Arity = ArgumentArity.ZeroOrOne
        };
        targetArg.DefaultValueFactory = _ => null;

        var fullOption = new Option<bool>("--full") { Description = "Enable broad target-appropriate audit expansion" };
        fullOption.Aliases.Add("--all");
        var symbolsOption = new Option<bool>("--symbols") { Description = "Allow PDB/symbol acquisition" };
        var sourceAuditOption = new Option<bool>("--source-audit") { Description = "Verify every SourceLink source URL (expensive; implies --symbols)" };
        sourceAuditOption.Aliases.Add("--source");
        var nugetOption = new Option<bool>("--nuget") { Description = "Expand package audit with NuGet registry signals" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Optional platform framework family (runtime, aspnetcore)" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select package/library TFM (e.g., net8.0)" };
        var versionOption = new Option<string?>("--version") { Description = "Platform or package version" };
        var nugetSourceOption = new Option<string[]>("--nuget-source")
        {
            Description = "NuGet source URL (replaces defaults, can repeat)",
            AllowMultipleArgumentsPerToken = true
        };

        auditCommand.Arguments.Add(targetArg);
        auditCommand.Options.Add(fullOption);
        auditCommand.Options.Add(symbolsOption);
        auditCommand.Options.Add(sourceAuditOption);
        auditCommand.Options.Add(nugetOption);
        auditCommand.Options.Add(frameworkOption);
        auditCommand.Options.Add(tfmOption);
        auditCommand.Options.Add(versionOption);
        auditCommand.Options.Add(nugetSourceOption);
        auditCommand.Options.Add(opts.AddSource);
        auditCommand.Options.Add(opts.NuGetConfig);
        AddAuditOutputOptions(auditCommand, opts);

        auditCommand.Subcommands.Add(CreateAuditPackageCommand(opts));
        auditCommand.Subcommands.Add(CreateAuditLibraryCommand(opts));

        auditCommand.SetAction(async (parseResult, ct) =>
        {
            var target = parseResult.GetValue(targetArg);
            if (string.IsNullOrWhiteSpace(target))
            {
                Console.Error.WriteLine("Error: Audit target required.");
                Console.Error.WriteLine("Examples:");
                Console.Error.WriteLine("  dotnet-inspect audit System.Text.Json");
                Console.Error.WriteLine("  dotnet-inspect audit System.Text.Json --full");
                Console.Error.WriteLine("  dotnet-inspect audit package Markout --full");
                return 1;
            }

            var auditOptions = ParseAuditOptions(parseResult, opts, fullOption, symbolsOption, sourceAuditOption, nugetOption,
                frameworkOption, tfmOption, versionOption, nugetSourceOption);

            if (auditOptions.NuGet && (auditOptions.Symbols || auditOptions.SourceAudit))
            {
                Console.Error.WriteLine("Error: --nuget audits package registry data and cannot be combined with --symbols or --source-audit.");
                Console.Error.WriteLine("Use 'audit library <target> --source-audit' for SourceLink verification.");
                return 1;
            }

            if (auditOptions.NuGet)
                return await ExecutePackageAuditAsync(target, auditOptions);

            if (LooksLikePackageFile(target))
                return await ExecutePackageAuditAsync(target, auditOptions);

            if (File.Exists(target))
                return await ExecuteLibraryAuditAsync(new LibraryAuditTarget(AssemblyName: target), auditOptions);

            if (PlatformResolver.IsPlatformCandidate(target))
            {
                bool verbose = auditOptions.Verbose;
                Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
                var (resolvedPath, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
                    target, HttpClientFactory.Shared, log, auditOptions.Framework,
                    useRuntimeAssemblies: true,
                    platformVersion: auditOptions.Version);

                if (resolvedPath != null && error == null)
                    return await ExecuteLibraryAuditAsync(new LibraryAuditTarget(PlatformAssembly: target), auditOptions);

                if (!string.IsNullOrEmpty(auditOptions.Framework) || !string.IsNullOrEmpty(auditOptions.Version))
                {
                    Console.Error.WriteLine($"Error: {error}");
                    Console.Error.WriteLine("Use 'audit package <package> --version <version>' for package audit.");
                    return 1;
                }

                if (auditOptions.Symbols || auditOptions.SourceAudit)
                {
                    Console.Error.WriteLine($"Error: '{target}' did not resolve to an installed platform library; --symbols and --source-audit require a library target.");
                    Console.Error.WriteLine("Use 'audit package <package> --full' for package registry audit.");
                    return 1;
                }
            }

            if (auditOptions.Symbols || auditOptions.SourceAudit)
            {
                Console.Error.WriteLine($"Error: '{target}' did not resolve to a library target; --symbols and --source-audit require a library target.");
                Console.Error.WriteLine("Use 'audit package <package> --full' for package registry audit.");
                return 1;
            }

            return await ExecutePackageAuditAsync(target, auditOptions);
        });

        return auditCommand;
    }

    private static Command CreateAuditPackageCommand(SharedOptions opts)
    {
        var packageCommand = new Command("package", "Audit a NuGet package");
        var packageArg = new Argument<string>("package") { Description = "Package name, package@version, or .nupkg path" };
        var fullOption = new Option<bool>("--full") { Description = "Enable broad package audit expansion" };
        fullOption.Aliases.Add("--all");
        var nugetOption = new Option<bool>("--nuget") { Description = "Expand with NuGet registry signals" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select package TFM (e.g., net8.0)" };
        var versionOption = new Option<string?>("--version") { Description = "Package version" };
        var nugetSourceOption = new Option<string[]>("--nuget-source")
        {
            Description = "NuGet source URL (replaces defaults, can repeat)",
            AllowMultipleArgumentsPerToken = true
        };

        packageCommand.Arguments.Add(packageArg);
        packageCommand.Options.Add(fullOption);
        packageCommand.Options.Add(nugetOption);
        packageCommand.Options.Add(tfmOption);
        packageCommand.Options.Add(versionOption);
        packageCommand.Options.Add(nugetSourceOption);
        packageCommand.Options.Add(opts.AddSource);
        packageCommand.Options.Add(opts.NuGetConfig);
        AddAuditOutputOptions(packageCommand, opts);

        packageCommand.SetAction(async (parseResult, ct) =>
        {
            var auditOptions = ParseAuditOptions(parseResult, opts, fullOption, null, null, nugetOption,
                null, tfmOption, versionOption, nugetSourceOption);
            return await ExecutePackageAuditAsync(parseResult.GetValue(packageArg)!, auditOptions);
        });

        return packageCommand;
    }

    private static Command CreateAuditLibraryCommand(SharedOptions opts)
    {
        var libraryCommand = new Command("library", "Audit a .NET library");
        var sourceArg = new Argument<string>("source") { Description = "Library file, platform library, package name, or package@version" };
        var fullOption = new Option<bool>("--full") { Description = "Enable broad library audit expansion" };
        fullOption.Aliases.Add("--all");
        var symbolsOption = new Option<bool>("--symbols") { Description = "Allow PDB/symbol acquisition" };
        var sourceAuditOption = new Option<bool>("--source-audit") { Description = "Verify every SourceLink source URL (expensive; implies --symbols)" };
        sourceAuditOption.Aliases.Add("--source");
        var frameworkOption = new Option<string?>("--framework") { Description = "Optional platform framework family (runtime, aspnetcore)" };
        var versionOption = new Option<string?>("--version") { Description = "Platform runtime version" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library TFM (e.g., net8.0)" };
        var nugetSourceOption = new Option<string[]>("--nuget-source")
        {
            Description = "NuGet source URL (replaces defaults, can repeat)",
            AllowMultipleArgumentsPerToken = true
        };

        libraryCommand.Arguments.Add(sourceArg);
        libraryCommand.Options.Add(fullOption);
        libraryCommand.Options.Add(symbolsOption);
        libraryCommand.Options.Add(sourceAuditOption);
        libraryCommand.Options.Add(frameworkOption);
        libraryCommand.Options.Add(versionOption);
        libraryCommand.Options.Add(tfmOption);
        libraryCommand.Options.Add(nugetSourceOption);
        libraryCommand.Options.Add(opts.AddSource);
        libraryCommand.Options.Add(opts.NuGetConfig);
        AddAuditOutputOptions(libraryCommand, opts);

        libraryCommand.SetAction(async (parseResult, ct) =>
        {
            var auditOptions = ParseAuditOptions(parseResult, opts, fullOption, symbolsOption, sourceAuditOption, null,
                frameworkOption, tfmOption, versionOption, nugetSourceOption);
            var source = parseResult.GetValue(sourceArg)!;
            var target = await ResolveLibraryTargetAsync(source, auditOptions);
            if (target == null)
                return 1;

            return await ExecuteLibraryAuditAsync(target, auditOptions);
        });

        return libraryCommand;
    }

    private static void AddAuditOutputOptions(Command command, SharedOptions opts)
    {
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        command.Options.Add(opts.Verbose);
        command.Options.Add(opts.Verbosity);
        command.Options.Add(opts.Tips);
        command.Options.Add(opts.Info);
    }

    private static AuditOptions ParseAuditOptions(
        ParseResult parseResult,
        SharedOptions opts,
        Option<bool>? fullOption,
        Option<bool>? symbolsOption,
        Option<bool>? sourceAuditOption,
        Option<bool>? nugetOption,
        Option<string?>? frameworkOption,
        Option<string?>? tfmOption,
        Option<string?>? versionOption,
        Option<string[]> nugetSourceOption)
    {
        var full = fullOption != null && parseResult.GetValue(fullOption);
        var sourceAudit = sourceAuditOption != null && parseResult.GetValue(sourceAuditOption);
        var symbols = sourceAudit || (symbolsOption != null && parseResult.GetValue(symbolsOption));
        var nuget = nugetOption != null && parseResult.GetValue(nugetOption);
        var verbosity = opts.ParseVerbosity(parseResult);

        return new AuditOptions(
            Full: full,
            Symbols: symbols,
            SourceAudit: sourceAudit,
            NuGet: nuget,
            Framework: frameworkOption == null ? null : parseResult.GetValue(frameworkOption),
            Tfm: tfmOption == null ? null : parseResult.GetValue(tfmOption),
            Version: versionOption == null ? null : parseResult.GetValue(versionOption),
            JsonOutput: parseResult.GetValue(opts.Json),
            Markdown: parseResult.GetValue(opts.Markdown),
            PlainText: parseResult.GetValue(opts.PlainText),
            Verbose: parseResult.GetValue(opts.Verbose),
            Verbosity: verbosity,
            SourceOptions: ParseAuditNuGetSourceOptions(parseResult, opts, nugetSourceOption));
    }

    private static NuGetSourceOptions ParseAuditNuGetSourceOptions(
        ParseResult parseResult,
        SharedOptions opts,
        Option<string[]> nugetSourceOption)
    {
        var sources = parseResult.GetValue(nugetSourceOption) ?? [];
        var addSources = parseResult.GetValue(opts.AddSource) ?? [];
        var configFile = parseResult.GetValue(opts.NuGetConfig);

        if (sources.Length == 0 && addSources.Length == 0 && configFile == null)
            return NuGetSourceOptions.Default;

        return new NuGetSourceOptions
        {
            Sources = sources,
            AdditionalSources = addSources,
            ConfigFile = configFile
        };
    }

    private static async Task<LibraryAuditTarget?> ResolveLibraryTargetAsync(string source, AuditOptions options)
    {
        if (File.Exists(source))
            return new LibraryAuditTarget(AssemblyName: source);

        if (PlatformResolver.IsPlatformCandidate(source))
        {
            Action<string>? log = options.Verbose ? msg => Console.Error.WriteLine(msg) : null;
            var (resolvedPath, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
                source, HttpClientFactory.Shared, log, options.Framework,
                useRuntimeAssemblies: true,
                platformVersion: options.Version);

            if (resolvedPath != null && error == null)
                return new LibraryAuditTarget(PlatformAssembly: source);

            if (!string.IsNullOrEmpty(options.Framework) || !string.IsNullOrEmpty(options.Version))
            {
                Console.Error.WriteLine($"Error: {error}");
                Console.Error.WriteLine("Use 'audit package <package> --version <version>' for package audit.");
                return null;
            }
        }

        return new LibraryAuditTarget(PackagePath: source);
    }

    private static async Task<int> ExecuteLibraryAuditAsync(LibraryAuditTarget target, AuditOptions audit)
    {
        audit = ExpandForLibrary(audit);

        var includeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Audit" };
        if (audit.SourceAudit)
        {
            includeSections.Add("SourceLink Audit");
            includeSections.Add("Missing Source Files");
        }

        var options = new AssemblyOptions
        {
            AssemblyName = target.AssemblyName,
            IncludeMetadata = true,
            IncludeReferences = true,
            PackagePath = target.PackagePath,
            PlatformAssembly = target.PlatformAssembly,
            PlatformFramework = audit.Framework,
            PlatformVersion = audit.Version,
            Tfm = audit.Tfm,
            JsonOutput = audit.JsonOutput,
            Markdown = audit.Markdown,
            OneLine = false,
            Format = audit.PlainText ? OutputFormat.PlainText : OutputFormat.Markdown,
            Verbose = audit.Verbose,
            Verbosity = audit.Verbosity,
            IncludeSections = includeSections,
            SourceOptions = audit.SourceOptions,
            Audit = true,
            AllowSymbolDownloads = audit.Symbols,
            SourceLinkAudit = audit.SourceAudit
        };

        return await AssemblyCommand.ExecuteAsync(options);
    }

    private static Task<int> ExecutePackageAuditAsync(string package, AuditOptions audit)
    {
        audit = ExpandForPackage(audit);

        var packageArgs = string.IsNullOrWhiteSpace(audit.Version)
            ? new[] { package }
            : new[] { package, audit.Version! };

        var options = new InspectionOptions
        {
            PackageArgs = packageArgs,
            ExplicitVersion = audit.Version,
            Tfm = audit.Tfm,
            JsonOutput = audit.JsonOutput,
            OneLine = false,
            Verbose = audit.Verbose,
            Verbosity = audit.Verbosity,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PackageSections.Audit },
            SourceOptions = audit.SourceOptions,
            TipLevel = TipLevel.Quiet,
            Audit = true,
            NuGetAudit = audit.NuGet
        };

        return PackageCommand.ExecuteAsync(options);
    }

    private static bool LooksLikePackageFile(string target) =>
        target.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

    private static AuditOptions ExpandForLibrary(AuditOptions audit)
    {
        var detailed = audit.Verbosity >= Verbosity.Detailed;
        return audit with
        {
            Symbols = audit.Symbols || audit.Full || detailed,
            SourceAudit = audit.SourceAudit || detailed
        };
    }

    private static AuditOptions ExpandForPackage(AuditOptions audit) =>
        audit.Full ? audit with { NuGet = true } : audit;

    private sealed record AuditOptions(
        bool Full,
        bool Symbols,
        bool SourceAudit,
        bool NuGet,
        string? Framework,
        string? Tfm,
        string? Version,
        bool JsonOutput,
        bool Markdown,
        bool PlainText,
        bool Verbose,
        Verbosity Verbosity,
        NuGetSourceOptions SourceOptions);

    private sealed record LibraryAuditTarget(
        string? AssemblyName = null,
        string? PlatformAssembly = null,
        string? PackagePath = null);
}
