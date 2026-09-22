using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using CSharpText;

namespace ILInspector.Metadata;

/// <summary>How much of an assembly's API surface one extraction projects.</summary>
public enum ApiSurfaceExtractionScope
{
    /// <summary>
    /// The default consumer surface: public types with their public members, minus the types and
    /// members the extractor hides.
    /// </summary>
    Public,

    /// <summary>Every type and member the extractor reaches, including non-public and hidden ones.</summary>
    IncludeAll,

    /// <summary>
    /// The default consumer surface plus non-public types, each carrying its complete member
    /// list. A public type keeps its public member list, and a public type the extractor hides
    /// stays hidden rather than re-entering with an include-all member list.
    /// </summary>
    PublicWithNonPublicTypes,
}

/// <summary>Which retention bound stopped a bounded API-surface extraction.</summary>
public enum ApiSurfaceExtractionBound
{
    /// <summary>The extraction would have retained more types than the caller allows.</summary>
    Types,

    /// <summary>The extraction would have retained more members than the caller allows.</summary>
    Members,

    /// <summary>The extraction would have retained more inspection failures than allowed.</summary>
    InspectionFailures,

    /// <summary>The extraction would have retained more type forwarders than allowed.</summary>
    TypeForwarders,

    /// <summary>The image contains more metadata rows than the caller allows the walk to inspect.</summary>
    MetadataRows,

    /// <summary>The extraction would have retained more text than the caller allows.</summary>
    RetainedTextCharacters,
}

/// <summary>
/// The hard retention bounds one bounded API-surface extraction runs under.
/// </summary>
/// <remarks>
/// A bound is enforced <em>before</em> the row that would exceed it is retained, so a caller with
/// a fixed output budget never materializes a surface larger than the budget it declared. Zero is
/// a legal bound: it means "this extraction has no remaining budget", which is exactly what a
/// caller spending one shared budget across several images has left when it is full.
/// Retained text is the sum of the character lengths in every string-bearing model field. Two
/// fields that reference the same string are charged separately because both fields survive into
/// the projected object graph and serialized shape. The extractor also observes text
/// incrementally while decoding nested signatures, attributes, generic constraints, and
/// interfaces, so concentrating the same output inside one member or type cannot defer the check
/// until after that complete model has been allocated.
/// </remarks>
public sealed record ApiSurfaceExtractionBounds
{
    public ApiSurfaceExtractionBounds(
        int maxTypes,
        int maxMembers,
        int maxInspectionFailures,
        int maxTypeForwarders,
        int maxMetadataRows)
        : this(
            maxTypes,
            maxMembers,
            maxInspectionFailures,
            maxTypeForwarders,
            maxMetadataRows,
            int.MaxValue)
    {
    }

    public ApiSurfaceExtractionBounds(
        int maxTypes,
        int maxMembers,
        int maxInspectionFailures,
        int maxTypeForwarders,
        int maxMetadataRows,
        int maxRetainedTextCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxTypes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMembers);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInspectionFailures);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTypeForwarders);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMetadataRows);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedTextCharacters);
        MaxTypes = maxTypes;
        MaxMembers = maxMembers;
        MaxInspectionFailures = maxInspectionFailures;
        MaxTypeForwarders = maxTypeForwarders;
        MaxMetadataRows = maxMetadataRows;
        MaxRetainedTextCharacters = maxRetainedTextCharacters;
    }

    /// <summary>The most types the extraction may retain.</summary>
    public int MaxTypes { get; }

    /// <summary>The most members the extraction may retain across every retained type.</summary>
    public int MaxMembers { get; }

    /// <summary>The most rejected metadata rows the extraction may retain as failures.</summary>
    public int MaxInspectionFailures { get; }

    /// <summary>The most type forwarders the extraction may retain.</summary>
    public int MaxTypeForwarders { get; }

    /// <summary>The most metadata rows the extraction may inspect.</summary>
    public int MaxMetadataRows { get; }

    /// <summary>The most text characters the extraction may retain across its model fields.</summary>
    public int MaxRetainedTextCharacters { get; }
}

/// <summary>The outcome of one bounded API-surface extraction.</summary>
/// <remarks>
/// The extraction is whole or absent. There is no partial case: an image that does not fit the
/// declared bounds is reported as <see cref="Exceeded"/> and its partially built surface is
/// discarded, so no consumer can mistake a shortened type or member list for the image's surface.
/// </remarks>
public abstract record ApiSurfaceExtractionResult
{
    private protected ApiSurfaceExtractionResult()
    {
    }

    /// <summary>The image's whole surface fit the declared bounds.</summary>
    public sealed record Extracted(
        ApiSurface Surface,
        int MetadataRows,
        int RetainedTextCharacters)
        : ApiSurfaceExtractionResult;

    /// <summary>
    /// The extraction was abandoned before retaining the row that would have exceeded
    /// <see cref="Bound"/>. Nothing is returned for this image.
    /// </summary>
    public sealed record Exceeded(ApiSurfaceExtractionBound Bound) : ApiSurfaceExtractionResult;
}

/// <summary>Type-kind facets of the compact public API inventory.</summary>
public enum ApiTypeInventoryKind
{
    Class,
    Struct,
    Interface,
    Enum,
    Delegate,
}

/// <summary>Exact cardinality of the compact public Type inventory, grouped by Type kind.</summary>
public sealed record ApiTypeInventoryCount(
    Guid ModuleVersionId,
    int Classes,
    int Structs,
    int Interfaces,
    int Enums,
    int Delegates)
{
    public int Total =>
        checked(Classes + Structs + Interfaces + Enums + Delegates);

    public int Count(ApiTypeInventoryKind kind) =>
        kind switch
        {
            ApiTypeInventoryKind.Class => Classes,
            ApiTypeInventoryKind.Struct => Structs,
            ApiTypeInventoryKind.Interface => Interfaces,
            ApiTypeInventoryKind.Enum => Enums,
            ApiTypeInventoryKind.Delegate => Delegates,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown API Type kind."),
        };
}

/// <summary>Why compact Type-inventory Count could not accept an image.</summary>
public enum ApiTypeInventoryCountDeclineReason
{
    TypeForwarders,
    MalformedExportedType,
    MalformedTypeIdentity,
    MalformedTypeRow,
}

/// <summary>
/// Outcome of requesting compact public Type-inventory cardinality directly from metadata.
/// </summary>
public abstract record ApiTypeInventoryCountResult
{
    private protected ApiTypeInventoryCountResult()
    {
    }

    /// <summary>The exact count completed without materializing Type or member rows.</summary>
    public sealed record Counted(ApiTypeInventoryCount Count)
        : ApiTypeInventoryCountResult;

    /// <summary>
    /// The image requires evidence outside the compact metadata Count capability.
    /// </summary>
    public sealed record Declined(
        ApiTypeInventoryCountDeclineReason Reason,
        string Detail)
        : ApiTypeInventoryCountResult;
}

/// <summary>
/// Extracts public API surface from assemblies.
/// </summary>
public static partial class ApiSurfaceExtractor
{
    private const string OptionalAttributeName = "System.Runtime.InteropServices.Optional";
    private const string DateTimeConstantAttributeName = "System.Runtime.CompilerServices.DateTimeConstant";
    private const byte ReservedSignatureFlag = 0x80;
    private const MethodAttributes PropertyAccessorDeclarationModifierMask =
        MethodAttributes.Static
        | MethodAttributes.Virtual
        | MethodAttributes.Abstract
        | MethodAttributes.NewSlot
        | MethodAttributes.Final;
    private const MethodAttributes RepresentablePropertyAccessorAttributeMask =
        MethodAttributes.MemberAccessMask
        | PropertyAccessorDeclarationModifierMask
        | MethodAttributes.HideBySig
        | MethodAttributes.SpecialName;
    private const MethodAttributes RequiredPropertyAccessorAttributes =
        MethodAttributes.HideBySig
        | MethodAttributes.SpecialName;
    private static readonly ConditionalWeakTable<
        MetadataReader,
        PrimitiveDefinitionClassification>
        PrimitiveDefinitionClassifications = new();

    /// <summary>
    /// Extracts the public type identities and member-kind counts needed by the compact platform
    /// API view without decoding signatures or materializing rich member models.
    /// </summary>
    public static ApiSurface ExtractSummary(PEReader peReader)
    {
        var surface = new ApiSurface();
        var reader = MetadataFormatAdmission.GetMetadataReader(peReader);
        ApiAssemblyIdentity? currentAssemblyIdentity = reader.IsAssembly
            ? ApiAssemblyIdentity.FromDefinition(reader)
            : null;
        surface.AssemblyIdentity = currentAssemblyIdentity;
        var extensionReceiverDefinitions =
            new Dictionary<ApiMember, MetadataTypeDefinitionName>();

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            int publicMethodCount = surface.PublicMethodCount;
            int publicPropertyCount = surface.PublicPropertyCount;
            int publicEventCount = surface.PublicEventCount;
            int publicFieldCount = surface.PublicFieldCount;
            try
            {
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                if (!typeDef.IsPublic)
                    continue;

                var typeAttributes = typeDef.Attributes;
                string metadataName = reader.GetString(typeDef.Name);
                if (TypeFilters.IsCompilerGenerated(metadataName))
                    continue;

                if (AttributeReader.HasHiddenAttribute(reader, typeDef.GetCustomAttributes()))
                    continue;

                var (typeNamespace, typeName) = GetApiTypeNameParts(reader, typeDefHandle);
                MetadataTypeDefinitionName definitionName =
                    MetadataTypeDefinitionNameReader.Read(
                        reader,
                        typeDefHandle)
                    switch
                    {
                        MetadataTypeDefinitionNameReadResult.Read read =>
                            read.Name,
                        MetadataTypeDefinitionNameReadResult.Rejected rejected =>
                            throw new MetadataRowRejectedException(
                                "type identity",
                                rejected.Failure),
                        _ => throw new InvalidOperationException(
                            "Unknown type-definition name result.")
                    };
                var apiType = new ApiType
                {
                    Namespace = typeNamespace,
                    Name = typeName,
                    MetadataName = GetMetadataName(reader, typeDefHandle),
                    DefinitionName = definitionName,
                    IntroducedTypeParameterCounts =
                        MetadataDeclarationQuery.GetIntroducedTypeParameterCounts(
                            reader,
                            typeDefHandle),
                    Kind = TypeKindName(
                        GetSummaryTypeKind(reader, typeDef)),
                    Layout = (ApiTypeLayout)(typeAttributes & TypeAttributes.LayoutMask),
                    Members = []
                };

                bool isExtensionClass =
                    (typeAttributes & (TypeAttributes.Sealed | TypeAttributes.Abstract))
                        == (TypeAttributes.Sealed | TypeAttributes.Abstract)
                    && AttributeReader.HasExtensionAttribute(
                        reader,
                        typeDef.GetCustomAttributes());
                CountSummaryMembers(
                    reader,
                    typeDef,
                    apiType,
                    surface,
                    isExtensionClass,
                    extensionReceiverDefinitions);
                surface.Types.Add(apiType);
                surface.PublicTypeCount++;
            }
            catch (MetadataRowRejectedException ex)
            {
                surface.PublicMethodCount = publicMethodCount;
                surface.PublicPropertyCount = publicPropertyCount;
                surface.PublicEventCount = publicEventCount;
                surface.PublicFieldCount = publicFieldCount;
                AddInspectionFailure(
                    surface,
                    budget: null,
                    ex.Operation,
                    typeDefHandle,
                    ex.Failure);
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                surface.PublicMethodCount = publicMethodCount;
                surface.PublicPropertyCount = publicPropertyCount;
                surface.PublicEventCount = publicEventCount;
                surface.PublicFieldCount = publicFieldCount;
                AddInspectionFailure(
                    surface,
                    budget: null,
                    "type summary row",
                    typeDefHandle,
                    MetadataTypeNameFailure.Malformed(typeDefHandle, ex.Message));
            }
        }

