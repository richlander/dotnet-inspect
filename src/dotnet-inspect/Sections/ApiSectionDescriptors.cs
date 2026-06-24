using ILInspector.Metadata;
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
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "class");
    }

    public sealed class Structs : ISectionDescriptor<ApiSurface>
    {
        public static string Name => "Structs";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "struct");
    }

    public sealed class Interfaces : ISectionDescriptor<ApiSurface>
    {
        public static string Name => "Interfaces";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "interface");
    }

    public sealed class Enums : ISectionDescriptor<ApiSurface>
    {
        public static string Name => "Enums";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "enum");
    }

    public sealed class Delegates : ISectionDescriptor<ApiSurface>
    {
        public static string Name => "Delegates";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "delegate");
    }
}

/// <summary>
/// Section descriptors for the api command type-detail view (single type with members).
/// Sections correspond to <see cref="Views.TypeView"/> sections and member-kind groupings.
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
            .Add<Constructors>()
            .Add<Fields>()
            .Add<Properties>()
            .Add<MethodGroups>()
            .Add<Methods>(HasMethods)
            .Add<MemberIndex>()
            .Add<Operators>(HasOperators)
            .Add<ExplicitInterfaceImplementations>(HasExplicitInterfaceImplementations)
            .Add<ExtensionMethods>(HasExtensionMethods)
            .Add<Events>()
            .Add<MethodAttributes>()
            .Add<UnsafeMembers>()
            .Add<TopLeverage>()
            .Add<SourceFiles>()
            .Add<DecompiledSource>()
            .Add<OriginalSource>()
            .Add<ILBody>()
            .Add<Facts>()
            .AddCategory(SectionCategoryNames.Audit, SectionNames.UnsafeMembers);
    }

    // ===== Declarative sections (rendered via Markout [MarkoutSection]) =====

    public sealed class Values : ISectionDescriptor<ApiType>
    {
        public static string Name => "Values";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Kind == "enum"
               && model.Members.Any(m => m.Kind == "field" && m.EnumValue.HasValue);
    }

    public sealed class TypeParameters : ISectionDescriptor<ApiType>
    {
        public static string Name => "Type Parameters";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.TypeParameters.Count > 0;
    }

    public sealed class TypeInterfaces : ISectionDescriptor<ApiType>
    {
        public static string Name => "Interfaces";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Interfaces.Count > 0;
    }

    public sealed class Baseclass : ISectionDescriptor<ApiType>
    {
        public static string Name => "Baseclass";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => !string.IsNullOrEmpty(model.BaseType)
               && model.BaseType != "System.Object"
               && model.BaseType != "System.ValueType"
               && model.BaseType != "System.Enum";
    }

    // ===== Member sections (rendered via PopulateMemberSections) =====

    public sealed class Constructors : ISectionDescriptor<ApiType>
    {
        public static string Name => "Constructors";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "constructor");
    }

    public sealed class Fields : ISectionDescriptor<ApiType>
    {
        public static string Name => "Fields";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "field" && !m.EnumValue.HasValue);
    }

    public sealed class Properties : ISectionDescriptor<ApiType>
    {
        public static string Name => "Properties";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "property");
    }

    public sealed class Methods : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.Methods;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => HasMethods(model);
    }

    public sealed class MemberIndex : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.MemberIndex;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => !MemberFilters.IsCompilerGenerated(m.Name));
    }

    public sealed class MethodGroups : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.MethodGroups;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => HasMethods(model);
    }

    public sealed class Events : ISectionDescriptor<ApiType>
    {
        public static string Name => "Events";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "event");
    }

    public sealed class Operators : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.Operators;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => HasOperators(model);
    }

    public sealed class ExplicitInterfaceImplementations : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.ExplicitInterfaceImplementations;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => HasExplicitInterfaceImplementations(model);
    }

    public sealed class ExtensionMethods : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.ExtensionMethods;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => HasExtensionMethods(model);
    }

    public sealed class MethodAttributes : ISectionDescriptor<ApiType>
    {
        public static string Name => "Custom Attributes";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsMethodLike);
    }

    public sealed class UnsafeMembers : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.UnsafeMembers;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsMethodLike);
    }

    public sealed class TopLeverage : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.TopLeverage;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsMethodLike);
    }

    public sealed class SourceLocations : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.SourceLocations;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsMethodLike);
    }

    public sealed class SourceFiles : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.SourceFiles;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => !string.IsNullOrWhiteSpace(model.FullName);
    }

    // ===== Expensive sections (decompiler output) =====

    public sealed class DecompiledSource : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.DecompiledSource;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            // Enums have no method bodies but the whole-type listing renders
            // their declaration and values.
            => model.Members.Any(IsMethodLike) || model.Kind == "enum";
    }

    public sealed class ILBody : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.IL;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsMethodLike);
    }

    public sealed class Facts : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.Facts;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1 && model.Members.Any(IsMethodLike);
    }

    public sealed class OriginalSource : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.OriginalSource;
        public static bool IsExpensive => true;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayFetchSources;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsMethodLike);
    }

    internal static bool IsMethodLike(ApiMember member) =>
        member.Kind is "method" or "constructor" or "operator" or "explicit-interface-implementation" or "extension-method";

    private static bool HasMethods(ApiType model)
        => model.Members.Any(m => m.Kind == "method");

    private static bool HasOperators(ApiType model)
        => model.Members.Any(m => m.Kind == "operator");

    private static bool HasExplicitInterfaceImplementations(ApiType model)
        => model.Members.Any(m => m.Kind == "explicit-interface-implementation");

    private static bool HasExtensionMethods(ApiType model)
        => model.Members.Any(m => m.Kind == "extension-method");
}

