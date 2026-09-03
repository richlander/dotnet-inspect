using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Command definition for <c>match</c>: pairwise structural-clone correspondence between two
/// methods in one retained assembly (issue #4304), plus seeded discovery that ranks a bounded
/// candidate population against one seed (issue #4740).
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
            Description = "Seed method selector (Type.Member) or MethodDef token (0x06000123)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var rightArg = new Argument<string?>("right")
        {
            Description = "Second method selector (Type.Member); with --similar, the candidate type scope",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, name@version, or name@A..B)" };
        var atOption = new Option<string?>("--at") { Description = "Address in a package range: exact version, #N, first, or last" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard)" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var configDirectoryOption = new Option<string?>("--nugetconfig-directory")
        {
            Description = "Source: discover the ambient NuGet.Config hierarchy from this directory",
        };
        var allOption = new Option<bool>("--all") { Description = "Include non-public members when resolving selectors" };
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var implementationOption = new Option<bool>("--implementation")
        {
            Description = "Also decompile both members and render a side-by-side C#/IL implementation-diff view",
        };
        var similarOption = new Option<bool>("--similar")
        {
            Description = "Discovery: rank candidate methods by structural similarity to the seed instead of comparing a pair",
        };
        var assemblyWideOption = new Option<bool>("--assembly-wide")
        {
            Description = "--similar: search every method in the candidate assembly instead of one type (unbounded scope)",
        };
        var topOption = new Option<int?>("--top")
        {
            Description = "--similar: number of ranked rows to render as text. Bounds presentation only; JSON keeps every candidate",
        };
        var maxResultsOption = new Option<int?>("--max-results")
        {
            Description = "--similar: product retrieval limit for ranked candidates (default 100)",
        };
        var maxMethodsOption = new Option<int?>("--max-methods")
        {
            Description = "--similar: product limit on candidate methods scanned (default 50000)",
        };

        matchCommand.Arguments.Add(leftArg);
        matchCommand.Arguments.Add(rightArg);
        matchCommand.Options.Add(packageOption);
        matchCommand.Options.Add(atOption);
        matchCommand.Options.Add(assemblyOption);
        matchCommand.Options.Add(platformOption);
        matchCommand.Options.Add(frameworkOption);
        matchCommand.Options.Add(tfmOption);
        matchCommand.Options.Add(configDirectoryOption);
        matchCommand.Options.Add(allOption);
        matchCommand.Options.Add(opts.Json);
        matchCommand.Options.Add(compactOption);
        matchCommand.Options.Add(implementationOption);
        matchCommand.Options.Add(similarOption);
        matchCommand.Options.Add(assemblyWideOption);
        matchCommand.Options.Add(topOption);
        matchCommand.Options.Add(maxResultsOption);
        matchCommand.Options.Add(maxMethodsOption);
        opts.AddTableOptionsTo(matchCommand);
        opts.AddOutputOptionsTo(matchCommand);
        opts.AddNuGetOptionsTo(matchCommand);

        matchCommand.SetAction(async (parseResult, ct) =>
        {
            var left = parseResult.GetValue(leftArg);
            var right = parseResult.GetValue(rightArg);
            var similar = parseResult.GetValue(similarOption);

            if (string.IsNullOrEmpty(left) || (!similar && string.IsNullOrEmpty(right)))
            {
                if (similar)
                {
                    CommandError.Write("match --similar requires a seed method selector.");
                    CommandError.WriteLine("Usage: dotnet-inspect match <Type.Member> [<CandidateType>] --similar --package <pkg>");
                    return 1;
                }

                // A discovery-only flag says what the caller meant more clearly than the missing
                // selector does, so answer that before demanding a second selector.
                string? discoveryOnly = MatchCommand.DiscoveryOnlyFlag(
                    parseResult.GetValue(assemblyWideOption),
                    parseResult.GetValue(topOption),
                    parseResult.GetValue(maxResultsOption),
                    parseResult.GetValue(maxMethodsOption));

                if (discoveryOnly is not null)
                {
                    MatchCommand.WriteDiscoveryOnlyError(discoveryOnly);
                    return 1;
                }

                CommandError.Write("match requires two method selectors (Type.Member).");
                CommandError.WriteLine("Usage: dotnet-inspect match <Type.MemberA> <Type.MemberB> --package <pkg>");

                return 1;
            }

            string? configDirectory = parseResult.GetValue(configDirectoryOption);
            if (configDirectory is not null)
            {
                try
                {
                    configDirectory = Path.GetFullPath(configDirectory);
                }
                catch (Exception ex) when (ex is
                    ArgumentException
                    or IOException
                    or NotSupportedException)
                {
                    CommandError.Write(
                        "--nugetconfig-directory must identify a usable directory.");
                    return 1;
                }

                if (!Directory.Exists(configDirectory))
                {
                    CommandError.Write(
                        $"NuGet config discovery directory not found: '{configDirectory}'.");
                    return 1;
                }
            }

            var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
            if (sourceOptions.ConfigFile is not null && configDirectory is not null)
            {
                CommandError.Write(
                    "--nugetconfig and --nugetconfig-directory cannot be combined.");
                return 1;
            }

            sourceOptions = sourceOptions with
            {
                ConfigDirectory = configDirectory,
            };
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
                Similar = similar,
                AssemblyWide = parseResult.GetValue(assemblyWideOption),
                Top = parseResult.GetValue(topOption),
                MaximumResults = parseResult.GetValue(maxResultsOption),
                MaximumMethods = parseResult.GetValue(maxMethodsOption),
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Verbose = parseResult.GetValue(opts.Verbose),
                Rows = opts.ParseRows(parseResult),
                Bare = parseResult.GetValue(opts.Bare),
                SourceOptions = sourceOptions,
            };

            return await MatchCommand.ExecuteAsync(options);
        });

        return matchCommand;
    }
}
