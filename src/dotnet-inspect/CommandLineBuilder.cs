using System.CommandLine;
using DotnetInspector.CommandLine;
using System.CommandLine.Help;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector;

/// <summary>
/// Builds the System.CommandLine command structure.
/// </summary>
public static class CommandLineBuilder
{
    /// <summary>
    /// When the -NN shorthand is used (e.g. -30), stores the line limit.
    /// Delegates to <see cref="ArgumentPreprocessor.HeadLines"/> for backward compatibility.
    /// </summary>
    public static int? HeadLines => ArgumentPreprocessor.HeadLines;

    /// <summary>
    /// Known commands for implicit package command detection.
    /// Delegates to <see cref="ArgumentPreprocessor.KnownCommands"/> for backward compatibility.
    /// </summary>
    public static HashSet<string> KnownCommands => ArgumentPreprocessor.KnownCommands;

    // Scope constants delegated to ScopeConstants for backward compatibility
    internal static string[] PlatformFrameworkNames => ScopeConstants.PlatformFrameworks;
    internal static string[] ExtensionsScopePackages => ScopeConstants.ExtensionsPackages;
    internal static string[] AspNetCoreScopePackages => ScopeConstants.AspNetCorePackages;
    internal static string[] CuratedScopePackages => ScopeConstants.CuratedPackages;

    /// <summary>
    /// Pre-processes args to handle implicit package command and platform framework shorthands.
    /// Delegates to <see cref="ArgumentPreprocessor.PreprocessArgs"/> for backward compatibility.
    /// </summary>
    public static string[] PreprocessArgs(string[] args) => ArgumentPreprocessor.PreprocessArgs(args);

    /// <summary>
    /// Creates the root command with all subcommands configured.
    /// </summary>
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand(
            $"{VersionInfo.ToolName} {VersionInfo.Version} - A CLI tool for inspecting .NET libraries and NuGet packages");

        // Shared options container (defined once, reused across commands)
        var opts = new SharedOptions();

        // Root-level display options (distinct instances so they appear in root help)
        var rootVerbosityOption = new Option<string?>("-v") { Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)" };
        rootCommand.Options.Add(rootVerbosityOption);
        var rootTipsOption = new Option<string?>("--tips") { Description = "Tip verbosity: q(uiet), m(inimal), d(etailed)", Arity = ArgumentArity.ZeroOrOne };
        rootTipsOption.Aliases.Add("-T");
        rootCommand.Options.Add(rootTipsOption);
        var offlineOption = new Option<bool>("--offline") { Description = "Disable all network access (use cached data only)" };
        rootCommand.Options.Add(offlineOption);

        // API command (deprecated, hidden)
        rootCommand.Subcommands.Add(ApiCommandDefinitions.CreateDeprecatedApiCommand());

        // Type command (type discovery, terse)
        rootCommand.Subcommands.Add(ApiCommandDefinitions.CreateTypeCommand(opts));

        // Member command (member inspection, docs by default)
        rootCommand.Subcommands.Add(ApiCommandDefinitions.CreateMemberCommand(opts));

        // Assembly command
        rootCommand.Subcommands.Add(InspectionCommandDefinitions.CreateAssemblyCommand(opts));

        // Cache command
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreateCacheCommand(opts));

        // Demo command
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreateDemoCommand(rootCommand, opts));

        // Diff command
        rootCommand.Subcommands.Add(InspectionCommandDefinitions.CreateDiffCommand(opts));

        // Depends command
        rootCommand.Subcommands.Add(SearchCommandDefinitions.CreateDependsCommand(opts));

        // Extensions command
        rootCommand.Subcommands.Add(SearchCommandDefinitions.CreateExtensionsCommand(opts));

        // Find command
        rootCommand.Subcommands.Add(SearchCommandDefinitions.CreateFindCommand(opts));

        // Implements command
        rootCommand.Subcommands.Add(SearchCommandDefinitions.CreateImplementsCommand(opts));

        // Package command
        rootCommand.Subcommands.Add(PackageCommandDefinitions.CreatePackageCommand(opts));

        // Router command (hidden, implicit default for bare names)
        rootCommand.Subcommands.Add(RouterCommandDefinition.Create(opts));

        // Samples command
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreateSamplesCommand(opts));

        // CLI command (meta command)
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreateCliCommand(rootCommand, opts));

        // LLMs.txt command (meta command, listed last)
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreateLlmsTxtCommand(opts));

        // Skill command
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreateSkillCommand(opts));

        // Perf command (hidden, for profiling various code paths)
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreatePerfCommand());

        // Perf-test command (hidden, for profiling)
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreatePerfTestCommand());

        // No-args: show help + tips
        rootCommand.SetAction((parseResult) =>
        {
            var sw = new System.IO.StringWriter();
            var original = Console.Out;
            Console.SetOut(sw);
            new HelpAction().Invoke(parseResult);
            Console.SetOut(original);
            Console.WriteLine(sw.ToString().TrimEnd());

            var verbosity = ParseVerbosity(parseResult.GetValue(rootVerbosityOption));
            var tipLevel = verbosity == Verbosity.Quiet || HeadLines != null
                ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(rootTipsOption), parseResult.GetResult(rootTipsOption) != null);
            Hints.WriteTips(tipLevel,
                new Tip(PackageCommand.Name, "<package>", "inspect a NuGet package"),
                new Tip(LlmsTxtCommand.Name, "", "complete usage examples"),
                new Tip("-T:d", "", "show more tips per command"),
                new Tip(TypeCommand.Name, "--package <package>", "discover types in package"),
                new Tip(MemberCommand.Name, "JsonSerializer --package System.Text.Json", "inspect type members"),
                new Tip(FindCommand.Name, "<pattern> --package <package>", "search package types"),
                new Tip(FindCommand.Name, "<pattern> --platform", "search platform libraries"));
        });

        return rootCommand;
    }

    // Parse helpers delegated to OptionParsers (for backward compatibility)
    public static Verbosity ParseVerbosity(string? value) => OptionParsers.ParseVerbosity(value);
    public static TipLevel ParseTipLevel(string? value, bool optionPresent) => OptionParsers.ParseTipLevel(value, optionPresent);
    public static HashSet<string>? ParseSectionList(string? value) => OptionParsers.ParseSectionList(value);
    public static NuGetSourceOptions ParseNuGetSourceOptions(
        ParseResult parseResult, Option<string[]> sourceOption,
        Option<string[]> addSourceOption, Option<string?> nugetConfigOption)
        => OptionParsers.ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption);

    /// <summary>
    /// Parses a -t value as either a numeric limit or null (glob patterns are handled separately).
    /// Delegates to <see cref="CommandLineHelpers.ParseTypeLimit"/> for backward compatibility.
    /// </summary>
    internal static int? ParseTypeLimit(string? value) => CommandLineHelpers.ParseTypeLimit(value);

    /// <summary>
    /// Classifies a positional argument by file extension.
    /// Delegates to <see cref="CommandLineHelpers.TryClassifyAsFilePath"/> for backward compatibility.
    /// </summary>
    internal static bool TryClassifyAsFilePath(string? positional, out string? libraryPath, out string? packagePath)
        => CommandLineHelpers.TryClassifyAsFilePath(positional, out libraryPath, out packagePath);

    /// <summary>
    /// Returns true if the value looks like a version number (e.g. "2.0.0", "8.0.0-preview.1").
    /// Delegates to <see cref="CommandLineHelpers.LooksLikeVersionNumber"/> for backward compatibility.
    /// </summary>
    internal static bool LooksLikeVersionNumber(string? value)
        => CommandLineHelpers.LooksLikeVersionNumber(value);
}
