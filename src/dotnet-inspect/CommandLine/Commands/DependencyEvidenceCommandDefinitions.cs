using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// The <c>dependency-evidence</c> command surface.
/// </summary>
/// <remarks>
/// Registered with no alias and no positional shorthand: every root is named by an explicit
/// source option, so a bare token never becomes a dependency-evidence root.
/// </remarks>
public static class DependencyEvidenceCommandDefinitions
{
    public static Command CreateDependencyEvidenceCommand(SharedOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);

        var command = new Command(
            DependencyEvidenceCommand.Name,
            "Report normalized direct dependency evidence for named package, nuspec, project, or package-prefix roots (declared scopes, constraints, and restored resolution; use 'depends' to walk the transitive tree)");

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Package root: ID, ID@VERSION, or a local .nupkg path (repeatable)",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        var nuspecOption = new Option<string[]>("--nuspec")
        {
            Description = "Direct nuspec root whose identity is self-attested (repeatable)",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        var projectOption = new Option<string[]>("--project")
        {
            Description = "Restored project root: project file, project directory, or project.assets.json (repeatable)",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        var packagePrefixOption = new Option<string?>("--package-prefix")
        {
            Description = $"Bounded NuGet Gallery manifest-profile root set ({DependencyEvidenceAcquisition.PackageProfileDefaultLimit} packages by default); exclusive with explicit roots",
        };
        var tfmOption = new Option<string?>("--tfm")
        {
            Description = "Requested target framework: selects a manifest group or a restored assets target without filtering declarations",
        };
        var previewOption = new Option<bool>("--preview")
        {
            Description = "Allow latest remote --package resolution to select a prerelease version",
        };
        var maxPackagesOption = new Option<int?>("--max-packages")
        {
            Description = $"Bound --package-prefix discovery (1 to {DependencyEvidenceAcquisition.PackageProfileMaximumLimit}, default {DependencyEvidenceAcquisition.PackageProfileDefaultLimit})",
        };
        var compactOption = new Option<bool>("--compact")
        {
            Description = "Minified JSON (use with --json)",
        };

        command.Options.Add(packageOption);
        command.Options.Add(nuspecOption);
        command.Options.Add(projectOption);
        command.Options.Add(packagePrefixOption);
        command.Options.Add(tfmOption);
        command.Options.Add(previewOption);
        command.Options.Add(maxPackagesOption);
        command.Options.Add(opts.Json);
        command.Options.Add(compactOption);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        opts.AddSectionOptionsTo(command);
        opts.AddCountOptionTo(command);
        opts.AddNuGetOptionsTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var options = new DependencyEvidenceOptions
            {
                Packages = parseResult.GetValue(packageOption) ?? [],
                Nuspecs = parseResult.GetValue(nuspecOption) ?? [],
                Projects = parseResult.GetValue(projectOption) ?? [],
                PackagePrefix = parseResult.GetValue(packagePrefixOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludePrerelease = parseResult.GetValue(previewOption),
                MaxPackages = parseResult.GetValue(maxPackagesOption),
                Verbosity = opts.ParseVerbosity(parseResult),
                JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
                CompactJson = parseResult.GetValue(compactOption),
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Discover = opts.ParseDiscover(parseResult),
                Tree = opts.ParseTree(parseResult),
                Schema = opts.ParseSchema(parseResult),
                Select = opts.ParseSelect(parseResult),
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Count = parseResult.GetValue(opts.Count),
                Rows = opts.ParseRows(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            };

            return await DependencyEvidenceCommand.ExecuteAsync(
                options,
                cancellationToken);
        });

        return command;
    }
}
