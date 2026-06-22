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
        var pathArg = new Argument<string>("event-log")
        {
            Description = "EventLogId or path to an MSBuild event stream JSONL file"
        };

        buildCommand.Arguments.Add(pathArg);
        buildCommand.Options.Add(opts.Json);
        buildCommand.Options.Add(opts.Markdown);
        buildCommand.Options.Add(opts.Mermaid);
        opts.AddTableOptionsTo(buildCommand);
        opts.AddSectionOptionsTo(buildCommand);
        buildCommand.Options.Add(opts.Limit);
        buildCommand.Options.Add(opts.Tail);
        var codeOption = new Option<string?>("--code") { Description = "Filter diagnostics by code, e.g. CS1061" };
        var severityOption = new Option<string?>("--severity") { Description = "Filter diagnostics by severity, e.g. error or warning" };
        var projectOption = new Option<string?>("--project") { Description = "Filter diagnostics by project path/name" };
        var fileOption = new Option<string?>("--file") { Description = "Filter diagnostics by source file path/name" };
        var diagnosticOption = new Option<string?>("--diagnostic") { Description = "Select one rich diagnostic card by selector or digest" };
        var clusterOption = new Option<string?>("--cluster") { Description = "Select one Explain cluster by id" };
        var cardsOption = new Option<int?>("--cards") { Description = "Limit rich diagnostic output to the first N cards" };
        var tailCardsOption = new Option<int?>("--tail-cards") { Description = "Limit rich diagnostic output to the last N cards" };
        buildCommand.Options.Add(codeOption);
        buildCommand.Options.Add(severityOption);
        buildCommand.Options.Add(projectOption);
        buildCommand.Options.Add(fileOption);
        buildCommand.Options.Add(diagnosticOption);
        buildCommand.Options.Add(clusterOption);
        buildCommand.Options.Add(cardsOption);
        buildCommand.Options.Add(tailCardsOption);

        buildCommand.SetAction((parseResult) =>
        {
            var options = new BuildOptions
            {
                Path = parseResult.GetValue(pathArg)!,
                Format = opts.ResolveFormat(parseResult, OutputFormat.Table),
                Discover = opts.ParseDiscover(parseResult),
                Select = opts.ParseSelect(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                RowLimit = parseResult.GetValue(opts.Limit),
                TailLimit = parseResult.GetValue(opts.Tail),
                Code = parseResult.GetValue(codeOption),
                Severity = parseResult.GetValue(severityOption),
                Project = parseResult.GetValue(projectOption),
                File = parseResult.GetValue(fileOption),
                Diagnostic = parseResult.GetValue(diagnosticOption),
                Cluster = parseResult.GetValue(clusterOption),
                CardLimit = parseResult.GetValue(cardsOption),
                TailCardLimit = parseResult.GetValue(tailCardsOption),
            };

            return BuildCommand.Execute(options);
        });

        return buildCommand;
    }
}
