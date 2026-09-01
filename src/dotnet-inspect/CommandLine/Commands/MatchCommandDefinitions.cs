using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Command definition for <c>match</c>: pairwise structural-clone correspondence between two
/// methods in one retained assembly (issue #4304).
/// </summary>
public static class MatchCommandDefinitions
{
    public static Command CreateMatchCommand(SharedOptions opts)
    {
        var matchCommand = new Command(
            MatchCommand.Name,
            "Compare two methods by structural clone equivalence (identity-agnostic)");

        var leftArg = new Argument<string?>("left")
        {
            Description = "First method selector (Type.Member)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var rightArg = new Argument<string?>("right")
        {
            Description = "Second method selector (Type.Member)",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, name@version, or name@A..B)" };
        var atOption = new Option<string?>("--at") { Description = "Address in a package range: exact version, #N, first, or last" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard)" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include non-public members when resolving selectors" };
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var implementationOption = new Option<bool>("--implementation")
        {
            Description = "Also decompile both members and render a side-by-side C#/IL implementation-diff view",
        };

        matchCommand.Arguments.Add(leftArg);
        matchCommand.Arguments.Add(rightArg);
        matchCommand.Options.Add(packageOption);
        matchCommand.Options.Add(atOption);
        matchCommand.Options.Add(assemblyOption);
        matchCommand.Options.Add(platformOption);
        matchCommand.Options.Add(frameworkOption);
        matchCommand.Options.Add(tfmOption);
        matchCommand.Options.Add(allOption);
        matchCommand.Options.Add(opts.Json);
        matchCommand.Options.Add(compactOption);
        matchCommand.Options.Add(implementationOption);
        opts.AddTableOptionsTo(matchCommand);
        opts.AddOutputOptionsTo(matchCommand);
        opts.AddNuGetOptionsTo(matchCommand);

        matchCommand.SetAction(async (parseResult, ct) =>
        {
            var left = parseResult.GetValue(leftArg);
            var right = parseResult.GetValue(rightArg);

            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                CommandError.Write("match requires two method selectors (Type.Member).");
                CommandError.WriteLine("Usage: dotnet-inspect match <Type.MemberA> <Type.MemberB> --package <pkg>");
                return 1;
            }

            if (opts.RejectUnsupportedDocumentJsonRowWindowBeforeAcquisition(
                parseResult,
                MatchCommand.Name))
            {
                return 1;
            }

            var options = new MatchOptions
            {
                LeftSelector = left,
                RightSelector = right,
                PackagePath = parseResult.GetValue(packageOption),
                PackageRangeAddress = parseResult.GetValue(atOption),
                AssemblyPath = parseResult.GetValue(assemblyOption),
                PlatformAssembly = parseResult.GetValue(platformOption),
                PlatformFramework = parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
                CompactJson = parseResult.GetValue(compactOption),
                IncludeImplementation = parseResult.GetValue(implementationOption),
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Verbose = parseResult.GetValue(opts.Verbose),
                Rows = opts.ParseRows(parseResult),
                Bare = parseResult.GetValue(opts.Bare),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            };

            return await MatchCommand.ExecuteAsync(options);
        });

        return matchCommand;
    }
}
