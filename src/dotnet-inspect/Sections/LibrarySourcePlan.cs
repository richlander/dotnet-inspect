using DotnetInspector.Models;
using DotnetInspector.Options;

namespace DotnetInspector.Sections;

[Flags]
internal enum LibrarySourcePlanModes
{
    None = 0,
    Detailed = 1 << 0,
    Explicit = 1 << 1,
    All = Detailed | Explicit,
}

internal readonly record struct LibrarySourcePlan(
    bool AllowPdbDownload,
    bool RunHeadAudit,
    bool RunIntegrity,
    bool CollectSourceFiles);

internal readonly record struct LibrarySourceSectionPlan(
    string Name,
    LibrarySourcePlanModes Modes,
    bool DownloadPdb,
    bool AuditSources,
    bool VerifyIntegrity,
    bool CollectSourceFiles);

internal static class LibrarySourcePlans
{
    private static readonly LibrarySourceSectionPlan[] s_sections =
    [
        Section<LibrarySections.ILOffset>(downloadPdb: true),
        Section<LibrarySections.SourceFiles>(downloadPdb: true, collectSourceFiles: true),
        Section<LibrarySections.Symbols>(downloadPdb: true),
        Section<LibrarySections.Signals>(downloadPdb: true, auditSources: true),
        Section<LibrarySections.SourceLinkAudit>(downloadPdb: true, auditSources: true),
        Section<LibrarySections.MissingSourceFiles>(downloadPdb: true, auditSources: true),
        Section<LibrarySections.SourceIntegrity>(downloadPdb: true, verifyIntegrity: true),
    ];

    internal static ReadOnlySpan<LibrarySourceSectionPlan> Sections => s_sections;

    internal static LibrarySourcePlan For(
        Verbosity userVerbosity,
        HashSet<string>? include)
    {
        bool downloadPdb = false;
        bool auditSources = false;
        bool verifyIntegrity = false;
        bool collectSourceFiles = false;
        bool hasExplicitSelection = include is { Count: > 0 };
        var mode = hasExplicitSelection
            ? LibrarySourcePlanModes.Explicit
            : userVerbosity >= Verbosity.Detailed
                ? LibrarySourcePlanModes.Detailed
                : LibrarySourcePlanModes.None;

        if (mode == LibrarySourcePlanModes.None)
            return default;

        foreach (var section in s_sections)
        {
            bool selected = hasExplicitSelection
                ? include!.Contains(section.Name)
                : (section.Modes & LibrarySourcePlanModes.Detailed) != 0;
            if (!selected || (section.Modes & mode) == 0)
                continue;

            downloadPdb |= section.DownloadPdb;
            auditSources |= section.AuditSources;
            verifyIntegrity |= section.VerifyIntegrity;
            collectSourceFiles |= section.CollectSourceFiles;
        }

        return new LibrarySourcePlan(
            downloadPdb,
            auditSources,
            verifyIntegrity,
            collectSourceFiles);
    }

    private static LibrarySourceSectionPlan Section<TDescriptor>(
        bool downloadPdb = false,
        bool auditSources = false,
        bool verifyIntegrity = false,
        bool collectSourceFiles = false)
        where TDescriptor : ISectionDescriptor<LibraryInspection>
        => new(
            TDescriptor.Name,
            TDescriptor.ExplicitOnly
                ? LibrarySourcePlanModes.Explicit
                : LibrarySourcePlanModes.All,
            downloadPdb,
            auditSources,
            verifyIntegrity,
            collectSourceFiles);
}
