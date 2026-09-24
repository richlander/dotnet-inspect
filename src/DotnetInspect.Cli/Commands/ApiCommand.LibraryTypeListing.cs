using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Sections;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Commands;

public static partial class ApiCommand
{
    internal static int WriteLibraryTypeListingOutput(
        LibraryTypeListingResult result,
        TypeOptions options)
    {
        if (options.Count)
            return WriteLibraryTypeCount(result.Document, options);

        CliApiSurface view =
            ApiOutputFormatter.BuildLibraryTypeView(
                result.Document,
                result.Rows);
        HashSet<string> includeSections =
            ApiTypeSectionDescriptors.CreatePipeline()
                .GetCandidateSections(
                    options.UserVerbosity,
                    options.IncludeSections);
        var writerOptions =
            new MarkoutWriterOptions
            {
                IncludeSections = includeSections,
                IncludeDescription =
                    options.Verbosity != Verbosity.Quiet,
            };
        DocumentSchema schema =
            ApiViewContext.Default
                .GetSchemaInfo<CliApiSurface>()!
                .ToDocumentSchema();
        RenderedSectionManifest manifest =
            RenderManifestFormatter.Capture(
                view,
                ApiViewContext.Default,
                writerOptions,
                schema);
        if (!DiagnoseProjection(
                manifest,
                options,
                schema,
                options.IncludeSections))
        {
            return 1;
        }

        var output = new StringWriter { NewLine = "\n" };
        MarkoutSerializer.Serialize(
            view,
            output,
            new MarkdownFormatter(),
            ApiViewContext.Default,
            writerOptions);
        OutputFormatter.WriteLfLine(
            Console.Out,
            output.ToString().TrimEnd());
        return 0;
    }

    private static int WriteLibraryTypeCount(
        LibraryDocument document,
        TypeOptions options)
    {
        LibraryTypePopulationCountOutcome.Counted count =
            document.Types.Count
                as LibraryTypePopulationCountOutcome.Counted
            ?? throw new InvalidOperationException(
                "Library Type Count output requires a counted outcome.");

        if (options.CountDefaultPopulation
            || options.IncludeSections is not { Count: > 0 })
        {
            CountOutput.WriteCount(count.Total);
            return 0;
        }

        var projection = new CountProjection();
        foreach (string section in options.IncludeSections)
        {
            projection.SetRows(
                section,
                section switch
                {
                    SectionNames.Classes => count.Classes,
                    SectionNames.Structs => count.Structs,
                    SectionNames.Interfaces => count.Interfaces,
                    SectionNames.Enums => count.Enums,
                    SectionNames.Delegates => count.Delegates,
                    SectionNames.TypeForwarders => count.Forwarders,
                    _ => throw new InvalidOperationException(
                        "Unknown Library Type inventory section."),
                });
        }

        IReadOnlyList<string>? ordered =
            OutputFormatter.ResolveCountMapSections(
                ApiTypeSectionDescriptors.CreatePipeline(),
                options.IncludeSections,
                fixedOverview: false);
        CountOutput.Write(
            projection,
            ordered,
            options.Format,
            options.NoHeader);
        return 0;
    }
}
