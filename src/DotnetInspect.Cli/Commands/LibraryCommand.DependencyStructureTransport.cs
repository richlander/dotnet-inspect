using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;

using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

using ILInspector.Research;

namespace DotnetInspect.Cli.Commands;

public partial class LibraryCommand
{
    private static readonly InspectionEnvelopeJsonContract<
        LibraryDependencyStructureDocument> s_dependencyStructureJson =
        new(
            "library-dependency-structure",
            1,
            LibraryDependencyStructureInspectionJson.Write);

    internal static bool IsExactDependencyStructureSelection(
        LibraryOptions options)
    {
        if (options.SelectDefault)
            return false;

        if (options.IncludeSections is { } included)
        {
            return included.Count == 1
                && included.Contains(SectionNames.DependencyStructure)
                && options.ExactIncludeSections is { Count: 1 } exact
                && exact.Contains(SectionNames.DependencyStructure);
        }

        string[] selectors =
        [
            .. (options.Select ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        return selectors is [var selector]
            && selector.Equals(
                SectionNames.DependencyStructure,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool RequestsDependencyStructureTransport(
        LibraryOptions options) =>
        IsExactDependencyStructureSelection(options)
        && (options.Count
            || options.EnvelopeOutput
            || options.JsonOutput);

    internal static bool ValidateDependencyStructureTransport(
        LibraryOptions options)
    {
        if (!IsExactDependencyStructureSelection(options)
            || (!options.EnvelopeOutput && !options.JsonOutput))
        {
            return true;
        }

        if (options.Count
            || options.Rows is not null
            || options.DependencyStructureRowSelection is not null
            || options.Fields is { Length: > 0 }
            || options.Columns is { Length: > 0 }
            || options.Print
            || options.PrintRow is not null
            || options.ProjectionRow is not null
            || options.Value
            || options.Urls
            || options.Paths
            || options.JsonArray
            || options.Tree
            || options.NoHeader
            || options.Discover is not null
            || options.Schema
            || options.Effective
            || options.ExtractResources is not null
            || options.IncludeReferences
            || options.ReferenceHierarchyDepth is not null)
        {
            CommandError.Write(
                "Complete Dependency Structure JSON does not support row, "
                    + "field, column, count, discovery, or presentation "
                    + "projections.");
            return false;
        }

        if (options.EnvelopeOutput
            && options.FormatFlagExplicitlySet)
        {
            CommandError.Write(
                "Dependency Structure --envelope cannot be combined with "
                    + "another output format.");
            return false;
        }

        if (string.Equals(
                options.Tfm,
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            CommandError.Write(
                "Complete Dependency Structure JSON requires one target "
                    + "framework; --tfm all is not supported.");
            return false;
        }

        return true;
    }

    private static int WriteDependencyStructureTransport(
        LibraryInspection inspection,
        LibraryOptions options)
    {
        switch (inspection.DependencyStructureQueryResult)
        {
            case LibraryDependencyStructureQueryResult.Available available
                when options.Count:
                if (available.Count is not { } count)
                {
                    CommandError.Write(
                        "Dependency Structure produced no Count result.");
                    return 1;
                }
                CountOutput.WriteCount(count, options.OutputPath);
                return 0;

            case LibraryDependencyStructureQueryResult.Available available:
                InspectionEnvelope<LibraryDependencyStructureDocument>
                    envelope =
                        LibraryDependencyStructureInspection.Envelope(
                            available);
                return InspectionEnvelopeOutput.TryWrite(
                    envelope,
                    s_dependencyStructureJson,
                    options.EnvelopeOutput,
                    options.CompactJson,
                    options.OutputPath)
                        ? 0
                        : 1;

            case LibraryDependencyStructureQueryResult.Unavailable unavailable:
                CommandError.Write(
                    "Dependency Structure is unavailable: "
                        + unavailable.Outcome.Message);
                return 1;

            case LibraryDependencyStructureQueryResult.SelectionFailed failed:
                CommandError.Write(
                    "Dependency Structure selection failed: "
                        + failed.Detail);
                return 1;

            case LibraryDependencyStructureQueryResult.Failed failed:
                CommandError.Write(failed.Error);
                return 1;

            case null:
                CommandError.Write(
                    "Dependency Structure produced no terminal result.");
                return 1;

            default:
                throw new InvalidOperationException(
                    "Unknown Dependency Structure result.");
        }
    }

    private static bool RejectUnavailableDependencyStructure(
        LibraryInspection inspection,
        LibraryOptions options)
    {
        if (!IsExactDependencyStructureSelection(options)
            || inspection.DependencyStructureQueryResult
                is LibraryDependencyStructureQueryResult.Available)
        {
            return false;
        }

        _ = WriteDependencyStructureTransport(inspection, options);
        return true;
    }
}
