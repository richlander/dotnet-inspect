using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

public static class BuildCommandDefinitions
{
    public static Command CreateBuildCommand(SharedOptions opts)
    {
        var buildCommand = new Command(BuildCommand.Name, "Inspect an MSBuild JSONL event stream");
        var pathArg = new Argument<string>("path")
        {
            Description = "Path to an MSBuild event stream JSONL file"
        };

        buildCommand.Arguments.Add(pathArg);
        buildCommand.Options.Add(opts.Json);
        buildCommand.Options.Add(opts.Mermaid);
        opts.AddTableOptionsTo(buildCommand);
        opts.AddSectionOptionsTo(buildCommand);

        buildCommand.SetAction((parseResult) =>
        {
            var options = new BuildOptions
            {
                Path = parseResult.GetValue(pathArg)!,
                Format = opts.ResolveFormat(parseResult, OutputFormat.Table),
                Discover = opts.ParseDiscover(parseResult),
                Select = opts.ParseSelect(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
            };

            return BuildCommand.Execute(options);
        });

        return buildCommand;
    }
}
