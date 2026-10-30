using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;

namespace DotnetInspect.Cli.Sections;

internal readonly record struct LibrarySourcePlan(
    bool AllowPdbDownload,
    bool CollectSourceFiles,
    bool ReadCachedPdb);

internal readonly record struct LibrarySourceSectionPlan(
    string Name,
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
        // Preserve automatic local PDB reads independently of acquisition requests:
        // the auto-rendered symbol-dependent sections (Symbols, Signals) appear from Normal up, so
        // a Normal render may consult an embedded, adjacent, or already-cached PDB
        // without touching the network. Explicit selection already authorizes a cache-first
        // download, so it needs no separate cache-only read.
        bool readCachedPdb = !hasExplicitSelection && userVerbosity >= Verbosity.Normal;

        if (!hasExplicitSelection)
            return new LibrarySourcePlan(false, false, readCachedPdb);

        foreach (var section in s_sections)
        {
            if (!include!.Contains(section.Name))
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
            downloadPdb,
            collectSourceFiles,
            readCachedPdb);
}