/// <summary>
/// Selects the appropriate member pipeline for list/detail contexts.
/// </summary>
public static class ApiMemberSectionPipelines
{
    public static SectionPipeline<ApiType> Create(ApiOptions options)
        => UsesDetailPipeline(options)
            ? ApiMemberDetailSectionDescriptors.CreatePipeline()
            : UsesOverloadInventoryPipeline(options)
                ? ApiMemberOverloadSectionDescriptors.CreatePipeline()
            : ApiMemberSectionDescriptors.CreatePipeline();

    public static bool UsesDetailPipeline(ApiOptions options)
        => options is MemberOptions { OverloadIndex: not null }
           || options is MemberOptions { MemberDigest: not null };

    public static bool UsesOverloadInventoryPipeline(ApiOptions options)
        => options is MemberOptions
           {
              OverloadIndex: null,
              MemberDigest: null,
              MemberFilter.Count: > 0
           };
}

/// <summary>
/// Section descriptors for member-name-scoped overload inventories.
/// </summary>
public static class ApiMemberOverloadSectionDescriptors
{
    public static SectionPipeline<ApiType> CreatePipeline()
    {
        return new SectionPipeline<ApiType>()
            .Add<ApiMemberSectionDescriptors.Values>()
            .Add<ApiMemberSectionDescriptors.TypeParameters>()
            .Add<ApiMemberSectionDescriptors.TypeInterfaces>()
            .Add<ApiMemberSectionDescriptors.Baseclass>()
            .Add<ApiMemberSectionDescriptors.Constructors>()
            .Add<ApiMemberSectionDescriptors.Fields>()
            .Add<ApiMemberSectionDescriptors.Properties>()
            .Add<ApiMemberDetailSectionDescriptors.Signature>()
            .Add<Methods>()
            .Add<ApiMemberSectionDescriptors.MemberIndex>()
            .Add<ApiMemberSectionDescriptors.SourceLocations>()
            .Add<ApiMemberSectionDescriptors.Operators>()
            .Add<ApiMemberSectionDescriptors.ExplicitInterfaceImplementations>()
            .Add<ApiMemberSectionDescriptors.ExtensionMethods>()
            .Add<ApiMemberSectionDescriptors.Events>()
            .Add<ApiMemberSectionDescriptors.MethodAttributes>()
            .Add<ApiMemberSectionDescriptors.DecompiledSource>()
            .Add<ApiMemberDetailSectionDescriptors.AnnotatedSource>()
            .Add<ApiMemberSectionDescriptors.OriginalSource>()
            .Add<ApiMemberDetailSectionDescriptors.Calls>()
            .Add<ApiMemberDetailSectionDescriptors.Callers>()
            .Add<ApiMemberDetailSectionDescriptors.CallGraph>()
            .Add<ApiMemberDetailSectionDescriptors.CallerGraph>()
            .Add<ApiMemberDetailSectionDescriptors.UnsafeOperations>()
            .Add<ApiMemberSectionDescriptors.ILBody>()
            .Add<ApiMemberSectionDescriptors.Facts>();
    }

    public sealed class Methods : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.Methods;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "method");
    }
}

/// <summary>
/// Section descriptors for a single selected member overload.
/// </summary>
public static class ApiMemberDetailSectionDescriptors
{
    public static SectionPipeline<ApiType> CreatePipeline()
    {
        return new SectionPipeline<ApiType>()
            .Add<Summary>()
            .Add<Signature>()
            .Add<MethodAttributes>()
            .Add<DecompiledSource>()
            .Add<AnnotatedSource>()
            .Add<OriginalSource>()
            .Add<SourceLocations>()
            .Add<Calls>()
            .Add<Callers>()
            .Add<CallGraph>()
            .Add<CallerGraph>()
            .Add<UnsafeOperations>()
            .Add<Facts>()
            .Add<ILBody>()
            .AddCategory(SectionCategoryNames.Source,
                SectionNames.DecompiledSource,
                SectionNames.AnnotatedSource,
                SectionNames.OriginalSource,
                SectionNames.IL);
    }

    public sealed class Summary : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.Summary;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1;
    }

    public sealed class Signature : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.Signature;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1;
    }

    public sealed class MethodAttributes : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.CustomAttributes;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    public sealed class DecompiledSource : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.DecompiledSource;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    public sealed class AnnotatedSource : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.AnnotatedSource;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    public sealed class OriginalSource : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.OriginalSource;
        public static bool IsExpensive => true;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayFetchSources;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    public sealed class SourceLocations : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.SourceLocations;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1
               && model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    public sealed class ILBody : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.IL;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    public sealed class Calls : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.Calls;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1
               && model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    public sealed class Callers : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.Callers;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1
               && model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    public sealed class CallGraph : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.CallGraph;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1
               && model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }
 
    public sealed class CallerGraph : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.CallerGraph;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1
               && model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }
 
    public sealed class UnsafeOperations : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.UnsafeOperations;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1
               && model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    /// <summary>
    /// The structured hidden-fact table for a single method — the agent-facing
    /// dual of the inline Decompiled Source view. <c>ExplicitOnly</c>: never
    /// auto-rendered (the Decompiled Source view already shows the same facts
    /// inline for humans), requested via <c>-S "Facts"</c>/<c>--json</c>/<c>--tsv</c>.
    /// </summary>
    public sealed class Facts : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.Facts;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1
               && model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

}
