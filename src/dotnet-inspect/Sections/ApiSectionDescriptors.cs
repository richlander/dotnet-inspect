using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public sealed record ApiTypeSectionCatalog(
    SectionPipeline<ApiSurface> Pipeline,
    InspectionQueryRegistry<ApiSurfaceQueryContext> QueryRegistry);

/// <summary>
/// Section descriptors for the api command type-list view (all types in an assembly).
/// Sections correspond to type-kind groupings: Classes, Structs, Interfaces, Enums, Delegates.
/// </summary>
public static class ApiTypeSectionDescriptors
{
    public static ApiTypeSectionCatalog CreateCatalog()
    {
        var queryRegistry = CreateQueryRegistry();
        return new ApiTypeSectionCatalog(
            CreatePipeline(queryRegistry.CostOf),
            queryRegistry);
    }

    internal static InspectionQueryRegistry<ApiSurfaceQueryContext> CreateQueryRegistry()
        => new InspectionQueryRegistry<ApiSurfaceQueryContext>()
            .Add(ApiSurfaceQuery.Definition, ApiSurfaceQuery.Execute);

    /// <summary>Builds the section pipeline for the type-list view.</summary>
    public static SectionPipeline<ApiSurface> CreatePipeline()
        => CreatePipeline(CreateQueryRegistry().CostOf);

    private static SectionPipeline<ApiSurface> CreatePipeline(
        Func<InspectionQueryDefinition, InspectionCost> queryCost)
    {
        return new SectionPipeline<ApiSurface>()
            .UseCuratedCatalog()
            .UseQueryCosts(queryCost)
            .WithoutComputedPoles()
            .Add<ApiInfo>(ApiSurfaceQuery.Definition)
            .Add<Classes>(ApiSurfaceQuery.Definition)
            .Add<Structs>(ApiSurfaceQuery.Definition)
            .Add<Interfaces>(ApiSurfaceQuery.Definition)
            .Add<Enums>(ApiSurfaceQuery.Definition)
            .Add<Delegates>(ApiSurfaceQuery.Definition)
            .SetInfoSections(SectionNames.ApiInfo)
            .AddBaseCategory(SectionCategoryNames.Surface,
                SectionNames.ApiInfo,
                SectionNames.Classes,
                SectionNames.Structs,
                SectionNames.Interfaces,
                SectionNames.Enums,
                SectionNames.Delegates);
    }

