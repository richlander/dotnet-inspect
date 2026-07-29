using DotnetInspector.Models;
using DotnetInspector.Views;

namespace DotnetInspector.Sections;

/// <summary>
/// Section descriptors for the package command.
/// Each descriptor declares its name, cost classification, scanner key, and a
/// <c>CanRender</c> check against <see cref="InspectionResult"/>.
/// </summary>
public static class PackageSectionDescriptors
{
    /// <summary>Builds the section pipeline with all package sections registered.</summary>
    public static SectionPipeline<InspectionResult> CreatePipeline()
    {
        return new SectionPipeline<InspectionResult>()
            .Add<Summary>()
            .Add<PackageInfo>()
            .Add<PackageReadme>()
            .Add<Signals>()
            .Add<Statistics>()
            .Add<TargetFrameworks>()
            .Add<LibraryFiles>()
            .Add<ReferenceFiles>()
            .Add<RuntimeFiles>()
            .Add<MarkdownFiles>()
            .Add<NuspecFiles>()
            .Add<SourceFiles>()
            .Add<Signature>()
            .Add<Dependencies>()
            .Add<Vulnerabilities>()
            .Add<Manifest>()
            .Add<RuntimeDependencies>()
            .Add<Files>()
            // The "Files:" family. Plain Files is the whole-package listing, so
            // it is deliberately not a member: including it would make
            // -S @Files render most rows twice.
            .AddCategory(SectionCategoryNames.Files, PackageFileFamily.SectionNames);
    }

    // ===== Primary sections (Summary preamble + Package Info) =====

    public sealed class Summary : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Summary;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model) => true;
    }

    public sealed class PackageInfo : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.PackageInfo;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model) => true;
    }

    public sealed class PackageReadme : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.PackageReadme;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.PackageReadmeFile != null
               || model.PackageFiles?.Any(file => file.IsReadme) == true;
    }

    public sealed class Signals : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Signals;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model) => true;
    }

    // ===== Expensive sections (require network) =====

    public sealed class Statistics : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Statistics;
        public static bool IsExpensive => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.TotalDownloads != null;
    }

    public sealed class TargetFrameworks : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.TargetFrameworks;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.TargetFrameworks is { Count: > 0 };
    }

    public sealed class LibraryFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.FilesLibrary;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => Matches(model, PackageSections.FilesLibrary)
               || model.LibraryFiles is { Count: > 0 };
    }

    public sealed class MarkdownFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.FilesMarkdown;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => Matches(model, PackageSections.FilesMarkdown);
    }

    /// <summary>
    /// <c>ref/</c> assets. Normal rather than explicit-only, matching
    /// <see cref="LibraryFiles"/>: reference assemblies are shipped payload of the same
    /// kind, the row count is bounded (22 for the largest measured package), and
    /// <c>CanRender</c> is content-gated, so packages without <c>ref/</c> are unaffected.
    /// It stays out of the bare <c>-S</c> preset, which is <c>Info</c>-gated.
    /// </summary>
    public sealed class ReferenceFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.FilesReference;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => Matches(model, PackageSections.FilesReference);
    }

    /// <summary>
    /// <c>runtimes/</c> (RID-specific) assets. Normal for the same reasons as
    /// <see cref="ReferenceFiles"/>.
    /// </summary>
    public sealed class RuntimeFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.FilesRuntime;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => Matches(model, PackageSections.FilesRuntime);
    }

    public sealed class NuspecFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.FilesNuspec;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => Matches(model, PackageSections.FilesNuspec);
    }

    public sealed class SourceFiles : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.SourceFiles;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
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
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.SignatureResult != null;
    }

    public sealed class Vulnerabilities : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Vulnerabilities;
        public static bool IsExpensive => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.Vulnerabilities is { Count: > 0 };
    }

    // ===== Normal sections (offline, cheap) =====

    public sealed class Dependencies : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Dependencies;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.DependencyGroups is { Count: > 0 };
    }

    public sealed class Manifest : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Manifest;
        public static bool IsExpensive => false;
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
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.RuntimeDependencies is { Count: > 0 };
    }

    public sealed class Files : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Files;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.Files is { Count: > 0 };
    }

    private static bool Matches(InspectionResult model, string section)
        => PackageFileFamily.PredicateFor(section) is { } predicate
           && model.PackageFiles?.Any(predicate) == true;
}
