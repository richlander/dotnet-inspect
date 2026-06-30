using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

public static class ProjectCommandDefinitions
{
    public static Command CreateProjectCommand(SharedOptions opts)
    {
        var projectCommand = new Command(ProjectCommand.Name, "Inspect restored project package references");

        var pathArg = new Argument<string?>("path")
        {
            Description = "Project file, project directory, or project.assets.json path (defaults to current directory)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var agentsIndexOption = new Option<bool>("--agents-index")
        {
            Description = "Emit a compact AGENTS.md frontmatter manifest for direct package dependencies"
        };
        var readmeOption = new Option<string?>("--readme")
        {
            Description = "Print the best package doc for one direct dependency, resolving its version from the project"
        };
        var tfmOption = new Option<string?>("--tfm")
        {
            Description = "Select target framework from project.assets.json (e.g., net10.0)"
        };
        var frontmatterOption = new Option<bool>("--frontmatter")
        {
            Description = "With --readme, print only the leading YAML frontmatter block"
        };
        frontmatterOption.Aliases.Add("--yaml-header");
        var bodyOption = new Option<bool>("--body")
        {
            Description = "With --readme, print only content after YAML frontmatter"
        };
        var outOption = new Option<string?>("--out")
        {
            Description = "Write output to file instead of stdout"
        };

        projectCommand.Arguments.Add(pathArg);
        projectCommand.Options.Add(agentsIndexOption);
        projectCommand.Options.Add(readmeOption);
        projectCommand.Options.Add(tfmOption);
        projectCommand.Options.Add(frontmatterOption);
        projectCommand.Options.Add(bodyOption);
        projectCommand.Options.Add(outOption);
        projectCommand.Options.Add(opts.Json);
        projectCommand.Options.Add(opts.Bare);
        opts.AddTableOptionsTo(projectCommand);
        opts.AddOutputOptionsTo(projectCommand);
        opts.AddSectionOptionsTo(projectCommand);
        opts.AddCountOptionTo(projectCommand);
        opts.AddPrintOptionTo(projectCommand);
        opts.AddShapeProjectionOptionsTo(projectCommand);
        opts.AddNuGetOptionsTo(projectCommand);

        projectCommand.SetAction(async (parseResult, ct) =>
        {
            var frontmatterRequested = parseResult.GetValue(frontmatterOption);
            var bodyRequested = parseResult.GetValue(bodyOption);
            var contentScope = frontmatterRequested
                ? PackageFileContentScope.Frontmatter
                : bodyRequested
                    ? PackageFileContentScope.Body
                    : PackageFileContentScope.Full;

            var options = new ProjectOptions
            {
                ProjectPath = parseResult.GetValue(pathArg) ?? ".",
                AgentsIndex = parseResult.GetValue(agentsIndexOption),
                ReadmePackageId = parseResult.GetValue(readmeOption),
                Print = parseResult.GetValue(opts.Print),
                PrintAll = parseResult.GetValue(opts.PrintAll),
                PrintRow = opts.ParsePrintRow(parseResult),
                Value = parseResult.GetValue(opts.Value),
                Urls = parseResult.GetValue(opts.Urls),
                Paths = parseResult.GetValue(opts.Paths),
                JsonArray = parseResult.GetValue(opts.JsonArray),
                Tfm = parseResult.GetValue(tfmOption),
                ContentScope = contentScope,
                FrontmatterRequested = frontmatterRequested,
                BodyRequested = bodyRequested,
                OutputPath = parseResult.GetValue(outOption),
                JsonOutput = parseResult.GetValue(opts.Json),
                OneLine = opts.ResolveOneLine(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Bare = parseResult.GetValue(opts.Bare),
                Discover = opts.ParseDiscover(parseResult),
                Tree = opts.ParseTree(parseResult),
                Schema = opts.ParseSchema(parseResult),
                Select = opts.ParseSelect(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Count = parseResult.GetValue(opts.Count),
                Rows = opts.ParseRows(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            return await ProjectCommand.ExecuteAsync(options);
        });

        return projectCommand;
    }
}