    /// <summary>
    /// API surface identity fact table.
    /// </summary>
    /// <remarks>
    /// The only section on this pipeline whose size does not grow with the assembly or the match,
    /// which is why it declares <see cref="SectionSizeClass.Fixed"/>. Every other section here
    /// enumerates matched types, so all of them scale with the target. <c>CanRender</c> is
    /// unconditional because the view always populates the section for this pipeline. The curated
    /// catalog promotes it to the minimal preset; quiet output retains the compact identity line.
    /// </remarks>
    public sealed class ApiInfo : ISectionDescriptor<ApiSurface>
    {
        public static string Name => SectionNames.ApiInfo;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model) => true;
    }

    public sealed class Classes : ISectionDescriptor<ApiSurface>
    {
        public static string Name => SectionNames.Classes;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "class");
    }

    public sealed class Structs : ISectionDescriptor<ApiSurface>
    {
        public static string Name => SectionNames.Structs;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "struct");
    }

    public sealed class Interfaces : ISectionDescriptor<ApiSurface>
    {
        public static string Name => SectionNames.Interfaces;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "interface");
    }

    public sealed class Enums : ISectionDescriptor<ApiSurface>
    {
        public static string Name => SectionNames.Enums;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiSurface model)
            => model.Types.Any(t => t.Kind == "enum");
    }

    public sealed class Delegates : ISectionDescriptor<ApiSurface>
    {
        public static string Name => SectionNames.Delegates;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
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
        => CreatePipeline(queryCost: null);

    internal static SectionPipeline<ApiType> CreateTypePipeline(
        Func<InspectionQueryDefinition, InspectionCost> queryCost)
        => CreatePipeline(queryCost);

    private static SectionPipeline<ApiType> CreatePipeline(
        Func<InspectionQueryDefinition, InspectionCost>? queryCost)
    {
        var pipeline = new SectionPipeline<ApiType>();
        bool queryBacked = queryCost is not null;
        if (queryCost is not null)
        {
            pipeline
                .UseCuratedCatalog()
                .UseQueryCosts(queryCost)
                .WithoutComputedPoles();
        }

        Add<TypeInfo>(pipeline, queryBacked);
        Add<Values>(pipeline, queryBacked);
        Add<TypeParameters>(pipeline, queryBacked);
        Add<TypeInterfaces>(pipeline, queryBacked);
        Add<Baseclass>(pipeline, queryBacked);
        Add<Constructors>(pipeline, queryBacked);
        Add<Finalizer>(pipeline, queryBacked);
        Add<Fields>(pipeline, queryBacked);
        Add<Properties>(pipeline, queryBacked);
        Add<MethodGroups>(pipeline, queryBacked);
        Add<Methods>(pipeline, queryBacked, HasMethods);
        Add<MemberIndex>(pipeline, queryBacked);
        Add<Operators>(pipeline, queryBacked, HasOperators);
        Add<ExplicitInterfaceImplementations>(
            pipeline,
            queryBacked,
            HasExplicitInterfaceImplementations);
        Add<ExtensionMethods>(pipeline, queryBacked, HasExtensionMethods);
        Add<Events>(pipeline, queryBacked);
        Add<MethodAttributes>(pipeline, queryBacked);
        Add<UnsafeMembers>(pipeline, queryBacked);
        Add<ExceptionRegions>(pipeline, queryBacked);
        Add<CalledTypes>(pipeline, queryBacked);
        Add<AllocationFacts>(pipeline, queryBacked);
        Add<SafetyFacts>(pipeline, queryBacked);
        Add<CostFacts>(pipeline, queryBacked);
        Add<TopLeverage>(pipeline, queryBacked);
        Add<OptimizationOpportunities>(pipeline, queryBacked);
        Add<SourceFiles>(pipeline, queryBacked);
        Add<DecompiledSource>(pipeline, queryBacked);
        Add<OriginalSource>(pipeline, queryBacked);
        Add<ApiMemberDetailSectionDescriptors.SourceDiff>(pipeline, queryBacked);
        Add<ILBody>(pipeline, queryBacked);
        Add<Facts>(pipeline, queryBacked);

        if (!queryBacked)
            return pipeline.AddCategory(SectionCategoryNames.Audit, SectionNames.UnsafeMembers);

        return pipeline
            .SetInfoSections(SectionNames.TypeInfo)
            .SetSectionSizes(SectionSizeClass.Fixed,
                SectionNames.Baseclass,
                SectionNames.Finalizer)
            .SetSectionSizes(SectionSizeClass.Informative,
                SectionNames.TypeParameters,
                SectionNames.TypeInterfaces)
            .SetSectionSizes(SectionSizeClass.Verbose,
                SectionNames.Values,
                SectionNames.Constructors,
                SectionNames.Fields,
                SectionNames.Properties,
                SectionNames.MethodGroups,
                SectionNames.Methods,
                SectionNames.MemberIndex,
                SectionNames.Operators,
                SectionNames.ExplicitInterfaceImplementations,
                SectionNames.ExtensionMethods,
                SectionNames.Events,
                SectionNames.CustomAttributes,
                SectionNames.UnsafeMembers,
                SectionNames.ExceptionRegions,
                SectionNames.CalledTypes,
                SectionNames.AllocationFacts,
                SectionNames.SafetyFacts,
                SectionNames.CostFacts,
                SectionNames.TopLeverage,
                SectionNames.PerformanceTriage,
                SectionNames.SourceFiles,
                SectionNames.DecompiledSource,
                SectionNames.OriginalSource,
                SectionNames.SourceDiff,
                SectionNames.IL,
                SectionNames.Facts)
            .SetSectionCosts(SectionCost.Moderated,
                SectionNames.SourceFiles)
            .SetSectionCosts(SectionCost.Unbounded,
                SectionNames.UnsafeMembers,
                SectionNames.ExceptionRegions,
                SectionNames.CalledTypes,
                SectionNames.AllocationFacts,
                SectionNames.SafetyFacts,
                SectionNames.CostFacts,
                SectionNames.TopLeverage,
                SectionNames.PerformanceTriage,
                SectionNames.DecompiledSource,
                SectionNames.OriginalSource,
                SectionNames.SourceDiff,
                SectionNames.IL,
                SectionNames.Facts)
            .AddBaseCategory(SectionCategoryNames.Surface,
                SectionNames.TypeInfo,
                SectionNames.Values,
                SectionNames.TypeParameters,
                SectionNames.TypeInterfaces,
                SectionNames.Baseclass,
                SectionNames.Constructors,
                SectionNames.Finalizer,
                SectionNames.Fields,
                SectionNames.Properties,
                SectionNames.MethodGroups,
                SectionNames.Methods,
                SectionNames.MemberIndex,
                SectionNames.Operators,
                SectionNames.ExplicitInterfaceImplementations,
                SectionNames.ExtensionMethods,
                SectionNames.Events,
                SectionNames.CustomAttributes)
            .AddCategory(SectionCategoryNames.Analysis,
                SectionNames.UnsafeMembers,
                SectionNames.ExceptionRegions,
                SectionNames.CalledTypes,
                SectionNames.AllocationFacts,
                SectionNames.SafetyFacts,
                SectionNames.CostFacts,
                SectionNames.TopLeverage,
                SectionNames.PerformanceTriage,
                SectionNames.IL,
                SectionNames.Facts)
            .AddCategory(SectionCategoryNames.Audit,
                SectionNames.UnsafeMembers,
                SectionNames.SafetyFacts)
            .AddCategory(SectionCategoryNames.Performance,
                SectionNames.AllocationFacts,
                SectionNames.CostFacts,
                SectionNames.TopLeverage,
                SectionNames.PerformanceTriage)
            .AddCategory(SectionCategoryNames.Source,
                SectionNames.DecompiledSource,
                SectionNames.OriginalSource,
                SectionNames.SourceDiff,
                SectionNames.IL)
            .AddCategory(SectionCategoryNames.SourceLink,
                SectionNames.SourceFiles);
    }

    private static void Add<TDescriptor>(
        SectionPipeline<ApiType> pipeline,
        bool queryBacked,
        Func<ApiType, bool>? isApplicable = null)
        where TDescriptor : ISectionDescriptor<ApiType>
    {
        if (queryBacked)
            pipeline.Add<TDescriptor>(ApiSurfaceQuery.Definition, isApplicable);
        else
            pipeline.Add<TDescriptor>(isApplicable);
    }

    // ===== Declarative sections (rendered via Markout [MarkoutSection]) =====

    /// <summary>
    /// Type identity fact table.
    /// </summary>
    /// <remarks>
    /// Declares <see cref="SectionSizeClass.Fixed"/> because its identity row does not grow with the
    /// type under inspection. <c>CanRender</c> is unconditional because the view always populates
    /// the section for this pipeline; the member-detail and overload-inventory views use different
    /// pipelines that do not register it. The curated type catalog promotes only this section to
    /// the focused minimal preset, gated by
    /// <c>Type_InfoPreset_IsExactlyTypeInfo</c>; quiet output retains the compact identity line.
    /// </remarks>
    public sealed class TypeInfo : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.TypeInfo;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model) => true;
    }

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
        => options is TypeOptions
            ? ApiMemberSectionDescriptors.CreateTypePipeline(
                ApiTypeSectionDescriptors.CreateQueryRegistry().CostOf)
            : CreateLegacy(options);

    internal static SectionPipeline<ApiType> Create(
        ApiOptions options,
        Func<InspectionQueryDefinition, InspectionCost>? queryCost)
        => options is TypeOptions
            ? ApiMemberSectionDescriptors.CreateTypePipeline(
                queryCost ?? ApiTypeSectionDescriptors.CreateQueryRegistry().CostOf)
            : CreateLegacy(options);

    private static SectionPipeline<ApiType> CreateLegacy(ApiOptions options)
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
            .Add<ApiMemberSectionDescriptors.OriginalSource>(HasSingleBodyBackedMember)
            .Add<ApiMemberDetailSectionDescriptors.SourceDiff>(HasSingleBodyBackedMember)
            .Add<ApiMemberDetailSectionDescriptors.Calls>()
            .Add<ApiMemberDetailSectionDescriptors.ExceptionRegions>()
            .Add<ApiMemberSectionDescriptors.AllocationFacts>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.SafetyFacts>(HasSingleBodyBackedMember)
            .Add<ApiMemberSectionDescriptors.CostFacts>(HasSingleBodyBackedMember)
            .Add<ApiMemberDetailSectionDescriptors.Callers>()
            .Add<ApiMemberDetailSectionDescriptors.CallGraph>()
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
        // A property/event resolves through the accessor the selected ordinal addresses, whose
        // PDB sequence points carry the authored source, so it renders like a method (#3278).
        public static bool CanRender(ApiType model)
            => model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
    }

    public sealed class SourceDiff : ISectionDescriptor<ApiType>
    {
        public static string Name => SectionNames.SourceDiff;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayFetchSources;
        public static string? ScannerKey => null;
        public static bool CanRender(ApiType model)
            => model.Members.Count == 1
               && model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
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
               && model.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked);
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
