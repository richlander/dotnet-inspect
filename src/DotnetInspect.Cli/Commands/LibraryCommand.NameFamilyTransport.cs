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
        LibraryNameFamilyDocument> s_nameFamilyJson =
        new(
            "library-name-families",
            1,
            LibraryNameFamilyInspectionJson.Write);

    private static readonly InspectionEnvelopeJsonContract<
        LibraryFamilyRoleCompositionDocument> s_familyRoleJson =
        new(
            "library-family-roles",
            1,
            LibraryFamilyRoleInspectionJson.Write);

    internal static bool IsExactNameFamilySelection(
        LibraryOptions options)
    {
        if (options.SelectDefault)
            return false;

        if (options.IncludeSections is { } included)
        {
            return included.Count == 1
                && included.Contains(SectionNames.NameFamilies)
                && options.ExactIncludeSections is { Count: 1 } exact
                && exact.Contains(SectionNames.NameFamilies);
        }

        string[] selectors =
        [
            .. (options.Select ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        return selectors is [var selector]
            && selector.Equals(
                SectionNames.NameFamilies,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool RequestsNameFamilyTransport(
        LibraryOptions options) =>
        IsExactNameFamilySelection(options)
        && (options.Count
            || options.EnvelopeOutput
            || options.JsonOutput);

    internal static bool IsExactNameFamilyRoleSelection(
        LibraryOptions options) =>
        IsExactSectionSelection(
            options,
            SectionNames.NameFamilyRoles)
        || IsExactSectionSelection(
            options,
            SectionNames.NameFamilyRoleTypes);

    internal static bool RequestsNameFamilyRoleTransport(
        LibraryOptions options) =>
        IsExactNameFamilyRoleSelection(options)
        && (options.Count
            || options.EnvelopeOutput
            || options.JsonOutput);

    internal static bool ValidateNameFamilyTransport(
        LibraryOptions options)
    {
        bool exactNameFamilies =
            IsExactNameFamilySelection(options);
        bool exactFamilyRoles =
            IsExactNameFamilyRoleSelection(options);
        if ((!exactNameFamilies && !exactFamilyRoles)
            || (!options.EnvelopeOutput && !options.JsonOutput))
        {
            return true;
        }

        if (options.Count
            || options.Rows is not null
            || options.NameFamilyRowSelection is not null
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
                "Complete name-family JSON does not support row, field, "
                    + "column, count, discovery, or presentation "
                    + "projections.");
            return false;
        }

        if (options.EnvelopeOutput
            && options.FormatFlagExplicitlySet)
        {
            CommandError.Write(
                "Name-family --envelope cannot be combined with another "
                    + "output format.");
            return false;
        }

        if (string.Equals(
                options.Tfm,
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            CommandError.Write(
                "Complete name-family JSON requires one target framework; "
                    + "--tfm all is not supported.");
            return false;
        }

        return true;
    }

    private static bool IsExactSectionSelection(
        LibraryOptions options,
        string section)
    {
        if (options.SelectDefault)
            return false;

        if (options.IncludeSections is { } included)
        {
            return included.Count == 1
                && included.Contains(section)
                && options.ExactIncludeSections is { Count: 1 } exact
                && exact.Contains(section);
        }

        string[] selectors =
        [
            .. (options.Select ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        return selectors is [var selector]
            && selector.Equals(
                section,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExactSectionSelection(
        IReadOnlySet<string>? sections,
        string section) =>
        sections is { Count: 1 }
        && sections.Contains(section);

    private static int WriteNameFamilyTransport(
        LibraryInspection inspection,
        LibraryOptions options)
    {
        switch (inspection.NameFamilyQueryResult)
        {
            case LibraryNameFamilyQueryResult.Available available
                when options.Count:
                if (available.Count is not { } count)
                {
                    CommandError.Write(
                        "Name Families produced no Count result.");
                    return 1;
                }
                CountOutput.WriteCount(
                    count,
                    options.OutputPath);
                return 0;

            case LibraryNameFamilyQueryResult.Available available:
                InspectionEnvelope<LibraryNameFamilyDocument> envelope =
                    LibraryNameFamilyInspection.Envelope(available);
                return InspectionEnvelopeOutput.TryWrite(
                    envelope,
                    s_nameFamilyJson,
                    options.EnvelopeOutput,
                    options.CompactJson,
                    options.OutputPath)
                        ? 0
                        : 1;

            case LibraryNameFamilyQueryResult.Unavailable unavailable:
                CommandError.Write(
                    "Name Families is unavailable: "
                        + unavailable.Detail);
                return 1;

            case LibraryNameFamilyQueryResult.Rejected rejected:
                CommandError.Write(
                    "Name Families was rejected: "
                        + rejected.Outcome.Detail);
                return 1;

            case LibraryNameFamilyQueryResult.SelectionFailed failed:
                CommandError.Write(
                    "Name Families selection failed: "
                        + failed.Detail);
                return 1;

            case LibraryNameFamilyQueryResult.Failed failed:
                CommandError.Write(failed.Error);
                return 1;

            case null:
                CommandError.Write(
                    "Name Families produced no terminal result.");
                return 1;

            default:
                throw new InvalidOperationException(
                    "Unknown Name Families result.");
        }
    }

    private static bool RejectUnavailableNameFamilies(
        LibraryInspection inspection,
        LibraryOptions options)
    {
        if (!IsExactNameFamilySelection(options)
            || inspection.NameFamilyQueryResult
                is LibraryNameFamilyQueryResult.Available)
        {
            return false;
        }

        _ = WriteNameFamilyTransport(inspection, options);
        return true;
    }

    private static int WriteNameFamilyRoleTransport(
        LibraryInspection inspection,
        LibraryOptions options)
    {
        switch (inspection.FamilyRoleQueryResult)
        {
            case LibraryFamilyRoleQueryResult.Available available
                when options.Count:
                if (available.Count is not { } count)
                {
                    CommandError.Write(
                        "Name Family Roles produced no Count result.");
                    return 1;
                }
                CountOutput.WriteCount(
                    count,
                    options.OutputPath);
                return 0;

            case LibraryFamilyRoleQueryResult.Available available:
                InspectionEnvelope<
                    LibraryFamilyRoleCompositionDocument> envelope =
                        LibraryFamilyRoleInspection.Envelope(available);
                return InspectionEnvelopeOutput.TryWrite(
                    envelope,
                    s_familyRoleJson,
                    options.EnvelopeOutput,
                    options.CompactJson,
                    options.OutputPath)
                        ? 0
                        : 1;

            case LibraryFamilyRoleQueryResult.NameFamiliesUnavailable
                unavailable:
                CommandError.Write(
                    "Name Family Roles is unavailable: "
                        + unavailable.Outcome.Detail);
                return 1;

            case LibraryFamilyRoleQueryResult.NameFamiliesRejected rejected:
                CommandError.Write(
                    "Name Family Roles name-family input was rejected: "
                        + rejected.Outcome.Detail);
                return 1;

            case LibraryFamilyRoleQueryResult.StructuralRejected rejected:
                CommandError.Write(
                    "Name Family Roles structural input was rejected: "
                        + rejected.Outcome.Detail);
                return 1;

            case LibraryFamilyRoleQueryResult.CompositionRejected rejected:
                CommandError.Write(
                    "Name Family Roles composition was rejected: "
                        + rejected.Outcome.Detail);
                return 1;

            case LibraryFamilyRoleQueryResult.PopulationUnavailable missing:
                CommandError.Write(
                    "Name Family Roles population is unavailable: "
                        + missing.RequestedPopulation);
                return 1;

            case LibraryFamilyRoleQueryResult.SelectionFailed failed:
                CommandError.Write(
                    "Name Family Roles selection failed: "
                        + failed.Detail);
                return 1;

            case LibraryFamilyRoleQueryResult.Failed failed:
                CommandError.Write(failed.Error);
                return 1;

            case null:
                CommandError.Write(
                    "Name Family Roles produced no terminal result.");
                return 1;

            default:
                throw new InvalidOperationException(
                    "Unknown Name Family Roles result.");
        }
    }

    private static bool RejectUnavailableNameFamilyRoles(
        LibraryInspection inspection,
        LibraryOptions options)
    {
        if (!IsExactNameFamilyRoleSelection(options)
            || inspection.FamilyRoleQueryResult
                is LibraryFamilyRoleQueryResult.Available)
        {
            return false;
        }

        _ = WriteNameFamilyRoleTransport(inspection, options);
        return true;
    }
}
