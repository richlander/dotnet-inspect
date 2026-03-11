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
            .Add<Statistics>()
            .Add<PackageDependencies>()
            .Add<Vulnerabilities>()
            .Add<RidPackages>()
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
        public static string Name => PackageSections.Package;
        public static bool IsExpensive => false;
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

    public sealed class Vulnerabilities : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Vulnerabilities;
        public static bool IsExpensive => true;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.Vulnerabilities is { Count: > 0 };
    }

    // ===== Normal sections (offline, cheap) =====

    public sealed class PackageDependencies : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.PackageDependencies;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.DependencyGroups is { Count: > 0 };
    }

    public sealed class RidPackages : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.RidPackages;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.RuntimeIdentifierPackages is { Count: > 0 };
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
