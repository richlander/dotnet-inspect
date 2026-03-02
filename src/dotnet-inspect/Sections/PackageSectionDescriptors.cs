using Markout;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Views;

namespace DotnetInspector.Sections;

/// <summary>
/// Section descriptors for the package command.
/// Each descriptor declares its name, minimum verbosity, scanner key, and a
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

    /// <summary>Builds the section schema map for discovery (-D).</summary>
    public static DocumentSchema CreateSchemaMap()
    {
        return new DocumentSchema()
            .AddSection(PackageSections.Summary)
            .Add(PackageSections.Package, "field",
                "Version", "Type", "Size", "TFM", "Built", "Published", "Source",
                "Authors", "Owners", "License", "Repository", "Verified",
                "Content", "Target Frameworks", "Runtime Identifiers", "Libraries",
                "Readme", "Vulnerabilities", "Tool Commands")
            .Add(PackageSections.Statistics, "field",
                "Published", "Downloads", "Version Downloads", "Version Count")
            .Add(PackageSections.PackageDependencies, "column",
                "Target Framework", "Id", "Version")
            .Add(PackageSections.Vulnerabilities, "list", "Vulnerability")
            .Add(PackageSections.RidPackages, "column",
                "RID", "Package", "Version")
            .Add(PackageSections.RuntimeDependencies, "column",
                "Id", "Version", "Type")
            .Add(PackageSections.Files, "list", "Path");
    }

    // ===== Always-on sections (Quiet verbosity) =====

    public sealed class Summary : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Summary;
        public static Verbosity MinVerbosity => Verbosity.Quiet;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model) => true;
    }

    // ===== Always-on sections (Minimal verbosity) =====

    public sealed class PackageInfo : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Package;
        public static Verbosity MinVerbosity => Verbosity.Minimal;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model) => true;
    }

    public sealed class RidPackages : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.RidPackages;
        public static Verbosity MinVerbosity => Verbosity.Minimal;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.RuntimeIdentifierPackages is { Count: > 0 };
    }

    public sealed class RuntimeDependencies : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.RuntimeDependencies;
        public static Verbosity MinVerbosity => Verbosity.Minimal;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.RuntimeDependencies is { Count: > 0 };
    }

    // ===== Normal verbosity sections =====

    public sealed class PackageDependencies : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.PackageDependencies;
        public static Verbosity MinVerbosity => Verbosity.Normal;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.DependencyGroups is { Count: > 0 };
    }

    // ===== Detailed verbosity sections =====

    public sealed class Statistics : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Statistics;
        public static Verbosity MinVerbosity => Verbosity.Detailed;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.TotalDownloads != null;
    }

    public sealed class Vulnerabilities : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Vulnerabilities;
        public static Verbosity MinVerbosity => Verbosity.Detailed;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.Vulnerabilities is { Count: > 0 };
    }

    public sealed class Files : ISectionDescriptor<InspectionResult>
    {
        public static string Name => PackageSections.Files;
        public static Verbosity MinVerbosity => Verbosity.Detailed;
        public static string? ScannerKey => null;
        public static bool CanRender(InspectionResult model)
            => model.Files is { Count: > 0 };
    }
}
