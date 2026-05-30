using DotnetInspector.Inspectors;
using DotnetInspector.Models;

namespace DotnetInspector.Sections;

/// <summary>
/// Section descriptors for the library command.
/// Each descriptor declares its name, cost classification, scanner key, and a
/// <c>CanRender</c> check against <see cref="LibraryInspection"/>.
/// </summary>
public static class LibrarySections
{
    // Scanner keys identify data collection steps in LibraryMetadataService
    public const string ScannerTransitiveRefs = "TransitiveRefs";
    public const string ScannerExtensionMethods = "ExtensionMethods";
    public const string ScannerClassifiedMethods = "ClassifiedMethods";
    public const string ScannerResources = "Resources";
    public const string ScannerCustomAttributes = "CustomAttributes";
    public const string ScannerTypeForwarders = "TypeForwarders";
    public const string ScannerAudit = "Audit";

    /// <summary>Builds the section pipeline with all library sections registered.</summary>
    public static SectionPipeline<LibraryInspection> CreatePipeline()
    {
        return new SectionPipeline<LibraryInspection>()
            .Add<LibraryInfo>()
            .Add<Symbols>()
            .Add<LibraryReferences>()
            .Add<LibraryReferencesTransitive>()
            .Add<Dependencies>()
            .Add<ExtensionMethods>()
            .Add<UnsafeMethods>()
            .Add<PInvokeMethods>()
            .Add<AsyncMethods>()
            .Add<Resources>()
            .Add<CustomAttributes>()
            .Add<TypeForwarders>()
            .Add<NonNormalizedPaths>();
    }

    /// <summary>Builds the scanner registry with all library scanners registered.</summary>
    public static ScannerRegistry CreateScannerRegistry()
    {
        return new ScannerRegistry()
            .Add(ScannerExtensionMethods, ctx =>
                ctx.Model.ExtensionMethods = LibraryMetadataService.ScanExtensionMethods(ctx.AssemblyPath, ctx.Logger))
            .Add(ScannerClassifiedMethods, ctx =>
                LibraryMetadataService.ScanClassifiedMethods(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerResources, ctx =>
                ctx.Model.Resources = LibraryMetadataService.ScanResources(ctx.AssemblyPath, ctx.Logger))
            .Add(ScannerCustomAttributes, ctx =>
                LibraryMetadataService.ScanCustomAttributes(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerTypeForwarders, ctx =>
                LibraryMetadataService.ScanTypeForwarders(ctx.AssemblyPath, ctx.Model, ctx.Logger));
    }

    // ===== Primary section =====

    public sealed class LibraryInfo : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Library Info";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.AssemblyInfo != null;
    }

    // ===== Expensive sections (require network) =====

    public sealed class Symbols : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Symbols";
        public static bool IsExpensive => true;
        public static string? ScannerKey => ScannerAudit;
        public static bool CanRender(LibraryInspection model) => true;
    }

    // ===== Normal sections (offline, cheap) =====

    public sealed class LibraryReferences : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Library References";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model)
            => model.AssemblyInfo?.References is { Count: > 0 }
               && model.AssemblyInfo?.TransitiveReferences is not { Count: > 0 };
    }

    public sealed class LibraryReferencesTransitive : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Library References (Transitive)";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerTransitiveRefs;
        public static bool CanRender(LibraryInspection model)
            => !model.UseDependenciesView
               && model.AssemblyInfo?.TransitiveReferences is { Count: > 0 };
    }

    public sealed class Dependencies : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Dependencies";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerTransitiveRefs;
        public static bool CanRender(LibraryInspection model)
            => model.UseDependenciesView
               && model.AssemblyInfo?.TransitiveReferences is { Count: > 0 };
    }

    public sealed class ExtensionMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Extension Methods";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerExtensionMethods;
        public static bool CanRender(LibraryInspection model)
            => model.ExtensionMethods is { Count: > 0 } || model.HasExtensionTypes;
    }

    public sealed class UnsafeMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Unsafe Methods";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerClassifiedMethods;
        public static bool CanRender(LibraryInspection model)
            => model.UnsafeMethods is { Count: > 0 } || model.HasUnsafeCode;
    }

    public sealed class PInvokeMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "P/Invoke Methods";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerClassifiedMethods;
        public static bool CanRender(LibraryInspection model)
            => model.PInvokeMethods is { Count: > 0 } || model.HasPInvokeImports;
    }

    public sealed class AsyncMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Async Methods";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerClassifiedMethods;
        public static bool CanRender(LibraryInspection model)
            => model.AsyncMethods is { Count: > 0 }
               || model.HasRuntimeAsync || model.HasStateMachineAsync;
    }

    public sealed class Resources : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Resources";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerResources;
        public static bool CanRender(LibraryInspection model)
            => model.Resources is { Count: > 0 } || model.HasManifestResources;
    }

    public sealed class CustomAttributes : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Custom Attributes";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerCustomAttributes;
        public static bool CanRender(LibraryInspection model)
            => model.CustomAttributes is { Count: > 0 } || model.HasAssemblyAttributes;
    }

    public sealed class TypeForwarders : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Type Forwarders";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerTypeForwarders;
        public static bool CanRender(LibraryInspection model)
            => model.TypeForwarders is { Count: > 0 } || model.HasExportedTypeForwarders;
    }

    public sealed class NonNormalizedPaths : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Non-normalized Paths";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null; // data comes from PdbContext (always collected)
        public static bool CanRender(LibraryInspection model) => model.NonNormalizedPaths is { Count: > 0 };
    }

}
