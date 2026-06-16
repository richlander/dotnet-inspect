using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the hidden router command that auto-resolves package or platform library.
/// </summary>
public static class RouterCommandDefinition
{
    /// <summary>
    /// Creates the router command with all options configured.
    /// </summary>
    public static Command Create(SharedOptions opts)
    {
        var routerCommand = new Command("router", "Auto-resolve package or platform library") { Hidden = true };

        var packageNameArg = new Argument<string[]>("package")
        {
            Description = "Package or platform library name",
            Arity = ArgumentArity.ZeroOrMore
        };

        routerCommand.Arguments.Add(packageNameArg);
        opts.AddAllOptionsTo(routerCommand);
        opts.AddCountOptionTo(routerCommand);

        // Version query options for the router
        var routerVersionOption = new Option<bool>("--version") { Description = "Show resolved version" };
        routerCommand.Options.Add(routerVersionOption);
        var routerLatestVersionOption = new Option<bool>("--latest-version") { Description = "Show latest stable version from nuget.org (add --preview for prerelease)" };
        routerCommand.Options.Add(routerLatestVersionOption);
        var routerVersionsOption = new Option<int?>("--versions") { Description = "List available versions (optionally limit count)", Arity = ArgumentArity.ZeroOrOne };
        routerVersionsOption.DefaultValueFactory = _ => null;
        routerCommand.Options.Add(routerVersionsOption);
        var routerPrereleaseOption = new Option<bool>("--preview") { Description = "Include prerelease versions for --versions and latest resolution" };
        routerPrereleaseOption.Aliases.Add("--prerelease");
        routerCommand.Options.Add(routerPrereleaseOption);

        var routerCompactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        routerCommand.Options.Add(routerCompactOption);
        var routerLibraryOption = new Option<string?>("--library")
        {
            Description = "Inspect a library from the resolved package; omit value to select the primary library when unambiguous",
            Arity = ArgumentArity.ZeroOrOne
        };
        routerCommand.Options.Add(routerLibraryOption);

        var commandArgs = new RouterOptionsParser.RouterCommandArgs(
            packageNameArg, routerVersionOption, routerLatestVersionOption, routerVersionsOption,
            routerPrereleaseOption, opts.OneLine, opts.NoHeaders, routerCompactOption, routerLibraryOption);

        routerCommand.SetAction(async (parseResult, ct) =>
        {
            var result = RouterOptionsParser.Parse(parseResult, opts, commandArgs);

            switch (result)
            {
                case RouterOptionsParser.ShowHelp:
                    HelpWriter.WriteHelp(routerCommand);
                    return 0;

                case RouterOptionsParser.Discovery d:
                    // Router-level discovery: show package sections (no input required)
                    var routerSchemaMap = InspectionContext.Default.GetSchemaInfo<InspectionResultView>()!.ToDocumentSchema();
                    var routerFormat = opts.ResolveFormat(parseResult, OutputFormat.Table);
                    var routerPipeline = PackageSectionDescriptors.CreatePipeline();
                    return DiscoverOutput.Execute(d.Discover, routerSchemaMap, tree: d.Tree,
                        json: routerFormat == OutputFormat.Json,
                        tsv: routerFormat == OutputFormat.Tsv,
                        jsonl: routerFormat == OutputFormat.Jsonl,
                        markdown: routerFormat == OutputFormat.Markdown,
                        verbosity: (int)opts.ParseVerbosity(parseResult),
                        sectionCategories: routerPipeline.GetCategoryMap());

                case RouterOptionsParser.ParseError error:
                    Console.Error.WriteLine(error.Message);
                    return 1;

                case RouterOptionsParser.UnrecognizedOption error:
                    Console.Error.WriteLine($"Error: Unrecognized option '{error.Option}'.");
                    return 1;

                case RouterOptionsParser.RouteRequest request:
                    return await RouterRouteRegistry.ExecuteAsync(request, opts, parseResult, commandArgs, routerVersionsOption);

                default:
                    return 1;
            }
        });

        return routerCommand;
    }
}
