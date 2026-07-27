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
            .Add<Finalizer>()
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
            .Add<ExceptionRegions>()
            .Add<CalledTypes>()
            .Add<AllocationFacts>()
            .Add<SafetyFacts>()
            .Add<CostFacts>()
            .Add<TopLeverage>()
            .Add<OptimizationOpportunities>()
            .Add<SourceFiles>()
            .Add<DecompiledSource>()
            .Add<OriginalSource>()
            .Add<ApiMemberDetailSectionDescriptors.SourceDiff>()
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

    public sealed class Finalizer : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.Finalizer;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(m => m.Kind == "finalizer");
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

    public sealed class CostOverlay : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.CostOverlay;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsMethodLike);
    }

    public sealed class SemanticsOverlay : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.SemanticsOverlay;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
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

    public sealed class ExceptionRegions : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.ExceptionRegions;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsMethodLike);
    }

    public sealed class CalledTypes : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.CalledTypes;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsMethodLike);
    }

    public sealed class AllocationFacts : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.AllocationFacts;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model) => model.Members.Any(IsBodyBacked);
    }

    public sealed class SafetyFacts : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.SafetyFacts;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model) => model.Members.Any(IsBodyBacked);
    }

    public sealed class CostFacts : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.CostFacts;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model) => model.Members.Any(IsBodyBacked);
    }

    public sealed class TopLeverage : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.TopLeverage;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        // Backed by the whole-assembly body index; list structurally during -D rather
        // than opening the index to probe, mirroring OptimizationOpportunities.
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsBodyBacked);
    }

    public sealed class OptimizationOpportunities : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.PerformanceTriage;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        // Backed by the whole-assembly body index; list structurally during -D rather
        // than opening the index to probe, mirroring SourceLocations/UnsafeOperations.
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(IsBodyBacked);
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
            => model.Members.Any(IsMethodLike)
               || !string.IsNullOrWhiteSpace(model.SourceUrl)
               || model.AdditionalSourceFiles.Count > 0;
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
            => model.Members.Count == 1 && model.Members.Any(IsBodyBacked);
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
        member.Kind is "method" or "constructor" or "finalizer" or "operator" or "explicit-interface-implementation" or "extension-method";

    /// <summary>
    /// True when the member carries executable IL that a body section can analyze.
    /// A method-like member is its own body; a property/event (including an indexer)
    /// has no body of its own but is backed by accessor methods (get/set, add/remove)
    /// whose tokens are recorded on the member. Body sections resolve such a member to
    /// its accessor method(s) (issue #3265). Fields carry no accessor token and stay
    /// body-less.
    /// </summary>
    internal static bool IsBodyBacked(ApiMember member) =>
        IsMethodLike(member) || HasAccessorTokens(member);

    /// <summary>
    /// True when a property/event member records at least one accessor method token
    /// (get/set/init for a property or indexer, add/remove for an event).
    /// </summary>
    internal static bool HasAccessorTokens(ApiMember member) =>
        member.Kind is "property" or "event"
        && (member.GetterToken is not null
            || member.SetterToken is not null
            || member.AdderToken is not null
            || member.RemoverToken is not null);

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
            .Add<ApiMemberSectionDescriptors.Finalizer>()
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
            .Add<ApiMemberSectionDescriptors.MethodAttributes>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.DecompiledSource>(HasSingleBodyBackedMember)
            .Add<ApiMemberDetailSectionDescriptors.FidelityCauses>(HasSingleBodyBackedMember)
            .Add<ApiMemberDetailSectionDescriptors.AppliedTaste>(HasSingleBodyBackedMember)
            .Add<ApiMemberDetailSectionDescriptors.AnnotatedSource>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.OriginalSource>(HasSingleMethodLikeMember)
            .Add<ApiMemberDetailSectionDescriptors.SourceDiff>(HasSingleMethodLikeMember)
            .Add<ApiMemberDetailSectionDescriptors.Calls>()
            .Add<ApiMemberDetailSectionDescriptors.ExceptionRegions>()
            .Add<ApiMemberSectionDescriptors.AllocationFacts>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.SafetyFacts>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.CostFacts>(HasSingleBodyBackedMember)
            .Add<ApiMemberDetailSectionDescriptors.Callers>()
            .Add<ApiMemberDetailSectionDescriptors.CallGraph>()
            .Add<ApiMemberDetailSectionDescriptors.CallerGraph>()
            .Add<ApiMemberDetailSectionDescriptors.UnsafeOperations>()
            .Add<ApiMemberSectionDescriptors.TopLeverage>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.OptimizationOpportunities>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.CostOverlay>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.SemanticsOverlay>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.ILBody>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.Facts>()
            .AddCategory(SectionCategoryNames.Source,
                SectionNames.DecompiledSource,
                SectionNames.AnnotatedSource,
                SectionNames.OriginalSource,
                SectionNames.SourceDiff,
                SectionNames.IL);
    }

    private static bool HasSingleBodyBackedMember(ApiType model)
        => model.Members.Count == 1 && model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);

    // SourceLink source-file sections (Original Source, Source Diff) resolve by the
    // selected member's own name/token; accessor source mapping is not yet wired, so
    // they stay method-only rather than rendering empty for a property/event (#3265).
    private static bool HasSingleMethodLikeMember(ApiType model)
        => model.Members.Count == 1 && model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);

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
            .Add<FidelityCauses>()
            .Add<AppliedTaste>()
            .Add<AnnotatedSource>()
            .Add<CostOverlay>()
            .Add<SemanticsOverlay>()
            .Add<OriginalSource>()
            .Add<SourceDiff>()
            .Add<SourceLocations>()
            .Add<Calls>()
            .Add<ExceptionRegions>()
            .Add<ApiMemberSectionDescriptors.AllocationFacts>()
            .Add<ApiMemberSectionDescriptors.SafetyFacts>()
            .Add<ApiMemberSectionDescriptors.CostFacts>()
            .Add<Callers>()
            .Add<CallGraph>()
            .Add<CallerGraph>()
            .Add<UnsafeOperations>()
            .Add<ApiMemberSectionDescriptors.TopLeverage>()
            .Add<ApiMemberSectionDescriptors.OptimizationOpportunities>()
            .Add<Facts>()
            .Add<ILBody>()
            .AddCategory(SectionCategoryNames.Source,
                SectionNames.DecompiledSource,
                SectionNames.AnnotatedSource,
                SectionNames.OriginalSource,
                SectionNames.SourceDiff,
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
            => model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
    }

    public sealed class DecompiledSource : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.DecompiledSource;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
    }

    public sealed class AnnotatedSource : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.AnnotatedSource;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
    }

    public sealed class FidelityCauses : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.FidelityCauses;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
    }

    public sealed class AppliedTaste : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.AppliedTaste;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
    }

    public sealed class CostOverlay : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.CostOverlay;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
    }

    public sealed class SemanticsOverlay : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.SemanticsOverlay;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
    }

    public sealed class OriginalSource : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.OriginalSource;
        public static bool IsExpensive => true;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayFetchSources;
        public static string? ScannerKey => null;
        // SourceLink source-file sections resolve PDB sequence points by the selected
        // member's own name/token; accessor source-line mapping is not yet wired, so a
        // property/event stays method-only here rather than rendering empty (issue #3265).
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    public sealed class SourceDiff : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.SourceDiff;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayFetchSources;
        public static string? ScannerKey => null;
        // SourceLink source-file section; accessor source mapping not yet wired (issue #3265).
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1
               && model.Members.Any(ApiMemberSectionDescriptors.IsMethodLike);
    }

    public sealed class SourceLocations : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.SourceLocations;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        // SourceLink source-file section; accessor source mapping not yet wired (issue #3265).
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
            => model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
    }

    public sealed class ExceptionRegions : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.ExceptionRegions;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1 && model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
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
               && model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
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
               && model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
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
               && model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
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
               && model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
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
               && model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
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
               && model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
    }

}
