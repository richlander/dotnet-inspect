using DotnetInspector.Metadata;
using DotnetInspector.Options;

namespace DotnetInspector.Sections;

/// <summary>
/// Section descriptors for the api command type-list view (all types in an assembly).
/// Sections correspond to type-kind groupings: Classes, Structs, Interfaces, Enums, Delegates.
/// </summary>
public static class ApiTypeSectionDescriptors
{
    /// <summary>Builds the section pipeline for the type-list view.</summary>
    public static SectionPipeline<ApiSurface> CreatePipeline()
    {
        return new SectionPipeline<ApiSurface>()
            .Add<Classes>()
            .Add<Structs>()
            .Add<Interfaces>()
            .Add<Enums>()
            .Add<Delegates>();
    }

    public sealed class Classes : ISectionDescriptor<ApiSurface>
    {
        public static string Name => "Classes";
        public static Verbosity MinVerbosity => Verbosity.Minimal;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "class");
    }

    public sealed class Structs : ISectionDescriptor<ApiSurface>
    {
        public static string Name => "Structs";
        public static Verbosity MinVerbosity => Verbosity.Minimal;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "struct");
    }

    public sealed class Interfaces : ISectionDescriptor<ApiSurface>
    {
        public static string Name => "Interfaces";
        public static Verbosity MinVerbosity => Verbosity.Minimal;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "interface");
    }

    public sealed class Enums : ISectionDescriptor<ApiSurface>
    {
        public static string Name => "Enums";
        public static Verbosity MinVerbosity => Verbosity.Minimal;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "enum");
    }

    public sealed class Delegates : ISectionDescriptor<ApiSurface>
    {
        public static string Name => "Delegates";
        public static Verbosity MinVerbosity => Verbosity.Minimal;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "delegate");
    }
}

/// <summary>
/// Section descriptors for the api command type-detail view (single type with members).
/// Sections correspond to <see cref="Views.ApiTypeView"/> sections and member-kind groupings.
/// </summary>
public static class ApiMemberSectionDescriptors
{
    /// <summary>Builds the section pipeline for the type-detail view.</summary>
    public static SectionPipeline<ApiType> CreatePipeline()
    {
        return new SectionPipeline<ApiType>()
            .Add<Values>()
            .Add<TypeParameters>()
            .Add<TypeInterfaces>()
            .Add<Baseclass>()
            .Add<Sources>()
            .Add<Constructors>()
            .Add<Fields>()
            .Add<Properties>()
            .Add<Methods>()
            .Add<Events>()
            .Add<ILBody>();
    }

    // ===== Declarative sections (rendered via Markout [MarkoutSection]) =====

    public sealed class Values : ISectionDescriptor<ApiType>
    {
        public static string Name => "Values";
        public static Verbosity MinVerbosity => Verbosity.Normal;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Kind == "enum"
               && model.Members.Any(m => m.Kind == "field" && m.EnumValue.HasValue);
    }

    public sealed class TypeParameters : ISectionDescriptor<ApiType>
    {
        public static string Name => "Type Parameters";
        public static Verbosity MinVerbosity => Verbosity.Normal;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.TypeParameters.Count > 0;
    }

    public sealed class TypeInterfaces : ISectionDescriptor<ApiType>
    {
        public static string Name => "Interfaces";
        public static Verbosity MinVerbosity => Verbosity.Detailed;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Interfaces.Count > 0;
    }

    public sealed class Baseclass : ISectionDescriptor<ApiType>
    {
        public static string Name => "Baseclass";
        public static Verbosity MinVerbosity => Verbosity.Detailed;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => !string.IsNullOrEmpty(model.BaseType)
               && model.BaseType != "System.Object"
               && model.BaseType != "System.ValueType"
               && model.BaseType != "System.Enum";
    }

    public sealed class Sources : ISectionDescriptor<ApiType>
    {
        public static string Name => "Sources";
        public static Verbosity MinVerbosity => Verbosity.Minimal;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.SourceFilePath != null;
    }

    // ===== Imperative sections (rendered via RenderMembersPerKind) =====

    public sealed class Constructors : ISectionDescriptor<ApiType>
    {
        public static string Name => "Constructors";
        public static Verbosity MinVerbosity => Verbosity.Quiet;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "constructor");
    }

    public sealed class Fields : ISectionDescriptor<ApiType>
    {
        public static string Name => "Fields";
        public static Verbosity MinVerbosity => Verbosity.Quiet;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "field" && !m.EnumValue.HasValue);
    }

    public sealed class Properties : ISectionDescriptor<ApiType>
    {
        public static string Name => "Properties";
        public static Verbosity MinVerbosity => Verbosity.Quiet;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "property");
    }

    public sealed class Methods : ISectionDescriptor<ApiType>
    {
        public static string Name => "Methods";
        public static Verbosity MinVerbosity => Verbosity.Quiet;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "method");
    }

    public sealed class Events : ISectionDescriptor<ApiType>
    {
        public static string Name => "Events";
        public static Verbosity MinVerbosity => Verbosity.Quiet;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "event");
    }

    public sealed class ILBody : ISectionDescriptor<ApiType>
    {
        public static string Name => "IL Body";
        public static Verbosity MinVerbosity => Verbosity.Quiet;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind is "method" or "constructor");
    }
}
