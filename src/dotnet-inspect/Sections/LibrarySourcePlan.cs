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
    bool CollectSourceFiles,
    bool ReadCachedPdb);

internal readonly record struct LibrarySourceSectionPlan(
    string Name,
    LibrarySourcePlanModes Modes,
    bool DownloadPdb,
    bool CollectSourceFiles,
    bool ReadCachedPdb);

internal static class LibrarySourcePlans
{
    private static readonly LibrarySourceSectionPlan[] s_sections =
    [
        Section<LibrarySections.ILOffset>(downloadPdb: true),
        Section<LibrarySections.SourceFiles>(downloadPdb: true, collectSourceFiles: true),
        Section<LibrarySections.SourceLinkDiagnostics>(readCachedPdb: true),
        Section<LibrarySections.Symbols>(downloadPdb: true),
        Section<LibrarySections.Signals>(downloadPdb: true),
        Section<LibrarySections.NonNormalizedPaths>(readCachedPdb: true),
    ];

    internal static ReadOnlySpan<LibrarySourceSectionPlan> Sections => s_sections;

    internal static LibrarySourcePlan For(LibraryOptions options)
        => For(options.UserVerbosity, options.UserIncludeSections);

    internal static LibrarySourcePlan For(
        Verbosity userVerbosity,
        HashSet<string>? include)
    {
        bool downloadPdb = false;
        bool collectSourceFiles = false;
        bool hasExplicitSelection = include is { Count: > 0 };
        var mode = hasExplicitSelection
            ? LibrarySourcePlanModes.Explicit
            : userVerbosity >= Verbosity.Detailed
                ? LibrarySourcePlanModes.Detailed
                : LibrarySourcePlanModes.None;

        // Cache-only PDB reads are network-free, so they are authorized one tier before downloads:
        // the auto-rendered symbol-dependent sections (Symbols, Signals) appear from Normal up, so
        // a Normal / bare-`S` render may consult an embedded, adjacent, or already-cached PDB
        // without touching the network. Explicit selection already authorizes a cache-first
        // download, so it needs no separate cache-only read.
        bool readCachedPdb = !hasExplicitSelection && userVerbosity >= Verbosity.Normal;

        if (mode == LibrarySourcePlanModes.None)
            return new LibrarySourcePlan(false, false, readCachedPdb);

        foreach (var section in s_sections)
        {
            bool selected = hasExplicitSelection
                ? include!.Contains(section.Name)
                : (section.Modes & LibrarySourcePlanModes.Detailed) != 0;
            if (!selected || (section.Modes & mode) == 0)
                continue;

            downloadPdb |= section.DownloadPdb;
            collectSourceFiles |= section.CollectSourceFiles;
            readCachedPdb |= section.ReadCachedPdb;
        }

        return new LibrarySourcePlan(
            downloadPdb,
            collectSourceFiles,
            readCachedPdb);
    }

    private static LibrarySourceSectionPlan Section<TDescriptor>(
        bool downloadPdb = false,
        bool collectSourceFiles = false,
        bool readCachedPdb = false)
        where TDescriptor : ISectionDescriptor<LibraryInspection>
        => new(
            TDescriptor.Name,
            TDescriptor.ExplicitOnly
                ? LibrarySourcePlanModes.Explicit
                : LibrarySourcePlanModes.All,
            downloadPdb,
            collectSourceFiles,
            readCachedPdb);
}
