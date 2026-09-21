using DotnetInspect.Cli.Models;
using ILInspector.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using SemanticRowSelection =
    DotnetInspect.Cli.CommandLine.CliSemanticRowSelection;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using InertText;
using Inspector.Findings;
using Markout;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{

    /// <summary>
    /// The printable sections are the document members of the package file family: each lists
    /// documents the package ships, so each row declares a payload <c>--print</c> can project.
    /// The whole-package listing is deliberately excluded — it lists assemblies and images too,
    /// and printability is a row capability rather than something a listing shape implies.
    /// </summary>
    private static bool ValidatePackagePrintSelection(HashSet<string>? sections)
    {
        if (sections is { Count: 1 }
            && PackageFileFamily.IsFamilySection(sections.Single()))
            return true;

        CommandError.Write("--print requires -S/--select to match exactly one printable section.");
        return false;
    }

    private static int WritePackageShapeProjection(InspectionResult result, InspectionOptions options)
    {
        var kind = options.Roots
            ? ShapeProjectionKind.Roots
            : ShapeProjectionOutput.GetKind(
                options.Value,
                options.Urls,
                options.Paths);
        var section = options.IncludeSections!.Single();
        var rows = section switch
        {
            PackageSections.PackageInfo => ProjectPackageInfo(result, section, kind, options),
            PackageSections.Files when options.Roots =>
                ProjectPackageFileRoots(result.Files, section),
            PackageSections.Files => ProjectPackageFiles(new InspectionResultView(result).Files, section, kind, options),
            PackageSections.FilesNuspec => ProjectPackageFiles(new InspectionResultView(result).NuspecFiles, section, kind, options),
            PackageSections.FilesReadme => ProjectPackageFiles(new InspectionResultView(result).PackageReadme, section, kind, options),
            PackageSections.FilesLicenses => ProjectPackageFiles(new InspectionResultView(result).LicenseFiles, section, kind, options),
            PackageSections.FilesSkills => ProjectPackageFiles(new InspectionResultView(result).SkillFiles, section, kind, options),
            PackageSections.SourceLinkFiles => ProjectPackageSourceFiles(result, section, kind, options),
            _ => []
        };

        if (rows.Count == 0
            && !PackageFileFamily.IsFamilySection(section)
            && section is not (PackageSections.PackageInfo or PackageSections.Files
                or PackageSections.FilesReadme or PackageSections.SourceLinkFiles))
        {
            CommandError.Write($"section '{section}' does not expose {kind.ToString().ToLowerInvariant()} values.");
            return 1;
        }

        return ShapeProjectionOutput.Write(rows,
            new ShapeProjectionOptions(
                kind,
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                new ProjectionDestination(options.OutputPath, options.Rows)));
    }

    private static List<ShapeProjectionRow> ProjectPackageFileRoots(
        IEnumerable<PackageFile>? files,
        string section)
        => PackageFileLister.ProjectRoots(files ?? [])
            .Select((root, index) =>
                new ShapeProjectionRow(
                    index + 1,
                    section,
                    root,
                    Path: root))
            .ToList();

    /// <summary>
    /// Projects the printable payload of the selected section's rows. Document sections list
    /// the files they describe as rows, so the documents printed here are the rows the section
    /// renders rather than a document re-derived from the command line. Cardinality, the
    /// <c>--row</c> selector, and output shape belong to <see cref="PrintProjectionOutput"/>,
    /// so a new printable section declares its rows and needs no printer of its own.
    /// </summary>
    private static int WritePackagePrintProjection(InspectionResult result, string extractPath, InspectionOptions options)
    {
        var section = options.IncludeSections!.Single();
        var predicate = PackageFileFamily.PredicateFor(section)
            ?? throw new InvalidOperationException($"'{section}' is not a printable section.");
        List<PackageFile>? sourceRows = result.PackageFiles?
            .Where(predicate)
            .ToList();
        List<PackageFileText>? rows = new PackageInspectionText(result)
            .SelectPackageFiles(predicate);

        // A family with no rows and a file listing that was never collected are different facts.
        // Reporting the second as the first would tell the caller this package ships no such
        // document when the truth is that nothing ever looked.
        if (rows is null)
        {
            CommandError.Write(
                $"the package file listing was not collected, so '{section}' cannot be printed.");
            return 1;
        }
        if (sourceRows is null || sourceRows.Count != rows.Count)
        {
            throw new InvalidOperationException(
                $"The raw and presentation rows for '{section}' do not agree.");
        }

        // The shared writer refuses an empty payload, but it has no row to name the section from,
        // so it can only say "selected section". This package does ship documents of other kinds,
        // and naming the empty section is what tells the caller which one is missing.
        if (rows.Count == 0)
        {
            CommandError.Write($"this package contains no '{section}' document to print.");
            return 1;
        }

        // A Markdown scope names a Markdown construct. The caller named this section explicitly,
        // so silently returning the whole document -- or an empty one -- would answer a question
        // they did not ask. Report that the scope does not apply to this document instead.
        var isReadmeSection = section.Equals(PackageSections.FilesReadme, StringComparison.OrdinalIgnoreCase);
        int nonMarkdownIndex = options.ContentScope == PackageFileContentScope.Full
            ? -1
            : sourceRows.FindIndex(row => !IsMarkdownDocument(row.Path, isReadmeSection));
        if (nonMarkdownIndex >= 0)
        {
            CommandError.Write(
                $"--frontmatter/--yaml-header and --body apply to Markdown documents; " +
                $"'{rows[nonMarkdownIndex].Path}' is not Markdown.");
            return 1;
        }

        // Row identity is metadata, so the selection is resolved before any document is read and
        // the payload of exactly one row is acquired -- one --print authorizes one fetch.
        var printableRows = new List<PrintableRow>(rows.Count);
        var sourceByRow = new Dictionary<PrintableRow, PackageFile>(
            ReferenceEqualityComparer.Instance);
        for (var i = 0; i < rows.Count; i++)
        {
            string path = rows[i].Path.ToString();
            var row = new PrintableRow(i + 1, section, path, path, null);
            printableRows.Add(row);
            sourceByRow.Add(row, sourceRows[i]);
        }

        return PrintProjectionOutput.Write(
            printableRows,
            row =>
            {
                PackageFileContent content = ReadPackageFileContent(
                    extractPath,
                    result.PackageName ?? string.Empty,
                    result.Version ?? string.Empty,
                    sourceByRow[row],
                    options.ContentScope,
                    normalizeGithubLinksToRaw: !options.PreferRenderedUrls,
                    includeExactContent: HasUnstructuredOutputPath(options)
                        && options.ContentScope == PackageFileContentScope.Full);
                return content.SelectedContent is { } selected
                    ? PrintableContent.FromContainmentSelection(selected)
                    : new PrintableContent(content.Content, content.ExactContent);
            },
            new PrintProjectionOptions(
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                options.Bare,
                PackagePayloadDestination(options),
                row => PackagePayloadDestination(
                    options,
                    PackageFileFamily.IsSkillDocument(sourceByRow[row]))));
    }

    private static List<ShapeProjectionRow> ProjectPackageFiles(IEnumerable<PackageFileRow>? files, string section, ShapeProjectionKind kind, InspectionOptions options)
    {
        List<ShapeProjectionRow> rows = [];
        var list = files?.ToList() ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            var file = list[i];
            string? value = kind switch
            {
                ShapeProjectionKind.Paths => file.Path,
                ShapeProjectionKind.Value => SelectPackageFileValue(file, options),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(value))
                continue;
            rows.Add(new ShapeProjectionRow(i + 1, section, value, Path: file.Path));
        }
        return rows;
    }

    private static string? SelectPackageFileValue(PackageFileRow file, InspectionOptions options)
    {
        var column = options.Columns?.SingleOrDefault() ?? options.Fields?.SingleOrDefault();
        return column?.ToLowerInvariant() switch
        {
            "path" => file.Path,
            "size" => file.Size.ToString(CultureInfo.InvariantCulture),
            _ => file.Path
        };
    }

    private static List<ShapeProjectionRow> ProjectPackageSourceFiles(InspectionResult result, string section, ShapeProjectionKind kind, InspectionOptions options)
    {
        var sourceRows = new InspectionResultView(result).SourceFiles ?? [];
        return sourceRows
            .Select((row, index) =>
            {
                string? value = kind switch
                {
                    ShapeProjectionKind.Urls => row.Url,
                    ShapeProjectionKind.Value => SelectPackageSourceValue(row, options),
                    _ => null
                };
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : new ShapeProjectionRow(index + 1, section, value, Label: row.Type, Url: row.Url);
            })
            .Where(row => row is not null)
            .Cast<ShapeProjectionRow>()
            .ToList();
    }

    private static string? SelectPackageSourceValue(PackageSourceFileRow row, InspectionOptions options)
    {
        var column = options.Columns?.SingleOrDefault() ?? options.Fields?.SingleOrDefault();
        return column?.ToLowerInvariant() switch
        {
            "library" => row.Library,
            "type" => row.Type,
            "url" => row.Url,
            _ => row.Url
        };
    }

    private static List<ShapeProjectionRow> ProjectPackageInfo(InspectionResult result, string section, ShapeProjectionKind kind, InspectionOptions options)
    {
        if (kind != ShapeProjectionKind.Value)
            return [];

        var selector = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(selector))
        {
            CommandError.Write("--value for Package Info requires --fields <name>.");
            return [];
        }

        if (ResolvePackageInfoFields([selector]) is not [var field])
            return [];

        string? value =
            new InspectionResultView(result).ResolvePackageInfoField(field);
        return string.IsNullOrWhiteSpace(value)
            ? []
            : [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static bool ValidatePathMatchMode(InspectionOptions options)
    {
        if (options.PathMatchMode.Equals("all", StringComparison.OrdinalIgnoreCase)
            || options.PathMatchMode.Equals("first", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        CommandError.Write($"--match must be 'all' or 'first', not '{options.PathMatchMode}'.");
        return false;
    }

    private static bool TryCreatePackageTarget(string packageArg, out PackageReferenceTarget target)
    {
        target = null!;
        var parsed = PackageExtractor.ParsePackageTarget(packageArg);
        if (parsed.IsLocalFile)
        {
            if (!File.Exists(packageArg))
            {
                CommandError.Write($"File not found: {packageArg}");
                return false;
            }

            target = parsed;
            return true;
        }

        if (!PackageExtractor.IsValidPackageReferenceVersion(parsed.Version))
        {
            CommandError.Write($"'{parsed.Version}' is not a valid package version.");
            CommandError.WriteLine("Versions look like: 1.0.0, 8.0.5, 13.0.3-beta1, 11.0.0-preview*");
            CommandError.WriteLine("Use id@version for per-package version pins.");
            return false;
        }

        target = parsed;
        return true;
    }
}