        AttachLocalExtensionMethods(surface, extensionReceiverDefinitions);
        ExtractTypeForwarders(reader, surface);
        return surface;
    }

    /// <summary>
    /// Counts the compact public Type inventory without retaining Type or member rows.
    /// </summary>
    /// <remarks>
    /// Type forwarders require assembly resolution and are outside this image-local capability.
    /// A malformed row declines the capability so callers can preserve their existing
    /// evidence-bearing fallback instead of treating a partial count as exact.
    /// </remarks>
    public static ApiTypeInventoryCountResult CountSummaryTypes(
        PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(peReader);

        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        Guid moduleVersionId = reader.GetGuid(
            reader.GetModuleDefinition().Mvid);
        foreach (ExportedTypeHandle exportedTypeHandle
            in reader.ExportedTypes)
        {
            try
            {
                ExportedType exportedType =
                    reader.GetExportedType(exportedTypeHandle);
                if (exportedType.IsForwarder
                    || exportedType.Implementation.Kind
                        == HandleKind.AssemblyReference)
                {
                    return new ApiTypeInventoryCountResult.Declined(
                        ApiTypeInventoryCountDeclineReason
                            .TypeForwarders,
                        "The image contains exported Type rows that require resolution.");
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                return new ApiTypeInventoryCountResult.Declined(
                    ApiTypeInventoryCountDeclineReason
                        .MalformedExportedType,
                    $"Exported Type inspection was rejected: {ex.Message}");
            }
        }

        var surface = new ApiSurface();
        int classes = 0;
        int structs = 0;
        int interfaces = 0;
        int enums = 0;
        int delegates = 0;
        foreach (TypeDefinitionHandle typeDefHandle
            in reader.TypeDefinitions)
        {
            try
            {
                TypeDefinition typeDef =
                    reader.GetTypeDefinition(typeDefHandle);
                if (!typeDef.IsPublic)
                    continue;

                string metadataName =
                    reader.GetString(typeDef.Name);
                if (TypeFilters.IsCompilerGenerated(metadataName)
                    || AttributeReader.HasHiddenAttribute(
                        reader,
                        typeDef.GetCustomAttributes()))
                {
                    continue;
                }

                MetadataTypeDefinitionNameReadResult name =
                    MetadataTypeDefinitionNameReader.Read(
                        reader,
                        typeDefHandle);
                if (name
                    is MetadataTypeDefinitionNameReadResult.Rejected rejected)
                {
                    return new ApiTypeInventoryCountResult.Declined(
                        ApiTypeInventoryCountDeclineReason
                            .MalformedTypeIdentity,
                        $"Type identity was rejected: {rejected.Failure.Detail}");
                }

                ApiTypeInventoryKind kind =
                    GetSummaryTypeKind(reader, typeDef);
                bool isExtensionClass =
                    (typeDef.Attributes
                        & (TypeAttributes.Sealed
                            | TypeAttributes.Abstract))
                    == (TypeAttributes.Sealed
                        | TypeAttributes.Abstract)
                    && AttributeReader.HasExtensionAttribute(
                        reader,
                        typeDef.GetCustomAttributes());
                CountSummaryMembers(
                    reader,
                    typeDef,
                    apiType: null,
                    surface,
                    isExtensionClass,
                    extensionReceiverDefinitions: null);

                switch (kind)
                {
                    case ApiTypeInventoryKind.Class:
                        classes++;
                        break;
                    case ApiTypeInventoryKind.Struct:
                        structs++;
                        break;
                    case ApiTypeInventoryKind.Interface:
                        interfaces++;
                        break;
                    case ApiTypeInventoryKind.Enum:
                        enums++;
                        break;
                    case ApiTypeInventoryKind.Delegate:
                        delegates++;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unknown compact API Type kind '{kind}'.");
                }
            }
            catch (Exception ex) when (
                ex is MetadataRowRejectedException
                    or BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                return new ApiTypeInventoryCountResult.Declined(
                    ApiTypeInventoryCountDeclineReason
                        .MalformedTypeRow,
                    $"Compact Type inventory rejected row "
                    + $"0x{MetadataTokens.GetToken(typeDefHandle):X8}: "
                    + ex.Message);
            }
        }

        return new ApiTypeInventoryCountResult.Counted(
            new(
                moduleVersionId,
                classes,
                structs,
                interfaces,
                enums,
                delegates));
    }

    private static ApiTypeInventoryKind GetSummaryTypeKind(
        MetadataReader reader,
        TypeDefinition typeDef)
    {
        TypeAttributes attributes = typeDef.Attributes;
        if ((attributes & TypeAttributes.Interface) != 0)
            return ApiTypeInventoryKind.Interface;
        if (typeDef.BaseType.IsNil)
            return ApiTypeInventoryKind.Class;

        return ResolveRequiredTypeName(
            reader,
            typeDef.BaseType)
            switch
            {
                "System.Enum" => ApiTypeInventoryKind.Enum,
                "System.ValueType" => ApiTypeInventoryKind.Struct,
                "System.Delegate"
                    or "System.MulticastDelegate" =>
                        ApiTypeInventoryKind.Delegate,
                _ => ApiTypeInventoryKind.Class,
            };
    }

    private static string TypeKindName(
        ApiTypeInventoryKind kind) =>
        kind switch
        {
            ApiTypeInventoryKind.Class => "class",
            ApiTypeInventoryKind.Struct => "struct",
            ApiTypeInventoryKind.Interface => "interface",
            ApiTypeInventoryKind.Enum => "enum",
            ApiTypeInventoryKind.Delegate => "delegate",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown API Type kind."),
        };

    public static ApiSurface Extract(PEReader peReader, bool includeAll = false, bool typesOnly = false, bool includeCompilerGenerated = false)
        => Extract(
            peReader,
            includeAll
                ? ApiSurfaceExtractionScope.IncludeAll
                : ApiSurfaceExtractionScope.Public,
            typesOnly,
            includeCompilerGenerated);

    /// <summary>
    /// Extracts one API surface at an explicit scope.
    /// </summary>
    /// <remarks>
    /// <see cref="ApiSurfaceExtractionScope.PublicWithNonPublicTypes"/> is a single walk, not a
    /// composition of two: the per-type decision below is exactly "would the public surface have
    /// kept this type?", and only a type the public surface excludes for its visibility carries
    /// the include-all member rules. Composing it from two extractions materialized the same
    /// image's surface twice and discarded most of the second.
    /// </remarks>
    public static ApiSurface Extract(
        PEReader peReader,
        ApiSurfaceExtractionScope scope,
        bool typesOnly = false,
        bool includeCompilerGenerated = false)
        => Extract(
            peReader,
            scope,
            typesOnly,
            includeCompilerGenerated,
            budget: null,
            constraintResolution: null);

    /// <summary>
    /// Extracts an API surface and classifies external named generic constraints
    /// through one frozen type-resolution generation.
    /// </summary>
    /// <remarks>
    /// The first pass records requests only for generic-parameter groups that the
    /// selected surface actually materialized. The resolved pass rereads those groups
    /// while <paramref name="peReader"/> remains alive and stores only
    /// <see cref="TypeParameterTypeKind"/> on the result; no generation-scoped
    /// resolution currency escapes with the surface.
    /// </remarks>
    internal static ApiSurface Extract(
        PEReader peReader,
        ResolvedAssemblyReference source,
        TypeResolutionCatalog catalog,
        IAssemblyBindingPolicy bindingPolicy,
        bool includeAll = false,
        bool typesOnly = false,
        bool includeCompilerGenerated = false)
        => Extract(
            peReader,
            source,
            catalog,
            bindingPolicy,
            includeAll
                ? ApiSurfaceExtractionScope.IncludeAll
                : ApiSurfaceExtractionScope.Public,
            typesOnly,
            includeCompilerGenerated);

    internal static ApiSurface Extract(
        PEReader peReader,
        ResolvedAssemblyReference source,
        TypeResolutionCatalog catalog,
        IAssemblyBindingPolicy bindingPolicy,
        ApiSurfaceExtractionScope scope,
        bool typesOnly = false,
        bool includeCompilerGenerated = false)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        var constraintResolution =
            new TypeParameterConstraintResolution(
                MetadataFormatAdmission.GetMetadataReader(peReader),
                source,
                catalog.MaxTypeResolutionRequests);
        ApiSurface surface = Extract(
            peReader,
            scope,
            typesOnly,
            includeCompilerGenerated,
            budget: null,
            constraintResolution);
        CompleteConstraintResolution(
            surface, constraintResolution, source, catalog, bindingPolicy);
        return surface;
    }

    static void CompleteConstraintResolution(
        ApiSurface surface,
        TypeParameterConstraintResolution constraintResolution,
        ResolvedAssemblyReference source,
        TypeResolutionCatalog catalog,
        IAssemblyBindingPolicy bindingPolicy,
        ExtractionBudget? budget = null)
    {
        if (constraintResolution.Requests.Count > 0)
        {
            using TypeResolutionContext context =
                catalog.CreateApiSurfaceContext(
                    bindingPolicy,
                    [source],
                    constraintResolution.Requests);
            constraintResolution.Apply(context);
        }
        AddConstraintResolutionFailure(
            surface,
            constraintResolution,
            source.Identity,
            budget);
    }

    static void AddConstraintResolutionFailure(
        ApiSurface surface,
        TypeParameterConstraintResolution constraintResolution,
        AssemblyReferenceIdentity subjectAssembly,
        ExtractionBudget? budget = null)
    {
        foreach (MetadataTypeNameFailure budgetFailure
            in constraintResolution.Plan.RequestBudgetFailures)
        {
            TrackConstraintResolutionFailure(
                surface,
                budgetFailure,
                subjectAssembly,
                budget: budget);
        }

        foreach (TypeParameterKindClassifier.ResolutionPlan
            .ResolutionFailureEntry resolutionFailure
            in constraintResolution.Plan.ResolutionFailureEntries)
        {
            TrackConstraintResolutionFailure(
                surface,
                resolutionFailure.Failure,
                subjectAssembly,
                resolutionFailure.DependencyAssembly,
                budget);
        }
    }

    static void TrackConstraintResolutionFailure(
        ApiSurface surface,
        MetadataTypeNameFailure failure,
        AssemblyReferenceIdentity subjectAssembly,
        AssemblyReferenceIdentity? dependencyAssembly = null,
        ExtractionBudget? budget = null)
    {
        var projected = new ApiSurfaceInspectionFailure(
            ApiSurface.ConstraintResolutionOperation,
            failure.SubjectToken ?? 0,
            failure.Mechanism,
            failure.Kind,
            failure.Detail,
            subjectAssembly,
            dependencyAssembly);
        budget?.RetainInspectionFailure(projected);
        var subject = new ApiSurfaceInspectionSubject(
            SourceAssemblyPath: null,
            projected.SubjectToken);
        surface.AddConstraintResolutionFailure(
            subject,
            projected);
    }

    /// <summary>
    /// Extracts one API surface at an explicit scope under hard retention bounds, abandoning the
    /// image before it retains the type or member that would exceed them.
    /// </summary>
    /// <remarks>
    /// This is the bounded peer of <see cref="Extract(PEReader, ApiSurfaceExtractionScope, bool, bool)"/>,
    /// and the only way to get a hard bound: checking an unbounded extraction's totals afterwards
    /// proves nothing about what was materialized to produce them. A host with a fixed output
    /// budget — Browser/Wasm is the motivating one — spends that budget image by image and gets
    /// <see cref="ApiSurfaceExtractionResult.Exceeded"/> for the first image that does not fit,
    /// rather than a surface it must then discard. Gated by
    /// <c>ApiSurfaceExtractorBoundsTests</c>.
    /// </remarks>
    public static ApiSurfaceExtractionResult ExtractBounded(
        PEReader peReader,
        ApiSurfaceExtractionScope scope,
        ApiSurfaceExtractionBounds bounds,
        bool typesOnly = false,
        bool includeCompilerGenerated = false)
        => ExtractBoundedCore(
            peReader, scope, bounds, typesOnly, includeCompilerGenerated,
            source: null, catalog: null, bindingPolicy: null);

    internal static ApiSurfaceExtractionResult ExtractBounded(
        PEReader peReader,
        ResolvedAssemblyReference source,
        TypeResolutionCatalog catalog,
        IAssemblyBindingPolicy bindingPolicy,
        ApiSurfaceExtractionScope scope,
        ApiSurfaceExtractionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        return ExtractBoundedCore(
            peReader, scope, bounds,
            typesOnly: false, includeCompilerGenerated: false,
            source, catalog, bindingPolicy);
    }

    static ApiSurfaceExtractionResult ExtractBoundedCore(
        PEReader peReader,
        ApiSurfaceExtractionScope scope,
        ApiSurfaceExtractionBounds bounds,
        bool typesOnly,
        bool includeCompilerGenerated,
        ResolvedAssemblyReference? source,
        TypeResolutionCatalog? catalog,
        IAssemblyBindingPolicy? bindingPolicy)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        try
        {
            using var operationContext = new MetadataOperationContext(
                new MetadataOperationPolicy(bounds.MaxMetadataRows));
            var budget = new ExtractionBudget(bounds);
            TypeParameterConstraintResolution? constraintResolution =
                source is null ? null : new(
                    MetadataFormatAdmission.GetMetadataReader(peReader),
                    source,
                    catalog!.MaxTypeResolutionRequests);
            ApiSurface surface = Extract(
                peReader,
                scope,
                typesOnly,
                includeCompilerGenerated,
                budget,
                constraintResolution,
                operationContext);
            if (constraintResolution is not null)
            {
                CompleteConstraintResolution(
                    surface, constraintResolution, source!, catalog!,
                    bindingPolicy!, budget);
            }
            return new ApiSurfaceExtractionResult.Extracted(
                surface,
                (int)operationContext.Counters.MetadataRows,
                budget.RetainedTextCharacters);
        }
        catch (ExtractionBoundExceededException exceeded)
        {
            return new ApiSurfaceExtractionResult.Exceeded(exceeded.Bound);
        }
    }

    static ApiSurface Extract(
        PEReader peReader,
        ApiSurfaceExtractionScope scope,
        bool typesOnly,
        bool includeCompilerGenerated,
        ExtractionBudget? budget,
        TypeParameterConstraintResolution? constraintResolution,
        MetadataOperationContext? operationContext = null)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        var surface = new ApiSurface();
        var reader = MetadataFormatAdmission.GetMetadataReader(peReader);
        Guid moduleVersionId = reader.GetGuid(
            reader.GetModuleDefinition().Mvid);
        if (operationContext is not null)
        {
            switch (operationContext.AdmitImage(reader))
            {
                case MetadataImageAdmissionResult.Admitted:
                    break;
                case MetadataImageAdmissionResult.Rejected
                    {
                        Failure.Kind:
                            MetadataOperationFailureKind.MetadataRowsExceeded,
                    }:
                    throw new ExtractionBoundExceededException(
                        ApiSurfaceExtractionBound.MetadataRows);
                default:
                    throw new InvalidOperationException(
                        "The metadata image admission returned an unsupported outcome.");
            }
        }
        var extensionReceiverDefinitions =
            new Dictionary<ApiMember, MetadataTypeDefinitionName>();
        MemorySafetyMetadataIndex? memorySafetyIndex = null;
        MemorySafetyMetadataIndex GetMemorySafetyIndex() =>
            memorySafetyIndex ??= MemorySafetyMetadataIndex.Create(reader);
        Action<string>? observeText =
            budget is null ? null : budget.ObservePendingText;
        var materializationContext = new AttributeDecoder.MaterializationContext(
            budget is null
                ? static _ => { }
                : budget.ObservePendingDecodeWork);
        Action<int>? observeDecodeWork = budget is null
            ? null
            : materializationContext.Observe;
        Action<int> observeAttributeMaterialize = materializationContext.Observe;

        ApiAssemblyIdentity? currentAssemblyIdentity = reader.IsAssembly
            ? ApiAssemblyIdentity.FromDefinition(
                reader,
                observeDecodeWork)
            : null;
        if (currentAssemblyIdentity is not null && budget is not null)
        {
            budget.RetainCommittedText(
                currentAssemblyIdentity.Name);
            if (currentAssemblyIdentity.Culture is not null)
            {
                budget.RetainCommittedText(
                    currentAssemblyIdentity.Culture);
            }
            if (currentAssemblyIdentity.PublicKeyToken is not null)
            {
                budget.RetainCommittedText(
                    currentAssemblyIdentity.PublicKeyToken);
            }
        }
        surface.AssemblyIdentity = currentAssemblyIdentity;
        var registeredRuntimeJsExportWrapperNames =
            new Dictionary<
                (string AssemblyName, string TypeName),
                List<(
                    string MemberName,
                    int RegistrationMethodToken,
                    int RegistrationCount)>>();
        if (!typesOnly)
        {
            var registrationMethods = new List<(
                MethodDefinitionHandle Handle,
                MethodDefinition Definition)>();
            foreach (MethodDefinitionHandle methodHandle
                in reader.MethodDefinitions)
            {
                try
                {
                    MethodDefinition method =
                        reader.GetMethodDefinition(methodHandle);
                    if ((method.Attributes
                            & (MethodAttributes.MemberAccessMask
                                | MethodAttributes.Static))
                            != (MethodAttributes.Private
                                | MethodAttributes.Static)
                        || !reader.StringComparer.Equals(
                            method.Name,
                            "__Register_"))
                    {
                        continue;
                    }

                    if (method.RelativeVirtualAddress == 0
                        || !HasVoidNullaryStaticSignature(
                            reader,
                            method))
                    {
                        continue;
                    }

                    TypeDefinition type = reader.GetTypeDefinition(
                        method.GetDeclaringType());
                    if (!reader.StringComparer.Equals(
                            type.Namespace,
                            "System.Runtime.InteropServices.JavaScript")
                        || !reader.StringComparer.Equals(
                            type.Name,
                            "__GeneratedInitializer"))
                    {
                        continue;
                    }

                    registrationMethods.Add((
                        methodHandle,
                        method));
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentOutOfRangeException)
                {
                    // Registration evidence is optional and fails closed.
                }
            }

            if (registrationMethods is
                [
                    (
                        MethodDefinitionHandle registrationHandle,
                        MethodDefinition registrationMethod),
                ])
            {
                try
                {
                    IReadOnlyList<RuntimeJsExportWrapperRegistration>
                        registrations = AttributeReader
                            .ReadRuntimeJsExportWrapperRegistrations(
                                reader,
                                registrationMethod.GetCustomAttributes(),
                                observeDecodeWork);
                    int registrationCount = registrations.Count;
                    foreach (RuntimeJsExportWrapperRegistration
                        registration in registrations)
                    {
                        var key = (
                            registration.TargetAssemblyName,
                            registration.TargetTypeName);
                        if (!registeredRuntimeJsExportWrapperNames
                                .TryGetValue(
                                    key,
                                    out List<(
                                        string MemberName,
                                        int RegistrationMethodToken,
                                        int RegistrationCount)>?
                                            candidates))
                        {
                            candidates = [];
                            registeredRuntimeJsExportWrapperNames.Add(
                                key,
                                candidates);
                        }

                        candidates.Add((
                            registration.MemberName,
                            MetadataTokens.GetToken(
                                registrationHandle),
                            registrationCount));
                    }
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                    or InvalidOperationException
                    or ArgumentOutOfRangeException)
                {
                    // Registration evidence is optional and fails closed.
                    registeredRuntimeJsExportWrapperNames.Clear();
                }
            }
        }

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            MetadataTypeDefinitionName? owningTypeDefinition = null;
            TypeAttributes? owningTypeAttributes = null;
            TypeDefinitionHandle owningTypeParent = default;
            int publicMethodCount = surface.PublicMethodCount;
            int publicPropertyCount = surface.PublicPropertyCount;
            int publicEventCount = surface.PublicEventCount;
            int publicFieldCount = surface.PublicFieldCount;
            TypeParameterConstraintResolution.Checkpoint?
                constraintCheckpoint =
                    constraintResolution?.CreateCheckpoint();
            try
            {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            var attributes = typeDef.Attributes;
            owningTypeAttributes = attributes;
            owningTypeParent = typeDef.GetDeclaringType();

            budget?.BeginTypeCandidate();
            observeDecodeWork?.Invoke(
                reader.GetBlobReader(typeDef.Name).Length
                    + reader.GetBlobReader(typeDef.Namespace).Length);
            string leafMetadataName = reader.GetString(typeDef.Name);

            // Skip compiler-generated types unless explicitly requested. The opt-in
            // surfaces closure/display/state-machine types and their real fields so
            // tooling (and compile-back reconstruction) can enumerate captured state.
            if (TypeFilters.IsCompilerGenerated(leafMetadataName) && !includeCompilerGenerated)
            {
                RetainFilteredRuntimeJsExportFacts(
                    reader,
                    typeDef,
                    surface,
                    budget,
                    observeDecodeWork);
                continue;
            }

            // Only include public types by default. The filtered-export scan
            // above intentionally precedes this visibility check: an authentic
            // row on a private compiler-generated lambda type remains relevant
            // failure evidence even though the type is not an API declaration.
            if (!typeDef.IsPublic && scope == ApiSurfaceExtractionScope.Public)
                continue;

            // Whether this type's members follow the include-all rules. Every member decision
            // below reads this local, so the composed scope keeps a public type's public member
            // list while a non-public type carries its complete one.
            bool includeAll = scope == ApiSurfaceExtractionScope.IncludeAll
                || (scope == ApiSurfaceExtractionScope.PublicWithNonPublicTypes
                    && !typeDef.IsPublic);

            // Skip EditorBrowsable(Never) and Obsolete types unless --all. A public type the
            // extractor hides stays hidden in the composed scope too: it is suppressed, not
            // demoted into the non-public bucket with an include-all member list.
            if (!includeAll
                && AttributeReader.HasHiddenAttribute(
                    reader,
                    typeDef.GetCustomAttributes(),
                    observeDecodeWork))
            {
                continue;
            }

            MetadataTypeDefinitionName definitionName =
                MetadataTypeDefinitionNameReader.Read(
                    reader,
                    typeDefHandle,
                    observeDecodeWork)
                switch
                {
                    MetadataTypeDefinitionNameReadResult.Read read => read.Name,
                    MetadataTypeDefinitionNameReadResult.Rejected rejected =>
                        throw new MetadataRowRejectedException(
                            "type identity",
                            rejected.Failure),
                    _ => throw new InvalidOperationException(
                        "Unknown type-definition name result.")
                };
            int projectedNameLength = definitionName.Namespace.Length;
            foreach (string segment in definitionName.Segments)
                projectedNameLength = checked(projectedNameLength + segment.Length + 1);
            observeDecodeWork?.Invoke(checked(projectedNameLength * 2));
            string? typeNamespace = definitionName.Namespace.Length == 0
                ? null
                : definitionName.Namespace;
            string typeName = string.Join(".", definitionName.Segments);
            string flattenedMetadataName = definitionName.ToNestedMetadataName();
            var typeContext = GenericContext.ForType(
                reader,
                typeDef,
                observeDecodeWork);
            budget?.BeginType();

            owningTypeDefinition = definitionName;
            var apiType = new ApiType
            {
                Namespace = typeNamespace,
                Name = typeName,
                MetadataName = flattenedMetadataName,
                DefinitionName = definitionName,
                IntroducedTypeParameterCounts =
                    MetadataDeclarationQuery.GetIntroducedTypeParameterCounts(
                        reader,
                        typeDefHandle),
                Accessibility = MetadataDeclarationQuery.TypeAccessibility(typeDef),
                MetadataToken = MetadataTokens.GetToken(typeDefHandle),
                Layout = (ApiTypeLayout)(attributes & TypeAttributes.LayoutMask),
                LayoutDetails = typesOnly
                    ? null
                    : ApiTypeLayoutFacts.Read(reader, moduleVersionId, typeDefHandle),
                MemorySafety = typesOnly
                    ? null
                    : new ApiModuleMemorySafetyFacts(
                        moduleVersionId, GetMemorySafetyIndex().Rules),
                IsSealed = (attributes & TypeAttributes.Sealed) != 0,
                IsAbstract = (attributes & TypeAttributes.Abstract) != 0,
                HasUnionAttribute = AttributeReader.HasUnionAttribute(
                    reader,
                    typeDef.GetCustomAttributes(),
                    observeDecodeWork),
                Attributes = AttributeReader.RenderAttributes(
                    reader,
                    typeDef.GetCustomAttributes(),
                    qualifyNames: true,
                    beforeRetain: observeText,
                    beforeMaterialize: observeAttributeMaterialize),
            };

            // Determine kind
            if ((attributes & TypeAttributes.Interface) != 0)
            {
                apiType.Kind = "interface";
            }
            else if (!typeDef.BaseType.IsNil)
            {
                string baseTypeName = ResolveRequiredTypeName(
                    reader,
                    typeDef.BaseType,
                    beforeRetainText: observeText,
                    beforeDecodeWork: observeDecodeWork);
                apiType.BaseType = ApplyDynamicView(
                    reader,
                    typeDef.BaseType,
                    typeDef.GetCustomAttributes(),
                    typeContext,
                    baseTypeName,
                    observeText,
                    observeDecodeWork);
                apiType.BaseTypeReference =
                    DecodeTypeDefinitionReference(
                        reader,
                        typeDef.BaseType,
                        typeContext,
                        observeText,
                        observeDecodeWork);

                apiType.Kind = baseTypeName switch
                {
                    "System.Enum" => "enum",
                    "System.ValueType" => "struct",
                    "System.Delegate" or "System.MulticastDelegate" => "delegate",
                    _ => "class"
                };
            }
            else
            {
                apiType.Kind = "class";
            }

            apiType.IsStatic = apiType.IsSealed && apiType.IsAbstract;

            // The ref struct / readonly struct modifiers. Their [IsByRefLike] /
            // [IsReadOnly] attributes are compiler-synthesized from syntax and so
            // suppressed from the attribute list (AttributeReader.IsReEmitted), so
            // the modifier is reconstructed here from the still-present attribute.
            if (apiType.Kind == "struct")
            {
                var typeAttributes = typeDef.GetCustomAttributes();
                apiType.IsByRefLike = AttributeReader.HasAttribute(
                    reader,
                    typeAttributes,
                    KnownAttributeNames.IsByRefLikeAttribute,
                    observeDecodeWork);
                apiType.IsReadOnly = AttributeReader.HasAttribute(
                    reader,
                    typeAttributes,
                    KnownAttributeNames.IsReadOnlyAttribute,
                    observeDecodeWork);
            }

            // Capture the wire-fidelity-relevant facts for an enum's JSON serialization: whether
            // it is [Flags] (STJ serializes named combinations as comma-joined strings, while
            // undefined combinations can remain numeric) and whether it carries a
            // JsonStringEnumConverter (declared values serialize by name, while the default
            // converter can still emit undefined values numerically).
            var jsonTypeAttributes = typeDef.GetCustomAttributes();
            apiType.JsonConverterAttributeCount =
                AttributeReader.CountJsonConverterAttributes(
                    reader,
                    jsonTypeAttributes,
                    observeDecodeWork);
            apiType.HasUnsupportedJsonWireAttributes =
                AttributeReader.HasUnsupportedJsonTypeWireAttributes(
                    reader,
                    jsonTypeAttributes,
                    observeDecodeWork);
            apiType.JsonPolymorphism =
                AttributeReader.ReadJsonPolymorphism(
                    reader,
                    jsonTypeAttributes,
                    currentAssemblyIdentity,
                    observeDecodeWork);
            apiType.JsonSerializableRoots =
                AttributeReader.ReadJsonSerializableRoots(
                    reader,
                    jsonTypeAttributes,
                    currentAssemblyIdentity,
                    out int jsonSerializableAttributeCount,
                    observeDecodeWork);
            apiType.JsonSerializableAttributeCount =
                jsonSerializableAttributeCount;
            if (jsonSerializableAttributeCount > 0)
            {
                apiType.HasSystemTextJsonSourceGenerationMarker =
                    AttributeReader
                        .HasSystemTextJsonSourceGenerationMarker(
                            reader,
                            jsonTypeAttributes,
                            observeDecodeWork);
            }
            if (apiType.Kind == "enum")
            {
                FlagsAttributeEvidence flagsEvidence =
                    AttributeReader.ReadFlagsAttributes(
                        reader,
                        jsonTypeAttributes,
                        observeDecodeWork);
                apiType.IsFlagsEnum = flagsEvidence.Count > 0;
                apiType.FlagsAttributeCount = flagsEvidence.Count;
                apiType.HasMalformedFlagsAttribute =
                    flagsEvidence.HasMalformedRow;
                apiType.HasJsonStringEnumConverter =
                    AttributeReader.HasJsonStringEnumConverterAttribute(
                        reader,
                        jsonTypeAttributes,
                        definitionName,
                        currentAssemblyIdentity,
                        observeDecodeWork);
            }

            if (AttributeReader.TryGetJsonSourceGenerationWireOptions(
                    reader,
                    jsonTypeAttributes,
                    out JsonWireNamingPolicy? namingPolicy,
                    out JsonSourceGenerationMode generationMode,
                    out JsonWireIgnoreCondition defaultIgnoreCondition,
                    out bool useStringEnumConverter,
                    observeDecodeWork))
            {
                apiType.JsonPropertyNamingPolicy = namingPolicy;
                apiType.JsonSourceGenerationMode = generationMode;
                apiType.JsonDefaultIgnoreCondition =
                    defaultIgnoreCondition;
                apiType.JsonUseStringEnumConverter =
                    useStringEnumConverter;
            }

            // Check if this is an extension class (static class with [Extension] attribute)
            bool isExtensionClass = apiType.IsStatic
                && AttributeReader.HasExtensionAttribute(
                    reader,
                    typeDef.GetCustomAttributes(),
                    observeDecodeWork);

            // Nullability context for annotated signatures
            byte typeNullableContext = NullabilityReader.GetTypeNullableContext(
                reader,
                typeDefHandle,
                observeDecodeWork);

            apiType.TypeParameters = GenericParameters(
                reader,
                typeDef.GetGenericParameters(),
                typeContext,
                typeNullableContext,
                includeVariance: true,
                typeDefHandle,
                observeText,
                observeDecodeWork,
                constraintResolution);

            // Get interfaces
            var interfaces = typeDef.GetInterfaceImplementations();
            if (interfaces.Count > 0)
            {
                apiType.Interfaces = [];
                foreach (var ifaceHandle in interfaces)
                {
                    var iface = reader.GetInterfaceImplementation(ifaceHandle);
                    string ifaceName = ResolveRequiredTypeName(
                        reader,
                        iface.Interface,
                        typeContext,
                        observeText,
                        observeDecodeWork);
                    ifaceName = ApplyDynamicView(
                        reader,
                        iface.Interface,
                        iface.GetCustomAttributes(),
                        typeContext,
                        ifaceName,
                        observeText,
                        observeDecodeWork);
                    apiType.Interfaces.Add(ifaceName);
                    if (DecodeTypeDefinitionReference(
                            reader,
                            iface.Interface,
                            typeContext,
                            observeText,
                            observeDecodeWork)
                        is { } interfaceReference)
                    {
                        apiType.InterfaceReferences.Add(
                            interfaceReference);
                    }
                }
            }

            // Get members (public only, or all when includeAll)
            if (!typesOnly)
            {
            apiType.Members = [];

            var explicitImplementationBodies = GetExplicitImplementationBodies(reader, typeDef);

            // Methods whose explicit `.override` MethodImpl targets
            // `System.Object::Finalize` — i.e. genuine class finalizers, the
            // slot the C# `~Type()` destructor compiles to.
            var objectFinalizeOverrides = GetObjectFinalizeOverrides(
                reader,
                typeDef,
                observeDecodeWork);

            // Getter/setter and adder/remover bodies are represented by their
            // property or event rows. Raiser and Other semantic methods have no
            // ApiMember token slots, so they stay methods.
            var accessorMethods = GetSemanticAccessorMethods(reader, typeDef);
            MemorySafetyMetadataIndex memorySafety = GetMemorySafetyIndex();
            bool accessorAssociationsAvailable =
                memorySafety.Rules is MemorySafetyRulesResult.Available
                && memorySafety.AssociationFailure is null;
            var runtimeJsExportWrapperCandidateMethods =
                new Dictionary<string, List<int>>(
                    StringComparer.Ordinal);

            // Methods
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var methodCustomAttributes =
                    method.GetCustomAttributes();
                string methodName = DecodeString(
                    reader,
                    method.Name,
                    observeDecodeWork);
                RuntimeJsExportAttributeEvidence jsExportEvidence =
                    AttributeReader.ReadRuntimeJsExportAttributes(
                        reader,
                        methodCustomAttributes,
                        observeDecodeWork);
                if (methodName.StartsWith(
                    "__Wrapper_",
                    StringComparison.Ordinal))
                {
                    if (!runtimeJsExportWrapperCandidateMethods
                            .TryGetValue(
                                methodName,
                                out List<int>? tokens))
                    {
                        tokens = [];
                        runtimeJsExportWrapperCandidateMethods.Add(
                            methodName,
                            tokens);
                    }
                    tokens.Add(MetadataTokens.GetToken(methodHandle));
                }
                var methodAccess = method.Attributes & MethodAttributes.MemberAccessMask;
                var isExplicitInterfaceImplementation = explicitImplementationBodies.Contains(methodHandle);
                if (methodAccess != MethodAttributes.Public && !includeAll && !isExplicitInterfaceImplementation)
                {
                    RetainFilteredRuntimeJsExportFact(
                        apiType,
                        methodName,
                        methodHandle,
                        jsExportEvidence);
                    continue;
                }

                // Ordinary MethodSemantics accessors are omitted from the method
                // list. A private MethodImpl accessor is the C#/VB explicit-
                // interface shape: its property or event row is private and would
                // hide the public contract. Public MethodImpl accessors — static
                // abstract implementations, covariant overrides, VB Implements —
                // stay on that public row. ApiSurfaceEmitSetTests is the gate.
                if (accessorMethods.TryGetValue(
                        methodHandle,
                        out ApiMethodSemanticsKind methodSemantics)
                    && IsCSharpAccessor(methodSemantics)
                    && !(isExplicitInterfaceImplementation
                        && methodAccess == MethodAttributes.Private))
                {
                    RetainFilteredRuntimeJsExportFact(
                        apiType,
                        methodName,
                        methodHandle,
                        jsExportEvidence);
                    continue;
                }

                // Keep generated bodies out of ordinary API views unless explicitly requested.
                if (methodName.StartsWith("<") && !includeCompilerGenerated)
                {
                    RetainFilteredRuntimeJsExportFact(
                        apiType,
                        methodName,
                        methodHandle,
                        jsExportEvidence);
                    continue;
                }

                // Skip EditorBrowsable(Never) methods unless --all; obsolete are surfaced with marker.
                if (!includeAll
                    && !isExplicitInterfaceImplementation
                    && AttributeReader.HasEditorBrowsableNeverAttribute(
                        reader,
                        methodCustomAttributes,
                        observeDecodeWork))
                {
                    RetainFilteredRuntimeJsExportFact(
                        apiType,
                        methodName,
                        methodHandle,
                        jsExportEvidence);
                    continue;
                }

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(
                    reader,
                    methodCustomAttributes,
                    out var obsoleteMessage,
                    observeDecodeWork);

                var methodAttributes = method.Attributes;
                var (isExtensionMethod, isReadOnlyMethod) =
                    AttributeReader.ReadMethodMarkerAttributes(
                        reader,
                        methodCustomAttributes,
                        includeExtension: isExtensionClass
                            && (methodAttributes & MethodAttributes.Static) != 0,
                        observeDecodeWork);
                var signature = GetMethodSignature(
                    reader,
                    typeContext,
                    methodHandle,
                    method,
                    typeNullableContext,
                    isExtensionMethod,
                    observeText,
                    observeDecodeWork,
                    constraintResolution,
                    observeAttributeMaterialize);
                var isOperator = IsOperatorMethodName(methodName);
                var modifiers = ApiMethodModifiers.FromAttributes(
                    methodAttributes,
                    isExplicitInterfaceImplementation);

                // A class finalizer is the `object.Finalize` override the C#
                // `~Type()` destructor compiles to. It is detected by the
                // overridden slot (not by name/signature shape), which excludes
                // the false positives a shape heuristic admits: an implicit
                // generic `Finalize<T>()`, an override of an unrelated
                // base/interface `Finalize()` slot, and an explicit
                // `IFoo.Finalize()` implementation. There are two slot-anchored
                // shapes:
                //   * Roslyn (C#) emits an explicit `.override` MethodImpl
                //     targeting `System.Object::Finalize`; `objectFinalizeOverrides`
                //     carries those.
                //   * The VB.NET compiler emits `Protected Overrides Sub Finalize()`
                //     with NO MethodImpl — it reuses the inherited object.Finalize
                //     slot implicitly; `IsImplicitObjectFinalizeOverride` proves
                //     that slot roots at `System.Object` over metadata alone.
                // A finalizer is never generic, so a method that overrides
                // object.Finalize while declaring its own type parameters is still
                // rejected — rendering it `~Type()` would erase `<T>`.
                var isFinalizer = apiType.Kind == "class"
                    && method.GetGenericParameters().Count == 0
                    && (objectFinalizeOverrides.Contains(methodHandle)
                        || IsImplicitObjectFinalizeOverride(
                            reader,
                            typeDefHandle,
                            method,
                            observeDecodeWork));

                var member = new ApiMember
                {
                    Name = methodName,
                    Kind = ClassifyMethodKind(
                        methodName,
                        isFinalizer,
                        isExplicitInterfaceImplementation),
                    MethodSemantics = accessorAssociationsAvailable
                        ? accessorMethods.GetValueOrDefault(
                            methodHandle,
                            ApiMethodSemanticsKind.None)
                        : null,
                    IsStatic = modifiers.IsStatic,
                    IsVirtual = modifiers.IsVirtual,
                    IsAbstract = modifiers.IsAbstract,
                    IsOverride = modifiers.IsOverride,
                    IsSealed = modifiers.IsSealed,
                    IsFinalizer = isFinalizer,
                    IsReadOnly = isReadOnlyMethod,
                    Signature = signature.Text,
                    SignatureModel = signature.Model,
                    SignatureDecodeStatus = signature.IsDegraded
                        ? SignatureDecodeStatus.Degraded
                        : null,
                    // Conversion operators overload on return type. SignatureModel is
                    // [JsonIgnore], so persist the return type on the serialized member
                    // too, letting the canonical-signature fallback disambiguate them on a
                    // round-tripped ApiSurface (where SignatureModel is gone).
                    ReturnType = ApiMemberIdentity.IsConversionOperator(methodName) ? signature.Model?.ReturnType : null,
                    MetadataToken = MetadataTokens.GetToken(methodHandle),
                    GenericArity =
                        method.GetGenericParameters().Count,
                    HasMethodBody =
                        method.RelativeVirtualAddress != 0,
                    MethodImplementation = ApiMethodImplementationFacts.Read(
                        reader, moduleVersionId, methodHandle),
                    IsUnsafe = HasUnsafeSignature(signature.Text)
                        || AttributeReader.HasRequiresUnsafeAttribute(
                            reader,
                            methodCustomAttributes,
                            observeDecodeWork),
                    MemorySafety = ApiMemorySafetyFacts.Read(
                        reader, GetMemorySafetyIndex(), moduleVersionId, methodHandle),
                    Accessibility = isExplicitInterfaceImplementation && !isOperator ? null : GetAccessibility(methodAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage,
                    HasRuntimeJsExport =
                        jsExportEvidence.HasValidRow,
                    RuntimeJsExportAttributeCount =
                        jsExportEvidence.Count,
                    HasMalformedRuntimeJsExportAttribute =
                        jsExportEvidence.HasMalformedRow,
                    Attributes = RenderMemberAttributes(
                        reader,
                        methodCustomAttributes,
                        observeText,
                        observeAttributeMaterialize)
                };

                // Check for extension method
                if (isExtensionMethod)
                {
                    member.IsExtension = true;
                    member.ExtendedType =
                        signature.Model?.ExtensionReceiverType;
                    if (GetFirstParameterDefinitionName(reader, typeDef, method)
                        is { } receiverDefinition)
                    {
                        extensionReceiverDefinitions.Add(member, receiverDefinition);
                    }
                    member.DeclaringType = apiType.FullName;
                }

                budget?.RetainMember(member);
                apiType.Members.Add(member);
                surface.PublicMethodCount++;
            }

            foreach (IGrouping<string, ApiMember> exports in
                apiType.Members
                    .Where(member =>
                        member.HasRuntimeJsExport
                        || member.RuntimeJsExportAttributeCount > 0
                        || member.HasMalformedRuntimeJsExportAttribute)
                    .GroupBy(
                        member => member.Name,
                        StringComparer.Ordinal))
            {
                List<(
                    string MemberName,
                    int RegistrationMethodToken,
                    int RegistrationCount)>? registrations = null;
                if (currentAssemblyIdentity is not null)
                {
                    registeredRuntimeJsExportWrapperNames.TryGetValue(
                        (
                            currentAssemblyIdentity.Name,
                            apiType.FullName),
                        out registrations);
                }
                List<RuntimeJsExportWrapperCandidate> candidates =
                    registrations?
                        .Where(registration =>
                            RuntimeJsExportWrapperName.IsCandidateFor(
                                registration.MemberName,
                                exports.Key)
                            && runtimeJsExportWrapperCandidateMethods
                                .ContainsKey(
                                    registration.MemberName))
                        .SelectMany(registration =>
                            runtimeJsExportWrapperCandidateMethods[
                                registration.MemberName]
                                .Select(wrapperToken =>
                                    new RuntimeJsExportWrapperCandidate(
                                        wrapperToken,
                                        registration
                                            .RegistrationMethodToken,
                                        registration
                                            .RegistrationCount)
                                    {
                                        ModuleVersionId =
                                            moduleVersionId,
                                    }))
                        .Distinct()
                        .ToList()
                    ?? [];
                int wrapperCount = candidates
                    .Select(candidate =>
                        candidate.WrapperMethodToken)
                    .Distinct()
                    .Count();
                bool hasWrapperCandidates =
                    wrapperCount >= exports.Count();
                foreach (ApiMember member in exports)
                {
                    member.HasRuntimeJsExportWrapperCandidate =
                        hasWrapperCandidates;
                    member.RuntimeJsExportWrapperCandidates =
                        candidates.Count == 0
                            ? null
                            : candidates;
                }
            }

            var fieldLikeEventBackingFieldNames = FieldLikeEventBackingFieldNames(
                reader, typeDef, observeDecodeWork);
            var autoPropertyBackingFields = AutoPropertyBackingFieldDescriptors(
                reader, typeDef, typeContext, observeText, observeDecodeWork);
            var backingStorage = ReadBackingStorageAssociations(
                reader, typeDef, typeContext, moduleVersionId,
                autoPropertyBackingFields, fieldLikeEventBackingFieldNames,
                observeText, observeDecodeWork);

            // Properties
            foreach (var propHandle in typeDef.GetProperties())
            {
                var prop = reader.GetPropertyDefinition(propHandle);
                var accessors = prop.GetAccessors();

                // Determine best accessor visibility
                MethodAttributes bestAccess = 0;
                bool isStaticProperty = false;
                bool isVirtualProperty = false;
                bool isAbstractProperty = false;
                bool isOverrideProperty = false;
                bool isSealedProperty = false;
                if (!accessors.Getter.IsNil)
                {
                    var getter = reader.GetMethodDefinition(accessors.Getter);
                    var getterAttributes = getter.Attributes;
                    bestAccess = getter.Attributes & MethodAttributes.MemberAccessMask;
                    isStaticProperty = (getterAttributes & MethodAttributes.Static) != 0;
                    isVirtualProperty = (getterAttributes & MethodAttributes.Virtual) != 0;
                    isAbstractProperty = (getterAttributes & MethodAttributes.Abstract) != 0;
                    isOverrideProperty = isVirtualProperty && (getterAttributes & MethodAttributes.NewSlot) == 0;
                    isSealedProperty = isOverrideProperty && (getterAttributes & MethodAttributes.Final) != 0;
                }
                if (!accessors.Setter.IsNil)
                {
                    var setter = reader.GetMethodDefinition(accessors.Setter);
                    var setterAttributes = setter.Attributes;
                    var setterAccess = setterAttributes & MethodAttributes.MemberAccessMask;
                    if (setterAccess > bestAccess)
                        bestAccess = setterAccess;
                    var setterVirtual = (setterAttributes & MethodAttributes.Virtual) != 0;
                    var setterOverride = setterVirtual && (setterAttributes & MethodAttributes.NewSlot) == 0;
                    isStaticProperty |= (setterAttributes & MethodAttributes.Static) != 0;
                    isVirtualProperty |= setterVirtual;
                    isAbstractProperty |= (setterAttributes & MethodAttributes.Abstract) != 0;
                    isOverrideProperty |= setterOverride;
                    isSealedProperty |= setterOverride && (setterAttributes & MethodAttributes.Final) != 0;
                }

                bool isPublicProp = bestAccess == MethodAttributes.Public;
                if (!isPublicProp && !includeAll)
                    continue;

                // Skip EditorBrowsable(Never) properties unless --all; obsolete are surfaced with marker.
                if (!includeAll
                    && AttributeReader.HasEditorBrowsableNeverAttribute(
                        reader,
                        prop.GetCustomAttributes(),
                        observeDecodeWork))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(
                    reader,
                    prop.GetCustomAttributes(),
                    out var obsoleteMessage,
                    observeDecodeWork);

                var propertySignature = GetPropertySignature(
                    reader,
                    typeContext,
                    prop,
                    accessors,
                    typeNullableContext,
                    explicitImplementationBodies,
                    includeAll,
                    observeText,
                    observeDecodeWork,
                    observeAttributeMaterialize);
                List<string?> jsonPropertyNames =
                    AttributeReader.ReadJsonPropertyNames(
                        reader,
                        prop.GetCustomAttributes(),
                        observeDecodeWork);
                int jsonConverterAttributeCount =
                    AttributeReader.CountJsonConverterAttributes(
                        reader,
                        prop.GetCustomAttributes(),
                        observeDecodeWork);
                JsonIncludeAttributeEvidence propertyJsonInclude =
                    AttributeReader.ReadJsonIncludeAttributes(
                        reader,
                        prop.GetCustomAttributes(),
                        observeDecodeWork);
                List<JsonWireIgnoreCondition?> propertyJsonIgnoreConditions =
                    AttributeReader.ReadJsonIgnoreConditions(
                        reader,
                        prop.GetCustomAttributes(),
                        observeDecodeWork);
                var member = new ApiMember
                {
                    Name = DecodeString(
                        reader,
                        prop.Name,
                        observeDecodeWork),
                    Kind = "property",
                    DeclarationMetadataToken =
                        MetadataTokens.GetToken(propHandle),
                    Signature = propertySignature.Text,
                    SignatureModel = propertySignature.Model,
                    IndexParameterCount =
                        propertySignature.Model?.ParameterCount,
                    SignatureDecodeStatus = propertySignature.IsDegraded
                        ? SignatureDecodeStatus.Degraded
                        : null,
                    IsStatic = isStaticProperty,
                    IsVirtual = isVirtualProperty,
                    IsAbstract = isAbstractProperty,
                    IsOverride = isOverrideProperty,
                    IsSealed = isSealedProperty,
                    IsUnsafe = HasUnsafeSignature(propertySignature.Text),
                    MemorySafety = ApiMemorySafetyFacts.Read(
                        reader, GetMemorySafetyIndex(), moduleVersionId, propHandle),
                    AccessorMemorySafety = ReadAccessorMemorySafety(
                        reader, GetMemorySafetyIndex(), moduleVersionId,
                        [accessors.Getter, accessors.Setter, .. accessors.Others]),
                    AccessorImplementations = ApiMethodImplementationFacts.ReadAccessors(
                        reader, moduleVersionId,
                        [accessors.Getter, accessors.Setter, .. accessors.Others]),
                    BackingStorage = backingStorage[MetadataTokens.GetToken(propHandle)],
                    Accessibility = GetAccessibility(bestAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage,
                    IsCompilerGenerated = AttributeReader.HasAttribute(
                        reader,
                        prop.GetCustomAttributes(),
                        KnownAttributeNames.CompilerGeneratedAttribute,
                        observeDecodeWork),
                    HasJsonInclude = propertyJsonInclude.Count > 0,
                    HasMalformedJsonInclude =
                        propertyJsonInclude.HasMalformedRow,
                    JsonIgnoreConditions = propertyJsonIgnoreConditions,
                    JsonPropertyName = jsonPropertyNames.Count == 1
                        ? jsonPropertyNames[0]
                        : null,
                    JsonPropertyNameAttributeValues = jsonPropertyNames,
                    JsonConverterAttributeCount =
                        jsonConverterAttributeCount,
                    HasUnsupportedJsonWireAttributes =
                        AttributeReader
                            .HasUnsupportedJsonMemberWireAttributes(
                                reader,
                                prop.GetCustomAttributes(),
                                observeDecodeWork),
                    Attributes = RenderMemberAttributes(
                        reader,
                        prop.GetCustomAttributes(),
                        observeText,
                        observeAttributeMaterialize),
                    GetterToken = accessors.Getter.IsNil ? null : MetadataTokens.GetToken(accessors.Getter),
                    SetterToken = accessors.Setter.IsNil ? null : MetadataTokens.GetToken(accessors.Setter),
                    GetterHasMethodBody = accessors.Getter.IsNil
                        ? null
                        : reader.GetMethodDefinition(accessors.Getter).RelativeVirtualAddress != 0,
                    SetterHasMethodBody = accessors.Setter.IsNil
                        ? null
                        : reader.GetMethodDefinition(accessors.Setter).RelativeVirtualAddress != 0,
                    HasGetter = !accessors.Getter.IsNil,
                    GetterAccessibility = accessors.Getter.IsNil
                        ? null
                        : GetAccessibility(
                            reader.GetMethodDefinition(accessors.Getter)
                                .Attributes
                                & MethodAttributes.MemberAccessMask),
                    HasSetter = !accessors.Setter.IsNil,
                    SetterAccessibility = accessors.Setter.IsNil
                        ? null
                        : GetAccessibility(
                            reader.GetMethodDefinition(accessors.Setter)
                                .Attributes
                                & MethodAttributes.MemberAccessMask),
                };

                budget?.RetainMember(member);
                apiType.Members.Add(member);
                surface.PublicPropertyCount++;
            }

            // Fields (non-backing fields; non-public included with --all)
            bool isEnum = apiType.Kind == "enum";

            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                var fieldAccess = field.Attributes & FieldAttributes.FieldAccessMask;
                if (fieldAccess != FieldAttributes.Public && !includeAll)
                    continue;

                string fieldName = DecodeString(
                    reader,
                    field.Name,
                    observeDecodeWork);

                // The enum storage slot supplies a type fact rather than a
                // declarable member, so presentation filters do not apply to it.
                if (isEnum && fieldName == "value__")
                {
                    apiType.EnumUnderlyingType = DecodeFieldType(
                        reader,
                        typeContext,
                        field,
                        typeNullableContext,
                        observeText,
                        observeDecodeWork).Text;
                    continue;
                }

                List<string?> jsonPropertyNames =
                    AttributeReader.ReadJsonPropertyNames(
                        reader,
                        field.GetCustomAttributes(),
                        observeDecodeWork);

                if (IsAutoPropertyBackingField(
                    reader,
                    field,
                    fieldName,
                    autoPropertyBackingFields,
                    typeContext,
                    observeText,
                    observeDecodeWork))
                {
                    if (jsonPropertyNames.Count > 0
                        && autoPropertyBackingFields is not null
                        && autoPropertyBackingFields.TryGetValue(
                            fieldName,
                            out AutoPropertyBackingField backingField))
                    {
                        apiType.FilteredJsonPropertyNameFacts.Add(
                            new FilteredJsonPropertyNameFact(
                                FilteredJsonPropertyNameKind
                                    .AutoPropertyBackingField,
                                backingField.PropertyName,
                                MetadataTokens.GetToken(fieldHandle),
                                jsonPropertyNames));
                    }
                    continue;
                }

                if (!IsSurfaceableFieldName(fieldName, includeCompilerGenerated))
                {
                    AddFilteredJsonPropertyNameFact(
                        apiType,
                        FilteredJsonPropertyNameKind.CompilerNamedField,
                        associatedMemberName: null,
                        MetadataTokens.GetToken(fieldHandle),
                        jsonPropertyNames);
                    continue; // Skip compiler-generated (<...>) fields unless opted in
                }

                if (IsFieldLikeEventBackingField(
                        reader,
                        field,
                        fieldName,
                        fieldLikeEventBackingFieldNames,
                        observeDecodeWork))
                {
                    AddFilteredJsonPropertyNameFact(
                        apiType,
                        FilteredJsonPropertyNameKind.EventBackingField,
                        fieldName,
                        MetadataTokens.GetToken(fieldHandle),
                        jsonPropertyNames);
                    continue; // Skip a field-like event's private, compiler-generated backing field
                }

                // Skip EditorBrowsable(Never) fields unless --all; obsolete are surfaced with marker.
                if (!includeAll
                    && AttributeReader.HasEditorBrowsableNeverAttribute(
                        reader,
                        field.GetCustomAttributes(),
                        observeDecodeWork))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(
                    reader,
                    field.GetCustomAttributes(),
                    out var obsoleteMessage,
                    observeDecodeWork);
                List<string?> jsonStringEnumMemberNames =
                    AttributeReader.ReadJsonStringEnumMemberNames(
                        reader,
                        field.GetCustomAttributes(),
                        observeDecodeWork);

                // Enum literal fields are constants, not fields in source, so they
                // do not need a field declaration type.
                string? fieldType = null;
                bool fieldSignatureDegraded = false;
                List<ApiTypeReferenceIdentity> fieldTypeReferences = [];
                if (!isEnum)
                {
                    (fieldType, fieldSignatureDegraded, fieldTypeReferences) =
                        DecodeFieldType(
                        reader,
                        typeContext,
                        field,
                        typeNullableContext,
                        observeText,
                        observeDecodeWork);
                }

                JsonIncludeAttributeEvidence fieldJsonInclude =
                    AttributeReader.ReadJsonIncludeAttributes(
                        reader,
                        field.GetCustomAttributes(),
                        observeDecodeWork);
                List<JsonWireIgnoreCondition?> fieldJsonIgnoreConditions =
                    AttributeReader.ReadJsonIgnoreConditions(
                        reader,
                        field.GetCustomAttributes(),
                        observeDecodeWork);
                var member = new ApiMember
                {
                    Name = fieldName,
                    Kind = "field",
                    DeclarationMetadataToken =
                        MetadataTokens.GetToken(fieldHandle),
                    FieldLayout = ApiFieldLayoutFacts.Read(
                        reader, moduleVersionId, typeDefHandle, fieldHandle),
                    ReturnType = fieldType,
                    SignatureModel = fieldType is null ? null : new ApiSignature
                    {
                        ReturnType = fieldType,
                        MemberName = fieldName,
                        ReturnTypeReferences = fieldTypeReferences,
                    },
                    SignatureDecodeStatus = fieldSignatureDegraded
                        ? SignatureDecodeStatus.Degraded
                        : null,
                    IsStatic = (field.Attributes & FieldAttributes.Static) != 0,
                    IsReadOnly = (field.Attributes & FieldAttributes.InitOnly) != 0,
                    IsConst = (field.Attributes & FieldAttributes.Literal) != 0,
                    MemorySafety = ApiMemorySafetyFacts.Read(
                        reader, GetMemorySafetyIndex(), moduleVersionId, fieldHandle),
                    Accessibility = GetFieldAccessibility(fieldAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage,
                    IsCompilerGenerated = AttributeReader.HasAttribute(
                        reader,
                        field.GetCustomAttributes(),
                        KnownAttributeNames.CompilerGeneratedAttribute,
                        observeDecodeWork),
                    HasJsonInclude = fieldJsonInclude.Count > 0,
                    HasMalformedJsonInclude =
                        fieldJsonInclude.HasMalformedRow,
                    JsonIgnoreConditions = fieldJsonIgnoreConditions,
                    JsonPropertyName = jsonPropertyNames.Count == 1
                        ? jsonPropertyNames[0]
                        : null,
                    JsonPropertyNameAttributeValues = jsonPropertyNames,
                    JsonConverterAttributeCount =
                        AttributeReader.CountJsonConverterAttributes(
                            reader,
                            field.GetCustomAttributes(),
                            observeDecodeWork),
                    HasUnsupportedJsonWireAttributes =
                        AttributeReader
                            .HasUnsupportedJsonMemberWireAttributes(
                                reader,
                                field.GetCustomAttributes(),
                                observeDecodeWork),
                    JsonStringEnumMemberNameAttributeValues =
                        jsonStringEnumMemberNames,
                    Attributes = RenderMemberAttributes(
                        reader,
                        field.GetCustomAttributes(),
                        observeText,
                        observeAttributeMaterialize)
                };

                if (!isEnum && member.IsConst)
                {
                    ConstantHandle constantHandle = field.GetDefaultValue();
                    if (!constantHandle.IsNil)
                    {
                        member.ConstantValueLiteral =
                            FormatFieldConstantLiteral(
                                reader,
                                reader.GetConstant(constantHandle));
                    }
                }

                // Read enum constant value
                if (isEnum && (field.Attributes & FieldAttributes.Literal) != 0)
                {
                    var constantHandle = field.GetDefaultValue();
                    if (!constantHandle.IsNil)
                    {
                        var constant = reader.GetConstant(constantHandle);
                        var blob = reader.GetBlobReader(constant.Value);
                        member.EnumValue = constant.TypeCode switch
                        {
                            ConstantTypeCode.SByte => blob.ReadSByte(),
                            ConstantTypeCode.Byte => blob.ReadByte(),
                            ConstantTypeCode.Int16 => blob.ReadInt16(),
                            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
                            ConstantTypeCode.Int32 => blob.ReadInt32(),
                            ConstantTypeCode.UInt32 => blob.ReadUInt32(),
                            ConstantTypeCode.Int64 => blob.ReadInt64(),
                            ConstantTypeCode.UInt64 => (long)blob.ReadUInt64(),
                            _ => null
                        };
                        blob = reader.GetBlobReader(constant.Value);
                        member.EnumValueLiteral = constant.TypeCode switch
                        {
                            ConstantTypeCode.SByte => blob.ReadSByte().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.Byte => blob.ReadByte().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.Int16 => blob.ReadInt16().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.UInt16 => blob.ReadUInt16().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.Int32 => blob.ReadInt32().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.UInt32 => blob.ReadUInt32().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.Int64 => blob.ReadInt64().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.UInt64 => blob.ReadUInt64().ToString(CultureInfo.InvariantCulture),
                            _ => null
                        };
                    }
                }

                budget?.RetainMember(member);
                apiType.Members.Add(member);
                surface.PublicFieldCount++;
            }

            // Events
            foreach (var eventHandle in typeDef.GetEvents())
            {
                var evt = reader.GetEventDefinition(eventHandle);
                var accessors = evt.GetAccessors();

                // Check if adder exists
                if (accessors.Adder.IsNil)
                    continue;

                var adder = reader.GetMethodDefinition(accessors.Adder);
                var adderAccess = adder.Attributes & MethodAttributes.MemberAccessMask;
                if (adderAccess != MethodAttributes.Public && !includeAll)
                    continue;

                // Skip EditorBrowsable(Never) events unless --all; obsolete are surfaced with marker.
                if (!includeAll
                    && AttributeReader.HasEditorBrowsableNeverAttribute(
                        reader,
                        evt.GetCustomAttributes(),
                        observeDecodeWork))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(
                    reader,
                    evt.GetCustomAttributes(),
                    out var obsoleteMessage,
                    observeDecodeWork);
                TypeNode? structuralEventNode = null;
                var eventType = ResolveRequiredTypeName(
                    reader,
                    evt.Type,
                    typeContext,
                    observeText,
                    observeDecodeWork,
                    captureTypeNode: node => structuralEventNode = node);
                var eventNullableBytes = NullabilityReader.GetNullableBytes(
                    reader,
                    evt.GetCustomAttributes(),
                    observeDecodeWork);
                eventNullableBytes ??= NullabilityReader.GetParameterNullableBytes(
                    reader,
                    adder.GetParameters(),
                    1,
                    observeDecodeWork);
                if (eventNullableBytes is { Length: > 0 } && eventNullableBytes[0] == 2 && !eventType.EndsWith("?", StringComparison.Ordinal))
                    eventType += "?";
                // A `dynamic` event handler (e.g. EventHandler<dynamic>) or a
                // named-tuple handler (EventHandler<(int a, int b)>) is always a
                // generic instantiation, so re-decode the TypeSpec through the
                // TypeNode tree to recover the dynamic / tuple view. Plain events
                // are untouched.
                var eventTupleNames = TupleElementNamesReader.GetTupleElementNames(
                    reader,
                    evt.GetCustomAttributes(),
                    observeDecodeWork);
                var eventDynamicFlags = evt.Type.Kind == HandleKind.TypeSpecification
                    ? DynamicReader.GetDynamicFlags(
                        reader,
                        evt.GetCustomAttributes(),
                        observeDecodeWork)
                    : null;
                if (evt.Type.Kind == HandleKind.TypeSpecification
                    && (eventDynamicFlags is not null || eventTupleNames is not null))
                {
                    var eventNode = GuardedProviderDecode.TypeSpec(
                        reader,
                        (TypeSpecificationHandle)evt.Type,
                        new TypeNodeProvider(observeText, observeDecodeWork),
                        typeContext,
                        (TypeNode)new DegradedTypeNode());
                    // Skip a rejected/degraded decode: its bare "object"/"dynamic" render
                    // would obliterate the resolved eventType string computed above.
                    if (!eventNode.IsDegraded)
                    {
                        int eventPos = 0;
                        eventNode.ApplyNullability(eventNullableBytes, ref eventPos, 0);
                        eventPos = 0;
                        eventNode.ApplyDynamic(eventDynamicFlags, ref eventPos);
                        eventNode.ApplyTupleNames(eventTupleNames);
                        eventType = eventNode.Render();
                    }
                }
                var adderAttributes = adder.Attributes;
                var isVirtualEvent = (adderAttributes & MethodAttributes.Virtual) != 0;
                var isOverrideEvent = isVirtualEvent && (adderAttributes & MethodAttributes.NewSlot) == 0;
                var accessorModels = new List<ApiAccessor>
                {
                    new()
                    {
                        Kind = "add",
                        ReturnAttributes = ReturnParameterAttributes(
                            reader,
                            adder.GetParameters(),
                            observeText,
                            observeAttributeMaterialize)
                    }
                };
                if (!accessors.Remover.IsNil)
                {
                    accessorModels.Add(new ApiAccessor
                    {
                        Kind = "remove",
                        ReturnAttributes = ReturnParameterAttributes(
                            reader,
                            reader.GetMethodDefinition(accessors.Remover).GetParameters(),
                            observeText,
                            observeAttributeMaterialize)
                    });
                }

                var eventTypeNodeProvider = observeText is null
                    ? TypeNodeProvider.Instance
                    : new TypeNodeProvider(observeText, observeDecodeWork);
                ApplyAccessorStructuralReturns(
                    accessorModels,
                    reader,
                    kind => kind switch
                    {
                        "add" => accessors.Adder,
                        "remove" => accessors.Remover,
                        _ => default,
                    },
                    eventTypeNodeProvider,
                    typeContext,
                    explicitImplementationBodies,
                    observeText,
                    observeDecodeWork);

                string eventName = DecodeString(
                    reader,
                    evt.Name,
                    observeDecodeWork);
                var member = new ApiMember
                {
                    Name = eventName,
                    Kind = "event",
                    DeclarationMetadataToken = MetadataTokens.GetToken(eventHandle),
                    MemorySafety = ApiMemorySafetyFacts.Read(
                        reader, GetMemorySafetyIndex(), moduleVersionId, eventHandle),
                    AccessorMemorySafety = ReadAccessorMemorySafety(
                        reader, GetMemorySafetyIndex(), moduleVersionId,
                        [accessors.Adder, accessors.Remover, accessors.Raiser, .. accessors.Others]),
                    AccessorImplementations = ApiMethodImplementationFacts.ReadAccessors(
                        reader, moduleVersionId,
                        [accessors.Adder, accessors.Remover, accessors.Raiser, .. accessors.Others]),
                    BackingStorage = backingStorage[MetadataTokens.GetToken(eventHandle)],
                    ReturnType = eventType,
                    Signature = $"{eventType} {SanitizeIdentifier(eventName)}",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = eventType,
                        StructuralReturnType =
                            structuralEventNode is
                                {
                                    IsDegraded: false,
                                    HasStructuralPayload: true
                                }
                                ? structuralEventNode.StructuralIdentity()
                                : null,
                        MemberName = eventName,
                        Accessors = accessorModels
                    },
                    IsStatic = (adderAttributes & MethodAttributes.Static) != 0,
                    IsVirtual = isVirtualEvent,
                    IsAbstract = (adderAttributes & MethodAttributes.Abstract) != 0,
                    IsOverride = isOverrideEvent,
                    IsSealed = isOverrideEvent && (adderAttributes & MethodAttributes.Final) != 0,
                    Accessibility = GetAccessibility(adderAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage,
                    AdderToken = accessors.Adder.IsNil
                        ? null
                        : MetadataTokens.GetToken(accessors.Adder),
                    RemoverToken = accessors.Remover.IsNil
                        ? null
                        : MetadataTokens.GetToken(accessors.Remover),
                    AdderHasMethodBody = adder.RelativeVirtualAddress != 0,
                    RemoverHasMethodBody = accessors.Remover.IsNil
                        ? null
                        : reader.GetMethodDefinition(accessors.Remover).RelativeVirtualAddress != 0
                };

                budget?.RetainMember(member);
                apiType.Members.Add(member);
                surface.PublicEventCount++;
            }
            } // end if (!typesOnly)

            budget?.RetainType(apiType);
            surface.Types.Add(apiType);
            surface.PublicTypeCount++;
            }
            catch (MetadataRowRejectedException ex)
            {
                if (constraintCheckpoint is { } checkpoint)
                    constraintResolution!.Rollback(checkpoint);
                surface.PublicMethodCount = publicMethodCount;
                surface.PublicPropertyCount = publicPropertyCount;
                surface.PublicEventCount = publicEventCount;
                surface.PublicFieldCount = publicFieldCount;
                AddInspectionFailure(
                    surface,
                    budget,
                    ex.Operation,
                    typeDefHandle,
                    ex.Failure,
                    owningType: typeDefHandle,
                    owningTypeParent: owningTypeParent,
                    owningTypeDefinition: owningTypeDefinition,
                    owningTypeAttributes: owningTypeAttributes);
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                if (constraintCheckpoint is { } checkpoint)
                    constraintResolution!.Rollback(checkpoint);
                surface.PublicMethodCount = publicMethodCount;
                surface.PublicPropertyCount = publicPropertyCount;
                surface.PublicEventCount = publicEventCount;
                surface.PublicFieldCount = publicFieldCount;
                AddInspectionFailure(
                    surface,
                    budget,
                    "type row",
                    typeDefHandle,
                    MetadataTypeNameFailure.Malformed(typeDefHandle, ex.Message),
                    owningType: typeDefHandle,
                    owningTypeParent: owningTypeParent,
                    owningTypeDefinition: owningTypeDefinition,
                    owningTypeAttributes: owningTypeAttributes);
            }
        }

        if (materializationContext.TryGetCachedIndexFailure(
                out MetadataTypeNameFailure? indexFailure))
        {
            AddInspectionFailure(
                surface,
                budget,
                ApiSurfaceInspectionFailure.EnumAttributeTypeIndexOperation,
                default,
                indexFailure);
        }

        AttachLocalExtensionMethods(
            surface,
            extensionReceiverDefinitions,
            budget);

        // Extract type forwarders (ExportedTypes that are forwarded to other assemblies)
        ExtractTypeForwarders(reader, surface, budget);

        ApiMemberIdentity.PopulateCanonicalIdentities(
            surface,
            budget is null ? null : budget.RetainCommittedText);
        return surface;
    }
}
