using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;
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
        var tfmOption = new Option<string?>("--tfm")
        {
            Description = "Select target framework from project.assets.json (e.g., net10.0)"
        };
        var frontmatterOption = new Option<bool>("--frontmatter")
        {
            Description = "With --print or --raw, print only the leading YAML frontmatter block"
        };
        frontmatterOption.Aliases.Add("--yaml-header");
        var bodyOption = new Option<bool>("--body")
        {
            Description = "With --print or --raw, print only content after YAML frontmatter"
        };
        var outOption = SharedOptions.CreateOutputPathOption();

        projectCommand.Arguments.Add(pathArg);
        projectCommand.Options.Add(tfmOption);
        projectCommand.Options.Add(frontmatterOption);
        projectCommand.Options.Add(bodyOption);
        projectCommand.Options.Add(outOption);
        SharedOptions.AddOutputPathValidator(projectCommand, outOption);
        opts.AddJsonOptionTo(projectCommand);
        projectCommand.Options.Add(opts.Raw);
        projectCommand.Options.Add(opts.Markdown);
        projectCommand.Options.Add(opts.PlainText);
        opts.AddTableOptionsTo(projectCommand);
        opts.AddOutputOptionsTo(
            projectCommand,
            validateLegacyRowWindow: result =>
                !IsProjectDocumentRowSelection(result, opts));
        opts.AddSectionOptionsTo(projectCommand);
        opts.AddCountOptionTo(projectCommand);
        opts.AddPrintOptionTo(projectCommand);
        opts.AddShapeProjectionOptionsTo(projectCommand);

        projectCommand.SetAction(async (parseResult, ct) =>
        {
            bool selectsProjectDocumentRows =
                IsProjectDocumentRowSelection(
                    parseResult.CommandResult,
                    opts);
            RowSelectionIntent<string>? rowSelection = null;
            if (selectsProjectDocumentRows
                && !CliRowSelectionCommandRegistry
                    .TryGetPreparedSemanticIntent(
                        parseResult,
                        "Project document",
                        out rowSelection,
                        out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

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
                Raw = parseResult.GetValue(opts.Raw),
                Discover = opts.ParseDiscover(parseResult),
                Tree = opts.ParseTree(parseResult),
                Schema = opts.ParseSchema(parseResult),
                Select = opts.ParseSelect(parseResult),
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Count = parseResult.GetValue(opts.Count),
                Rows = selectsProjectDocumentRows
                    ? null
                    : opts.ParseRows(parseResult),
                RowSelection = rowSelection,
                Verbose = parseResult.GetValue(opts.Verbose)
            };

            return await ProjectCommand.ExecuteAsync(options);
        });

        CliRowSelectionCommandRegistry.Register(
            projectCommand,
            new(
                opts.Limit,
                opts.Rows,
                top: null,
                orderBy: null,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            result => IsProjectDocumentRowSelection(result, opts),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return projectCommand;
    }

    internal static bool IsProjectDocumentRowSelection(
        ParseResult result,
        SharedOptions opts)
        => IsProjectDocumentRowSelection(result.CommandResult, opts);

    internal static bool IsProjectDocumentRowSelection(
        CommandResult result,
        SharedOptions opts)
    {
        if (result.GetResult(opts.Discover) is { Implicit: false })
            return false;

        string? selectValue = opts.SelectText(result);
        bool selectDefault =
            result.GetResult(opts.Select) is { Implicit: false }
            && string.IsNullOrWhiteSpace(selectValue);
        string[]? selectors = string.IsNullOrWhiteSpace(selectValue)
            ? null
            : selectValue.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);
        SectionCatalog<ProjectDiscoveryModel> catalog =
            ProjectSections.Catalog;
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            selectors,
            catalog.SelectableSectionNames,
            catalog.InfoSectionNames,
            catalog.SelectionCategoryMap,
            selectDefault);
        return !selection.HasError
            && selection.Sections is { Count: 1 };
    }
}
