using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

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

    // The library pipeline owns the verbosity at which each implicitly rendered section appears.
    private static readonly Lazy<SectionPipeline<LibraryInspection>> s_pipeline =
        new(LibrarySections.CreatePipeline);

    internal static ReadOnlySpan<LibrarySourceSectionPlan> Sections => s_sections;

    internal static LibrarySourcePlan For(LibraryOptions options)
        => For(
            options.UserVerbosity,
            options.UserIncludeSections,
            DotnetInspector.Networking.HttpClientFactory.IsOffline);

    /// <summary>
    /// Plans PDB and source work for a library render. Outside <c>--offline</c>, an implicitly
    /// rendered section that declares PDB facts may acquire a missing PDB at the verbosity where
    /// it renders (docs/design/progressive-disclosure.md#network-policy). Under <c>--offline</c>,
    /// implicit renders below Detailed read only embedded, adjacent, or cached PDBs.
    /// </summary>
    internal static LibrarySourcePlan For(
        Verbosity userVerbosity,
        HashSet<string>? include,
        bool offline = false)
    {
        bool downloadPdb = false;
        bool collectSourceFiles = false;
        bool hasExplicitSelection = include is { Count: > 0 };
        var mode = hasExplicitSelection
            ? LibrarySourcePlanModes.Explicit
            : userVerbosity >= Verbosity.Detailed
                || (!offline && userVerbosity >= Verbosity.Normal)
                ? LibrarySourcePlanModes.Detailed
                : LibrarySourcePlanModes.None;

        // The auto-rendered symbol-dependent sections (Symbols, Signals) appear from Normal up.
        // Online, they may acquire a missing PDB there; under --offline a Normal / bare-`S` render
        // still consults an embedded, adjacent, or already-cached PDB without touching the network.
        // Explicit selection already authorizes a cache-first download, so it needs no separate
        // cache-only read.
        bool readCachedPdb = !hasExplicitSelection && userVerbosity >= Verbosity.Normal;

        if (mode == LibrarySourcePlanModes.None)
            return new LibrarySourcePlan(false, false, readCachedPdb);

        foreach (var section in s_sections)
        {
            bool selected = hasExplicitSelection
                ? include!.Contains(section.Name)
                : (section.Modes & LibrarySourcePlanModes.Detailed) != 0
                    && RendersImplicitlyAt(section.Name, userVerbosity);
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

    private static bool RendersImplicitlyAt(string section, Verbosity userVerbosity)
        => userVerbosity >= s_pipeline.Value.GetRequiredVerbosity(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { section });

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
