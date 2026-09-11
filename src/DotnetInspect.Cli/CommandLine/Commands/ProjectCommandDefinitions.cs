using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

public static class ProjectCommandDefinitions
{
    public static Command CreateProjectCommand(SharedOptions opts)
    {
        var projectCommand = new Command(
            ProjectCommand.Name,
            "Inspect package skills and README files from a restored project")
        {
            TreatUnmatchedTokensAsErrors = true
        };

        var pathArg = new Argument<string?>("path")
        {
            Description = "Project file, project directory, or project.assets.json path (defaults to current directory)",
            Arity = ArgumentArity.ZeroOrOne
        };
        pathArg.Validators.Add(result =>
        {
            if (result.Tokens.Count == 0)
                return;

            if (GetRemovedOptionError(result.Tokens[^1].Value) is { } error)
                result.AddError(error);
        });
        var tfmOption = new Option<string?>("--tfm")
        {
            Description = "Select target framework from project.assets.json (e.g., net10.0)"
        };
        var frontmatterOption = new Option<bool>("--frontmatter")
        {
            Description = "With --print or --bare, print only the leading YAML frontmatter block"
        };
        frontmatterOption.Aliases.Add("--yaml-header");
        var bodyOption = new Option<bool>("--body")
        {
            Description = "With --print or --bare, print only content after YAML frontmatter"
        };
        var outOption = new Option<string?>("--out")
        {
            Description = "Write output to file instead of stdout"
        };
        outOption.Aliases.Add("--output");
        outOption.Aliases.Add("-o");

        projectCommand.Arguments.Add(pathArg);
        projectCommand.Options.Add(tfmOption);
        projectCommand.Options.Add(frontmatterOption);
        projectCommand.Options.Add(bodyOption);
        projectCommand.Options.Add(outOption);
        opts.AddJsonOptionTo(projectCommand);
        projectCommand.Options.Add(opts.Bare);
        projectCommand.Options.Add(opts.Markdown);
        projectCommand.Options.Add(opts.PlainText);
        opts.AddTableOptionsTo(projectCommand);
        opts.AddOutputOptionsTo(projectCommand);
        opts.AddSectionOptionsTo(projectCommand);
        opts.AddCountOptionTo(projectCommand);
        opts.AddPrintOptionTo(projectCommand);
        opts.AddShapeProjectionOptionsTo(projectCommand);

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
                Print = parseResult.GetValue(opts.Print),
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
                Format = opts.ResolveFormat(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Bare = parseResult.GetValue(opts.Bare),
                Discover = opts.ParseDiscover(parseResult),
                Tree = opts.ParseTree(parseResult),
                Schema = opts.ParseSchema(parseResult),
                Select = opts.ParseSelect(parseResult),
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Count = parseResult.GetValue(opts.Count),
                Rows = opts.ParseRows(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose)
            };

            return await ProjectCommand.ExecuteAsync(options);
        });

        return projectCommand;
    }

    internal static string? GetRemovedOptionError(string option)
    {
        if (option.Equals("--agents-index", StringComparison.Ordinal)
            || option.StartsWith("--agents-index=", StringComparison.Ordinal))
        {
            return "'--agents-index' is no longer supported. Inspect package "
                + "skills with '-S Skills'; package AGENTS.md files are not a "
                + "supported project document surface.";
        }

        if (option.Equals("--readme", StringComparison.Ordinal)
            || option.StartsWith("--readme=", StringComparison.Ordinal))
        {
            return "'--readme' is no longer valid. Select package README rows "
                + "with '-S \"Package README file\"' and add '--print --row N' "
                + "to print one document.";
        }

        return null;
    }

    internal static string? GetRemovedOptionErrorFromParseMessage(
        string message)
    {
        foreach (string option in new[] { "--agents-index", "--readme" })
        {
            if (message.Contains($"'{option}'", StringComparison.Ordinal)
                || message.Contains($"'{option}=", StringComparison.Ordinal))
            {
                return GetRemovedOptionError(option);
            }
        }

        return null;
    }
}
