using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Commands;

public partial class DependsCommand
{
    private static readonly InspectionEnvelopeJsonContract<ProjectInputDocument>
        ProjectInputJson = new(
            "project-input", 1, ProjectInputJsonContext.ContentTypeInfo);

    internal static async Task<int> ExecuteProjectDocumentAsync(
        string path,
        DependsOptions options,
        CancellationToken cancellationToken)
    {
        SectionCatalog<ProjectInputDocument> catalog = ProjectInputSections.Catalog;
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Select,
            catalog.SelectableSectionNames,
            catalog.InfoSectionNames,
            catalog.SelectionCategoryMap,
            options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selection))
            return 1;

        var schema = new DocumentSchema()
            .Add(ProjectInputSections.Dependencies, "column",
                ["Package", "Constraint", "Condition"])
            .Add(ProjectInputSections.RestoredPackages, "column",
                ["Package", "Version", "Role"])
            .Add(ProjectInputSections.Failures, "column",
                ["Phase", "Reason", "Message"]);
        if (options.Discover is { } discover)
        {
            return DiscoverOutput.Execute(
                discover,
                schema,
                DiscoveryOutputRequest.Create(
                    options.Format, false, options.Format == OutputFormat.Table,
                    options.NoHeader, (int)options.Verbosity, options),
                sectionCostAnnotations: catalog.Pipeline.GetCostAnnotations(),
                sectionCategories: catalog.SelectionCategoryMap);
        }
        if (options.Schema)
        {
            CommandError.Write("--schema requires -D/--discover.");
            return 1;
        }
        if (options.CompactJson
            && options.Format != OutputFormat.Json
            && !options.EnvelopeOutput)
        {
            CommandError.Write("--compact requires --json or --envelope.");
            return 1;
        }

        string declaredName = Path.GetFileName(path);
        ProjectInputKind kind;
        if (declaredName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            kind = ProjectInputKind.AuthoredProject;
        else if (declaredName.Equals("project.assets.json", StringComparison.OrdinalIgnoreCase))
            kind = ProjectInputKind.RestoredProject;
        else
        {
            CommandError.Write("--project-document requires a .csproj or project.assets.json file.");
            return 1;
        }
        if (kind == ProjectInputKind.AuthoredProject && options.Tfm is not null)
        {
            CommandError.Write("--tfm selects restored assets, not authored Project syntax.");
            return 1;
        }

        InspectionEnvelope<ProjectInputDocument> inspection;
        try
        {
            await using FileStream stream = File.OpenRead(path);
            int maximum = kind == ProjectInputKind.AuthoredProject
                ? AuthoredProjectDependencyFactsQuery.MaxProjectBytes
                : RestoredProjectDependencyFactsQuery.MaxAssetsBytes;
            byte[] buffer = new byte[(int)Math.Min(stream.Length, maximum + 1L)];
            int length = await stream.ReadAtLeastAsync(
                buffer, buffer.Length, throwOnEndOfStream: false,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            inspection = ProjectInputInspection.Execute(
                kind,
                declaredName,
                buffer.AsMemory(0, length),
                options.Tfm is { } framework
                    ? new RestoredProjectTargetRequest(framework)
                    : null);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            CommandError.Write(exception);
            return 1;
        }
        cancellationToken.ThrowIfCancellationRequested();

        ProjectInputDocument content = inspection.Content;
        int exitCode = ProjectDocumentIsIncomplete(content) ? 1 : 0;
        if (options.EnvelopeOutput
            || options.Format == OutputFormat.Json
                && options.Select is null && !options.SelectDefault)
        {
            if (!InspectionEnvelopeOutput.TryWrite(
                    inspection, ProjectInputJson,
                    options.EnvelopeOutput, options.CompactJson))
            {
                return 1;
            }
        }
        else
        {
            HashSet<string> candidates = catalog.Pipeline.GetCandidateSections(
                options.Verbosity, selection.Sections, options.SelectDefault);
            string[] sections =
            [
                .. catalog.Pipeline.AllSectionNames.Where(candidates.Contains),
            ];
            OutputFormat format = !options.OutputFormatExplicitlySet
                && OutputFormatResolver.GetEnvironmentOverride() is null
                && sections.Length == 1
                    ? OutputFormat.Table
                    : options.Format;
            if (format is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl
                && sections.Length != 1)
            {
                CommandError.Write(
                    "Table, TSV, and JSONL require exactly one Project input section; use -S.");
                return 1;
            }
            if (format is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl)
            {
                OutputFormatter.WriteProjectedTable(
                    Console.Out, !options.NoHeader,
                    format == OutputFormat.Tsv, format == OutputFormat.Jsonl,
                    columns: null, fields: null,
                    (writer, formatter, writerOptions) =>
                    {
                        var markout = new MarkoutWriter(writer, formatter, writerOptions);
                        WriteProjectInputTable(markout, content, sections[0]);
                        markout.Flush();
                    },
                    maxRows: null);
            }
            else if (format == OutputFormat.Json)
            {
                OutputFormatter.WriteProjectedJson(
                    Console.Out, columns: null, fields: null,
                    (writer, formatter, writerOptions) =>
                    {
                        var markout = new MarkoutWriter(writer, formatter, writerOptions);
                        WriteProjectInputDocument(markout, content, sections);
                        markout.Flush();
                    },
                    maxRows: null);
            }
            else if (sections.Length > 0)
            {
                var writer = new MarkoutWriter(
                    Console.Out,
                    format == OutputFormat.PlainText
                        ? new PlainTextFormatter()
                        : new MarkdownFormatter());
                WriteProjectInputDocument(writer, content, sections);
                writer.Flush();
            }
        }

        if (exitCode != 0)
        {
            CommandError.Write(
                "Project input inspection is incomplete, unavailable, or failed; "
                + "retained evidence and failures remain in --json or --envelope output.");
            foreach (DependencyEvidenceFailureRow failure in content.Evidence.Failures)
                CommandError.Write(failure.Message.ToString());
        }
        return exitCode;
    }

    private static bool ProjectDocumentIsIncomplete(ProjectInputDocument content) =>
        content.AuthoredProject is AuthoredProjectDependencyFactsResult.Incomplete
            or AuthoredProjectDependencyFactsResult.Failed
        || content.RestoredTraversal is RestoredProjectDependencyTraversalResult.Unavailable
            or RestoredProjectDependencyTraversalResult.Failed
        || content.RestoredTraversal is RestoredProjectDependencyTraversalResult.Available
        {
            Value.Completion: not RestoredProjectTraversalCompletion.Complete,
        }
        || content.Evidence.Summary.Phases.Processing.Incomplete > 0
        || content.Evidence.Summary.Phases.Processing.Failed > 0
        || !content.Evidence.Failures.IsEmpty
        || content.Evidence.Summary.IsTruncated;

    private static void WriteProjectInputDocument(
        MarkoutWriter writer,
        ProjectInputDocument content,
        string[] sections)
    {
        writer.WriteHeading(1, "Project input");
        writer.WriteFields(
        [
            new("Name", content.DeclaredName.ToString()),
            new("Basis", content.Kind.ToString()),
            new("Root set", content.Evidence.Summary.RootSetCompletion.ToString()),
            new("Outcome", ProjectDocumentIsIncomplete(content)
                ? "Incomplete, unavailable, or failed" : "Available"),
            new("Share", "Local input is not projectable"),
        ]);
        if (content.Kind == ProjectInputKind.AuthoredProject)
            writer.WriteParagraph("Authored syntax only; constraints are not resolved versions. No MSBuild evaluation or restore.");
        foreach (string section in sections)
        {
            writer.WriteHeading(2, section);
            WriteProjectInputTable(writer, content, section);
        }
    }

    private static void WriteProjectInputTable(
        MarkoutWriter writer,
        ProjectInputDocument content,
        string section)
    {
        switch (section)
        {
            case ProjectInputSections.Dependencies:
                AuthoredProjectDependencyFacts? authored = content.AuthoredProject switch
                {
                    AuthoredProjectDependencyFactsResult.Available available => available.Value,
                    AuthoredProjectDependencyFactsResult.Incomplete incomplete => incomplete.Value,
                    _ => null,
                };
                string[][] declarations = authored is not null
                    ? [.. authored.PackageDeclarations.Select(row => new[]
                    {
                        row.SourcePackageIdSpelling?.ToString()
                            ?? row.CanonicalPackageId ?? "(unresolved)",
                        row.SourceVersionConstraintSpelling?.ToString()
                            ?? row.CanonicalVersionConstraint ?? "(unresolved)",
                        row.Condition switch
                        {
                            AuthoredProjectDependencyCondition.Unconditional => "Unconditional",
                            AuthoredProjectDependencyCondition.TargetFramework target =>
                                target.SourceSpelling.ToString(),
                            AuthoredProjectDependencyCondition.Unresolved unresolved =>
                                string.Join("; ", unresolved.SourceSpellings),
                            _ => throw new InvalidOperationException("Unknown authored condition."),
                        },
                    })]
                    : [.. content.Evidence.Dependencies.Select(row => new[]
                    {
                        row.SourcePackageIdSpelling.ToString(),
                        row.SourceVersionConstraintSpelling.ToString(),
                        row.FrameworkSpelling.ToString(),
                    })];
                writer.WriteTable(
                    ["Package", "Constraint", "Condition"],
                    ["package", "constraint", "condition"], declarations);
                break;
            case ProjectInputSections.RestoredPackages:
                writer.WriteTable(
                    ["Package", "Version", "Role"],
                    ["package", "version", "role"],
                    [.. content.Evidence.RestoredPackages.Select(row => new[]
                    {
                        row.PackageId, row.PackageVersion, row.Role.ToString(),
                    })]);
                break;
            case ProjectInputSections.Failures:
                writer.WriteTable(
                    ["Phase", "Reason", "Message"],
                    ["phase", "reason", "message"],
                    [.. content.Evidence.Failures.Select(row => new[]
                    {
                        row.Phase.ToString(), row.Reason, row.Message.ToString(),
                    })]);
                break;
        }
    }
}
