using DotnetInspector.Models;
using DotnetInspector.Views;

namespace DotnetInspector.Sections;

/// <summary>
/// Section descriptors for the package command.
///
/// This is a curated catalog: each descriptor declares a <see cref="SectionSizeClass"/> (how
/// its row count grows across the universe of packages) and a <see cref="SectionCost"/> (what
/// producing it costs), and the verbosity ladder is computed from those two axes rather than
/// from registration order or a hand-maintained preset list. A section's absence from a view
/// therefore always means "not applicable to this package", never "too long for this one".
/// </summary>
public static class PackageSectionDescriptors
{
    /// <summary>Builds the section pipeline with all package sections registered.</summary>
    public static SectionPipeline<InspectionResult> CreatePipeline()
    {
        return new SectionPipeline<InspectionResult>()
            .UseCuratedCatalog()
            .Add<Summary>()
            .Add<PackageInfo>()
            .Add<PackageReadme>()
            .Add<Signals>()
            .Add<Statistics>()
            .Add<TargetFrameworks>()
            .Add<MarkdownFiles>()
            .Add<NuspecFiles>()
            .Add<SkillFiles>()
            .Add<SourceFiles>()
            .Add<Signature>()
            .Add<Dependencies>()
            .Add<Vulnerabilities>()
            .Add<Manifest>()
            .Add<RuntimeDependencies>()
            .Add<Files>()
            // The package file family. Plain "Package files" is the whole-package listing,
            // so it is deliberately not a member: including it would make
            // -S @Files render most rows twice.
            .AddCategory(SectionCategoryNames.Files, PackageFileFamily.SectionNames)
            // SourceLink: Files carries the "SourceLink:" prefix, which advertises a door,
            // so the door has to exist here too. It is a one-member family today only
            // because the package command has not yet grown the other three sections the
            // library command exposes; the name matching across commands is the point.
            .AddCategory(SectionCategoryNames.SourceLink, [PackageSections.SourceLinkFiles]);
    }

    // ===== Primary sections (Summary preamble + Package Info) =====

    public sealed class Summary : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Summary;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model) => true;
    }

    public sealed class PackageInfo : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.PackageInfo;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model) => true;
    }

    /// <summary>
    /// The best package README (README.md, then PACKAGE.md, then the declared readme).
    /// Structurally one row or none, and read from the extracted package, so it is
    /// <see cref="SectionSizeClass.Fixed"/> and network-free.
    /// </summary>
    public sealed class PackageReadme : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.FilesReadme;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.PackageReadmeFile != null
               || model.PackageFiles?.Any(file => file.IsReadme) == true;
    }

    public sealed class Signals : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Signals;
        // Cost, not IsExpensive, carries the network truth in a curated catalog. Measured at
        // ~1s warm for Markout and System.Text.Json: bounded registry work, so Moderated —
        // auto-runs at -v:d only, and stays in the visible catalog like library's Signals.
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static SectionCost Cost => SectionCost.Moderated;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model) => true;
    }

    // ===== Network-bound sections =====

    public sealed class Statistics : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Statistics;
        // One bounded registry lookup (~0.25s measured): auto-runs at -v:d, never at bare -S
        // or -v:n. Cost carries the network truth, so IsExpensive stays false.
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static SectionCost Cost => SectionCost.Moderated;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.TotalDownloads != null;
    }

    public sealed class TargetFrameworks : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.TargetFrameworks;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.TargetFrameworks is { Count: > 0 };
    }

    public sealed class MarkdownFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.FilesMarkdown;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => Matches(model, PackageSections.FilesMarkdown);
    }

    public sealed class SkillFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.FilesSkills;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => Matches(model, PackageSections.FilesSkills);
    }

    public sealed class NuspecFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.FilesNuspec;
        public static bool IsExpensive => false;
        // Exactly one row for every package that has a manifest.
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => Matches(model, PackageSections.FilesNuspec);
    }

    public sealed class SourceFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.SourceLinkFiles;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        // PDB acquisition plus a per-document listing: never auto-run. Matches the library
        // declaration of the same section; the @SourceLink door keeps it discoverable.
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.SourceFiles is { Count: > 0 }
               || model.LibraryFiles is { Count: > 0 }
               || model.AssemblyCount > 0;
    }

    public sealed class Signature : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Signature;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.SignatureResult != null;
    }

    public sealed class Vulnerabilities : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Vulnerabilities;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static SectionCost Cost => SectionCost.Moderated;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.Vulnerabilities is { Count: > 0 };
    }

    // ===== Offline sections =====

    public sealed class Dependencies : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Dependencies;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.DependencyGroups is { Count: > 0 };
    }

    public sealed class Manifest : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Manifest;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => !string.IsNullOrWhiteSpace(model.PackageName)
               || !string.IsNullOrWhiteSpace(model.Version)
               || !string.IsNullOrWhiteSpace(model.ToolFormat)
               || model.ToolCommands is { Count: > 0 }
               || model.RuntimeIdentifierPackages is { Count: > 0 };
    }

    public sealed class RuntimeDependencies : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.RuntimeDependencies;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.RuntimeDependencies is { Count: > 0 };
    }

    /// <summary>
    /// The whole-package listing. Its row count tracks package size with no bound, so it is
    /// <see cref="SectionCost.Unbounded"/> and never auto-renders — structural layout data is
    /// not identity metadata, and mixing it into the identity views conflates the two.
    ///
    /// It is <see cref="Noisy"/>: cheap but long, never auto-rendered, yet kept in the visible
    /// catalog so <c>-D</c> still advertises the whole-package listing rather than hiding the
    /// command's headline capability behind a door it cannot join.
    /// </summary>
    public sealed class Files : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Files;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool Noisy => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.Files is { Count: > 0 };
    }

    private static bool Matches(InspectionResult model, string section)
        => PackageFileFamily.PredicateFor(section) is { } predicate
           && model.PackageFiles?.Any(predicate) == true;
}
