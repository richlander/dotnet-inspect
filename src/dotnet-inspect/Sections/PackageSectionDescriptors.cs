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
            .Add<Signals>()
            .Add<Statistics>()
            .Add<TargetFrameworks>()
            .Add<LibraryFiles>()
            .Add<Signature>()
            .Add<Dependencies>()
            .Add<Vulnerabilities>()
            .Add<Manifest>()
            .Add<RuntimeDependencies>()
            .Add<Files>();
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
        public static string Name => PackageSections.LibraryFiles;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.LibraryFiles is { Count: > 0 };
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
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.Files is { Count: > 0 };
    }
}
