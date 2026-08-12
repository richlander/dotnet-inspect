using DotnetInspector.Models;
using DotnetInspector.Queries;
using DotnetInspector.Views;

namespace DotnetInspector.Sections;

public sealed record PackageSectionCatalog(
    SectionPipeline<InspectionResult> Pipeline,
    InspectionQueryRegistry<SourceLinkQueryContext> QueryRegistry);

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
        var queryRegistry = CreateQueryRegistry();
        return CreatePipeline(queryRegistry.CostOf);
    }

    public static PackageSectionCatalog CreateCatalog()
    {
        var queryRegistry = CreateQueryRegistry();
        return new PackageSectionCatalog(
            CreatePipeline(queryRegistry.CostOf),
            queryRegistry);
    }

    public static InspectionQueryRegistry<SourceLinkQueryContext> CreateQueryRegistry()
        => new InspectionQueryRegistry<SourceLinkQueryContext>()
            .AddSourceLinkQueries(static context => context);

    private static SectionPipeline<InspectionResult> CreatePipeline(
        Func<InspectionQueryDefinition, InspectionCost> queryCost)
    {
        return new SectionPipeline<InspectionResult>()
            .UseCuratedCatalog()
            .UseQueryCosts(queryCost)
            .WithoutComputedPoles()
            .Add<Summary>()
            .Add<PackageInfo>()
            .Add<PackageReadme>()
            .Add<Signals>()
            .Add<AuditArtifactText>()
            .Add<AuditIdentifierConfusion>()
            .Add<Statistics>()
            .Add<TargetFrameworks>()
            .Add<NuspecFiles>()
            .Add<SkillFiles>()
            .Add<SourceFiles>()
            .Add<SourceLinkAvailability>(
                SourceAvailabilityQuery.Definition,
                HasLibraries)
            .Add<SourceLinkIntegrity>(
                SourceIntegrityQuery.Definition,
                HasLibraries)
            .Add<SourceLinkMissingFiles>(
                SourceAvailabilityQuery.Definition,
                HasLibraries)
            .Add<Signature>()
            .Add<Dependencies>()
            .Add<Vulnerabilities>()
            .Add<Manifest>()
            .Add<RuntimeDependencies>()
            .Add<Files>()
            // Package-native evidence is the command's primary base category. The unbounded
            // whole-package listing belongs here rather than @Files: selecting @Package is an
            // explicit request for the complete package lens, while keeping the listing out of
            // @Files prevents its curated subsets from rendering the same paths twice.
            .AddBaseCategory(
                SectionCategoryNames.Package,
                PackageSections.PackageInfo,
                PackageSections.Signals,
                PackageSections.Statistics,
                PackageSections.TargetFrameworks,
                PackageSections.Signature,
                PackageSections.Dependencies,
                PackageSections.Vulnerabilities,
                PackageSections.Manifest,
                PackageSections.RuntimeDependencies,
                PackageSections.Files)
            // The package file family. Plain "Package files" is the whole-package listing,
            // so it is deliberately not a member: including it would make
            // -S @Files render most rows twice.
            .AddBaseCategory(SectionCategoryNames.Files, PackageFileFamily.SectionNames)
            .AddCategory(
                SectionCategoryNames.Dependencies,
                PackageSections.Dependencies,
                PackageSections.RuntimeDependencies)
            .AddCategory(
                SectionCategoryNames.Audit,
                PackageSections.Signals,
                PackageSections.AuditArtifactText,
                PackageSections.AuditIdentifierConfusion,
                PackageSections.Signature,
                PackageSections.Vulnerabilities,
                PackageSections.SourceLinkAvailability,
                PackageSections.SourceLinkMissingFiles,
                PackageSections.SourceLinkIntegrity)
            .AddCategory(
                SectionCategoryNames.SourceLink,
                PackageSections.SourceLinkFiles,
                PackageSections.SourceLinkAvailability,
                PackageSections.SourceLinkMissingFiles,
                PackageSections.SourceLinkIntegrity);
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

    /// <summary>
    /// One content-free row per artifact-derived package field that required visual
    /// containment. Package file paths make the possible row count track package size,
    /// so the section is available only through an exact or category selection.
    /// </summary>
    public sealed class AuditArtifactText : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.AuditArtifactText;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => new PackageInspectionText(model).ConcernCases.Count > 0;
    }

    public sealed class AuditIdentifierConfusion : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.AuditIdentifierConfusion;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => IdentifierConfusionAudit.InspectPackage(model).Count > 0;
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

    public sealed class SourceLinkAvailability : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.SourceLinkAvailability;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.SourceAvailability != null;
    }

    public sealed class SourceLinkIntegrity : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.SourceLinkIntegrity;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.SourceIntegrity != null;
    }

    public sealed class SourceLinkMissingFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.SourceLinkMissingFiles;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.SourceAvailability is { } availability
               && (availability.MissingFiles is { Count: > 0 }
                   || availability.UnavailableLibraries is { Count: > 0 }
                   || availability.FailedLibraries is { Count: > 0 });
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

    private static bool HasLibraries(InspectionResult model)
        => model.LibraryFiles is { Count: > 0 }
           || model.AssemblyCount > 0;
}
