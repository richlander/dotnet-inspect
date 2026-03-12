using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the source command for SourceLink file discovery.
/// </summary>
public static class SourceCommandDefinitions
{
    public static Command CreateSourceCommand(SharedOptions opts)
    {
        var sourceCommand = new Command(SourceCommand.Name, "Show SourceLink source file URLs for types in a package or library");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package and optional type name. When no --package/--library/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, or name@version)" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard). @version for specific" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete types" };
        var memberOption = new Option<string?>("-m") { Description = "Member name to resolve (e.g., Clear, TryGetValue:2 for overload)" };
        memberOption.Aliases.Add("--member");
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter types by glob pattern (e.g., *Json*, Progress*)" };
        typeFilterOption.Aliases.Add("--type");
        var verifyOption = new Option<bool>("--verify") { Description = "Verify SourceLink URLs are accessible (HTTP HEAD)" };
        var auditOption = new Option<bool>("--audit") { Description = "Full source audit: verify all PDB source files are accessible" };
        var browsableUrlsOption = new Option<bool>("--browsable-urls") { Description = "Use /blob/ URLs for browser viewing (default: /raw/ for LLM consumption)" };
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };

        sourceCommand.Arguments.Add(argsArg);
        sourceCommand.Options.Add(packageOption);
        sourceCommand.Options.Add(assemblyOption);
        sourceCommand.Options.Add(platformOption);
        sourceCommand.Options.Add(frameworkOption);
        sourceCommand.Options.Add(tfmOption);
        sourceCommand.Options.Add(allOption);
        sourceCommand.Options.Add(memberOption);
        sourceCommand.Options.Add(typeFilterOption);
        sourceCommand.Options.Add(verifyOption);
        sourceCommand.Options.Add(auditOption);
        sourceCommand.Options.Add(browsableUrlsOption);
        sourceCommand.Options.Add(opts.Json);
        sourceCommand.Options.Add(compactOption);
        sourceCommand.Options.Add(oneLineOption);
        sourceCommand.Options.Add(noHeaderOption);
        opts.AddSectionOptionsTo(sourceCommand);
        sourceCommand.Options.Add(opts.Markdown);
        sourceCommand.Options.Add(opts.PlainText);
        opts.AddOutputOptionsTo(sourceCommand);
        opts.AddNuGetOptionsTo(sourceCommand);

        var commandArgs = new SourceOptionsParser.SourceCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, frameworkOption, tfmOption,
            allOption, memberOption, typeFilterOption, verifyOption, auditOption, browsableUrlsOption,
            compactOption, oneLineOption, noHeaderOption);

        sourceCommand.SetAction(async (parseResult, ct) =>
        {
            var result = await SourceOptionsParser.ParseAsync(parseResult, opts, commandArgs);

            switch (result)
            {
                case SourceOptionsParser.Discovery d:
                    var schemaMap = MarkoutContext.Default.GetSchemaInfo<SourceListView>()!.ToDocumentSchema();
                    return DiscoverOutput.Execute(d.Discover, schemaMap, tree: d.Tree);

                case SourceOptionsParser.ShowHelp:
                    HelpWriter.WriteHelp(sourceCommand);
                    return 0;

                case SourceOptionsParser.VersionError error:
                    Console.Error.WriteLine(error.Message);
                    return 1;

                case SourceOptionsParser.UnrecognizedOption error:
                    Console.Error.WriteLine($"Error: Unrecognized option '{error.Option}'.");
                    return 1;

                case SourceOptionsParser.Success success:
                    return await SourceCommand.ExecuteAsync(success.Options);

                default:
                    return 1;
            }
        });

        return sourceCommand;
    }
}
