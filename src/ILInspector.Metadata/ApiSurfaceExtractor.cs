using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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

/// <summary>
/// Extracts public API surface from assemblies.
/// </summary>
public static class ApiSurfaceExtractor
{
    private const string OptionalAttributeName = "System.Runtime.InteropServices.Optional";
    private const string DateTimeConstantAttributeName = "System.Runtime.CompilerServices.DateTimeConstant";
    private static readonly ConditionalWeakTable<
        MetadataReader,
        PrimitiveDefinitionClassification>
        PrimitiveDefinitionClassifications = new();
    private static readonly ConditionalWeakTable<
        MetadataReader,
        FinalizerMethodImplementationCache>
        FinalizerMethodImplementationCaches = new();

    /// <summary>
    /// Extracts the public type identities and member-kind counts needed by the compact platform
    /// API view without decoding ordinary member signatures or materializing rich member models.
    /// Extension-property association signatures are decoded only to keep method counts exact.
    /// </summary>
    public static ApiSurface ExtractSummary(PEReader peReader)
    {
        var surface = new ApiSurface();
        var reader = peReader.GetMetadataReader();
        ApiAssemblyIdentity? currentAssemblyIdentity = reader.IsAssembly
            ? ApiAssemblyIdentity.FromDefinition(reader)
            : null;
        surface.AssemblyIdentity = currentAssemblyIdentity;
        var extensionReceiverDefinitions =
            new Dictionary<ApiMember, MetadataTypeDefinitionName>();
        var currentScope = ReadCurrentScope(reader, surface, budget: null);
        var localTypes = currentScope is null
            ? []
            : LocalTypes(reader, currentScope, surface, budget: null);
        var accessorMethods = ReadAccessorMethods(
            reader,
            surface,
            budget: null,
            currentScope: currentScope);

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
                var apiType = new ApiType
                {
                    Namespace = typeNamespace,
                    Name = typeName,
                    MetadataName = GetMetadataName(reader, typeDefHandle),
                    DefinitionName =
                        MetadataTypeDefinitionNameReader.Read(reader, typeDefHandle)
                        is MetadataTypeDefinitionNameReadResult.Read read
                            ? read.Name
                            : null,
                    IntroducedTypeParameterCounts =
                        MetadataDeclarationQuery.GetIntroducedTypeParameterCounts(
                            reader,
                            typeDefHandle),
                    Kind = SummaryTypeKind(reader, typeDef),
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
                    typeDefHandle,
                    typeDef,
                    apiType,
                    surface,
                    isExtensionClass,
                    extensionReceiverDefinitions,
                    accessorMethods,
                    currentScope,
                    localTypes);
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

    public static ApiSurface Extract(PEReader peReader, bool includeAll = false, bool typesOnly = false, bool includeCompilerGenerated = false)
        => Extract(
            peReader,
            includeAll
                ? ApiSurfaceExtractionScope.IncludeAll
                : ApiSurfaceExtractionScope.Public,
            typesOnly,
            includeCompilerGenerated);

    public static ApiSurface Extract(
        MetadataReader reader,
        bool includeAll = false,
        bool typesOnly = false,
        bool includeCompilerGenerated = false,
        IOperatorTypeRelationshipResolver? operatorRelationshipResolver = null)
        => Extract(
            reader,
            includeAll
                ? ApiSurfaceExtractionScope.IncludeAll
                : ApiSurfaceExtractionScope.Public,
            typesOnly,
            includeCompilerGenerated,
            budget: null,
            constraintResolution: null,
            operatorRelationshipResolver);

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
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindingPolicy);

        var constraintResolution =
            new TypeParameterConstraintResolution(
                peReader.GetMetadataReader(),
                source,
                catalog.MaxTypeResolutionRequests);
        ApiSurface surface = Extract(
            peReader,
            includeAll
                ? ApiSurfaceExtractionScope.IncludeAll
                : ApiSurfaceExtractionScope.Public,
            typesOnly,
            includeCompilerGenerated,
            budget: null,
            constraintResolution);
        if (constraintResolution.Requests.Count == 0)
        {
            AddConstraintResolutionFailure(
                surface,
                constraintResolution,
                source.Identity);
            return surface;
        }

        int requestCount;
        do
        {
            requestCount = constraintResolution.Requests.Count;
            using TypeResolutionContext context =
                catalog.CreateApiSurfaceContext(
                    bindingPolicy,
                    [source],
                    constraintResolution.Requests);
            constraintResolution.Apply(context);
        }
        while (constraintResolution.Requests.Count > requestCount);
        AddConstraintResolutionFailure(
            surface,
            constraintResolution,
            source.Identity);
        return surface;
    }

    static void AddConstraintResolutionFailure(
        ApiSurface surface,
        TypeParameterConstraintResolution constraintResolution,
        AssemblyReferenceIdentity subjectAssembly)
    {
        foreach (MetadataTypeNameFailure budgetFailure
            in constraintResolution.Plan.RequestBudgetFailures)
        {
            TrackConstraintResolutionFailure(
                surface,
                budgetFailure,
                subjectAssembly);
        }

        foreach (TypeParameterKindClassifier.ResolutionPlan
            .ResolutionFailureEntry resolutionFailure
            in constraintResolution.Plan.ResolutionFailureEntries)
        {
            TrackConstraintResolutionFailure(
                surface,
                resolutionFailure.Failure,
                subjectAssembly,
                resolutionFailure.DependencyAssembly);
        }
    }

    static void TrackConstraintResolutionFailure(
        ApiSurface surface,
        MetadataTypeNameFailure failure,
        AssemblyReferenceIdentity subjectAssembly,
        AssemblyReferenceIdentity? dependencyAssembly = null)
    {
        var projected = new ApiSurfaceInspectionFailure(
            ApiSurface.ConstraintResolutionOperation,
            failure.SubjectToken ?? 0,
            failure.Mechanism,
            failure.Kind,
            failure.Detail,
            subjectAssembly,
            dependencyAssembly);
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
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        try
        {
            var budget = new ExtractionBudget(bounds);
            ApiSurface surface = Extract(
                peReader,
                scope,
                typesOnly,
                includeCompilerGenerated,
                budget,
                constraintResolution: null);
            return new ApiSurfaceExtractionResult.Extracted(
                surface,
                budget.MetadataRows,
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
        TypeParameterConstraintResolution? constraintResolution)
        => Extract(
            peReader.GetMetadataReader(),
            scope,
            typesOnly,
            includeCompilerGenerated,
            budget,
            constraintResolution);

    static ApiSurface Extract(
        MetadataReader reader,
        ApiSurfaceExtractionScope scope,
        bool typesOnly,
        bool includeCompilerGenerated,
        ExtractionBudget? budget,
        TypeParameterConstraintResolution? constraintResolution,
        IOperatorTypeRelationshipResolver? operatorRelationshipResolver = null)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        var surface = new ApiSurface();
        Guid moduleVersionId = reader.GetGuid(
            reader.GetModuleDefinition().Mvid);
        var extensionReceiverDefinitions =
            new Dictionary<ApiMember, MetadataTypeDefinitionName>();
        budget?.AdmitMetadataRows(reader);
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
        var currentScope = typesOnly
            ? null
            : ReadCurrentScope(reader, surface, budget);
        var localTypes = typesOnly || currentScope is null
            ? []
            : LocalTypes(reader, currentScope, surface, budget);
        var accessorMethods = typesOnly
            ? new AccessorMethodOwnership()
            : ReadAccessorMethods(
                reader,
                surface,
                budget,
                currentScope);

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            MetadataTypeDefinitionName? owningTypeDefinition = null;
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

            if (typesOnly
                && scope == ApiSurfaceExtractionScope.Public
                && !typeDef.IsPublic)
            {
                continue;
            }

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
                IsSealed = (attributes & TypeAttributes.Sealed) != 0,
                IsAbstract = (attributes & TypeAttributes.Abstract) != 0,
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

            if (AttributeReader.TryGetJsonSourceGenerationOptions(
                    reader,
                    jsonTypeAttributes,
                    out JsonWireNamingPolicy? namingPolicy,
                    out JsonSourceGenerationMode generationMode,
                    observeDecodeWork))
            {
                apiType.JsonPropertyNamingPolicy = namingPolicy;
                apiType.JsonSourceGenerationMode = generationMode;
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
                }
            }

            // Get members (public only, or all when includeAll)
            if (!typesOnly)
            {
            apiType.Members = [];

            var methodImplementations = ReadOwnedMethodImplementations(
                reader,
                typeDefHandle,
                typeDef,
                surface,
                budget,
                owningTypeDefinition,
                currentScope,
                localTypes,
                observeDecodeWork);
            var explicitImplementationBodies = GetExplicitImplementationBodies(
                reader,
                typeDef,
                methodImplementations,
                currentScope,
                localTypes,
                observeDecodeWork);
            // Methods whose explicit `.override` MethodImpl targets
            // `System.Object::Finalize` — i.e. genuine class finalizers, the
            // slot the C# `~Type()` destructor compiles to.
            var objectFinalizeOverrides = GetObjectFinalizeOverrides(
                reader,
                methodImplementations,
                observeDecodeWork);
            bool isFinalizerOwner = IsFinalizerOwner(typeDef, apiType.Kind);

            // Getter/setter and adder/remover bodies are represented by their
            // property or event rows. Raiser and Other semantic methods have no
            // ApiMember token slots, so they stay methods.
            var runtimeJsExportWrapperCandidateMethods =
                new Dictionary<string, List<int>>(
                    StringComparer.Ordinal);

            // Methods
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var methodCustomAttributes =
                    method.GetCustomAttributes();
                var methodAccess = method.Attributes & MethodAttributes.MemberAccessMask;
                var isExplicitInterfaceImplementation = explicitImplementationBodies.Contains(methodHandle);
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
                var isOwnedAccessor = accessorMethods.Contains(methodHandle);
                var isRetainedExplicitImplementation = isExplicitInterfaceImplementation
                    && (!isOwnedAccessor
                        || methodAccess != MethodAttributes.Public
                        || methodName.Contains('.', StringComparison.Ordinal));
                var isFinalizer = isFinalizerOwner
                    && method.GetGenericParameters().Count == 0
                    && (objectFinalizeOverrides.Contains(methodHandle)
                        || IsImplicitObjectFinalizeOverride(
                            reader,
                            typeDefHandle,
                            method,
                            currentScope,
                            localTypes,
                            observeDecodeWork));
                if (methodAccess != MethodAttributes.Public
                    && !includeAll
                    && !isRetainedExplicitImplementation
                    && !isFinalizer)
                {
                    RetainFilteredRuntimeJsExportFact(
                        apiType,
                        methodName,
                        methodHandle,
                        jsExportEvidence);
                    continue;
                }

                // Ordinary accessors are represented by their owning property/event.
                // Explicit-interface accessors remain method entries because whole-type
                // decompilation consumes their bodies. Preserve legal ordinary methods
                // whose names or flags merely resemble accessors.
                if (isOwnedAccessor && !isRetainedExplicitImplementation)
                {
                    RetainFilteredRuntimeJsExportFact(
                        apiType,
                        methodName,
                        methodHandle,
                        jsExportEvidence);
                    continue;
                }

                // Skip compiler-generated methods (lambdas, state machines, etc.)
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
                    && !isRetainedExplicitImplementation
                    && !isFinalizer
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
                bool isExtensionMethod = isExtensionClass
                    && (methodAttributes & MethodAttributes.Static) != 0
                    && AttributeReader.HasExtensionAttribute(
                        reader,
                        methodCustomAttributes,
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
                var isOperator = OperatorMetadata.IsMetadataOperator(reader, method);
                var isVirtual = (methodAttributes & MethodAttributes.Virtual) != 0;
                var isNewSlot = (methodAttributes & MethodAttributes.NewSlot) != 0;
                var isOverride = isVirtual && !isNewSlot && !isRetainedExplicitImplementation && !isFinalizer;

                OperatorMetadata.DeclarationClassification?
                    operatorClassification = isOperator
                        ? constraintResolution?.ClassifyOperator(
                            methodHandle)
                            ?? OperatorMetadata
                                .ClassifyCSharpOperatorDeclaration(
                                    reader,
                                    method,
                                    operatorRelationshipResolver)
                        : null;
                bool hasOperatorPairingIdentity = isOperator
                    && (OperatorNames.RequiredOperatorSibling(methodName) is not null
                        || OperatorNames.CheckedOperator(methodName) is not null);
                var member = new ApiMember
                {
                    Name = methodName,
                    Kind = methodName switch
                    {
                        ".ctor" => "constructor",
                        _ when isOperator => "operator",
                        _ when isFinalizer => "finalizer",
                        _ when isRetainedExplicitImplementation => "explicit-interface-implementation",
                        _ => "method"
                    },
                    IsStatic = (methodAttributes & MethodAttributes.Static) != 0,
                    IsVirtual = isVirtual,
                    IsAbstract = (methodAttributes & MethodAttributes.Abstract) != 0,
                    IsOverride = isOverride,
                    IsSealed = isOverride && (methodAttributes & MethodAttributes.Final) != 0,
                    IsFinalizer = isFinalizer,
                    Signature = signature.Text,
                    SignatureModel = signature.Model,
                    CSharpOperatorDeclaration = isOperator
                        ? operatorClassification switch
                        {
                            OperatorMetadata.DeclarationClassification.Yes =>
                                true,
                            OperatorMetadata.DeclarationClassification.No =>
                                false,
                            _ => null,
                        }
                        : null,
                    HasCSharpOperatorDeclarationClassification =
                        isOperator,
                    HasOperatorPairingKey = hasOperatorPairingIdentity,
                    OperatorPairingKey = hasOperatorPairingIdentity
                        ? TryOperatorPairingKey(reader, method)
                        : null,
                    SignatureDecodeStatus = signature.IsDegraded
                        ? SignatureDecodeStatus.Degraded
                        : null,
                    // Conversion operators overload on return type. Persist it on the
                    // member too so older or abbreviated surfaces without SignatureModel
                    // retain the canonical-signature fallback.
                    ReturnType = ApiMemberIdentity.IsConversionOperator(methodName) ? signature.Model?.ReturnType : null,
                    MetadataToken = MetadataTokens.GetToken(methodHandle),
                    GenericArity =
                        method.GetGenericParameters().Count,
                    HasMethodBody =
                        method.RelativeVirtualAddress != 0,
                    IsUnsafe = HasUnsafeSignature(signature.Text)
                        || AttributeReader.HasRequiresUnsafeAttribute(
                            reader,
                            methodCustomAttributes,
                            observeDecodeWork),
                    Accessibility = isFinalizer || isRetainedExplicitImplementation && !isOperator
                        ? null
                        : GetAccessibility(methodAccess),
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
                if (operatorClassification
                    == OperatorMetadata
                        .DeclarationClassification.Unknown)
                {
                    constraintResolution?.TrackOperator(
                        methodHandle,
                        member);
                }

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

            // Properties
            foreach (var propHandle in typeDef.GetProperties())
            {
                var prop = reader.GetPropertyDefinition(propHandle);
                var accessors = prop.GetAccessors();
                MethodDefinitionHandle getterHandle =
                    !accessors.Getter.IsNil
                    && accessorMethods.Contains(propHandle, accessors.Getter)
                        ? accessors.Getter
                        : default;
                MethodDefinitionHandle setterHandle =
                    !accessors.Setter.IsNil
                    && accessorMethods.Contains(propHandle, accessors.Setter)
                        ? accessors.Setter
                        : default;
                if (getterHandle.IsNil && setterHandle.IsNil)
                    continue;

                // Determine best accessor visibility
                MethodAttributes bestAccess = 0;
                bool isStaticProperty = false;
                bool isVirtualProperty = false;
                bool isAbstractProperty = false;
                bool isOverrideProperty = false;
                bool isSealedProperty = false;
                if (!getterHandle.IsNil)
                {
                    var getter = reader.GetMethodDefinition(getterHandle);
                    var getterAttributes = getter.Attributes;
                    bestAccess = getter.Attributes & MethodAttributes.MemberAccessMask;
                    isStaticProperty = (getterAttributes & MethodAttributes.Static) != 0;
                    isVirtualProperty = (getterAttributes & MethodAttributes.Virtual) != 0;
                    isAbstractProperty = (getterAttributes & MethodAttributes.Abstract) != 0;
                    isOverrideProperty = isVirtualProperty && (getterAttributes & MethodAttributes.NewSlot) == 0;
                    isSealedProperty = isOverrideProperty && (getterAttributes & MethodAttributes.Final) != 0;
                }
                if (!setterHandle.IsNil)
                {
                    var setter = reader.GetMethodDefinition(setterHandle);
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
                    getterHandle,
                    setterHandle,
                    typeNullableContext,
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
                    GetterToken = getterHandle.IsNil ? null : MetadataTokens.GetToken(getterHandle),
                    SetterToken = setterHandle.IsNil ? null : MetadataTokens.GetToken(setterHandle),
                    HasGetter = !getterHandle.IsNil,
                    GetterAccessibility = getterHandle.IsNil
                        ? null
                        : GetAccessibility(
                            reader.GetMethodDefinition(getterHandle)
                                .Attributes
                                & MethodAttributes.MemberAccessMask),
                    HasSetter = !setterHandle.IsNil,
                    SetterAccessibility = setterHandle.IsNil
                        ? null
                        : GetAccessibility(
                            reader.GetMethodDefinition(setterHandle)
                                .Attributes
                                & MethodAttributes.MemberAccessMask),
                };

                budget?.RetainMember(member);
                apiType.Members.Add(member);
                surface.PublicPropertyCount++;
            }

            // Fields (non-backing fields; non-public included with --all)
            bool isEnum = apiType.Kind == "enum";

            // A C# field-like event's compiler-generated backing field is private, is itself
            // marked [CompilerGenerated], and shares the event's exact (unmangled) name. That
            // pre-scan and the per-field fold below are factored into shared helpers so
            // API-surface extraction and compile-back reconstruction agree on the fold.
            var fieldLikeEventBackingFieldNames = FieldLikeEventBackingFieldNames(
                reader,
                typeDef,
                observeDecodeWork);
            var autoPropertyBackingFields = AutoPropertyBackingFieldDescriptors(
                reader,
                typeDef,
                typeContext,
                observeText,
                observeDecodeWork);

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

                // Decode field type. For enums the special value__ field carries
                // the underlying type; literal fields are constants, not fields in
                // source, so they do not need a field declaration type.
                string? fieldType = null;
                bool fieldSignatureDegraded = false;
                List<ApiTypeReferenceIdentity> fieldTypeReferences = [];
                if (isEnum)
                {
                    if (fieldName == "value__")
                        apiType.EnumUnderlyingType = DecodeFieldType(
                            reader,
                            typeContext,
                            field,
                            typeNullableContext,
                            observeText,
                            observeDecodeWork).Text;
                }
                else
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

                if (!TrySelectEventAccessors(
                        reader,
                        accessors,
                        accessorMethods,
                        eventHandle,
                        includeAll,
                        out var selectedAccessors))
                {
                    continue;
                }
                var representative = selectedAccessors.Representative;
                var representativeAccess = selectedAccessors.Accessibility;

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
                var eventType = ResolveRequiredTypeName(
                    reader,
                    evt.Type,
                    typeContext,
                    observeText,
                    observeDecodeWork);
                var eventNullableBytes = NullabilityReader.GetNullableBytes(
                    reader,
                    evt.GetCustomAttributes(),
                    observeDecodeWork);
                eventNullableBytes ??= NullabilityReader.GetParameterNullableBytes(
                    reader,
                    representative.GetParameters(),
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
                var representativeAttributes = representative.Attributes;
                var isVirtualEvent = (representativeAttributes & MethodAttributes.Virtual) != 0;
                var isOverrideEvent = isVirtualEvent && (representativeAttributes & MethodAttributes.NewSlot) == 0;
                var accessorModels = new List<ApiAccessor>();
                if (!selectedAccessors.Adder.IsNil)
                {
                    accessorModels.Add(new ApiAccessor
                    {
                        Kind = "add",
                        ReturnAttributes = ReturnParameterAttributes(
                            reader,
                            reader.GetMethodDefinition(selectedAccessors.Adder).GetParameters(),
                            observeText,
                            observeAttributeMaterialize)
                    });
                }
                if (!selectedAccessors.Remover.IsNil)
                {
                    accessorModels.Add(new ApiAccessor
                    {
                        Kind = "remove",
                        ReturnAttributes = ReturnParameterAttributes(
                            reader,
                            reader.GetMethodDefinition(selectedAccessors.Remover).GetParameters(),
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
                    ReturnType = eventType,
                    Signature = $"{eventType} {SanitizeIdentifier(eventName)}",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = eventType,
                        MemberName = eventName,
                        Accessors = accessorModels
                    },
                    IsStatic = (representativeAttributes & MethodAttributes.Static) != 0,
                    IsVirtual = isVirtualEvent,
                    IsAbstract = (representativeAttributes & MethodAttributes.Abstract) != 0,
                    IsOverride = isOverrideEvent,
                    IsSealed = isOverrideEvent && (representativeAttributes & MethodAttributes.Final) != 0,
                    Accessibility = GetAccessibility(representativeAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage,
                    AdderToken = selectedAccessors.Adder.IsNil
                        ? null
                        : MetadataTokens.GetToken(selectedAccessors.Adder),
                    RemoverToken = selectedAccessors.Remover.IsNil
                        ? null
                        : MetadataTokens.GetToken(selectedAccessors.Remover)
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
                    owningTypeDefinition: owningTypeDefinition);
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
                    owningTypeDefinition: owningTypeDefinition);
            }
        }

        if (materializationContext.TryGetCachedIndexFailure(
                out MetadataTypeNameFailure? indexFailure))
        {
            AddInspectionFailure(
                surface,
                budget,
                "enum attribute type index",
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

    private static void CountSummaryMembers(
        MetadataReader reader,
        TypeDefinitionHandle typeDefHandle,
        TypeDefinition typeDef,
        ApiType apiType,
        ApiSurface surface,
        bool isExtensionClass,
        Dictionary<ApiMember, MetadataTypeDefinitionName> extensionReceiverDefinitions,
        AccessorMethodOwnership accessorMethods,
        MetadataTypeScope? currentScope,
        IReadOnlyDictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> localTypes)
    {
        var methodImplementations = ReadOwnedMethodImplementations(
            reader,
            typeDefHandle,
            typeDef,
            surface,
            budget: null,
            apiType.DefinitionName,
            currentScope,
            localTypes,
            beforeDecodeWork: null);
        var explicitImplementationBodies = GetExplicitImplementationBodies(
            reader,
            typeDef,
            methodImplementations,
            currentScope,
            localTypes,
            beforeDecodeWork: null);
        var objectFinalizeOverrides = GetObjectFinalizeOverrides(
            reader,
            methodImplementations);
        bool isFinalizerOwner = IsFinalizerOwner(typeDef, apiType.Kind);

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var methodAccess = method.Attributes & MethodAttributes.MemberAccessMask;
            bool isExplicitImplementation = explicitImplementationBodies.Contains(methodHandle);
            string methodName = reader.GetString(method.Name);
            bool isRetainedExplicitImplementation = isExplicitImplementation
                && (!accessorMethods.Contains(methodHandle)
                    || methodAccess != MethodAttributes.Public
                    || methodName.Contains('.', StringComparison.Ordinal));
            bool isFinalizer = isFinalizerOwner
                && method.GetGenericParameters().Count == 0
                && (objectFinalizeOverrides.Contains(methodHandle)
                    || IsImplicitObjectFinalizeOverride(
                        reader,
                        typeDefHandle,
                        method,
                        currentScope,
                        localTypes));
            if (methodAccess != MethodAttributes.Public
                && !isRetainedExplicitImplementation
                && !isFinalizer)
            {
                continue;
            }

            if ((accessorMethods.Contains(methodHandle) && !isRetainedExplicitImplementation)
                || methodName.StartsWith('<'))
            {
                continue;
            }

            if (!isRetainedExplicitImplementation
                && !isFinalizer
                && AttributeReader.HasEditorBrowsableNeverAttribute(reader, method.GetCustomAttributes()))
            {
                continue;
            }

            var member = new ApiMember
            {
                Name = methodName,
                Kind = "method",
                IsStatic = (method.Attributes & MethodAttributes.Static) != 0
            };
            if (isExtensionClass
                && member.IsStatic
                && AttributeReader.HasExtensionAttribute(
                    reader,
                    method.GetCustomAttributes()))
            {
                int token = MetadataTokens.GetToken(methodHandle);
                member.IsExtension = true;
                member.ExtendedType = GetFirstParameterType(reader, typeDef, method);
                if (GetFirstParameterDefinitionName(reader, typeDef, method)
                    is { } receiverDefinition)
                {
                    extensionReceiverDefinitions.Add(member, receiverDefinition);
                }
                member.DeclaringType = apiType.FullName;
                member.MetadataToken = token;
                member.Signature = token.ToString("X8", CultureInfo.InvariantCulture);
            }

            apiType.Members.Add(member);
            surface.PublicMethodCount++;
        }

        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var accessors = property.GetAccessors();
            MethodDefinitionHandle getterHandle =
                !accessors.Getter.IsNil
                && accessorMethods.Contains(propertyHandle, accessors.Getter)
                    ? accessors.Getter
                    : default;
            MethodDefinitionHandle setterHandle =
                !accessors.Setter.IsNil
                && accessorMethods.Contains(propertyHandle, accessors.Setter)
                    ? accessors.Setter
                    : default;
            if (getterHandle.IsNil && setterHandle.IsNil)
                continue;

            MethodAttributes bestAccess = 0;
            if (!getterHandle.IsNil)
            {
                bestAccess = reader.GetMethodDefinition(getterHandle).Attributes
                    & MethodAttributes.MemberAccessMask;
            }
            if (!setterHandle.IsNil)
            {
                var setterAccess = reader.GetMethodDefinition(setterHandle).Attributes
                    & MethodAttributes.MemberAccessMask;
                if (setterAccess > bestAccess)
                    bestAccess = setterAccess;
            }

            if (bestAccess != MethodAttributes.Public
                || AttributeReader.HasEditorBrowsableNeverAttribute(reader, property.GetCustomAttributes()))
            {
                continue;
            }

            apiType.Members.Add(new ApiMember
            {
                Name = reader.GetString(property.Name),
                Kind = "property"
            });
            surface.PublicPropertyCount++;
        }

        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public)
                continue;

            string fieldName = reader.GetString(field.Name);
            if (!IsSurfaceableFieldName(fieldName, includeCompilerGenerated: false)
                || AttributeReader.HasEditorBrowsableNeverAttribute(reader, field.GetCustomAttributes()))
            {
                continue;
            }

            apiType.Members.Add(new ApiMember { Name = fieldName, Kind = "field" });
            surface.PublicFieldCount++;
        }

        foreach (var eventHandle in typeDef.GetEvents())
        {
            var evt = reader.GetEventDefinition(eventHandle);
            var accessors = evt.GetAccessors();
            if (!TrySelectEventAccessors(
                    reader,
                    accessors,
                    accessorMethods,
                    eventHandle,
                    includeAll: false,
                    out _))
            {
                continue;
            }
            if (AttributeReader.HasEditorBrowsableNeverAttribute(
                    reader,
                    evt.GetCustomAttributes()))
            {
                continue;
            }

            apiType.Members.Add(new ApiMember
            {
                Name = reader.GetString(evt.Name),
                Kind = "event"
            });
            surface.PublicEventCount++;
        }
    }

    private static void ExtractTypeForwarders(
        MetadataReader reader,
        ApiSurface surface,
        ExtractionBudget? budget = null)
    {
        Action<int>? observeDecodeWork = budget is null
            ? null
            : budget.ObservePendingDecodeWork;
        foreach (var exportedTypeHandle in reader.ExportedTypes)
        {
            try
            {
                var exportedType = reader.GetExportedType(exportedTypeHandle);

                // Type forwarders have IsForwarder flag set
                if (!exportedType.IsForwarder)
                    continue;

                budget?.BeginTypeForwarder();
                MetadataTypeDefinitionName? definitionName;
                string fullName;
                if (budget is null)
                {
                    fullName = reader.ResolveFullTypeName(exportedTypeHandle) switch
                    {
                        RelationshipTraversalResult<string>.Completed completed =>
                            completed.Value,
                        RelationshipTraversalResult<string>.Rejected rejected =>
                            throw new MetadataRowRejectedException(
                                "type forwarder identity",
                                MetadataTypeNameFailure.From(rejected.Rejection)),
                        _ => throw new InvalidOperationException(
                            "Unknown exported-type relationship result."),
                    };
                    definitionName =
                        MetadataTypeDefinitionNameReader.Read(
                            reader,
                            exportedTypeHandle)
                        is MetadataTypeDefinitionNameReadResult.Read read
                            ? read.Name
                            : null;
                }
                else
                {
                    definitionName =
                        MetadataTypeDefinitionNameReader.Read(
                            reader,
                            exportedTypeHandle,
                            observeDecodeWork) switch
                        {
                            MetadataTypeDefinitionNameReadResult.Read read => read.Name,
                            MetadataTypeDefinitionNameReadResult.Rejected rejected =>
                                throw new MetadataRowRejectedException(
                                    "type forwarder identity",
                                    rejected.Failure),
                            _ => throw new InvalidOperationException(
                                "Unknown exported-type name result."),
                        };
                    fullName = definitionName.ToMetadataFullName();
                }

                // Get the target assembly
                string targetAssembly = "";
                if (exportedType.Implementation.Kind == HandleKind.AssemblyReference)
                {
                    var assemblyRef = reader.GetAssemblyReference((AssemblyReferenceHandle)exportedType.Implementation);
                    targetAssembly = budget is null
                        ? reader.GetString(assemblyRef.Name)
                        : DecodeString(
                            reader,
                            assemblyRef.Name,
                            observeDecodeWork);
                }

                var typeForwarder = new TypeForwarder
                {
                    DefinitionName = definitionName,
                    TypeName = fullName,
                    TargetAssembly = targetAssembly
                };
                budget?.RetainTypeForwarder(typeForwarder);
                surface.TypeForwarders.Add(typeForwarder);
            }
            catch (MetadataRowRejectedException ex)
            {
                AddInspectionFailure(
                    surface,
                    budget,
                    ex.Operation,
                    exportedTypeHandle,
                    ex.Failure);
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                AddInspectionFailure(
                    surface,
                    budget,
                    "type forwarder row",
                    exportedTypeHandle,
                    MetadataTypeNameFailure.Malformed(exportedTypeHandle, ex.Message));
            }
        }
    }

    static AccessorMethodOwnership ReadAccessorMethods(
        MetadataReader reader,
        ApiSurface surface,
        ExtractionBudget? budget,
        MetadataTypeScope? currentScope)
    {
        var methods = new AccessorMethodOwnership();
        Dictionary<EntityHandle, TypeDefinitionHandle> associationOwners = [];
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            try
            {
                var type = reader.GetTypeDefinition(typeHandle);
                foreach (var propertyHandle in type.GetProperties())
                {
                    associationOwners[(EntityHandle)propertyHandle] = typeHandle;
                }
                foreach (var eventHandle in type.GetEvents())
                {
                    associationOwners[(EntityHandle)eventHandle] = typeHandle;
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                AddInspectionFailure(
                    surface,
                    budget,
                    "accessor owner",
                    typeHandle,
                    MetadataTypeNameFailure.Malformed(typeHandle, ex.Message));
            }
        }

        HashSet<PhysicalAccessorRole> roles = [];
        int semanticsRows = reader.GetTableRowCount(TableIndex.MethodSemantics);
        for (int rowId = 1; rowId <= semanticsRows; rowId++)
        {
            try
            {
                var semantics = ReadMethodSemanticsRow(reader, rowId);
                if (!TryGetAccessorRole(
                        semantics.Association,
                        semantics.Attributes,
                        out var role,
                        out string? operation))
                {
                    continue;
                }

                if (!associationOwners.TryGetValue(
                        semantics.Association,
                        out TypeDefinitionHandle owningType))
                {
                    AddInspectionFailure(
                        surface,
                        budget,
                        operation,
                        semantics.Association,
                        MetadataTypeNameFailure.Malformed(
                            semantics.Association,
                            "The MethodSemantics association has no TypeDef owner."));
                    continue;
                }

                if (!roles.Add(new PhysicalAccessorRole(
                        semantics.Association,
                        semantics.Attributes)))
                {
                    AddInspectionFailure(
                        surface,
                        budget,
                        operation,
                        semantics.Association,
                        MetadataTypeNameFailure.Malformed(
                            semantics.Association,
                            $"The physical MethodSemantics {semantics.Attributes} role "
                            + "appears more than once for this association."));
                }

                AddOwnedAccessor(
                    reader,
                    surface,
                    budget,
                    methods,
                    owningType,
                    semantics.Association,
                    semantics.Method,
                    operation);
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or OverflowException)
            {
                AddInspectionFailure(
                    surface,
                    budget,
                    "method semantics",
                    default,
                    MetadataTypeNameFailure.Malformed(default, ex.Message));
            }
        }

        methods.AddExtensionImplementations(
            ExtensionPropertyImplementationMethods(
                reader,
                surface,
                budget,
                currentScope));
        return methods;
    }

    private readonly record struct PhysicalAccessorRole(
        EntityHandle Association,
        MethodSemanticsAttributes Attributes);

    private readonly record struct MethodSemanticsRow(
        MethodDefinitionHandle Method,
        EntityHandle Association,
        MethodSemanticsAttributes Attributes);

    private static bool TryGetAccessorRole(
        EntityHandle association,
        MethodSemanticsAttributes attributes,
        out MethodSemanticsAttributes role,
        out string operation)
    {
        role = attributes;
        operation = (association.Kind, attributes) switch
        {
            (HandleKind.PropertyDefinition, MethodSemanticsAttributes.Getter) =>
                "property getter",
            (HandleKind.PropertyDefinition, MethodSemanticsAttributes.Setter) =>
                "property setter",
            (HandleKind.EventDefinition, MethodSemanticsAttributes.Adder) =>
                "event adder",
            (HandleKind.EventDefinition, MethodSemanticsAttributes.Remover) =>
                "event remover",
            _ => "",
        };
        return operation.Length != 0;
    }

    private static unsafe MethodSemanticsRow ReadMethodSemanticsRow(
        MetadataReader reader,
        int rowId)
    {
        const int SemanticsFlagsSize = sizeof(ushort);
        int methodIndexSize =
            reader.GetTableRowCount(TableIndex.MethodDef) <= ushort.MaxValue
                ? sizeof(ushort)
                : sizeof(int);
        int associationIndexSize =
            Math.Max(
                reader.GetTableRowCount(TableIndex.Event),
                reader.GetTableRowCount(TableIndex.Property))
            <= 0x7fff
                ? sizeof(ushort)
                : sizeof(int);
        int rowSize = reader.GetTableRowSize(TableIndex.MethodSemantics);
        if (rowSize != SemanticsFlagsSize + methodIndexSize + associationIndexSize)
        {
            throw new BadImageFormatException(
                "The MethodSemantics row size does not match its ECMA-335 schema.");
        }

        int tableOffset = reader.GetTableMetadataOffset(TableIndex.MethodSemantics);
        long rowEnd = checked(
            (long)tableOffset + (long)rowId * rowSize);
        if (tableOffset < 0
            || rowId < 1
            || rowEnd > reader.MetadataLength)
        {
            throw new BadImageFormatException(
                "The MethodSemantics row lies outside the metadata image.");
        }

        int rowOffset = checked(
            tableOffset + checked((rowId - 1) * rowSize));
        byte* metadata = reader.MetadataPointer;
        var attributes = (MethodSemanticsAttributes)unchecked(
            (ushort)(metadata[rowOffset] | metadata[rowOffset + 1] << 8));
        int methodRow = ReadMetadataIndex(
            metadata,
            checked(rowOffset + SemanticsFlagsSize),
            methodIndexSize);
        int association = ReadMetadataIndex(
            metadata,
            checked(rowOffset + SemanticsFlagsSize + methodIndexSize),
            associationIndexSize);
        if (methodRow < 1
            || methodRow > reader.GetTableRowCount(TableIndex.MethodDef))
        {
            throw new BadImageFormatException(
                "The MethodSemantics method index is outside the MethodDef table.");
        }

        int associationRow = association >> 1;
        bool isProperty = (association & 1) != 0;
        int associationLimit = reader.GetTableRowCount(
            isProperty ? TableIndex.Property : TableIndex.Event);
        if (associationRow < 1 || associationRow > associationLimit)
        {
            throw new BadImageFormatException(
                "The MethodSemantics association is outside its target table.");
        }

        EntityHandle associationHandle = isProperty
            ? (EntityHandle)MetadataTokens.PropertyDefinitionHandle(associationRow)
            : MetadataTokens.EventDefinitionHandle(associationRow);
        return new MethodSemanticsRow(
            MetadataTokens.MethodDefinitionHandle(methodRow),
            associationHandle,
            attributes);
    }

    private static unsafe int ReadMetadataIndex(
        byte* metadata,
        int offset,
        int size)
        => size == sizeof(ushort)
            ? metadata[offset] | metadata[offset + 1] << 8
            : metadata[offset]
                | metadata[offset + 1] << 8
                | metadata[offset + 2] << 16
                | metadata[offset + 3] << 24;

    private sealed class AccessorMethodOwnership
    {
        readonly HashSet<MethodDefinitionHandle> _methods = [];
        readonly HashSet<(EntityHandle Association, MethodDefinitionHandle Method)> _associations = [];

        internal bool Contains(MethodDefinitionHandle method) => _methods.Contains(method);

        internal bool Contains(EntityHandle association, MethodDefinitionHandle method)
            => _associations.Contains((association, method));

        internal void Add(EntityHandle association, MethodDefinitionHandle method)
        {
            _methods.Add(method);
            _associations.Add((association, method));
        }

        internal void AddExtensionImplementations(
            IEnumerable<MethodDefinitionHandle> methods)
        {
            _methods.UnionWith(methods);
        }
    }

    private static void AddOwnedAccessor(
        MetadataReader reader,
        ApiSurface surface,
        ExtractionBudget? budget,
        AccessorMethodOwnership methods,
        TypeDefinitionHandle owningType,
        EntityHandle association,
        MethodDefinitionHandle accessor,
        string operation)
    {
        if (accessor.IsNil)
            return;

        try
        {
            var declaringType = reader.GetMethodDefinition(accessor).GetDeclaringType();
            if (declaringType == owningType)
            {
                methods.Add(association, accessor);
                return;
            }

            AddInspectionFailure(
                surface,
                budget,
                operation,
                association,
                MetadataTypeNameFailure.Malformed(
                    association,
                    $"Accessor 0x{MetadataTokens.GetToken(accessor):x8} belongs to type "
                    + $"0x{MetadataTokens.GetToken(declaringType):x8}, not association owner "
                    + $"0x{MetadataTokens.GetToken(owningType):x8}."));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            AddInspectionFailure(
                surface,
                budget,
                operation,
                association,
                MetadataTypeNameFailure.Malformed(association, ex.Message));
        }
    }

    private readonly record struct EventAccessorSelection(
        MethodDefinitionHandle Adder,
        MethodDefinitionHandle Remover,
        MethodDefinition Representative,
        MethodAttributes Accessibility);

    private static bool TrySelectEventAccessors(
        MetadataReader reader,
        EventAccessors accessors,
        AccessorMethodOwnership ownedAccessorMethods,
        EventDefinitionHandle association,
        bool includeAll,
        out EventAccessorSelection selection)
    {
        MethodDefinitionHandle adder = Owned(accessors.Adder);
        MethodDefinitionHandle remover = Owned(accessors.Remover);
        MethodDefinitionHandle representativeHandle = default;
        MethodDefinition representative = default;
        MethodAttributes bestAccess = 0;

        Consider(adder);
        Consider(remover);
        if (representativeHandle.IsNil
            || (!includeAll && bestAccess != MethodAttributes.Public))
        {
            selection = default;
            return false;
        }

        selection = new EventAccessorSelection(
            adder,
            remover,
            representative,
            bestAccess);
        return true;

        MethodDefinitionHandle Owned(MethodDefinitionHandle handle) =>
            !handle.IsNil && ownedAccessorMethods.Contains(association, handle)
                ? handle
                : default;

        void Consider(MethodDefinitionHandle handle)
        {
            if (handle.IsNil)
                return;

            var candidate = reader.GetMethodDefinition(handle);
            var access = candidate.Attributes & MethodAttributes.MemberAccessMask;
            if (representativeHandle.IsNil || access > bestAccess)
            {
                representativeHandle = handle;
                representative = candidate;
                bestAccess = access;
            }
        }
    }

    static HashSet<MethodDefinitionHandle> ExtensionPropertyImplementationMethods(
        MetadataReader reader,
        ApiSurface surface,
        ExtractionBudget? budget,
        MetadataTypeScope? currentScope)
    {
        Action<int>? beforeDecodeWork =
            budget is null ? null : budget.ObservePendingDecodeWork;
        HashSet<MethodDefinitionHandle> methods = [];
        foreach (var extensionClassHandle in reader.TypeDefinitions)
        {
            try
            {
                var extensionClass = reader.GetTypeDefinition(extensionClassHandle);
                if (!AttributeReader.HasExtensionAttribute(
                        reader,
                        extensionClass.GetCustomAttributes(),
                        beforeDecodeWork))
                {
                    continue;
                }
                var extensionClassContext = GenericContext.ForType(
                    reader,
                    extensionClass,
                    beforeDecodeWork);

                foreach (var groupingTypeHandle in extensionClass.GetNestedTypes())
                {
                    var groupingType = reader.GetTypeDefinition(groupingTypeHandle);
                    if (!AttributeReader.HasExtensionAttribute(
                            reader,
                            groupingType.GetCustomAttributes(),
                            beforeDecodeWork))
                    {
                        continue;
                    }

                    foreach (var propertyHandle in groupingType.GetProperties())
                    {
                        var property = reader.GetPropertyDefinition(propertyHandle);
                        var accessors = property.GetAccessors();
                        if (!accessors.Getter.IsNil
                                && reader.GetMethodDefinition(accessors.Getter).GetDeclaringType()
                                    != groupingTypeHandle
                            || !accessors.Setter.IsNil
                                && reader.GetMethodDefinition(accessors.Setter).GetDeclaringType()
                                    != groupingTypeHandle)
                        {
                            continue;
                        }

                        if (!TryGetExtensionMarkerName(
                                reader,
                                property,
                                accessors,
                                out string? markerName,
                                beforeDecodeWork))
                        {
                            continue;
                        }

                        var markerTypeHandle = groupingType.GetNestedTypes().FirstOrDefault(handle =>
                            reader.StringComparer.Equals(
                                reader.GetTypeDefinition(handle).Name,
                                markerName!));
                        if (markerTypeHandle.IsNil)
                            continue;
                        var markerType = reader.GetTypeDefinition(markerTypeHandle);
                        var markerMethodHandle = markerType.GetMethods().FirstOrDefault(handle =>
                            reader.StringComparer.Equals(
                                reader.GetMethodDefinition(handle).Name,
                                "<Extension>$"));
                        if (markerMethodHandle.IsNil)
                            continue;

                        var context = GenericContext.ForType(
                            reader,
                            markerType,
                            beforeDecodeWork);
                        if (!TryDecodeExtensionSignature(
                                reader,
                                reader.GetMethodDefinition(markerMethodHandle),
                                context,
                                currentScope,
                                beforeDecodeWork,
                                out var markerSignature)
                            || markerSignature.ParameterTypes.Length != 1
                            || !TryDecodeExtensionSignature(
                                reader,
                                property,
                                context,
                                currentScope,
                                beforeDecodeWork,
                                out var propertySignature))
                        {
                            continue;
                        }

                        string propertyName = DecodeString(
                            reader,
                            property.Name,
                            beforeDecodeWork);
                        int implementationGenericArity = groupingType.GetGenericParameters().Count;
                        foreach (var methodHandle in extensionClass.GetMethods())
                        {
                            var method = reader.GetMethodDefinition(methodHandle);
                            string methodName = DecodeString(
                                reader,
                                method.Name,
                                beforeDecodeWork);
                            bool getter = !accessors.Getter.IsNil
                                && HasAccessorName(methodName, "get_", propertyName);
                            bool setter = !accessors.Setter.IsNil
                                && HasAccessorName(methodName, "set_", propertyName);
                            if (!getter && !setter)
                                continue;
                            if (!TryDecodeExtensionSignature(
                                    reader,
                                    method,
                                    GenericContext.ForMethod(
                                        reader,
                                        extensionClassContext,
                                        method,
                                        beforeDecodeWork),
                                    currentScope,
                                    beforeDecodeWork,
                                    out var implementationSignature))
                            {
                                continue;
                            }

                            var markerAccessorHandle = getter
                                ? accessors.Getter
                                : accessors.Setter;
                            bool isStaticExtensionMember = reader
                                .GetMethodDefinition(markerAccessorHandle)
                                .Attributes.HasFlag(MethodAttributes.Static);
                            if (ExtensionParametersMatch(
                                    implementationSignature.ParameterTypes,
                                    propertySignature,
                                    markerSignature.ParameterTypes[0],
                                    includeReceiver: !isStaticExtensionMember,
                                    includeValue: setter)
                                && method.GetGenericParameters().Count == implementationGenericArity
                                && (getter
                                    ? ExtensionSignatureTypesEqual(
                                        implementationSignature.ReturnType,
                                        propertySignature.ReturnType)
                                    : implementationSignature.ReturnType
                                        is PrimitiveExtensionSignatureType
                                        {
                                            TypeCode: PrimitiveTypeCode.Void
                                        }))
                            {
                                methods.Add(methodHandle);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                AddInspectionFailure(
                    surface,
                    budget,
                    "extension property accessors",
                    extensionClassHandle,
                    MetadataTypeNameFailure.Malformed(extensionClassHandle, ex.Message));
            }
        }
        return methods;
    }

    private readonly record struct ExtensionDecodedSignature(
        ExtensionSignatureType ReturnType,
        ImmutableArray<ExtensionSignatureType> ParameterTypes);

    private static bool TryDecodeExtensionSignature(
        MetadataReader reader,
        MethodDefinition method,
        GenericContext context,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork,
        out ExtensionDecodedSignature signature)
    {
        var provider = new ExtensionSignatureTypeProvider(
            currentScope,
            beforeDecodeWork);
        var decoded = GuardedProviderDecode.MethodResult(
            reader,
            method,
            provider,
            context,
            fallbackReturn: null);
        return TryReadExtensionSignature(decoded, out signature);
    }

    private static bool TryDecodeExtensionSignature(
        MetadataReader reader,
        PropertyDefinition property,
        GenericContext context,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork,
        out ExtensionDecodedSignature signature)
    {
        var provider = new ExtensionSignatureTypeProvider(
            currentScope,
            beforeDecodeWork);
        var decoded = GuardedProviderDecode.PropertyResult(
            reader,
            property,
            provider,
            context,
            fallbackReturn: null);
        return TryReadExtensionSignature(decoded, out signature);
    }

    private static bool TryReadExtensionSignature(
        GuardedProviderDecode.DecodeResult<
            MethodSignature<ExtensionSignatureType?>> decoded,
        out ExtensionDecodedSignature signature)
    {
        if (decoded.IsDegraded
            || decoded.Value.ReturnType is null
            || decoded.Value.ParameterTypes.Any(static parameter => parameter is null))
        {
            signature = default;
            return false;
        }

        var parameterTypes = ImmutableArray.CreateBuilder<ExtensionSignatureType>(
            decoded.Value.ParameterTypes.Length);
        foreach (ExtensionSignatureType? parameter in decoded.Value.ParameterTypes)
            parameterTypes.Add(parameter!);
        signature = new ExtensionDecodedSignature(
            decoded.Value.ReturnType,
            parameterTypes.MoveToImmutable());
        return true;
    }

    private abstract record ExtensionSignatureType;

    private sealed record PrimitiveExtensionSignatureType(
        PrimitiveTypeCode TypeCode)
        : ExtensionSignatureType;

    private sealed record NamedExtensionSignatureType(
        MetadataNamedTypeIdentity Identity)
        : ExtensionSignatureType;

    private sealed record ArrayExtensionSignatureType(
        ExtensionSignatureType ElementType,
        int Rank,
        ImmutableArray<int> Sizes,
        ImmutableArray<int> LowerBounds)
        : ExtensionSignatureType;

    private sealed record WrappedExtensionSignatureType(
        ExtensionSignatureType ElementType,
        ExtensionSignatureTypeKind Kind)
        : ExtensionSignatureType;

    private sealed record GenericExtensionSignatureType(
        ExtensionSignatureType GenericType,
        ImmutableArray<ExtensionSignatureType> TypeArguments)
        : ExtensionSignatureType;

    private sealed record GenericParameterExtensionSignatureType(
        string? Name,
        bool IsMethodParameter,
        int Index)
        : ExtensionSignatureType;

    private sealed record FunctionPointerExtensionSignatureType(
        SignatureCallingConvention CallingConvention,
        SignatureAttributes Attributes,
        int GenericParameterCount,
        int RequiredParameterCount,
        ImmutableArray<ExtensionSignatureType> ParameterTypes,
        ExtensionSignatureType ReturnType)
        : ExtensionSignatureType;

    private sealed record ModifiedExtensionSignatureType(
        ExtensionSignatureType Modifier,
        ExtensionSignatureType UnmodifiedType,
        bool IsRequired)
        : ExtensionSignatureType;

    private enum ExtensionSignatureTypeKind
    {
        SzArray,
        ByReference,
        Pointer,
        Pinned,
    }

    private sealed class ExtensionSignatureTypeProvider(
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork)
        : ISignatureTypeProvider<ExtensionSignatureType?, GenericContext?>
    {
        public ExtensionSignatureType? GetPrimitiveType(
            PrimitiveTypeCode typeCode)
        {
            Observe(16);
            return new PrimitiveExtensionSignatureType(typeCode);
        }

        public ExtensionSignatureType? GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
            => currentScope is null
                ? null
                : ReadDefinitionIdentity(
                    reader,
                    handle,
                    currentScope,
                    beforeDecodeWork) is { } identity
                    ? new NamedExtensionSignatureType(identity)
                    : null;

        public ExtensionSignatureType? GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
            => ReadReferenceIdentity(
                    reader,
                    handle,
                    currentScope,
                    beforeDecodeWork) is { } identity
                ? new NamedExtensionSignatureType(identity)
                : null;

        public ExtensionSignatureType? GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
            => GuardedProviderDecode.TypeSpec(
                reader,
                handle,
                this,
                context,
                fallback: null);

        public ExtensionSignatureType? GetSZArrayType(
            ExtensionSignatureType? elementType)
            => Wrap(elementType, ExtensionSignatureTypeKind.SzArray);

        public ExtensionSignatureType? GetArrayType(
            ExtensionSignatureType? elementType,
            ArrayShape shape)
        {
            Observe(checked(16L + Math.Max(shape.Rank, 0)));
            return elementType is null
                ? null
                : new ArrayExtensionSignatureType(
                    elementType,
                    shape.Rank,
                    shape.Sizes,
                    shape.LowerBounds);
        }

        public ExtensionSignatureType? GetByReferenceType(
            ExtensionSignatureType? elementType)
            => Wrap(elementType, ExtensionSignatureTypeKind.ByReference);

        public ExtensionSignatureType? GetPointerType(
            ExtensionSignatureType? elementType)
            => Wrap(elementType, ExtensionSignatureTypeKind.Pointer);

        public ExtensionSignatureType? GetPinnedType(
            ExtensionSignatureType? elementType)
            => Wrap(elementType, ExtensionSignatureTypeKind.Pinned);

        public ExtensionSignatureType? GetGenericInstantiation(
            ExtensionSignatureType? genericType,
            ImmutableArray<ExtensionSignatureType?> typeArguments)
        {
            Observe(checked(16L + typeArguments.Length * 8L));
            if (genericType is null
                || typeArguments.Any(static argument => argument is null))
            {
                return null;
            }

            var arguments = ImmutableArray.CreateBuilder<ExtensionSignatureType>(
                typeArguments.Length);
            foreach (ExtensionSignatureType? argument in typeArguments)
                arguments.Add(argument!);
            return new GenericExtensionSignatureType(
                genericType,
                arguments.MoveToImmutable());
        }

        public ExtensionSignatureType? GetGenericTypeParameter(
            GenericContext? context,
            int index)
            => GenericParameter(
                context?.TypeParameters,
                index,
                isMethodParameter: false);

        public ExtensionSignatureType? GetGenericMethodParameter(
            GenericContext? context,
            int index)
            => GenericParameter(
                context?.MethodParameters,
                index,
                isMethodParameter: true);

        public ExtensionSignatureType? GetFunctionPointerType(
            MethodSignature<ExtensionSignatureType?> signature)
        {
            Observe(checked(16L + signature.ParameterTypes.Length * 8L));
            if (signature.ReturnType is null
                || signature.ParameterTypes.Any(static parameter => parameter is null))
            {
                return null;
            }

            var parameters = ImmutableArray.CreateBuilder<ExtensionSignatureType>(
                signature.ParameterTypes.Length);
            foreach (ExtensionSignatureType? parameter in signature.ParameterTypes)
                parameters.Add(parameter!);
            return new FunctionPointerExtensionSignatureType(
                signature.Header.CallingConvention,
                signature.Header.Attributes,
                signature.GenericParameterCount,
                signature.RequiredParameterCount,
                parameters.MoveToImmutable(),
                signature.ReturnType);
        }

        public ExtensionSignatureType? GetModifiedType(
            ExtensionSignatureType? modifier,
            ExtensionSignatureType? unmodifiedType,
            bool isRequired)
        {
            Observe(16);
            return modifier is null || unmodifiedType is null
                ? null
                : new ModifiedExtensionSignatureType(
                    modifier,
                    unmodifiedType,
                    isRequired);
        }

        ExtensionSignatureType? Wrap(
            ExtensionSignatureType? elementType,
            ExtensionSignatureTypeKind kind)
        {
            Observe(16);
            return elementType is null
                ? null
                : new WrappedExtensionSignatureType(elementType, kind);
        }

        ExtensionSignatureType? GenericParameter(
            IReadOnlyList<string>? parameters,
            int index,
            bool isMethodParameter)
        {
            Observe(16);
            if (index < 0)
                return null;

            string? name = parameters is not null && index < parameters.Count
                ? parameters[index]
                : null;
            return new GenericParameterExtensionSignatureType(
                name,
                isMethodParameter,
                index);
        }

        void Observe(long units) =>
            beforeDecodeWork?.Invoke((int)Math.Min(units, int.MaxValue));
    }

    // Extension markers join independently decoded property and implementation
    // signatures, whose generic contexts may carry equivalent source names.
    // MethodImpl ownership instead follows metadata identity, where that name
    // bridge could turn unrelated generic parameters into the same interface.
    // The structural representation remains shared so the guarded decoder and
    // safety accounting have one owner; only this extension-specific comparison
    // permits the source-name bridge.
    private static bool ExtensionSignatureTypesEqual(
        ExtensionSignatureType left,
        ExtensionSignatureType right)
        => SignatureTypesEqual(left, right, bridgeGenericParameterNames: true);

    private static bool MethodImplSignatureTypesEqual(
        ExtensionSignatureType left,
        ExtensionSignatureType right)
        => SignatureTypesEqual(left, right, bridgeGenericParameterNames: false);

    private static bool SignatureTypesEqual(
        ExtensionSignatureType left,
        ExtensionSignatureType right,
        bool bridgeGenericParameterNames)
        => (left, right) switch
        {
            (PrimitiveExtensionSignatureType l, PrimitiveExtensionSignatureType r) =>
                l.TypeCode == r.TypeCode,
            (NamedExtensionSignatureType l, NamedExtensionSignatureType r) =>
                MetadataNamedTypeIdentityComparer.Instance.Equals(
                    l.Identity,
                    r.Identity),
            (ArrayExtensionSignatureType l, ArrayExtensionSignatureType r) =>
                l.Rank == r.Rank
                && l.Sizes.SequenceEqual(r.Sizes)
                && l.LowerBounds.SequenceEqual(r.LowerBounds)
                && SignatureTypesEqual(
                    l.ElementType,
                    r.ElementType,
                    bridgeGenericParameterNames),
            (WrappedExtensionSignatureType l, WrappedExtensionSignatureType r) =>
                l.Kind == r.Kind
                && SignatureTypesEqual(
                    l.ElementType,
                    r.ElementType,
                    bridgeGenericParameterNames),
            (GenericExtensionSignatureType l, GenericExtensionSignatureType r) =>
                l.TypeArguments.Length == r.TypeArguments.Length
                && SignatureTypesEqual(
                    l.GenericType,
                    r.GenericType,
                    bridgeGenericParameterNames)
                && SignatureTypeSequencesEqual(
                    l.TypeArguments,
                    r.TypeArguments,
                    bridgeGenericParameterNames),
            (GenericParameterExtensionSignatureType l, GenericParameterExtensionSignatureType r) =>
                bridgeGenericParameterNames
                && l.Name is not null
                && r.Name is not null
                    ? StringComparer.Ordinal.Equals(l.Name, r.Name)
                    : l.IsMethodParameter == r.IsMethodParameter
                    && l.Index == r.Index,
            (FunctionPointerExtensionSignatureType l, FunctionPointerExtensionSignatureType r) =>
                l.CallingConvention == r.CallingConvention
                && l.Attributes == r.Attributes
                && l.GenericParameterCount == r.GenericParameterCount
                && l.RequiredParameterCount == r.RequiredParameterCount
                && SignatureTypesEqual(
                    l.ReturnType,
                    r.ReturnType,
                    bridgeGenericParameterNames)
                && SignatureTypeSequencesEqual(
                    l.ParameterTypes,
                    r.ParameterTypes,
                    bridgeGenericParameterNames),
            (ModifiedExtensionSignatureType l, ModifiedExtensionSignatureType r) =>
                l.IsRequired == r.IsRequired
                && SignatureTypesEqual(
                    l.Modifier,
                    r.Modifier,
                    bridgeGenericParameterNames)
                && SignatureTypesEqual(
                    l.UnmodifiedType,
                    r.UnmodifiedType,
                    bridgeGenericParameterNames),
            _ => false,
        };

    private static bool SignatureTypeSequencesEqual(
        ImmutableArray<ExtensionSignatureType> left,
        ImmutableArray<ExtensionSignatureType> right,
        bool bridgeGenericParameterNames)
    {
        if (left.Length != right.Length)
            return false;

        for (int index = 0; index < left.Length; index++)
        {
            if (!SignatureTypesEqual(
                    left[index],
                    right[index],
                    bridgeGenericParameterNames))
            {
                return false;
            }
        }
        return true;
    }

    private static int GetMethodImplSignatureTypeHashCode(
        ExtensionSignatureType type)
    {
        var hash = new HashCode();
        AddMethodImplSignatureTypeHashCode(ref hash, type);
        return hash.ToHashCode();
    }

    private static void AddMethodImplSignatureTypeHashCode(
        ref HashCode hash,
        ExtensionSignatureType type)
    {
        switch (type)
        {
            case PrimitiveExtensionSignatureType primitive:
                hash.Add(0);
                hash.Add(primitive.TypeCode);
                return;
            case NamedExtensionSignatureType named:
                hash.Add(1);
                hash.Add(
                    MetadataNamedTypeIdentityComparer.Instance.GetHashCode(
                        named.Identity));
                return;
            case ArrayExtensionSignatureType array:
                hash.Add(2);
                hash.Add(array.Rank);
                foreach (int size in array.Sizes)
                    hash.Add(size);
                foreach (int lowerBound in array.LowerBounds)
                    hash.Add(lowerBound);
                AddMethodImplSignatureTypeHashCode(ref hash, array.ElementType);
                return;
            case WrappedExtensionSignatureType wrapped:
                hash.Add(3);
                hash.Add(wrapped.Kind);
                AddMethodImplSignatureTypeHashCode(ref hash, wrapped.ElementType);
                return;
            case GenericExtensionSignatureType generic:
                hash.Add(4);
                AddMethodImplSignatureTypeHashCode(ref hash, generic.GenericType);
                hash.Add(generic.TypeArguments.Length);
                foreach (ExtensionSignatureType argument in generic.TypeArguments)
                    AddMethodImplSignatureTypeHashCode(ref hash, argument);
                return;
            case GenericParameterExtensionSignatureType parameter:
                hash.Add(5);
                hash.Add(parameter.IsMethodParameter);
                hash.Add(parameter.Index);
                return;
            case FunctionPointerExtensionSignatureType functionPointer:
                hash.Add(6);
                hash.Add(functionPointer.CallingConvention);
                hash.Add(functionPointer.Attributes);
                hash.Add(functionPointer.GenericParameterCount);
                hash.Add(functionPointer.RequiredParameterCount);
                AddMethodImplSignatureTypeHashCode(
                    ref hash,
                    functionPointer.ReturnType);
                foreach (ExtensionSignatureType parameterType
                    in functionPointer.ParameterTypes)
                {
                    AddMethodImplSignatureTypeHashCode(ref hash, parameterType);
                }
                return;
            case ModifiedExtensionSignatureType modified:
                hash.Add(7);
                hash.Add(modified.IsRequired);
                AddMethodImplSignatureTypeHashCode(ref hash, modified.Modifier);
                AddMethodImplSignatureTypeHashCode(
                    ref hash,
                    modified.UnmodifiedType);
                return;
            default:
                throw new InvalidOperationException(
                    $"Unsupported MethodImpl signature type {type.GetType().Name}.");
        }
    }

    private static bool ExtensionParametersMatch(
        ImmutableArray<ExtensionSignatureType> implementation,
        ExtensionDecodedSignature property,
        ExtensionSignatureType receiver,
        bool includeReceiver,
        bool includeValue)
    {
        int expectedCount =
            property.ParameterTypes.Length
            + (includeReceiver ? 1 : 0)
            + (includeValue ? 1 : 0);
        if (implementation.Length != expectedCount)
            return false;

        int index = 0;
        if (includeReceiver
            && !ExtensionSignatureTypesEqual(
                implementation[index++],
                receiver))
        {
            return false;
        }
        foreach (ExtensionSignatureType parameter in property.ParameterTypes)
        {
            if (!ExtensionSignatureTypesEqual(
                    implementation[index++],
                    parameter))
            {
                return false;
            }
        }
        return !includeValue
            || ExtensionSignatureTypesEqual(
                implementation[index],
                property.ReturnType);
    }

    private static bool HasAccessorName(
        string methodName,
        string prefix,
        string propertyName) =>
        methodName.StartsWith(prefix, StringComparison.Ordinal)
        && methodName.AsSpan(prefix.Length).SequenceEqual(propertyName);

    static bool TryGetExtensionMarkerName(
        MetadataReader reader,
        PropertyDefinition property,
        PropertyAccessors accessors,
        out string? markerName,
        Action<int>? beforeDecodeWork)
        => TryGetExtensionMarkerName(
                reader,
                property.GetCustomAttributes(),
                out markerName,
                beforeDecodeWork)
            || !accessors.Getter.IsNil
            && TryGetExtensionMarkerName(
                reader,
                reader.GetMethodDefinition(accessors.Getter).GetCustomAttributes(),
                out markerName,
                beforeDecodeWork)
            || !accessors.Setter.IsNil
            && TryGetExtensionMarkerName(
                reader,
                reader.GetMethodDefinition(accessors.Setter).GetCustomAttributes(),
                out markerName,
                beforeDecodeWork);

    private static bool TryGetExtensionMarkerName(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out string? markerName,
        Action<int>? beforeDecodeWork)
    {
        if (beforeDecodeWork is not null)
        {
            foreach (var attributeHandle in attributes)
            {
                beforeDecodeWork(
                    reader.GetBlobReader(
                        reader.GetCustomAttribute(attributeHandle).Value).Length);
            }
        }

        return AttributeReader.TryGetExtensionMarkerName(
            reader,
            attributes,
            out markerName,
            beforeDecodeWork);
    }

    private static byte? GetEffectiveNullable(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        byte nullableContext,
        Action<int>? beforeDecodeWork = null)
    {
        var bytes = NullabilityReader.GetNullableBytes(
            reader,
            attributes,
            beforeDecodeWork);
        if (bytes is { Length: > 0 })
            return bytes[0];
        return nullableContext != 0 ? nullableContext : null;
    }

    private static List<TypeParameter> GenericParameters(
        MetadataReader reader,
        GenericParameterHandleCollection handles,
        GenericContext context,
        byte nullableContext,
        bool includeVariance,
        EntityHandle subject,
        Action<string>? beforeRetain = null,
        Action<int>? beforeDecodeWork = null,
        TypeParameterConstraintResolution? constraintResolution = null)
    {
        GenericContext.ValidateParameterIndices(reader, handles);
        var parameters = new List<TypeParameter>();
        var tracked =
            new List<(GenericParameterHandle Handle, TypeParameter Parameter)>();

        // Shared across the list because `where T : U` chains run through it: answering
        // each parameter from scratch would rewalk the chain's whole tail, which is
        // quadratic in the number of parameters.
        var chain = new TypeParameterKindClassifier.ChainState(
            constraintResolution?.Plan,
            subject);
        IReadOnlyList<string> contextNames = includeVariance
            ? context.TypeParameters
            : context.MethodParameters;
        foreach (var paramHandle in handles)
        {
            var param = reader.GetGenericParameter(paramHandle);
            var typeParam = new TypeParameter
            {
                Name = param.Index < contextNames.Count
                    ? contextNames[param.Index]
                    : DecodeString(
                        reader,
                        param.Name,
                        beforeDecodeWork)
            };
            beforeRetain?.Invoke(typeParam.Name);
            var structured = new List<TypeParameterConstraint>();

            var attrs = param.Attributes;
            if (includeVariance && GenericConstraintKeywords.VarianceKeyword(attrs) is { } variance)
            {
                beforeRetain?.Invoke(variance);
                typeParam.Variance = variance;
            }

            var nullable = GetEffectiveNullable(
                reader,
                param.GetCustomAttributes(),
                nullableContext,
                beforeDecodeWork);
            var isUnmanaged = AttributeReader.HasAttribute(
                reader,
                param.GetCustomAttributes(),
                KnownAttributeNames.IsUnmanagedAttribute,
                beforeDecodeWork);

            if (GenericConstraintKeywords.PrimaryKeyword(attrs, nullable ?? 0, isUnmanaged) is { } primaryKeyword)
            {
                beforeRetain?.Invoke(primaryKeyword);
                beforeRetain?.Invoke(primaryKeyword);
                typeParam.Constraints.Add(primaryKeyword);
                structured.Add(new TypeParameterConstraint(primaryKeyword, IsTypeName: false));
            }

            foreach (var constraintHandle in param.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                string constraintTypeName = ResolveRequiredTypeName(
                    reader,
                    constraint.Type,
                    context,
                    beforeRetain,
                    beforeDecodeWork);
                if (constraintTypeName is "System.ValueType" or "System.Object")
                    continue;
                var formatted = FormatConstraintType(
                    reader,
                    constraint,
                    constraintTypeName,
                    nullableContext,
                    beforeDecodeWork);
                beforeRetain?.Invoke(formatted);
                beforeRetain?.Invoke(formatted);
                typeParam.Constraints.Add(formatted);
                structured.Add(new TypeParameterConstraint(formatted, IsTypeName: true));
            }

            if (GenericConstraintKeywords.NewConstraintKeyword(attrs) is { } newConstraint)
            {
                beforeRetain?.Invoke(newConstraint);
                beforeRetain?.Invoke(newConstraint);
                typeParam.Constraints.Add(newConstraint);
                structured.Add(new TypeParameterConstraint(newConstraint, IsTypeName: false));
            }
            if (GenericConstraintKeywords.AllowsRefStructKeyword(attrs) is { } allowsRefStruct)
            {
                beforeRetain?.Invoke(allowsRefStruct);
                beforeRetain?.Invoke(allowsRefStruct);
                typeParam.Constraints.Add(allowsRefStruct);
                structured.Add(new TypeParameterConstraint(allowsRefStruct, IsTypeName: false));
            }

            typeParam.StructuredConstraints = structured;
            typeParam.TypeKind = TypeParameterKindClassifier.Classify(
                reader,
                paramHandle,
                hasValueTypeConstraint: (attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0,
                hasReferenceTypeConstraint: (attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0,
                chain);
            parameters.Add(typeParam);
            tracked.Add((paramHandle, typeParam));
        }

        constraintResolution?.Track(subject, tracked);
        return parameters;
    }

    private static string FormatConstraintType(
        MetadataReader reader,
        GenericParameterConstraint constraint,
        string constraintTypeName,
        byte nullableContext,
        Action<int>? beforeDecodeWork)
    {
        var nullable = GetEffectiveNullable(
            reader,
            constraint.GetCustomAttributes(),
            nullableContext,
            beforeDecodeWork);
        return nullable == 2 && !constraintTypeName.EndsWith("?", StringComparison.Ordinal)
            ? $"{constraintTypeName}?"
            : constraintTypeName;
    }

    private static (string? Namespace, string Name) GetApiTypeNameParts(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var result = MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(
            reader,
            handle);
        if (result is RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Rejected rejected)
        {
            throw new MetadataRowRejectedException(
                "type identity",
                MetadataTypeNameFailure.From(rejected.Rejection));
        }

        var chain = ((RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Completed)result).Value;
        var rootNamespace = reader.GetString(
            reader.GetTypeDefinition(chain.Handles[0]).Namespace);
        string name = string.Join(
            ".",
            chain.Handles.Select(current =>
                reader.GetString(reader.GetTypeDefinition(current).Name)));
        string fullName = rootNamespace.Length == 0
            ? name
            : $"{rootNamespace}.{name}";
        if (rootNamespace.Length == 0)
            return (null, fullName);

        var prefix = rootNamespace + ".";
        return fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? (rootNamespace, fullName[prefix.Length..])
            : (rootNamespace, fullName);
    }

    private static string SummaryTypeKind(
        MetadataReader reader,
        TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return "interface";

        if (typeDef.BaseType.IsNil)
            return "class";

        return TypeResolver.GetTypeName(reader, typeDef.BaseType) switch
        {
            "System.Enum" => "enum",
            "System.ValueType" => "struct",
            "System.Delegate" or "System.MulticastDelegate" => "delegate",
            _ => "class"
        };
    }

    private static string GetMetadataName(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var result = MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(
            reader,
            handle);
        if (result is RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Rejected rejected)
        {
            throw new MetadataRowRejectedException(
                "type metadata identity",
                MetadataTypeNameFailure.From(rejected.Rejection));
        }

        var chain = ((RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Completed)result).Value;
        return string.Join(
            "+",
            chain.Handles.Select(current =>
                reader.GetString(reader.GetTypeDefinition(current).Name)));
    }

    private static (
        string Text,
        bool IsDegraded,
        List<ApiTypeReferenceIdentity> References) DecodeFieldType(
        MetadataReader reader,
        GenericContext context,
        FieldDefinition field,
        byte typeNullableContext,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null)
    {
        var typeNodeProvider = beforeRetainText is null
            ? TypeNodeProvider.Instance
            : new TypeNodeProvider(beforeRetainText, beforeDecodeWork);
        var fieldNode = GuardedProviderDecode.Field(
            reader,
            field,
            typeNodeProvider,
            context,
            (TypeNode)new DegradedTypeNode());
        var fieldBytes = NullabilityReader.GetNullableBytes(
            reader,
            field.GetCustomAttributes(),
            beforeDecodeWork);
        int pos = 0;
        fieldNode.ApplyNullability(fieldBytes, ref pos, typeNullableContext);
        var fieldDynamicFlags = DynamicReader.GetDynamicFlags(
            reader,
            field.GetCustomAttributes(),
            beforeDecodeWork);
        pos = 0;
        fieldNode.ApplyDynamic(fieldDynamicFlags, ref pos);
        fieldNode.ApplyTupleNames(
            TupleElementNamesReader.GetTupleElementNames(
                reader,
                field.GetCustomAttributes(),
                beforeDecodeWork));
        return (
            fieldNode.Render(),
            fieldNode.IsDegraded,
            [.. fieldNode.ReferencedTypes().Distinct()]);
    }

    private readonly record struct OwnedMethodImplementation(
        MethodDefinitionHandle Body,
        EntityHandle Declaration);

    private static List<OwnedMethodImplementation> ReadOwnedMethodImplementations(
        MetadataReader reader,
        TypeDefinitionHandle owningType,
        TypeDefinition typeDef,
        ApiSurface surface,
        ExtractionBudget? budget,
        MetadataTypeDefinitionName? owningTypeDefinition,
        MetadataTypeScope? currentScope,
        IReadOnlyDictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> localTypes,
        Action<int>? beforeDecodeWork)
    {
        List<OwnedMethodImplementation> implementations = [];
        HashSet<MethodImplementationDeclarationKey> declarations =
            new(MethodImplementationDeclarationKeyComparer.Instance);
        foreach (var implementationHandle in typeDef.GetMethodImplementations())
        {
            try
            {
                var implementation =
                    reader.GetMethodImplementation(implementationHandle);
                var declaration = new MethodImplementationDeclarationKey(
                    implementation.MethodDeclaration,
                    TryGetMethodImplementationDeclarationIdentity(
                        reader,
                        implementation.MethodDeclaration,
                        currentScope,
                        beforeDecodeWork));
                if (!declarations.Add(declaration))
                {
                    AddInspectionFailure(
                        surface,
                        budget,
                        "method implementation declaration",
                        implementationHandle,
                        MetadataTypeNameFailure.Malformed(
                            implementationHandle,
                            $"Declaration 0x{MetadataTokens.GetToken(implementation.MethodDeclaration):x8} "
                            + "appears more than once for the same MethodImpl owner."),
                        owningType: owningType,
                        owningTypeDefinition: owningTypeDefinition);
                    continue;
                }

                if (implementation.MethodBody.Kind == HandleKind.MemberReference)
                {
                    var memberReference = reader.GetMemberReference(
                        (MemberReferenceHandle)implementation.MethodBody);
                    if (TryResolveKnownLocalType(
                            reader,
                            memberReference.Parent,
                            currentScope,
                            localTypes,
                            beforeDecodeWork,
                            out var referencedType))
                    {
                        if (referencedType != owningType
                            && !IsLocalBaseType(
                                reader,
                                owningType,
                                referencedType,
                                currentScope,
                                localTypes,
                                beforeDecodeWork))
                        {
                            AddInspectionFailure(
                                surface,
                                budget,
                                "method implementation body",
                                implementationHandle,
                                MetadataTypeNameFailure.Malformed(
                                    implementationHandle,
                                    $"MemberRef body 0x{MetadataTokens.GetToken(implementation.MethodBody):x8} "
                                    + $"belongs to unrelated type 0x{MetadataTokens.GetToken(referencedType):x8}."),
                                owningType: owningType,
                                owningTypeDefinition: owningTypeDefinition);
                        }
                        else if (referencedType == owningType)
                        {
                            var resolvedBody = ResolveLocalMemberReferenceBody(
                                reader,
                                referencedType,
                                memberReference,
                                currentScope,
                                beforeDecodeWork);
                            if (resolvedBody is { } localBody)
                            {
                                implementations.Add(
                                    new OwnedMethodImplementation(
                                        localBody,
                                        implementation.MethodDeclaration));
                            }
                            else if (memberReference.Parent.Kind
                                != HandleKind.TypeSpecification)
                            {
                                AddInspectionFailure(
                                    surface,
                                    budget,
                                    "method implementation body",
                                    implementationHandle,
                                    MetadataTypeNameFailure.Malformed(
                                        implementationHandle,
                                        $"MemberRef body 0x{MetadataTokens.GetToken(implementation.MethodBody):x8} "
                                        + "does not identify one method on the MethodImpl owner."),
                                    owningType: owningType,
                                    owningTypeDefinition: owningTypeDefinition);
                            }
                        }
                    }
                    continue;
                }

                if (implementation.MethodBody.Kind != HandleKind.MethodDefinition)
                {
                    AddInspectionFailure(
                        surface,
                        budget,
                        "method implementation body",
                        implementationHandle,
                        MetadataTypeNameFailure.Malformed(
                            implementationHandle,
                            $"Unsupported MethodImpl body kind {implementation.MethodBody.Kind}."),
                        owningType: owningType,
                        owningTypeDefinition: owningTypeDefinition);
                    continue;
                }

                var body = (MethodDefinitionHandle)implementation.MethodBody;
                var declaringType =
                    reader.GetMethodDefinition(body).GetDeclaringType();
                if (declaringType != owningType
                    && IsLocalBaseType(
                        reader,
                        owningType,
                        declaringType,
                        currentScope,
                        localTypes,
                        beforeDecodeWork))
                    continue;
                if (declaringType != owningType)
                {
                    AddInspectionFailure(
                        surface,
                        budget,
                        "method implementation body",
                        implementationHandle,
                        MetadataTypeNameFailure.Malformed(
                            implementationHandle,
                            $"Method body 0x{MetadataTokens.GetToken(body):x8} belongs to type "
                            + $"0x{MetadataTokens.GetToken(declaringType):x8}, not MethodImpl owner "
                            + $"0x{MetadataTokens.GetToken(owningType):x8} or one of its base types."),
                        owningType: owningType,
                        owningTypeDefinition: owningTypeDefinition);
                    continue;
                }

                implementations.Add(
                    new OwnedMethodImplementation(
                        body,
                        implementation.MethodDeclaration));
            }
            catch (Exception ex) when (
                ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                AddInspectionFailure(
                    surface,
                    budget,
                    "method implementation body",
                    implementationHandle,
                    MetadataTypeNameFailure.Malformed(
                        implementationHandle,
                        ex.Message),
                    owningType: owningType,
                    owningTypeDefinition: owningTypeDefinition);
            }
        }
        return implementations;
    }

    private static MethodDefinitionHandle? ResolveLocalMemberReferenceBody(
        MetadataReader reader,
        TypeDefinitionHandle declaringType,
        MemberReference memberReference,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork)
    {
        MethodDefinitionHandle match = default;
        string memberName = DecodeString(
            reader,
            memberReference.Name,
            beforeDecodeWork);
        foreach (var candidateHandle in reader.GetTypeDefinition(declaringType).GetMethods())
        {
            var candidate = reader.GetMethodDefinition(candidateHandle);
            if (!reader.StringComparer.Equals(candidate.Name, memberName)
                || !MethodSignaturesEqual(
                    reader,
                    candidate,
                    memberReference,
                    currentScope,
                    beforeDecodeWork))
            {
                continue;
            }

            if (!match.IsNil)
                return null;
            match = candidateHandle;
        }

        return match.IsNil ? null : match;
    }

    private static bool MethodSignaturesEqual(
        MetadataReader reader,
        MethodDefinition method,
        MemberReference memberReference,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork)
    {
        MethodSignatureIdentity? methodIdentity =
            TryGetMethodSignatureIdentity(
                reader,
                method,
                currentScope,
                beforeDecodeWork);
        MethodSignatureIdentity? memberReferenceIdentity =
            TryGetMethodSignatureIdentity(
                reader,
                memberReference,
                currentScope,
                beforeDecodeWork);
        return methodIdentity is not null
            && methodIdentity.Equals(memberReferenceIdentity);
    }

    private static MethodImplementationDeclarationIdentity?
        TryGetMethodImplementationDeclarationIdentity(
        MetadataReader reader,
        EntityHandle declaration,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork)
    {
        EntityHandle declaringType;
        string name;
        MethodSignatureIdentity? signature;
        switch (declaration.Kind)
        {
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition(
                    (MethodDefinitionHandle)declaration);
                declaringType = method.GetDeclaringType();
                name = DecodeString(reader, method.Name, beforeDecodeWork);
                signature = TryGetMethodSignatureIdentity(
                    reader,
                    method,
                    currentScope,
                    beforeDecodeWork);
                break;
            case HandleKind.MemberReference:
                var memberReference = reader.GetMemberReference(
                    (MemberReferenceHandle)declaration);
                declaringType = memberReference.Parent;
                name = DecodeString(
                    reader,
                    memberReference.Name,
                    beforeDecodeWork);
                signature = TryGetMethodSignatureIdentity(
                    reader,
                    memberReference,
                    currentScope,
                    beforeDecodeWork);
                break;
            default:
                return null;
        }

        MetadataNamedTypeIdentity? typeIdentity = TryGetNamedTypeIdentity(
            reader,
            declaringType,
            currentScope,
            beforeDecodeWork);
        if (typeIdentity is null || signature is null)
            return null;

        ExtensionSignatureType? constructedTypeIdentity = declaringType.Kind
            == HandleKind.TypeSpecification
            ? TryGetTypeStructuralIdentity(
                reader,
                (TypeSpecificationHandle)declaringType,
                currentScope,
                beforeDecodeWork)
            : null;
        if (declaringType.Kind == HandleKind.TypeSpecification
            && constructedTypeIdentity is null)
        {
            return null;
        }

        return new MethodImplementationDeclarationIdentity(
            typeIdentity,
            constructedTypeIdentity,
            name,
            signature);
    }

    private static ExtensionSignatureType? TryGetTypeStructuralIdentity(
        MetadataReader reader,
        TypeSpecificationHandle type,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork)
    {
        return GuardedProviderDecode.TypeSpec(
            reader,
            type,
            new ExtensionSignatureTypeProvider(
                currentScope,
                beforeDecodeWork),
            context: null,
            fallback: null);
    }

    private static MethodSignatureIdentity? TryGetMethodSignatureIdentity(
        MetadataReader reader,
        MethodDefinition method,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork)
    {
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                reader,
                method.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            return null;
        }

        try
        {
            MethodSignature<ExtensionSignatureType?> signature =
                GuardedProviderDecode.Method(
                    reader,
                    method,
                    new ExtensionSignatureTypeProvider(
                        currentScope,
                        beforeDecodeWork),
                    context: null,
                    fallbackReturn: null);
            return CreateMethodSignatureIdentity(signature);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or OverflowException)
        {
            return null;
        }
    }

    private static MethodSignatureIdentity? TryGetMethodSignatureIdentity(
        MetadataReader reader,
        MemberReference memberReference,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork)
    {
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                reader,
                memberReference.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            return null;
        }

        try
        {
            MethodSignature<ExtensionSignatureType?> signature =
                GuardedProviderDecode.MemberRefMethod(
                    reader,
                    memberReference,
                    new ExtensionSignatureTypeProvider(
                        currentScope,
                        beforeDecodeWork),
                    context: null,
                    fallbackReturn: null);
            return CreateMethodSignatureIdentity(signature);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or OverflowException)
        {
            return null;
        }
    }

    private static MethodSignatureIdentity? CreateMethodSignatureIdentity(
        MethodSignature<ExtensionSignatureType?> signature)
    {
        if (signature.ReturnType is null
            || signature.ParameterTypes.Any(static type => type is null))
        {
            return null;
        }

        return new MethodSignatureIdentity(
            signature.Header.RawValue,
            signature.GenericParameterCount,
            signature.RequiredParameterCount,
            signature.ReturnType,
            [.. signature.ParameterTypes.Select(
                static parameter => parameter!)]);
    }

    private sealed class MethodSignatureIdentity : IEquatable<MethodSignatureIdentity>
    {
        private readonly int _hashCode;

        internal MethodSignatureIdentity(
            byte header,
            int genericParameterCount,
            int requiredParameterCount,
            ExtensionSignatureType returnType,
            ExtensionSignatureType[] parameterTypes)
        {
            Header = header;
            GenericParameterCount = genericParameterCount;
            RequiredParameterCount = requiredParameterCount;
            ReturnType = returnType;
            ParameterTypes = parameterTypes;
            var hash = new HashCode();
            hash.Add(header);
            hash.Add(genericParameterCount);
            hash.Add(requiredParameterCount);
            hash.Add(GetMethodImplSignatureTypeHashCode(returnType));
            foreach (ExtensionSignatureType parameterType in parameterTypes)
                hash.Add(GetMethodImplSignatureTypeHashCode(parameterType));
            _hashCode = hash.ToHashCode();
        }

        internal byte Header { get; }
        internal int GenericParameterCount { get; }
        internal int RequiredParameterCount { get; }
        internal ExtensionSignatureType ReturnType { get; }
        internal ExtensionSignatureType[] ParameterTypes { get; }

        public bool Equals(MethodSignatureIdentity? other)
            => ReferenceEquals(this, other)
                || other is not null
                    && _hashCode == other._hashCode
                    && Header == other.Header
                    && GenericParameterCount == other.GenericParameterCount
                    && RequiredParameterCount == other.RequiredParameterCount
                    && MethodImplSignatureTypesEqual(ReturnType, other.ReturnType)
                    && ParameterTypes.Length == other.ParameterTypes.Length
                    && ParameterTypes.Zip(
                        other.ParameterTypes,
                        static (left, right) =>
                            MethodImplSignatureTypesEqual(left, right))
                        .All(static equal => equal);

        public override bool Equals(object? obj)
            => Equals(obj as MethodSignatureIdentity);

        public override int GetHashCode() => _hashCode;
    }

    private sealed class MethodImplementationDeclarationIdentity :
        IEquatable<MethodImplementationDeclarationIdentity>
    {
        private readonly int _hashCode;

        internal MethodImplementationDeclarationIdentity(
            MetadataNamedTypeIdentity declaringType,
            ExtensionSignatureType? constructedTypeIdentity,
            string name,
            MethodSignatureIdentity signature)
        {
            DeclaringType = declaringType;
            ConstructedTypeIdentity = constructedTypeIdentity;
            Name = name;
            Signature = signature;
            var hash = new HashCode();
            hash.Add(
                MetadataNamedTypeIdentityComparer.Instance.GetHashCode(
                    declaringType));
            hash.Add(
                constructedTypeIdentity is null
                    ? 0
                    : GetMethodImplSignatureTypeHashCode(
                        constructedTypeIdentity));
            hash.Add(name, StringComparer.Ordinal);
            hash.Add(signature);
            _hashCode = hash.ToHashCode();
        }

        internal MetadataNamedTypeIdentity DeclaringType { get; }
        internal ExtensionSignatureType? ConstructedTypeIdentity { get; }
        internal string Name { get; }
        internal MethodSignatureIdentity Signature { get; }

        public bool Equals(MethodImplementationDeclarationIdentity? other)
            => ReferenceEquals(this, other)
                || other is not null
                    && _hashCode == other._hashCode
                    && MetadataNamedTypeIdentityComparer.Instance.Equals(
                        DeclaringType,
                        other.DeclaringType)
                    && (ConstructedTypeIdentity is null
                        ? other.ConstructedTypeIdentity is null
                        : other.ConstructedTypeIdentity is not null
                        && MethodImplSignatureTypesEqual(
                            ConstructedTypeIdentity,
                            other.ConstructedTypeIdentity))
                    && string.Equals(Name, other.Name, StringComparison.Ordinal)
                    && Signature.Equals(other.Signature);

        public override bool Equals(object? obj)
            => Equals(obj as MethodImplementationDeclarationIdentity);

        public override int GetHashCode() => _hashCode;
    }

    private readonly record struct MethodImplementationDeclarationKey(
        EntityHandle Handle,
        MethodImplementationDeclarationIdentity? CanonicalIdentity);

    private sealed class MethodImplementationDeclarationKeyComparer :
        IEqualityComparer<MethodImplementationDeclarationKey>
    {
        internal static MethodImplementationDeclarationKeyComparer Instance { get; } =
            new();

        public bool Equals(
            MethodImplementationDeclarationKey left,
            MethodImplementationDeclarationKey right)
            => left.CanonicalIdentity is not null
                && right.CanonicalIdentity is not null
                ? left.CanonicalIdentity.Equals(right.CanonicalIdentity)
                : left.Handle == right.Handle;

        public int GetHashCode(MethodImplementationDeclarationKey value)
            => value.CanonicalIdentity?.GetHashCode()
                ?? value.Handle.GetHashCode();
    }

    private sealed class FinalizerMethodImplementationCache
    {
        private readonly object _gate = new();
        private readonly ApiSurface _failures = new();
        private readonly Dictionary<TypeDefinitionHandle, bool>
            _finalizerOwners = [];
        private Dictionary<
            MetadataNamedTypeIdentity,
            TypeDefinitionHandle?> _localTypes = [];
        private readonly Dictionary<
            TypeDefinitionHandle,
            IReadOnlyList<OwnedMethodImplementation>> _implementations = [];
        private MetadataTypeScope? _currentScope;
        private bool _initialized;

        internal bool IsFinalizerMethod(
            MetadataReader reader,
            MethodDefinitionHandle methodHandle)
        {
            lock (_gate)
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                if (!IsFinalizerBodyShape(reader, method))
                {
                    return false;
                }

                TypeDefinitionHandle typeHandle = method.GetDeclaringType();
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                EnsureInitialized(reader);
                if (!_finalizerOwners.TryGetValue(
                        typeHandle,
                        out bool isFinalizerOwner))
                {
                    isFinalizerOwner = IsFinalizerOwner(reader, type);
                    _finalizerOwners.Add(typeHandle, isFinalizerOwner);
                }
                if (!isFinalizerOwner)
                {
                    return false;
                }

                foreach (OwnedMethodImplementation implementation
                    in GetImplementations(reader, typeHandle, type))
                {
                    if (implementation.Body == methodHandle
                        && ReferencesObjectFinalize(
                            reader,
                            implementation.Declaration))
                    {
                        return true;
                    }
                }

                // No MethodImpl: fall back to the implicit-slot shape the VB.NET compiler emits.
                return IsImplicitObjectFinalizeOverride(
                    reader,
                    typeHandle,
                    method,
                    _currentScope,
                    _localTypes);
            }
        }

        IReadOnlyList<OwnedMethodImplementation> GetImplementations(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            TypeDefinition type)
        {
            if (_implementations.TryGetValue(
                    typeHandle,
                    out IReadOnlyList<OwnedMethodImplementation>? cached))
            {
                return cached;
            }

            IReadOnlyList<OwnedMethodImplementation> implementations =
                _currentScope is null
                    ? []
                    : ReadOwnedMethodImplementations(
                        reader,
                        typeHandle,
                        type,
                        _failures,
                        budget: null,
                        owningTypeDefinition: null,
                        currentScope: _currentScope,
                        localTypes: _localTypes,
                        beforeDecodeWork: null);
            _implementations.Add(typeHandle, implementations);
            return implementations;
        }

        void EnsureInitialized(MetadataReader reader)
        {
            if (_initialized)
                return;

            _initialized = true;
            try
            {
                _currentScope = CurrentScope(
                    reader,
                    beforeDecodeWork: null);
                if (_currentScope is not null)
                {
                    _localTypes = LocalTypes(
                        reader,
                        _currentScope,
                        _failures,
                        budget: null);
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or OverflowException)
            {
                _currentScope = null;
                _localTypes = [];
            }
        }
    }

    private static bool IsLocalBaseType(
        MetadataReader reader,
        TypeDefinitionHandle derivedType,
        TypeDefinitionHandle candidateBase,
        MetadataTypeScope? currentScope,
        IReadOnlyDictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> localTypes,
        Action<int>? beforeDecodeWork)
    {
        HashSet<TypeDefinitionHandle> visited = [derivedType];
        var current = reader.GetTypeDefinition(derivedType);
        for (int depth = 0; depth < MaxBaseChainDepth; depth++)
        {
            if (!TryResolveKnownLocalType(
                    reader,
                    current.BaseType,
                    currentScope,
                    localTypes,
                    beforeDecodeWork,
                    out var baseType)
                || !visited.Add(baseType))
            {
                return false;
            }

            if (baseType == candidateBase)
                return true;
            current = reader.GetTypeDefinition(baseType);
        }

        return false;
    }

    private static HashSet<MethodDefinitionHandle> GetExplicitImplementationBodies(
        MetadataReader reader,
        TypeDefinition typeDef,
        IReadOnlyList<OwnedMethodImplementation> implementations,
        MetadataTypeScope? currentScope,
        IReadOnlyDictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> localTypes,
        Action<int>? beforeDecodeWork)
    {
        HashSet<MethodDefinitionHandle> handles = [];
        Dictionary<EntityHandle, InterfaceMethodImplOwnership> ownershipByDeclaringType = [];
        foreach (var implementation in implementations)
        {
            EntityHandle declaringType = MethodImplementationDeclarationType(
                reader,
                implementation.Declaration);
            if (!ownershipByDeclaringType.TryGetValue(
                    declaringType,
                    out InterfaceMethodImplOwnership ownership))
            {
                ownership = TargetsImplementedInterface(
                    reader,
                    typeDef,
                    declaringType,
                    currentScope,
                    localTypes,
                    beforeDecodeWork);
                ownershipByDeclaringType.Add(declaringType, ownership);
            }

            if (ownership
                is not InterfaceMethodImplOwnership.ProvenNonInterface)
            {
                handles.Add(implementation.Body);
            }
        }

        return handles;
    }

    private enum InterfaceMethodImplOwnership
    {
        ProvenNonInterface,
        ProvenInterface,
        UnresolvedExternalInheritance,
    }

    private static EntityHandle MethodImplementationDeclarationType(
        MetadataReader reader,
        EntityHandle declaration)
        => declaration.Kind switch
        {
            HandleKind.MethodDefinition => reader
                .GetMethodDefinition((MethodDefinitionHandle)declaration)
                .GetDeclaringType(),
            HandleKind.MemberReference => reader
                .GetMemberReference((MemberReferenceHandle)declaration)
                .Parent,
            _ => default,
        };

    private static InterfaceMethodImplOwnership TargetsImplementedInterface(
        MetadataReader reader,
        TypeDefinition typeDef,
        EntityHandle declaringType,
        MetadataTypeScope? currentScope,
        IReadOnlyDictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> localTypes,
        Action<int>? beforeDecodeWork)
    {
        if (declaringType.Kind is not (
                HandleKind.TypeDefinition
                or HandleKind.TypeReference
                or HandleKind.TypeSpecification))
        {
            return InterfaceMethodImplOwnership.ProvenNonInterface;
        }

        if (declaringType.Kind == HandleKind.TypeDefinition
            && !reader.GetTypeDefinition((TypeDefinitionHandle)declaringType)
                .Attributes.HasFlag(TypeAttributes.Interface))
        {
            return InterfaceMethodImplOwnership.ProvenNonInterface;
        }

        var declarationIdentity = TryGetNamedTypeIdentity(
            reader,
            declaringType,
            currentScope,
            beforeDecodeWork);
        var declarationSignature = TryGetExtensionSignatureType(
            reader,
            declaringType,
            currentScope,
            beforeDecodeWork);
        foreach (var handle in typeDef.GetInterfaceImplementations())
        {
            EntityHandle interfaceType =
                reader.GetInterfaceImplementation(handle).Interface;
            if (interfaceType == declaringType
                || declarationSignature is not null
                && TryGetExtensionSignatureType(
                        reader,
                        interfaceType,
                        currentScope,
                        beforeDecodeWork) is { } interfaceSignature
                && MethodImplSignatureTypesEqual(
                    interfaceSignature,
                    declarationSignature))
            {
                return InterfaceMethodImplOwnership.ProvenInterface;
            }
        }

        if (IsSystemObjectType(reader, declaringType, beforeDecodeWork)
            || TargetsClassHierarchy(
                reader,
                typeDef,
                declaringType,
                declarationIdentity,
                currentScope,
                localTypes,
                beforeDecodeWork))
        {
            return InterfaceMethodImplOwnership.ProvenNonInterface;
        }

        var pending = new Queue<InterfaceImplementationCandidate>();
        var visitedLocalInterfaces = new List<InterfaceImplementationCandidate>();
        var traversalBudget = new InterfaceImplementationTraversalBudget(
            beforeDecodeWork);
        bool hasUnresolvedExternalOwnership = declarationIdentity is null
            || declarationSignature is null
            || !TryGetUniqueLocalType(localTypes, declarationIdentity, out _);
        var currentType = typeDef;
        HashSet<TypeDefinitionHandle> visitedBaseTypes = [];
        for (int depth = 0; depth < MaxBaseChainDepth; depth++)
        {
            foreach (var handle in currentType.GetInterfaceImplementations())
            {
                EntityHandle interfaceType =
                    reader.GetInterfaceImplementation(handle).Interface;
                if (TryGetExtensionSignatureType(
                        reader,
                        interfaceType,
                        currentScope,
                        beforeDecodeWork) is { } interfaceSignature)
                {
                    if (ContainsUnsubstitutedGenericParameter(interfaceSignature)
                        || !traversalBudget.TryEnqueue(
                            interfaceType,
                            interfaceSignature,
                            pending))
                    {
                        hasUnresolvedExternalOwnership = true;
                    }
                }
                else
                {
                    hasUnresolvedExternalOwnership = true;
                }
            }

            var baseType = currentType.BaseType;
            if (baseType.IsNil
                || IsSystemObjectType(reader, baseType, beforeDecodeWork))
                break;
            if (!TryResolveKnownLocalType(
                    reader,
                    baseType,
                    currentScope,
                    localTypes,
                    beforeDecodeWork,
                    out var baseDefinition)
                || !visitedBaseTypes.Add(baseDefinition))
            {
                hasUnresolvedExternalOwnership = true;
                break;
            }

            currentType = reader.GetTypeDefinition(baseDefinition);
        }
        if (visitedBaseTypes.Count == MaxBaseChainDepth)
            hasUnresolvedExternalOwnership = true;

        while (pending.TryDequeue(out var candidate))
        {
            if (candidate.Handle == declaringType
                || declarationSignature is not null
                && MethodImplSignatureTypesEqual(
                    candidate.Identity,
                    declarationSignature))
            {
                return InterfaceMethodImplOwnership.ProvenInterface;
            }

            // Same-module definitions let us close inherited InterfaceImpl edges.
            // External inheritance cannot be proven from this reader alone. Keep
            // that uncertainty distinct from a proven class MethodImpl so default
            // extraction does not silently drop a possible explicit implementation.
            if (!TryResolveKnownLocalType(
                    reader,
                    candidate.Handle,
                    currentScope,
                    localTypes,
                    beforeDecodeWork,
                    out var interfaceHandle))
            {
                hasUnresolvedExternalOwnership = true;
                continue;
            }
            if (visitedLocalInterfaces.Any(
                    visited => visited.Handle == interfaceHandle
                        && MethodImplSignatureTypesEqual(
                            visited.Identity,
                            candidate.Identity)))
            {
                continue;
            }
            visitedLocalInterfaces.Add(
                new InterfaceImplementationCandidate(
                    interfaceHandle,
                    candidate.Identity));

            var interfaceDefinition = reader.GetTypeDefinition(interfaceHandle);
            foreach (var handle in interfaceDefinition.GetInterfaceImplementations())
            {
                EntityHandle inheritedInterface =
                    reader.GetInterfaceImplementation(handle).Interface;
                if (TryGetExtensionSignatureType(
                        reader,
                        inheritedInterface,
                        currentScope,
                        beforeDecodeWork) is not { } inheritedSignature)
                {
                    hasUnresolvedExternalOwnership = true;
                    continue;
                }

                if (!traversalBudget.TrySubstitute(
                        inheritedSignature,
                        candidate.Identity,
                        out var substitutedInterface)
                    || ContainsUnsubstitutedGenericParameter(
                        substitutedInterface)
                    || !traversalBudget.TryEnqueue(
                        inheritedInterface,
                        substitutedInterface,
                        pending))
                {
                    hasUnresolvedExternalOwnership = true;
                }
            }
        }

        return hasUnresolvedExternalOwnership
            ? InterfaceMethodImplOwnership.UnresolvedExternalInheritance
            : InterfaceMethodImplOwnership.ProvenNonInterface;
    }

    private readonly record struct InterfaceImplementationCandidate(
        EntityHandle Handle,
        ExtensionSignatureType Identity);

    /// <summary>
    /// Bounds the constructed identities created while following InterfaceImpl
    /// inheritance. The visited key must include arguments: handle-only keys
    /// conflate distinct constructed interfaces, while a key containing every
    /// construction does not terminate for <c>I&lt;T&gt; : I&lt;I&lt;T&gt;&gt;</c>.
    /// A refused candidate is intentionally reported to the caller as
    /// unresolved ownership, preserving the MethodImpl body rather than
    /// confidently suppressing it as an ordinary method.
    /// </summary>
    private sealed class InterfaceImplementationTraversalBudget(
        Action<int>? observeDecodeWork)
    {
        int _candidateCount;
        int _workNodes;

        internal bool TryEnqueue(
            EntityHandle handle,
            ExtensionSignatureType identity,
            Queue<InterfaceImplementationCandidate> pending)
        {
            if (_candidateCount >=
                    MetadataSafetyPolicy.MaxConstructedInterfaceImplementationCandidates
                || !TryMeasureIdentity(identity, out int nodes)
                || !TryCharge(nodes))
            {
                return false;
            }

            _candidateCount++;
            pending.Enqueue(
                new InterfaceImplementationCandidate(handle, identity));
            return true;
        }

        internal bool TrySubstitute(
            ExtensionSignatureType inheritedInterface,
            ExtensionSignatureType constructedInterface,
            out ExtensionSignatureType substitutedInterface)
        {
            if (!TryMeasureSubstitution(
                    inheritedInterface,
                    constructedInterface,
                    out int nodes)
                || !TryCharge(nodes))
            {
                substitutedInterface = default!;
                return false;
            }

            substitutedInterface = SubstituteInterfaceTypeParameters(
                inheritedInterface,
                constructedInterface);
            return true;
        }

        bool TryCharge(int nodes)
        {
            if (nodes > MetadataSafetyPolicy.MaxConstructedInterfaceImplementationWorkNodes
                - _workNodes)
            {
                return false;
            }

            // This model is allocated by substitution after SRM decodes the
            // metadata signature, so charge it through the same extraction-wide
            // work ledger that pays for all other signature-derived shapes.
            observeDecodeWork?.Invoke(nodes);
            _workNodes += nodes;
            return true;
        }

        static bool TryMeasureIdentity(
            ExtensionSignatureType identity,
            out int nodes)
        {
            nodes = 0;
            return TryMeasureIdentity(identity, depth: 1, ref nodes);
        }

        static bool TryMeasureIdentity(
            ExtensionSignatureType type,
            int depth,
            ref int nodes)
        {
            if (!TryCountNode(depth, ref nodes))
                return false;

            return type switch
            {
                PrimitiveExtensionSignatureType
                    or NamedExtensionSignatureType
                    or GenericParameterExtensionSignatureType => true,
                ArrayExtensionSignatureType array => TryMeasureIdentity(
                    array.ElementType,
                    depth + 1,
                    ref nodes),
                WrappedExtensionSignatureType wrapped => TryMeasureIdentity(
                    wrapped.ElementType,
                    depth + 1,
                    ref nodes),
                GenericExtensionSignatureType generic =>
                    TryMeasureIdentity(generic.GenericType, depth + 1, ref nodes)
                    && TryMeasureIdentities(
                        generic.TypeArguments,
                        depth + 1,
                        ref nodes),
                FunctionPointerExtensionSignatureType functionPointer =>
                    TryMeasureIdentity(
                        functionPointer.ReturnType,
                        depth + 1,
                        ref nodes)
                    && TryMeasureIdentities(
                        functionPointer.ParameterTypes,
                        depth + 1,
                        ref nodes),
                ModifiedExtensionSignatureType modified =>
                    TryMeasureIdentity(modified.Modifier, depth + 1, ref nodes)
                    && TryMeasureIdentity(
                        modified.UnmodifiedType,
                        depth + 1,
                        ref nodes),
                _ => false,
            };
        }

        static bool TryMeasureIdentities(
            ImmutableArray<ExtensionSignatureType> types,
            int depth,
            ref int nodes)
        {
            foreach (ExtensionSignatureType type in types)
            {
                if (!TryMeasureIdentity(type, depth, ref nodes))
                    return false;
            }
            return true;
        }

        static bool TryMeasureSubstitution(
            ExtensionSignatureType inheritedInterface,
            ExtensionSignatureType constructedInterface,
            out int nodes)
        {
            ImmutableArray<ExtensionSignatureType> arguments =
                constructedInterface is GenericExtensionSignatureType generic
                    ? generic.TypeArguments
                    : [];
            nodes = 0;
            return TryMeasureSubstitution(
                inheritedInterface,
                arguments,
                depth: 1,
                ref nodes);
        }

        static bool TryMeasureSubstitution(
            ExtensionSignatureType type,
            ImmutableArray<ExtensionSignatureType> arguments,
            int depth,
            ref int nodes)
        {
            if (type is GenericParameterExtensionSignatureType
                {
                    IsMethodParameter: false,
                    Index: var index
                }
                && index < arguments.Length)
            {
                return TryMeasureIdentity(arguments[index], depth, ref nodes);
            }

            if (!TryCountNode(depth, ref nodes))
                return false;

            return type switch
            {
                PrimitiveExtensionSignatureType
                    or NamedExtensionSignatureType
                    or GenericParameterExtensionSignatureType => true,
                ArrayExtensionSignatureType array => TryMeasureSubstitution(
                    array.ElementType,
                    arguments,
                    depth + 1,
                    ref nodes),
                WrappedExtensionSignatureType wrapped => TryMeasureSubstitution(
                    wrapped.ElementType,
                    arguments,
                    depth + 1,
                    ref nodes),
                GenericExtensionSignatureType generic =>
                    TryMeasureSubstitution(
                        generic.GenericType,
                        arguments,
                        depth + 1,
                        ref nodes)
                    && TryMeasureSubstitutions(
                        generic.TypeArguments,
                        arguments,
                        depth + 1,
                        ref nodes),
                FunctionPointerExtensionSignatureType functionPointer =>
                    TryMeasureSubstitution(
                        functionPointer.ReturnType,
                        arguments,
                        depth + 1,
                        ref nodes)
                    && TryMeasureSubstitutions(
                        functionPointer.ParameterTypes,
                        arguments,
                        depth + 1,
                        ref nodes),
                ModifiedExtensionSignatureType modified =>
                    TryMeasureSubstitution(
                        modified.Modifier,
                        arguments,
                        depth + 1,
                        ref nodes)
                    && TryMeasureSubstitution(
                        modified.UnmodifiedType,
                        arguments,
                        depth + 1,
                        ref nodes),
                _ => false,
            };
        }

        static bool TryMeasureSubstitutions(
            ImmutableArray<ExtensionSignatureType> types,
            ImmutableArray<ExtensionSignatureType> arguments,
            int depth,
            ref int nodes)
        {
            foreach (ExtensionSignatureType type in types)
            {
                if (!TryMeasureSubstitution(
                        type,
                        arguments,
                        depth,
                        ref nodes))
                {
                    return false;
                }
            }
            return true;
        }

        static bool TryCountNode(int depth, ref int nodes)
        {
            if (depth >
                    MetadataSafetyPolicy.MaxConstructedInterfaceImplementationDepth
                || nodes >=
                    MetadataSafetyPolicy.MaxConstructedInterfaceImplementationWorkNodes)
            {
                return false;
            }

            nodes++;
            return true;
        }
    }

    private static bool ContainsUnsubstitutedGenericParameter(
        ExtensionSignatureType type)
        => type switch
        {
            GenericParameterExtensionSignatureType => true,
            ArrayExtensionSignatureType array =>
                ContainsUnsubstitutedGenericParameter(array.ElementType),
            WrappedExtensionSignatureType wrapped =>
                ContainsUnsubstitutedGenericParameter(wrapped.ElementType),
            GenericExtensionSignatureType generic =>
                ContainsUnsubstitutedGenericParameter(generic.GenericType)
                || generic.TypeArguments.Any(
                    ContainsUnsubstitutedGenericParameter),
            FunctionPointerExtensionSignatureType functionPointer =>
                ContainsUnsubstitutedGenericParameter(functionPointer.ReturnType)
                || functionPointer.ParameterTypes.Any(
                    ContainsUnsubstitutedGenericParameter),
            ModifiedExtensionSignatureType modified =>
                ContainsUnsubstitutedGenericParameter(modified.Modifier)
                || ContainsUnsubstitutedGenericParameter(
                    modified.UnmodifiedType),
            _ => false,
        };

    private static ExtensionSignatureType? TryGetExtensionSignatureType(
        MetadataReader reader,
        EntityHandle handle,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork)
    {
        var provider = new ExtensionSignatureTypeProvider(
            currentScope,
            beforeDecodeWork);
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => provider.GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeReference => provider.GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeSpecification => provider.GetTypeFromSpecification(
                reader,
                context: null,
                handle: (TypeSpecificationHandle)handle,
                rawTypeKind: 0),
            _ => null,
        };
    }

    private static ExtensionSignatureType SubstituteInterfaceTypeParameters(
        ExtensionSignatureType type,
        ExtensionSignatureType constructedInterface)
    {
        ImmutableArray<ExtensionSignatureType> arguments =
            constructedInterface is GenericExtensionSignatureType generic
                ? generic.TypeArguments
                : [];
        return SubstituteInterfaceTypeParameters(type, arguments);
    }

    private static ExtensionSignatureType SubstituteInterfaceTypeParameters(
        ExtensionSignatureType type,
        ImmutableArray<ExtensionSignatureType> arguments)
        => type switch
        {
            GenericParameterExtensionSignatureType
                {
                    IsMethodParameter: false,
                    Index: var index
                }
                when index < arguments.Length => arguments[index],
            ArrayExtensionSignatureType array => new ArrayExtensionSignatureType(
                SubstituteInterfaceTypeParameters(array.ElementType, arguments),
                array.Rank,
                array.Sizes,
                array.LowerBounds),
            WrappedExtensionSignatureType wrapped => new WrappedExtensionSignatureType(
                SubstituteInterfaceTypeParameters(wrapped.ElementType, arguments),
                wrapped.Kind),
            GenericExtensionSignatureType generic => new GenericExtensionSignatureType(
                SubstituteInterfaceTypeParameters(generic.GenericType, arguments),
                generic.TypeArguments.Select(
                    argument => SubstituteInterfaceTypeParameters(
                        argument,
                        arguments)).ToImmutableArray()),
            FunctionPointerExtensionSignatureType functionPointer =>
                new FunctionPointerExtensionSignatureType(
                    functionPointer.CallingConvention,
                    functionPointer.Attributes,
                    functionPointer.GenericParameterCount,
                    functionPointer.RequiredParameterCount,
                    functionPointer.ParameterTypes.Select(
                        parameter => SubstituteInterfaceTypeParameters(
                            parameter,
                            arguments)).ToImmutableArray(),
                    SubstituteInterfaceTypeParameters(
                        functionPointer.ReturnType,
                        arguments)),
            ModifiedExtensionSignatureType modified =>
                new ModifiedExtensionSignatureType(
                    SubstituteInterfaceTypeParameters(
                        modified.Modifier,
                        arguments),
                    SubstituteInterfaceTypeParameters(
                        modified.UnmodifiedType,
                        arguments),
                    modified.IsRequired),
            _ => type,
        };

    private static bool TargetsClassHierarchy(
        MetadataReader reader,
        TypeDefinition typeDef,
        EntityHandle declaringType,
        MetadataNamedTypeIdentity? declarationIdentity,
        MetadataTypeScope? currentScope,
        IReadOnlyDictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> localTypes,
        Action<int>? beforeDecodeWork)
    {
        EntityHandle baseType = typeDef.BaseType;
        HashSet<TypeDefinitionHandle> visited = [];
        for (int depth = 0;
            !baseType.IsNil && depth < MaxBaseChainDepth;
            depth++)
        {
            if (baseType == declaringType)
                return true;
            var baseIdentity = TryGetNamedTypeIdentity(
                reader,
                baseType,
                currentScope,
                beforeDecodeWork);
            if (declarationIdentity is not null
                && baseIdentity is not null
                && HaveSameNamedType(
                    reader,
                    baseType,
                    baseIdentity,
                    declaringType,
                    declarationIdentity,
                    currentScope,
                    localTypes,
                    beforeDecodeWork))
            {
                return true;
            }
            if (!TryResolveKnownLocalType(
                    reader,
                    baseType,
                    currentScope,
                    localTypes,
                    beforeDecodeWork,
                    out var baseDefinition)
                || !visited.Add(baseDefinition))
            {
                return false;
            }

            baseType = reader.GetTypeDefinition(baseDefinition).BaseType;
        }

        return false;
    }

    private static Dictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> LocalTypes(
        MetadataReader reader,
        MetadataTypeScope currentScope,
        ApiSurface surface,
        ExtractionBudget? budget)
    {
        Action<int>? beforeDecodeWork =
            budget is null ? null : budget.ObservePendingDecodeWork;
        Dictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> types =
            new(MetadataNamedTypeIdentityComparer.Instance);
        foreach (var handle in reader.TypeDefinitions)
        {
            if (ReadDefinitionIdentity(
                    reader,
                    handle,
                    currentScope,
                    beforeDecodeWork) is { } identity)
            {
                if (!types.TryAdd(identity, handle))
                {
                    types[identity] = null;
                    AddInspectionFailure(
                        surface,
                        budget,
                        "type identity",
                        handle,
                        MetadataTypeNameFailure.Malformed(
                            handle,
                            "Duplicate type definition identity "
                            + $"'{identity.Name.ToMetadataFullName()}'."));
                }
            }
        }

        return types;
    }

    private static bool TryGetUniqueLocalType(
        IReadOnlyDictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> localTypes,
        MetadataNamedTypeIdentity identity,
        out TypeDefinitionHandle handle)
    {
        if (localTypes.TryGetValue(identity, out var candidate)
            && candidate is { } unique)
        {
            handle = unique;
            return true;
        }

        handle = default;
        return false;
    }

    private static bool TryResolveKnownLocalType(
        MetadataReader reader,
        EntityHandle type,
        MetadataTypeScope? currentScope,
        IReadOnlyDictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> localTypes,
        Action<int>? beforeDecodeWork,
        out TypeDefinitionHandle definition)
    {
        if (type.Kind != HandleKind.TypeReference
            && TryResolvePhysicalLocalType(
                reader,
                type,
                beforeDecodeWork) is { } physical)
        {
            definition = physical;
            return true;
        }
        if (TryGetNamedTypeIdentity(
                reader,
                type,
                currentScope,
                beforeDecodeWork) is { } identity
            && TryGetUniqueLocalType(localTypes, identity, out definition))
        {
            return true;
        }
        definition = default;
        return false;
    }

    private static TypeDefinitionHandle? TryResolvePhysicalLocalType(
        MetadataReader reader,
        EntityHandle type,
        Action<int>? beforeDecodeWork = null) =>
        type.Kind switch
        {
            HandleKind.TypeDefinition => (TypeDefinitionHandle)type,
            HandleKind.TypeReference => TryResolveModuleScopedLocalType(
                reader,
                (TypeReferenceHandle)type,
                beforeDecodeWork),
            HandleKind.TypeSpecification => GuardedProviderDecode.TypeSpec(
                reader,
                (TypeSpecificationHandle)type,
                beforeDecodeWork is null
                    ? PhysicalLocalTypeProvider.Instance
                    : new PhysicalLocalTypeProvider(beforeDecodeWork),
                context: null,
                fallback: null),
            _ => null,
        };

    private static TypeDefinitionHandle? TryResolveModuleScopedLocalType(
        MetadataReader reader,
        TypeReferenceHandle handle,
        Action<int>? beforeDecodeWork)
    {
        Span<TypeReferenceHandle> rootToLeaf =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                reader,
                handle,
                rootToLeaf,
                out _,
                out EntityHandle terminal,
                out _)
            || terminal.Kind != HandleKind.ModuleDefinition
            || MetadataTypeDefinitionNameReader.Read(
                reader,
                handle,
                beforeDecodeWork)
                is not MetadataTypeDefinitionNameReadResult.Read read)
        {
            return null;
        }

        TypeDefinitionHandle match = default;
        foreach (var candidate in reader.TypeDefinitions)
        {
            beforeDecodeWork?.Invoke(1);
            var result = MetadataTypeDefinitionNameReader.Matches(
                reader,
                candidate,
                read.Name,
                out _);
            if (result != MetadataTypeDefinitionNameMatch.Match)
                continue;
            if (!match.IsNil)
                return null;
            match = candidate;
        }

        return match.IsNil ? null : match;
    }

    private static bool HaveSameNamedType(
        MetadataReader reader,
        EntityHandle left,
        MetadataNamedTypeIdentity leftIdentity,
        EntityHandle right,
        MetadataNamedTypeIdentity rightIdentity,
        MetadataTypeScope? currentScope,
        IReadOnlyDictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?> localTypes,
        Action<int>? beforeDecodeWork)
    {
        if (left == right)
            return true;
        if (!MetadataNamedTypeIdentityComparer.Instance.Equals(
                leftIdentity,
                rightIdentity))
            return false;
        if (!localTypes.ContainsKey(leftIdentity))
            return true;

        return TryResolveKnownLocalType(
                reader,
                left,
                currentScope,
                localTypes,
                beforeDecodeWork,
                out var leftDefinition)
            && TryResolveKnownLocalType(
                reader,
                right,
                currentScope,
                localTypes,
                beforeDecodeWork,
                out var rightDefinition)
            && leftDefinition == rightDefinition;
    }

    private static MetadataNamedTypeIdentity? TryGetNamedTypeIdentity(
        MetadataReader reader,
        EntityHandle handle,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork) =>
        handle.Kind switch
        {
            HandleKind.TypeDefinition => currentScope is null
                ? null
                : ReadDefinitionIdentity(
                    reader,
                    (TypeDefinitionHandle)handle,
                    currentScope,
                    beforeDecodeWork),
            HandleKind.TypeReference => ReadReferenceIdentity(
                reader,
                (TypeReferenceHandle)handle,
                currentScope,
                beforeDecodeWork),
            HandleKind.TypeSpecification => GuardedProviderDecode.TypeSpec(
                reader,
                (TypeSpecificationHandle)handle,
                new NamedTypeIdentityProvider(
                    currentScope,
                    beforeDecodeWork),
                context: null,
                fallback: null),
            _ => null,
        };

    private static MetadataNamedTypeIdentity? ReadDefinitionIdentity(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        MetadataTypeScope currentScope,
        Action<int>? beforeDecodeWork) =>
        MetadataTypeDefinitionNameReader.Read(
            reader,
            handle,
            beforeDecodeWork)
            is MetadataTypeDefinitionNameReadResult.Read read
                ? new(read.Name, currentScope)
                : null;

    private static MetadataNamedTypeIdentity? ReadReferenceIdentity(
        MetadataReader reader,
        TypeReferenceHandle handle,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork)
    {
        if (MetadataTypeDefinitionNameReader.Read(
                reader,
                handle,
                beforeDecodeWork)
                is not MetadataTypeDefinitionNameReadResult.Read read
            || ReferenceScope(
                reader,
                handle,
                currentScope,
                beforeDecodeWork) is not { } scope)
        {
            return null;
        }

        return new(read.Name, scope);
    }

    private static MetadataTypeScope CurrentScope(
        MetadataReader reader,
        Action<int>? beforeDecodeWork) =>
        reader.IsAssembly
            ? new(
                ReadAssemblyDefinitionIdentity(reader, beforeDecodeWork),
                null)
            : new(
                null,
                DecodeString(
                    reader,
                    reader.GetModuleDefinition().Name,
                    beforeDecodeWork));

    private static MetadataTypeScope? ReadCurrentScope(
        MetadataReader reader,
        ApiSurface surface,
        ExtractionBudget? budget)
    {
        try
        {
            return CurrentScope(
                reader,
                budget is null ? null : budget.ObservePendingDecodeWork);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            EntityHandle subject = MetadataTokens.EntityHandle(
                reader.IsAssembly ? 0x20000001 : 0x00000001);
            AddInspectionFailure(
                surface,
                budget,
                "module identity",
                subject,
                MetadataTypeNameFailure.Malformed(subject, ex.Message));
            return null;
        }
    }

    private static MetadataTypeScope? ReferenceScope(
        MetadataReader reader,
        TypeReferenceHandle handle,
        MetadataTypeScope? currentScope,
        Action<int>? beforeDecodeWork)
    {
        Span<TypeReferenceHandle> rootToLeaf =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                reader,
                handle,
                rootToLeaf,
                out _,
                out EntityHandle terminal,
                out _))
        {
            return null;
        }
        if (terminal.IsNil)
            return null;

        return terminal.Kind switch
        {
            HandleKind.ModuleDefinition => currentScope,
            HandleKind.ModuleReference => new(
                null,
                DecodeString(
                    reader,
                    reader.GetModuleReference((ModuleReferenceHandle)terminal).Name,
                    beforeDecodeWork)),
            HandleKind.AssemblyReference => new(
                ReadAssemblyReferenceIdentity(
                    reader,
                    (AssemblyReferenceHandle)terminal,
                    beforeDecodeWork),
                null),
            _ => null,
        };
    }

    private static AssemblyReferenceIdentity ReadAssemblyDefinitionIdentity(
        MetadataReader reader,
        Action<int>? beforeDecodeWork)
    {
        if (!reader.IsAssembly)
            throw new BadImageFormatException("The metadata image is not an assembly.");

        var definition = reader.GetAssemblyDefinition();
        if (!definition.PublicKey.IsNil)
        {
            beforeDecodeWork?.Invoke(
                reader.GetBlobReader(definition.PublicKey).Length);
        }
        return new AssemblyReferenceIdentity(
            DecodeString(reader, definition.Name, beforeDecodeWork),
            definition.Version,
            definition.Culture.IsNil
                ? null
                : DecodeString(
                    reader,
                    definition.Culture,
                    beforeDecodeWork),
            AssemblyReferenceIdentity.TokenOrNull(
                reader,
                definition.PublicKey,
                isPublicKey: true));
    }

    private static AssemblyReferenceIdentity ReadAssemblyReferenceIdentity(
        MetadataReader reader,
        AssemblyReferenceHandle handle,
        Action<int>? beforeDecodeWork)
    {
        var reference = reader.GetAssemblyReference(handle);
        beforeDecodeWork?.Invoke(reader.GetBlobReader(reference.Name).Length);
        if (!reference.Culture.IsNil)
        {
            beforeDecodeWork?.Invoke(
                reader.GetBlobReader(reference.Culture).Length);
        }
        if (!reference.PublicKeyOrToken.IsNil)
        {
            beforeDecodeWork?.Invoke(
                reader.GetBlobReader(reference.PublicKeyOrToken).Length);
        }
        return AssemblyReferenceIdentity.From(
            handle,
            AssemblyReferenceIdentity.RetainedProjection(reader));
    }

    private sealed record MetadataTypeScope(
        AssemblyReferenceIdentity? Assembly,
        string? Module);

    private sealed record MetadataNamedTypeIdentity(
        MetadataTypeDefinitionName Name,
        MetadataTypeScope Scope);

    private sealed class MetadataNamedTypeIdentityComparer :
        IEqualityComparer<MetadataNamedTypeIdentity>
    {
        internal static MetadataNamedTypeIdentityComparer Instance { get; } =
            new();

        public bool Equals(
            MetadataNamedTypeIdentity? left,
            MetadataNamedTypeIdentity? right) =>
            ReferenceEquals(left, right)
            || left is not null
                && right is not null
                && left.Name == right.Name
                && AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    left.Scope.Assembly,
                    right.Scope.Assembly)
                && StringComparer.Ordinal.Equals(
                    left.Scope.Module,
                    right.Scope.Module);

        public int GetHashCode(MetadataNamedTypeIdentity identity)
        {
            var hash = new HashCode();
            hash.Add(identity.Name);
            hash.Add(
                identity.Scope.Assembly is null
                    ? 0
                    : AssemblyReferenceIdentity.EquivalentComparer.GetHashCode(
                        identity.Scope.Assembly));
            hash.Add(identity.Scope.Module, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }

    private sealed class NamedTypeIdentityProvider :
        ISignatureTypeProvider<MetadataNamedTypeIdentity?, object?>
    {
        private readonly MetadataTypeScope? currentScope;
        private readonly Action<int>? beforeDecodeWork;

        internal NamedTypeIdentityProvider(
            MetadataTypeScope? currentScope,
            Action<int>? beforeDecodeWork)
        {
            this.currentScope = currentScope;
            this.beforeDecodeWork = beforeDecodeWork;
        }

        public MetadataNamedTypeIdentity? GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind) =>
            currentScope is null
                ? null
                : ReadDefinitionIdentity(
                    reader,
                    handle,
                    currentScope,
                    beforeDecodeWork);

        public MetadataNamedTypeIdentity? GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind) =>
            ReadReferenceIdentity(
                reader,
                handle,
                currentScope,
                beforeDecodeWork);

        public MetadataNamedTypeIdentity? GetTypeFromSpecification(
            MetadataReader reader,
            object? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            GuardedProviderDecode.TypeSpec(
                reader,
                handle,
                this,
                context,
                fallback: null);

        public MetadataNamedTypeIdentity? GetGenericInstantiation(
            MetadataNamedTypeIdentity? genericType,
            ImmutableArray<MetadataNamedTypeIdentity?> typeArguments)
        {
            Observe(checked(16L + typeArguments.Length * 8L));
            // Interface ownership follows the generic type definition while the
            // MethodImpl signature remains responsible for constructed arguments.
            return genericType;
        }

        public MetadataNamedTypeIdentity? GetModifiedType(
            MetadataNamedTypeIdentity? modifier,
            MetadataNamedTypeIdentity? unmodifiedType,
            bool isRequired)
        {
            Observe(16);
            return unmodifiedType;
        }

        public MetadataNamedTypeIdentity? GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            Observe(16);
            return null;
        }
        public MetadataNamedTypeIdentity? GetSZArrayType(MetadataNamedTypeIdentity? elementType)
        {
            Observe(16);
            return null;
        }
        public MetadataNamedTypeIdentity? GetArrayType(
            MetadataNamedTypeIdentity? elementType,
            ArrayShape shape)
        {
            Observe(16L + Math.Max(shape.Rank, 0));
            return null;
        }
        public MetadataNamedTypeIdentity? GetByReferenceType(
            MetadataNamedTypeIdentity? elementType)
        {
            Observe(16);
            return null;
        }
        public MetadataNamedTypeIdentity? GetPointerType(
            MetadataNamedTypeIdentity? elementType)
        {
            Observe(16);
            return null;
        }
        public MetadataNamedTypeIdentity? GetPinnedType(
            MetadataNamedTypeIdentity? elementType)
        {
            Observe(16);
            return elementType;
        }
        public MetadataNamedTypeIdentity? GetGenericTypeParameter(
            object? context,
            int index)
        {
            Observe(16);
            return null;
        }
        public MetadataNamedTypeIdentity? GetGenericMethodParameter(
            object? context,
            int index)
        {
            Observe(16);
            return null;
        }
        public MetadataNamedTypeIdentity? GetFunctionPointerType(
            MethodSignature<MetadataNamedTypeIdentity?> signature)
        {
            Observe(checked(16L + signature.ParameterTypes.Length * 8L));
            return null;
        }

        private void Observe(long units) =>
            beforeDecodeWork?.Invoke((int)Math.Min(units, int.MaxValue));
    }

    private sealed class PhysicalLocalTypeProvider :
        ISignatureTypeProvider<TypeDefinitionHandle?, object?>
    {
        private readonly Action<int>? beforeDecodeWork;

        internal static PhysicalLocalTypeProvider Instance { get; } =
            new(beforeDecodeWork: null);

        internal PhysicalLocalTypeProvider(Action<int>? beforeDecodeWork) =>
            this.beforeDecodeWork = beforeDecodeWork;

        public TypeDefinitionHandle? GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            Observe(16);
            return handle;
        }

        public TypeDefinitionHandle? GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind) =>
            TryResolveModuleScopedLocalType(
                reader,
                handle,
                beforeDecodeWork);

        public TypeDefinitionHandle? GetTypeFromSpecification(
            MetadataReader reader,
            object? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            GuardedProviderDecode.TypeSpec(
                reader,
                handle,
                this,
                context,
                fallback: null);

        public TypeDefinitionHandle? GetGenericInstantiation(
            TypeDefinitionHandle? genericType,
            ImmutableArray<TypeDefinitionHandle?> typeArguments)
        {
            Observe(checked(16L + typeArguments.Length * 8L));
            return genericType;
        }

        public TypeDefinitionHandle? GetModifiedType(
            TypeDefinitionHandle? modifier,
            TypeDefinitionHandle? unmodifiedType,
            bool isRequired)
        {
            Observe(16);
            return unmodifiedType;
        }

        public TypeDefinitionHandle? GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            Observe(16);
            return null;
        }
        public TypeDefinitionHandle? GetSZArrayType(TypeDefinitionHandle? elementType)
        {
            Observe(16);
            return null;
        }
        public TypeDefinitionHandle? GetArrayType(
            TypeDefinitionHandle? elementType,
            ArrayShape shape)
        {
            Observe(16L + Math.Max(shape.Rank, 0));
            return null;
        }
        public TypeDefinitionHandle? GetByReferenceType(
            TypeDefinitionHandle? elementType)
        {
            Observe(16);
            return null;
        }
        public TypeDefinitionHandle? GetPointerType(
            TypeDefinitionHandle? elementType)
        {
            Observe(16);
            return null;
        }
        public TypeDefinitionHandle? GetPinnedType(
            TypeDefinitionHandle? elementType)
        {
            Observe(16);
            return elementType;
        }
        public TypeDefinitionHandle? GetGenericTypeParameter(
            object? context,
            int index)
        {
            Observe(16);
            return null;
        }
        public TypeDefinitionHandle? GetGenericMethodParameter(
            object? context,
            int index)
        {
            Observe(16);
            return null;
        }
        public TypeDefinitionHandle? GetFunctionPointerType(
            MethodSignature<TypeDefinitionHandle?> signature)
        {
            Observe(checked(16L + signature.ParameterTypes.Length * 8L));
            return null;
        }

        private void Observe(long units) =>
            beforeDecodeWork?.Invoke((int)Math.Min(units, int.MaxValue));
    }

    /// <summary>
    /// The set of methods on <paramref name="typeDef"/> whose explicit
    /// <c>.override</c> MethodImpl targets <c>System.Object::Finalize</c> — the
    /// slot a C# <c>~Type()</c> destructor compiles to. Requiring exact body and
    /// declaration signatures lets the C# writer spell <c>~Type()</c> for real
    /// finalizers while excluding malformed MethodImpl rows, an unrelated
    /// <c>Finalize</c> slot, or an explicit interface implementation.
    /// </summary>
    private static HashSet<MethodDefinitionHandle> GetObjectFinalizeOverrides(
        MetadataReader reader,
        IReadOnlyList<OwnedMethodImplementation> implementations,
        Action<int>? beforeDecodeWork = null)
    {
        HashSet<MethodDefinitionHandle> handles = [];
        foreach (var implementation in implementations)
        {
            var body = reader.GetMethodDefinition(implementation.Body);
            if (IsFinalizerBodyShape(
                    reader,
                    body,
                    beforeDecodeWork)
                && ReferencesObjectFinalize(
                    reader,
                    implementation.Declaration,
                    beforeDecodeWork))
                handles.Add(implementation.Body);
        }

        return handles;
    }

    /// <summary>
    /// True when <paramref name="methodHandle"/> is a C# destructor / VB finalizer: a non-generic
    /// method named <c>Finalize</c> that overrides <c>System.Object::Finalize</c>, either via an
    /// explicit <c>.override</c> MethodImpl (the Roslyn/C# shape) or by implicitly reusing the
    /// inherited object.Finalize slot (the VB.NET shape, which carries no MethodImpl). This mirrors
    /// the <see cref="ApiMember.IsFinalizer"/> object.Finalize-override signal and is shared with the
    /// source-mapping producer (<see cref="PdbContext.EnumerateMemberSources"/>) so a destructor's
    /// <c>~Type()</c> source line is anchored from metadata identity rather than inferred from source
    /// text. The <c>Finalize</c> name gate keeps the MethodImpl enumeration off the hot path for
    /// every other method.
    /// </summary>
    internal static bool IsFinalizerMethod(MetadataReader reader, MethodDefinitionHandle methodHandle)
    {
        try
        {
            return FinalizerMethodImplementationCaches
                .GetValue(
                    reader,
                    static _ => new FinalizerMethodImplementationCache())
                .IsFinalizerMethod(reader, methodHandle);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return false;
        }
    }

    private static bool IsFinalizerOwner(
        TypeDefinition type,
        string typeKind)
        => (type.Attributes & TypeAttributes.Interface) == 0
            && string.Equals(typeKind, "class", StringComparison.Ordinal);

    private static bool IsFinalizerOwner(
        MetadataReader reader,
        TypeDefinition type)
    {
        if ((type.Attributes & TypeAttributes.Interface) != 0)
            return false;

        // Keep the PDB-only classifier aligned with API extraction's
        // class-only gate. A malformed value type or delegate can carry a
        // finalizer-shaped MethodImpl, but it is not a class destructor.
        try
        {
            return TypeResolver.GetTypeName(reader, type.BaseType) is not (
                "System.Enum"
                or "System.ValueType"
                or "System.Delegate"
                or "System.MulticastDelegate");
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            // PDB projection has no surface failure channel. Treat a malformed
            // owner as not proven to be a class instead of leaking a reader
            // exception from a finalizer predicate.
            return false;
        }
    }

    // A malformed or adversarial base-type chain can be arbitrarily long or cyclic; visited sets
    // stop in-assembly cycles, and this cap bounds all hierarchy classifications over a long
    // legitimate (or degenerate) chain. Real API hierarchies are far shallower than this.
    private const int MaxBaseChainDepth = 256;

    /// <summary>
    /// True when <paramref name="method"/> on <paramref name="typeDefHandle"/> implicitly overrides
    /// <c>System.Object.Finalize</c> through the inherited virtual slot rather than an explicit
    /// <c>.override</c> MethodImpl — the shape the VB.NET compiler emits for
    /// <c>Protected Overrides Sub Finalize()</c>. The method must be a non-generic, parameterless,
    /// <c>void</c>-returning, non-static, non-abstract virtual that reuses (does not new-slot) the
    /// inherited slot, and the declaring type's base chain must be provably rooted at
    /// <c>System.Object</c> using metadata alone (SRM-only, no inspected-assembly loading):
    /// <list type="bullet">
    /// <item>a base reference resolving to the strong-name-anchored <c>System.Object</c> confirms;</item>
    /// <item>an in-assembly base that introduces its own <c>new virtual void Finalize()</c> slot is a
    /// custom slot (the round-1 false-positive shape) and rejects;</item>
    /// <item>a local generic base is followed through its
    /// <see cref="TypeSpecification"/> to its physical type definition;</item>
    /// <item>a base that leaves the assembly without resolving to <c>System.Object</c> cannot be
    /// proven and rejects conservatively, so no guessed <c>~Type()</c> is spelled for an
    /// unresolvable chain.</item>
    /// </list>
    /// </summary>
    private static bool IsImplicitObjectFinalizeOverride(
        MetadataReader reader,
        TypeDefinitionHandle typeDefHandle,
        MethodDefinition method,
        MetadataTypeScope? currentScope,
        IReadOnlyDictionary<MetadataNamedTypeIdentity, TypeDefinitionHandle?>? localTypes,
        Action<int>? beforeDecodeWork = null)
    {
        if (!string.Equals(
                DecodeString(reader, method.Name, beforeDecodeWork),
                "Finalize",
                StringComparison.Ordinal))
            return false;

        var attributes = method.Attributes;
        // A finalizer reuses the inherited object.Finalize slot: Virtual and NOT NewSlot. An explicit
        // interface implementation is NewSlot (and name-mangled), so it is excluded here too. Static
        // and abstract methods are never finalizers.
        if ((attributes & MethodAttributes.Virtual) == 0
            || (attributes & MethodAttributes.NewSlot) != 0
            || (attributes & MethodAttributes.Static) != 0
            || (attributes & MethodAttributes.Abstract) != 0)
            return false;
        if (method.GetGenericParameters().Count != 0)
            return false;
        if (!HasVoidNullaryInstanceSignature(
                reader,
                method,
                beforeDecodeWork))
            return false;

        // Walk the base-type chain. The slot roots at whichever ancestor first declares a
        // `new virtual void Finalize()`; for a genuine finalizer that ancestor is System.Object,
        // which we recognize by reaching its (typically cross-assembly) reference without any
        // in-assembly base introducing its own Finalize slot first.
        var visited = new HashSet<TypeDefinitionHandle>();
        var currentType = reader.GetTypeDefinition(typeDefHandle);
        for (int depth = 0; depth < MaxBaseChainDepth; depth++)
        {
            var baseHandle = currentType.BaseType;
            if (baseHandle.IsNil)
                return false;

            if (IsSystemObjectType(
                    reader,
                    baseHandle,
                    beforeDecodeWork))
            {
                return true;
            }

            TypeDefinitionHandle? resolvedBase =
                localTypes is not null
                    && TryResolveKnownLocalType(
                        reader,
                        baseHandle,
                        currentScope,
                        localTypes,
                        beforeDecodeWork,
                        out var knownLocalBase)
                        ? knownLocalBase
                        : TryResolvePhysicalLocalType(
                            reader,
                            baseHandle,
                            beforeDecodeWork);
            if (resolvedBase is not { } baseTypeHandle
                || !visited.Add(baseTypeHandle))
            {
                return false;
            }

            // Recognize the in-assembly System.Object root before the custom-slot rejection:
            // the genuine object.Finalize is itself a NewSlot virtual, so testing
            // DeclaresNewVirtualFinalize first would wrongly reject the real root when
            // inspecting the core library that defines System.Object.
            if (IsSystemObjectType(
                    reader,
                    baseTypeHandle,
                    beforeDecodeWork))
                return true; // in-assembly System.Object (inspecting the core library itself)
            var baseType = reader.GetTypeDefinition(baseTypeHandle);
            if (DeclaresNewVirtualFinalize(
                    reader,
                    baseType,
                    beforeDecodeWork))
                return false; // custom Finalize slot introduced below object — not a destructor
            currentType = baseType;
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="type"/> declares a <c>new virtual void Finalize()</c> — a
    /// parameterless, <c>void</c>-returning, non-generic method named <c>Finalize</c> that new-slots
    /// its own virtual slot. Such a slot shadows <c>object.Finalize</c>, so an override binding to it
    /// is not the object finalizer and must not be spelled <c>~Type()</c>. A base that merely
    /// <em>overrides</em> Finalize (reuse-slot) does not introduce a new slot and is walked past.
    /// </summary>
    private static bool DeclaresNewVirtualFinalize(
        MetadataReader reader,
        TypeDefinition type,
        Action<int>? beforeDecodeWork = null)
    {
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (!string.Equals(
                    DecodeString(reader, method.Name, beforeDecodeWork),
                    "Finalize",
                    StringComparison.Ordinal))
                continue;
            var attributes = method.Attributes;
            if ((attributes & MethodAttributes.Virtual) != 0
                && (attributes & MethodAttributes.NewSlot) != 0
                && method.GetGenericParameters().Count == 0
                && HasVoidNullaryInstanceSignature(
                    reader,
                    method,
                    beforeDecodeWork))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="method"/>'s signature is exactly the fixed <c>object.Finalize</c>
    /// slot signature: an instance (<c>HASTHIS</c>, no explicit <c>this</c>), default-calling-convention,
    /// non-generic method with no parameters and a plain <c>void</c> return (no custom modifiers or
    /// by-ref). A vararg or generic calling convention, an explicit-this or static signature, extra
    /// parameters, or a non-void/modified return cannot bind that slot and rejects — so a name-only
    /// collision cannot masquerade as a finalizer. A malformed or truncated signature blob is treated
    /// as a non-match (returns false) rather than throwing.
    /// </summary>
    private static bool HasVoidNullaryInstanceSignature(
        MetadataReader reader,
        MethodDefinition method,
        Action<int>? beforeDecodeWork = null) =>
        HasVoidNullaryInstanceSignature(
            reader,
            method.Signature,
            beforeDecodeWork);

    private static bool HasVoidNullaryInstanceSignature(
        MetadataReader reader,
        BlobHandle signature,
        Action<int>? beforeDecodeWork = null)
    {
        try
        {
            var blob = reader.GetBlobReader(signature);
            beforeDecodeWork?.Invoke(blob.Length);
            var header = blob.ReadSignatureHeader();
            // object.Finalize is `instance void ()` with the default managed calling convention.
            // Reject anything else: field/property sigs, vararg/unmanaged conventions, generic
            // methods, static signatures, and explicit-this.
            if (header.Kind != SignatureKind.Method
                || header.CallingConvention != SignatureCallingConvention.Default
                || header.IsGeneric
                || !header.IsInstance
                || header.HasExplicitThis)
                return false;
            if (blob.ReadCompressedInteger() != 0) // parameter count
                return false;
            // Return type: a plain ELEMENT_TYPE_VOID. Any leading custom modifier or by-ref token is
            // read here instead of Void and correctly rejects.
            return blob.ReadSignatureTypeCode() == SignatureTypeCode.Void
                && blob.RemainingBytes == 0;
        }
        catch (BadImageFormatException)
        {
            // A truncated or otherwise malformed signature blob is not the object.Finalize slot.
            return false;
        }
    }

    private static bool HasVoidNullaryStaticSignature(
        MetadataReader reader,
        MethodDefinition method)
    {
        try
        {
            BlobReader blob = reader.GetBlobReader(method.Signature);
            SignatureHeader header = blob.ReadSignatureHeader();
            return header.Kind == SignatureKind.Method
                && header.CallingConvention
                    == SignatureCallingConvention.Default
                && !header.IsGeneric
                && !header.IsInstance
                && !header.HasExplicitThis
                && blob.ReadCompressedInteger() == 0
                && blob.ReadSignatureTypeCode()
                    == SignatureTypeCode.Void;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static bool IsFinalizerBodyShape(
        MetadataReader reader,
        MethodDefinition method,
        Action<int>? beforeDecodeWork = null)
    {
        if (!string.Equals(
                DecodeString(reader, method.Name, beforeDecodeWork),
                "Finalize",
                StringComparison.Ordinal)
            || method.GetGenericParameters().Count != 0)
        {
            return false;
        }

        var attributes = method.Attributes;
        return (attributes & MethodAttributes.Static) == 0
            && (attributes & MethodAttributes.Abstract) == 0
            && HasVoidNullaryInstanceSignature(
                reader,
                method,
                beforeDecodeWork);
    }

    /// <summary>
    /// True when a <c>.override</c> MethodImpl declaration names the exact
    /// <c>void System.Object::Finalize()</c> slot.
    /// The target is a <see cref="MemberReferenceHandle"/> in the common case
    /// (object lives in another assembly) and a <see cref="MethodDefinitionHandle"/>
    /// only when inspecting the assembly that defines <c>System.Object</c>.
    /// </summary>
    private static bool ReferencesObjectFinalize(
        MetadataReader reader,
        EntityHandle methodDeclaration,
        Action<int>? beforeDecodeWork = null)
    {
        switch (methodDeclaration.Kind)
        {
            case HandleKind.MemberReference:
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)methodDeclaration);
                return string.Equals(
                        DecodeString(reader, memberRef.Name, beforeDecodeWork),
                        "Finalize",
                        StringComparison.Ordinal)
                    && IsSystemObjectType(
                        reader,
                        memberRef.Parent,
                        beforeDecodeWork)
                    && HasVoidNullaryInstanceSignature(
                        reader,
                        memberRef.Signature,
                        beforeDecodeWork);
            case HandleKind.MethodDefinition:
                var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)methodDeclaration);
                return string.Equals(
                        DecodeString(reader, methodDef.Name, beforeDecodeWork),
                        "Finalize",
                        StringComparison.Ordinal)
                    && IsSystemObjectType(
                        reader,
                        methodDef.GetDeclaringType(),
                        beforeDecodeWork)
                    && HasVoidNullaryInstanceSignature(
                        reader,
                        methodDef,
                        beforeDecodeWork);
            default:
                return false;
        }
    }

    /// <summary>True when <paramref name="typeHandle"/> resolves to <c>System.Object</c>.</summary>
    private static bool IsSystemObjectType(
        MetadataReader reader,
        EntityHandle typeHandle,
        Action<int>? beforeDecodeWork = null)
    {
        switch (typeHandle.Kind)
        {
            case HandleKind.TypeReference:
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)typeHandle);
                // Cross-assembly reference (the normal Roslyn case): the target
                // assembly cannot be resolved from metadata alone (SRM-only, no
                // inspected-assembly loading), so we cannot check its object is
                // the real root. Require the reference to resolve through a
                // recognized core library — matched by both assembly name and
                // its strong-name public-key token — so that an adversarial
                // `System.Object` defined in an arbitrary or name-impersonating
                // assembly is rejected.
                return string.Equals(
                        DecodeString(reader, typeRef.Namespace, beforeDecodeWork),
                        "System",
                        StringComparison.Ordinal)
                    && string.Equals(
                        DecodeString(reader, typeRef.Name, beforeDecodeWork),
                        "Object",
                        StringComparison.Ordinal)
                    && ResolvesThroughCoreLibrary(
                        reader,
                        typeRef.ResolutionScope,
                        beforeDecodeWork);
            case HandleKind.TypeDefinition:
                return CoreLibraryRootAuthentication
                    .IsUniqueTopLevelCoreLibraryRoot(
                        reader,
                        (TypeDefinitionHandle)typeHandle);
            default:
                return false;
        }
    }

    // The reference assemblies and runtime cores that define the real
    // System.Object, paired with the strong-name public-key token(s) each is
    // legitimately signed with. `mscorlib` shipped under several Microsoft
    // tokens across historical profiles (desktop .NET Framework, Silverlight/
    // PCL/Windows Phone, and the .NET Compact Framework), so a name maps to a
    // set of accepted tokens. A TypeReference to `System.Object` that resolves
    // through any other assembly — or through an assembly that impersonates one
    // of these names without carrying a matching Microsoft public-key token —
    // is an adversarial or accidental lookalike, not the runtime finalizer slot.
    private static readonly Dictionary<string, byte[][]> CoreLibraryPublicKeyTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["System.Runtime"] = new[] { new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a } },
            ["System.Private.CoreLib"] = new[] { new byte[] { 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e } },
            ["mscorlib"] = new[]
            {
                new byte[] { 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89 }, // .NET Framework (desktop)
                new byte[] { 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e }, // Silverlight / PCL / Windows Phone
                new byte[] { 0x96, 0x9d, 0xb8, 0x05, 0x3d, 0x33, 0x22, 0xac }, // .NET Compact Framework
            },
            ["netstandard"] = new[] { new byte[] { 0xcc, 0x7b, 0x13, 0xff, 0xcd, 0x2d, 0xdd, 0x51 } },
        };

    /// <summary>
    /// True when <paramref name="resolutionScope"/> is an
    /// <see cref="AssemblyReference"/> to a recognized core library — matched by
    /// both assembly name and one of that library's legitimate strong-name
    /// public-key tokens — the resolution scope a real cross-assembly
    /// <c>System.Object</c> reference carries. Nested
    /// (<see cref="TypeReference"/>), module, and nil scopes are rejected:
    /// <c>System.Object</c> is never a nested type, and a same-module object is
    /// a <see cref="TypeDefinition"/> handled elsewhere. An assembly that
    /// impersonates a core-library name but lacks (or forges) a matching
    /// public-key token is rejected.
    /// </summary>
    internal static bool ResolvesThroughCoreLibrary(MetadataReader reader, EntityHandle resolutionScope)
        => ResolvesThroughCoreLibrary(
            reader,
            resolutionScope,
            beforeDecodeWork: null);

    private static bool ResolvesThroughCoreLibrary(
        MetadataReader reader,
        EntityHandle resolutionScope,
        Action<int>? beforeDecodeWork)
    {
        if (resolutionScope.Kind != HandleKind.AssemblyReference)
            return false;
        try
        {
            var handle = (AssemblyReferenceHandle)resolutionScope;
            var assemblyRef = reader.GetAssemblyReference(handle);
            beforeDecodeWork?.Invoke(
                reader.GetBlobReader(assemblyRef.Name).Length);
            if (!assemblyRef.Culture.IsNil)
            {
                beforeDecodeWork?.Invoke(
                    reader.GetBlobReader(assemblyRef.Culture).Length);
            }
            if (!assemblyRef.PublicKeyOrToken.IsNil)
            {
                beforeDecodeWork?.Invoke(
                    reader.GetBlobReader(assemblyRef.PublicKeyOrToken).Length);
            }

            return ResolvesThroughCoreLibrary(
                AssemblyReferenceIdentity.From(
                    handle,
                    AssemblyReferenceIdentity.RetainedProjection(
                        reader)));
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or ArgumentException)
        {
            return false;
        }
    }

    internal static bool IsCoreLibraryAssemblyDefinition(
        MetadataReader reader)
    {
        if (!reader.IsAssembly)
            return false;
        try
        {
            return ResolvesThroughCoreLibrary(
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader));
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or ArgumentException)
        {
            return false;
        }
    }

    internal static bool ResolvesThroughCoreLibrary(
        AssemblyReferenceIdentity reference)
    {
        if (reference.PublicKeyToken is not { } token
            || !CoreLibraryPublicKeyTokens.TryGetValue(
                reference.Name,
                out byte[][]? expectedTokens))
        {
            return false;
        }

        foreach (byte[] expected in expectedTokens)
        {
            if (string.Equals(
                    token,
                    Convert.ToHexString(expected),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AttachLocalExtensionMethods(
        ApiSurface surface,
        IReadOnlyDictionary<ApiMember, MetadataTypeDefinitionName> extensionReceiverDefinitions,
        ExtractionBudget? budget = null)
    {
        var targets = new Dictionary<MetadataTypeDefinitionName, ApiType>();
        var ambiguousTargets = new HashSet<MetadataTypeDefinitionName>();
        foreach (ApiType type in surface.Types)
        {
            if (type.DefinitionName is not { } definitionName
                || ambiguousTargets.Contains(definitionName))
            {
                continue;
            }

            if (!targets.TryAdd(definitionName, type))
            {
                targets.Remove(definitionName);
                ambiguousTargets.Add(definitionName);
            }
        }

        foreach (var declaringType in surface.Types)
        {
            var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var extension in declaringType.Members)
            {
                overloadCounts.TryGetValue(extension.Name, out int declaringOverloadIndex);
                declaringOverloadIndex++;
                overloadCounts[extension.Name] = declaringOverloadIndex;
                if (!extensionReceiverDefinitions.TryGetValue(
                        extension,
                        out MetadataTypeDefinitionName? targetName)
                    || !targets.TryGetValue(
                        targetName,
                        out ApiType? targetType))
                {
                    continue;
                }
                if (ReferenceEquals(targetType, declaringType))
                    continue;
                string declaringTypeCanonicalName =
                    ApiMemberIdentity.FormatTypeAnchorName(declaringType);
                if (targetType.Members.Any(member =>
                    member.Kind == "extension-method"
                    && string.Equals(
                        member.DeclaringTypeCanonicalName
                            ?? member.DeclaringType,
                        declaringTypeCanonicalName,
                        StringComparison.Ordinal)
                    && string.Equals(member.Name, extension.Name, StringComparison.Ordinal)
                    && string.Equals(member.Signature, extension.Signature, StringComparison.Ordinal)))
                {
                    continue;
                }

                var attached = new ApiMember
                {
                    Name = extension.Name,
                    Kind = "extension-method",
                    ReturnType = extension.ReturnType,
                    Signature = extension.Signature,
                    SignatureModel = extension.SignatureModel,
                    SignatureDecodeStatus = extension.SignatureDecodeStatus,
                    MetadataToken = extension.MetadataToken,
                    IsStatic = extension.IsStatic,
                    IsVirtual = extension.IsVirtual,
                    IsAbstract = extension.IsAbstract,
                    IsOverride = extension.IsOverride,
                    IsSealed = extension.IsSealed,
                    IsUnsafe = extension.IsUnsafe,
                    IsExtension = true,
                    ExtendedType = extension.ExtendedType,
                    DeclaringType = declaringType.FullName,
                    DeclaringTypeCanonicalName =
                        declaringTypeCanonicalName,
                    DeclaringOverloadIndex = declaringOverloadIndex,
                    IsObsolete = extension.IsObsolete,
                    ObsoleteMessage = extension.ObsoleteMessage,
                    Documentation = extension.Documentation
                };
                budget?.RetainAttachedMember(attached);
                targetType.Members.Add(attached);
            }
        }
    }

    /// <summary>
    /// Names of a type's field-like events. A C# field-like event's compiler-generated backing
    /// field is private, is itself marked <c>[CompilerGenerated]</c>, and shares the event's exact
    /// (unmangled) name. Only events whose adder is <c>[CompilerGenerated]</c> (i.e. genuinely
    /// field-like) contribute a name; hand-authored or non-C# accessors are excluded so a
    /// legitimate same-named field is not suppressed.
    /// </summary>
    static HashSet<string>? FieldLikeEventBackingFieldNames(
        MetadataReader reader,
        TypeDefinition typeDef,
        Action<int>? beforeDecodeWork = null)
    {
        HashSet<string>? names = null;
        foreach (var eventHandle in typeDef.GetEvents())
        {
            var eventDef = reader.GetEventDefinition(eventHandle);
            var adder = eventDef.GetAccessors().Adder;
            if (adder.IsNil
                || !AttributeReader.HasAttribute(
                    reader,
                    reader.GetMethodDefinition(adder).GetCustomAttributes(),
                    KnownAttributeNames.CompilerGeneratedAttribute,
                    beforeDecodeWork))
            {
                continue;
            }

            (names ??= new HashSet<string>(StringComparer.Ordinal)).Add(
                DecodeString(reader, eventDef.Name, beforeDecodeWork));
        }

        return names;
    }

    /// <summary>
    /// True when a field is a field-like event's private, compiler-generated backing field. The
    /// decisive signal is the candidate field's own <c>[CompilerGenerated]</c> marker (not the
    /// accessor's): the C# CS0102 same-name restriction does not bind arbitrary IL, so a genuine
    /// field could share an event's name; requiring the field itself to be private and
    /// compiler-generated keeps it from being folded away.
    /// </summary>
    static bool IsFieldLikeEventBackingField(
        MetadataReader reader,
        FieldDefinition field,
        string fieldName,
        HashSet<string>? fieldLikeEventBackingFieldNames,
        Action<int>? beforeDecodeWork = null)
        => (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private
           && fieldLikeEventBackingFieldNames?.Contains(fieldName) == true
           && AttributeReader.HasAttribute(
               reader,
               field.GetCustomAttributes(),
               KnownAttributeNames.CompilerGeneratedAttribute,
               beforeDecodeWork);

    /// <summary>
    /// A declared auto-property's backing-field descriptor: the property name, decoded return type,
    /// and whether its accessors are static. A genuine backing field must agree with the latter two,
    /// so a merely same-named compiler-generated field of a different type or staticness is not
    /// folded.
    /// </summary>
    readonly record struct AutoPropertyBackingField(
        string PropertyName,
        string PropertyType,
        bool IsStatic);

    /// <summary>
    /// Maps each of a type's auto-property backing-field names (<c>&lt;Prop&gt;k__BackingField</c>)
    /// to its <see cref="AutoPropertyBackingField"/> descriptor. Only genuine auto-properties
    /// contribute: the property has a <c>[CompilerGenerated]</c> accessor (auto signal) and a
    /// decodable return type, and its name carries no <c>&lt;</c> or <c>.</c> (compiler-generated or
    /// explicit-interface names cannot name a C# auto-property). The per-field fold then also
    /// requires the candidate field's type and staticness to match this descriptor, mirroring the
    /// discriminator the compile-back planner historically applied so a same-named but
    /// type/static-mismatched or non-auto-property field is preserved rather than silently dropped.
    /// </summary>
    static Dictionary<string, AutoPropertyBackingField>? AutoPropertyBackingFieldDescriptors(
        MetadataReader reader,
        TypeDefinition typeDef,
        GenericContext context,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null)
    {
        Dictionary<string, AutoPropertyBackingField>? descriptors = null;
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            string propertyName = DecodeString(
                reader,
                property.Name,
                beforeDecodeWork);
            if (propertyName.Contains('<', StringComparison.Ordinal)
                || propertyName.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryGetAutoPropertyAccessorStaticness(
                    reader,
                    property.GetAccessors(),
                    out bool isStatic,
                    beforeDecodeWork))
                continue; // Not an auto-property: no [CompilerGenerated] accessor.

            string? propertyType;
            if (beforeDecodeWork is null)
            {
                if (!GuardedSignatureText.PropertyText(reader, property, context)
                        .TryGetValue(out var propertySignature))
                {
                    continue; // Undecodable property signature: cannot prove a type match.
                }
                propertyType = propertySignature.ReturnType;
            }
            else
            {
                TypeNodeProvider provider =
                    new(beforeRetainText, beforeDecodeWork);
                MethodSignature<TypeNode> signature =
                    GuardedProviderDecode.Property(
                        reader,
                        property,
                        provider,
                        context,
                        (TypeNode)new DegradedTypeNode());
                if (signature.ReturnType.IsDegraded)
                    continue;
                propertyType = signature.ReturnType.Render();
            }

            (descriptors ??= new Dictionary<string, AutoPropertyBackingField>(StringComparer.Ordinal))
                [$"<{propertyName}{GeneratedNameGrammar.BackingFieldSuffix}"]
                    = new AutoPropertyBackingField(
                        propertyName,
                        propertyType,
                        isStatic);
        }

        return descriptors;
    }

    /// <summary>
    /// True when a property is an auto-property, i.e. either accessor is <c>[CompilerGenerated]</c>;
    /// <paramref name="isStatic"/> reports that accessor's staticness, which the backing field's own
    /// storage must share.
    /// </summary>
    static bool TryGetAutoPropertyAccessorStaticness(
        MetadataReader reader,
        PropertyAccessors accessors,
        out bool isStatic,
        Action<int>? beforeDecodeWork = null)
    {
        if (!accessors.Getter.IsNil)
        {
            var getter = reader.GetMethodDefinition(accessors.Getter);
            if (AttributeReader.HasAttribute(
                    reader,
                    getter.GetCustomAttributes(),
                    KnownAttributeNames.CompilerGeneratedAttribute,
                    beforeDecodeWork))
            {
                isStatic = (getter.Attributes & MethodAttributes.Static) != 0;
                return true;
            }
        }

        if (!accessors.Setter.IsNil)
        {
            var setter = reader.GetMethodDefinition(accessors.Setter);
            if (AttributeReader.HasAttribute(
                    reader,
                    setter.GetCustomAttributes(),
                    KnownAttributeNames.CompilerGeneratedAttribute,
                    beforeDecodeWork))
            {
                isStatic = (setter.Attributes & MethodAttributes.Static) != 0;
                return true;
            }
        }

        isStatic = false;
        return false;
    }

    /// <summary>
    /// True when a field is a genuine auto-property backing field that reconstruction will
    /// re-synthesize from auto-property syntax: it is <c>[CompilerGenerated]</c>, its name matches a
    /// declared auto-property's backing-field name, and its staticness and type agree with that
    /// property. Requiring type and staticness agreement (not the mangled name shape alone) mirrors
    /// the compile-back planner's historical discriminator, so a same-named but type/static-mismatched
    /// or non-auto-property compiler-generated field is preserved (on reconstruction no auto-property
    /// re-creates it, so the raw field must stay declared).
    /// </summary>
    static bool IsAutoPropertyBackingField(
        MetadataReader reader,
        FieldDefinition field,
        string fieldName,
        Dictionary<string, AutoPropertyBackingField>? autoPropertyBackingFields,
        GenericContext context,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null)
    {
        if (autoPropertyBackingFields is null
            || !autoPropertyBackingFields.TryGetValue(fieldName, out var descriptor))
        {
            return false;
        }

        if (!AttributeReader.HasAttribute(
                reader,
                field.GetCustomAttributes(),
                KnownAttributeNames.CompilerGeneratedAttribute,
                beforeDecodeWork))
            return false;

        if (((field.Attributes & FieldAttributes.Static) != 0) != descriptor.IsStatic)
            return false;

        if (beforeDecodeWork is null)
        {
            return GuardedSignatureText.FieldText(reader, field, context)
                    .TryGetValue(out var fieldType)
                && fieldType == descriptor.PropertyType;
        }

        TypeNode node = GuardedProviderDecode.Field(
            reader,
            field,
            new TypeNodeProvider(beforeRetainText, beforeDecodeWork),
            context,
            new DegradedTypeNode());
        return !node.IsDegraded && node.Render() == descriptor.PropertyType;
    }

    /// <summary>
    /// Whether a field name belongs to a type's declarable field surface based on its name alone.
    /// Compiler-generated (<c>&lt;...&gt;</c>) fields are excluded unless
    /// <paramref name="includeCompilerGenerated"/> is set; ordinary fields are surfaced. Backing
    /// fields (auto-property, field-like event) and an enum's <c>value__</c> slot carry additional
    /// positive-evidence checks applied by callers.
    /// </summary>
    static bool IsSurfaceableFieldName(string name, bool includeCompilerGenerated)
    {
        if (name.StartsWith('<'))
            return includeCompilerGenerated;
        return true;
    }

    /// <summary>
    /// The field handles that make up a type's declarable field surface: ordinary fields,
    /// excluding synthesized auto-property backing fields (positive <c>[CompilerGenerated]</c>
    /// evidence), an enum's storage slot (<c>value__</c>), and a field-like event's
    /// compiler-generated backing field. Compiler-generated fields (e.g. state-machine hoisted
    /// locals, display-class captures) are included only when
    /// <paramref name="includeCompilerGenerated"/> is set; non-public fields only when
    /// <paramref name="includeAll"/> is set. This is the single field-inclusion decision shared by
    /// API-surface extraction and compile-back reconstruction so both agree on which fields a type
    /// really has.
    /// </summary>
    public static List<FieldDefinitionHandle> SurfaceFieldHandles(
        MetadataReader reader,
        TypeDefinition typeDef,
        bool includeAll,
        bool includeCompilerGenerated)
    {
        bool isEnum = IsEnum(reader, typeDef);
        var context = GenericContext.ForType(reader, typeDef);
        var fieldLikeEventBackingFieldNames = FieldLikeEventBackingFieldNames(reader, typeDef);
        var autoPropertyBackingFields = AutoPropertyBackingFieldDescriptors(reader, typeDef, context);
        var handles = new List<FieldDefinitionHandle>();
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public && !includeAll)
                continue;

            string fieldName = reader.GetString(field.Name);
            if (isEnum && fieldName == "value__")
                continue; // An enum's storage slot is not a declarable field member
            if (!IsSurfaceableFieldName(fieldName, includeCompilerGenerated))
                continue;
            if (IsAutoPropertyBackingField(reader, field, fieldName, autoPropertyBackingFields, context))
                continue; // Skip a synthesized auto-property backing field (re-synthesized on reconstruction)
            if (IsFieldLikeEventBackingField(reader, field, fieldName, fieldLikeEventBackingFieldNames))
                continue;

            handles.Add(fieldHandle);
        }

        return handles;
    }

    /// <summary>
    /// Populates DerivedTypes for a specific type by scanning all types in the surface.
    /// </summary>
    public static void PopulateDerivedTypes(ApiSurface surface, ApiType targetType)
    {
        var fullName = string.IsNullOrEmpty(targetType.Namespace)
            ? targetType.Name
            : $"{targetType.Namespace}.{targetType.Name}";

        List<string> derivedTypes = [];

        foreach (var type in surface.Types)
        {
            if (type == targetType)
                continue;

            // Check if this type's base is our target
            if (type.BaseType == fullName)
            {
                var derivedFullName = string.IsNullOrEmpty(type.Namespace)
                    ? type.Name
                    : $"{type.Namespace}.{type.Name}";
                derivedTypes.Add(derivedFullName);
            }

            // Check if this type implements our target (if target is an interface)
            if (targetType.Kind == "interface" && type.Interfaces != null)
            {
                if (type.Interfaces.Contains(fullName))
                {
                    var derivedFullName = string.IsNullOrEmpty(type.Namespace)
                        ? type.Name
                        : $"{type.Namespace}.{type.Name}";
                    if (!derivedTypes.Contains(derivedFullName))
                        derivedTypes.Add(derivedFullName);
                }
            }
        }

        if (derivedTypes.Count > 0)
        {
            derivedTypes.Sort(StringComparer.Ordinal);
            targetType.DerivedTypes = derivedTypes;
        }
    }

    private static (string Text, ApiSignature Model, bool IsDegraded) GetMethodSignature(
        MetadataReader reader,
        GenericContext typeContext,
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        byte typeNullableContext,
        bool captureExtensionReceiver = false,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null,
        TypeParameterConstraintResolution? constraintResolution = null,
        Action<int>? beforeAttributeMaterialize = null)
    {
        Action<int>? attributeMaterialize =
            beforeAttributeMaterialize ?? beforeDecodeWork;
        string name = DecodeString(
            reader,
            method.Name,
            beforeDecodeWork);
        var context = GenericContext.ForMethod(
            reader,
            typeContext,
            method,
            beforeDecodeWork);
        var typeNodeProvider = beforeRetainText is null
            ? TypeNodeProvider.Instance
            : new TypeNodeProvider(beforeRetainText, beforeDecodeWork);
        var treeSignature = GuardedProviderDecode.Method(
            reader,
            method,
            typeNodeProvider,
            context,
            (TypeNode)new DegradedTypeNode());

        // Determine the effective nullable default: method overrides type
        byte nullableDefault =
            NullabilityReader.GetNullableContext(
                reader,
                method.GetCustomAttributes(),
                beforeDecodeWork)
            ?? typeNullableContext;

        // Apply nullability to return type
        var paramHandles = method.GetParameters();
        var returnBytes = NullabilityReader.GetParameterNullableBytes(
            reader,
            paramHandles,
            0,
            beforeDecodeWork);
        int pos = 0;
        treeSignature.ReturnType.ApplyNullability(returnBytes, ref pos, nullableDefault);
        var returnDynamicFlags = DynamicReader.GetParameterDynamicFlags(
            reader,
            paramHandles,
            0,
            beforeDecodeWork);
        pos = 0;
        treeSignature.ReturnType.ApplyDynamic(returnDynamicFlags, ref pos);
        treeSignature.ReturnType.ApplyTupleNames(
            TupleElementNamesReader.GetParameterTupleElementNames(
                reader,
                paramHandles,
                0,
                beforeDecodeWork));

        // Build parameter list with nullability
        var paramTypes = treeSignature.ParameterTypes;
        string? extensionReceiverType = null;
        if (captureExtensionReceiver && paramTypes.Length > 0)
        {
            beforeDecodeWork?.Invoke(
                (int)Math.Min(
                    paramTypes[0].EstimatedRenderedLength,
                    int.MaxValue));
            extensionReceiverType = paramTypes[0].RenderCanonical();
            beforeRetainText?.Invoke(extensionReceiverType);
        }

        List<string> parameters = [];
        List<ApiParameter> parameterModels = [];
        for (int i = 0; i < paramTypes.Length; i++)
        {
            // Apply nullability to this parameter's type tree
            var paramBytes = NullabilityReader.GetParameterNullableBytes(
                reader,
                paramHandles,
                i + 1,
                beforeDecodeWork);
            pos = 0;
            paramTypes[i].ApplyNullability(paramBytes, ref pos, nullableDefault);
            var paramDynamicFlags = DynamicReader.GetParameterDynamicFlags(
                reader,
                paramHandles,
                i + 1,
                beforeDecodeWork);
            pos = 0;
            paramTypes[i].ApplyDynamic(paramDynamicFlags, ref pos);
            paramTypes[i].ApplyTupleNames(
                TupleElementNamesReader.GetParameterTupleElementNames(
                    reader,
                    paramHandles,
                    i + 1,
                    beforeDecodeWork));
            string type = paramTypes[i].Render();
            string canonicalType = paramTypes[i].RenderCanonical();

            // Parameter handles may include return parameter at SequenceNumber 0
            // Actual parameters have SequenceNumber 1, 2, 3...
            var (paramName, isParams, refKind, hasDefault, defaultValue, attributes) =
                GetParameterInfo(
                    reader,
                    paramHandles,
                    i + 1,
                    beforeRetainText,
                    attributeMaterialize);
            paramName ??= $"arg{i}";

            var isByRef = type.StartsWith("ref ", StringComparison.Ordinal);
            if (isByRef)
            {
                type = type["ref ".Length..];
                canonicalType = canonicalType["ref ".Length..];
                refKind ??= "ref";
            }
            else
            {
                refKind = null;
            }

            var modifier = isParams ? "params" : refKind;
            bool acceptsNullDefault = AcceptsNullDefault(paramTypes[i]);
            string? defaultValueText = DefaultValueText(
                reader,
                defaultValue,
                type,
                hasDefault,
                acceptsNullDefault,
                beforeDecodeWork);
            var paramStr = FormatParameter(
                type,
                paramName,
                modifier,
                hasDefault,
                defaultValue,
                defaultValueText);

            beforeRetainText?.Invoke(paramStr);
            var parameterModel = new ApiParameter
            {
                Attributes = attributes,
                Name = paramName,
                Type = type,
                CanonicalType = canonicalType,
                StructuralType = paramTypes[i].HasStructuralPayload
                    ? paramTypes[i].StructuralIdentity()
                    : null,
                TypeReferences =
                    [.. paramTypes[i].ReferencedTypes().Distinct()],
                Modifier = modifier,
                HasDefault = hasDefault,
                DefaultValueText = defaultValueText
            };
            ObserveText(parameterModel, beforeRetainText);
            parameters.Add(paramStr);
            parameterModels.Add(parameterModel);
        }

        string paramStr2 = string.Join(", ", parameters);
        var returnType = FormatMethodReturnType(
            reader,
            treeSignature.ReturnType,
            paramHandles,
            beforeDecodeWork);
        var canonicalReturnType = FormatCanonicalMethodReturnType(
            reader,
            treeSignature.ReturnType,
            paramHandles,
            beforeDecodeWork);
        var returnAttributes = ReturnParameterAttributes(
            reader,
            paramHandles,
            beforeRetainText,
            attributeMaterialize);
        var methodTypeParameters = GenericParameters(
            reader,
            method.GetGenericParameters(),
            context,
            nullableDefault,
            includeVariance: false,
            methodHandle,
            beforeRetainText,
            beforeDecodeWork,
            constraintResolution);
        var methodName = context.MethodParameters.Count > 0
            ? $"{name}<{string.Join(", ", methodTypeParameters.Select(parameter => parameter.Name))}>"
            : name;
        // MemberName carries identity (ApiMemberIdentity parses it for docids and
        // the generic-parameter map), so it keeps the raw metadata spelling; only
        // the rendered signature is sanitized (issue #3319).
        var displayName = context.MethodParameters.Count > 0
            ? $"{SanitizeMemberDisplayName(name)}<{string.Join(", ", methodTypeParameters.Select(parameter => SanitizeIdentifier(parameter.Name)))}>"
            : SanitizeMemberDisplayName(name);
        return ($"{returnType} {displayName}({paramStr2})", new ApiSignature
        {
            ExtensionReceiverType = extensionReceiverType,
            ReturnType = returnType,
            CanonicalReturnType = canonicalReturnType,
            StructuralReturnType = treeSignature.ReturnType.HasStructuralPayload
                ? treeSignature.ReturnType.StructuralIdentity()
                : null,
            ReturnTypeReferences =
                [.. treeSignature.ReturnType.ReferencedTypes().Distinct()],
            ReturnTypeDefinitionReference =
                treeSignature.ReturnType.DefinitionReference(),
            ReturnTypeShape =
                ApiTypeShapeFactory.FromTypeNode(
                    treeSignature.ReturnType),
            ReturnAttributes = returnAttributes,
            MemberName = methodName,
            TypeParameters = methodTypeParameters,
            Parameters = parameterModels
        }, treeSignature.ReturnType.IsDegraded
            || treeSignature.ParameterTypes.Any(parameter => parameter.IsDegraded));
    }

    private static List<string> ReturnParameterAttributes(
        MetadataReader reader,
        ParameterHandleCollection handles,
        Action<string>? beforeRetain = null,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var handle in handles)
        {
            if (reader.GetParameter(handle).SequenceNumber == 0)
                return AttributeReader.RenderParameterAttributes(
                    reader,
                    handle,
                    beforeRetain: beforeRetain,
                    beforeMaterialize: beforeMaterialize);
        }

        return [];
    }

    private static List<string> RenderMemberAttributes(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<string>? beforeRetain = null,
        Action<int>? beforeMaterialize = null)
        => AttributeReader.RenderAttributes(
            reader,
            attributes,
            skipAttribute: static name => name == "System.ObsoleteAttribute",
            qualifyNames: true,
            beforeRetain: beforeRetain,
            beforeMaterialize: beforeMaterialize);

    private static string FormatMethodReturnType(
        MetadataReader reader,
        TypeNode returnType,
        ParameterHandleCollection paramHandles,
        Action<int>? beforeMaterialize = null)
    {
        var rendered = returnType.Render();
        if (!rendered.StartsWith("ref ", StringComparison.Ordinal)
            || !IsReadOnlyByRefReturn(
                reader,
                returnType,
                paramHandles,
                beforeMaterialize))
        {
            return rendered;
        }

        return $"ref readonly {rendered["ref ".Length..]}";
    }

    /// <summary>
    /// Canonical (tuple-erased) counterpart to <see cref="FormatMethodReturnType"/>. Mirrors
    /// its <c>ref readonly</c> synthesis so the canonical return spelling preserves by-ref
    /// return modifiers used by member identity, differing from the display spelling only in
    /// tuple rendering.
    /// </summary>
    private static string FormatCanonicalMethodReturnType(
        MetadataReader reader,
        TypeNode returnType,
        ParameterHandleCollection paramHandles,
        Action<int>? beforeMaterialize = null)
    {
        var rendered = returnType.RenderCanonical();
        if (!rendered.StartsWith("ref ", StringComparison.Ordinal)
            || !IsReadOnlyByRefReturn(
                reader,
                returnType,
                paramHandles,
                beforeMaterialize))
        {
            return rendered;
        }

        return $"ref readonly {rendered["ref ".Length..]}";
    }

    private static bool IsReadOnlyByRefReturn(
        MetadataReader reader,
        TypeNode returnType,
        ParameterHandleCollection paramHandles,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var handle in paramHandles)
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber == 0
                && HasReadOnlyByRefAttribute(
                    reader,
                    parameter.GetCustomAttributes(),
                    beforeMaterialize))
                return true;
        }

        return returnType.HasRequiredModifier("System.Runtime.CompilerServices", "IsReadOnlyAttribute")
            || returnType.HasRequiredModifier("System.Runtime.CompilerServices", "RequiresLocationAttribute")
            || returnType.HasRequiredModifier("System.Runtime.InteropServices", "InAttribute");
    }

    private static bool HasReadOnlyByRefAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
        => AttributeReader.HasAttribute(
                reader,
                attributes,
                KnownAttributeNames.IsReadOnlyAttribute,
                beforeMaterialize)
            || AttributeReader.HasAttribute(
                reader,
                attributes,
                "System.Runtime.CompilerServices.RequiresLocationAttribute",
                beforeMaterialize);

    private static (string? name, bool isParams, string? refKind, bool hasDefault, object? defaultValue, List<string> attributes) GetParameterInfo(
        MetadataReader reader,
        ParameterHandleCollection handles,
        int sequenceNumber,
        Action<string>? beforeRetain = null,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var handle in handles)
        {
            var param = reader.GetParameter(handle);
            if (param.SequenceNumber == sequenceNumber)
            {
                string name = DecodeString(
                    reader,
                    param.Name,
                    beforeMaterialize);
                var attributes = param.GetCustomAttributes();
                bool isParams = AttributeReader.HasAttribute(
                        reader,
                        attributes,
                        "System.ParamArrayAttribute",
                        beforeMaterialize)
                    || AttributeReader.HasAttribute(
                        reader,
                        attributes,
                        KnownAttributeNames.ParamCollectionAttribute,
                        beforeMaterialize);
                var renderedAttributes = AttributeReader.RenderParameterAttributes(
                    reader,
                    handle,
                    beforeRetain: beforeRetain,
                    beforeMaterialize: beforeMaterialize);
                // An interop-marshalled `ref` parameter sets both In and Out, so
                // neither flag alone identifies a C# `out`/`in`. Spelling such a
                // parameter `out` breaks definite assignment in the body.
                bool isOut = (param.Attributes & System.Reflection.ParameterAttributes.Out) != 0;
                bool isIn = (param.Attributes & System.Reflection.ParameterAttributes.In) != 0;
                string? refKind = isOut && !isIn
                    ? "out"
                    : isIn && !isOut
                        ? "in"
                        : null;

                bool hasDefault = (param.Attributes & System.Reflection.ParameterAttributes.HasDefault) != 0;
                object? defaultValue = null;

                if (TryReadAttributedParameterDefault(
                    reader,
                    attributes,
                    out var attributedDefault,
                    beforeMaterialize))
                {
                    hasDefault = true;
                    defaultValue = attributedDefault;
                }
                else if (hasDefault)
                {
                    var constantHandle = param.GetDefaultValue();
                    if (!constantHandle.IsNil)
                    {
                        var constant = reader.GetConstant(constantHandle);
                        beforeMaterialize?.Invoke(
                            reader.GetBlobReader(constant.Value).Length);
                        defaultValue = ReadConstantValue(reader, constant);
                    }
                }

                return (name, isParams, refKind, hasDefault, defaultValue, renderedAttributes);
            }
        }

        return (null, false, null, false, null, []);
    }

    private sealed record DateTimeConstantDefault(long Ticks);

    private static bool TryReadAttributedParameterDefault(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out object? defaultValue,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            var attributeTypeName = AttributeReader.GetAttributeTypeName(
                reader,
                attribute.Constructor,
                beforeMaterialize);
            if (attributeTypeName == KnownAttributeNames.DecimalConstantAttribute)
            {
                ObserveAttributeValue(reader, attribute, beforeMaterialize);
                if (TryReadDecimalConstantAttribute(
                    reader,
                    attribute,
                    out var decimalValue,
                    beforeMaterialize))
                {
                    defaultValue = decimalValue;
                    return true;
                }
            }

            if (attributeTypeName == KnownAttributeNames.DateTimeConstantAttribute)
            {
                ObserveAttributeValue(reader, attribute, beforeMaterialize);
                if (TryReadDateTimeConstantAttribute(
                    reader,
                    attribute,
                    out var ticks,
                    beforeMaterialize))
                {
                    defaultValue = new DateTimeConstantDefault(ticks);
                    return true;
                }
            }
        }

        defaultValue = null;
        return false;
    }

    static void ObserveAttributeValue(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize)
        => beforeMaterialize?.Invoke(
            reader.GetBlobReader(attribute.Value).Length);

    private static bool TryReadDecimalConstantAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out decimal value,
        Action<int>? beforeMaterialize = null)
    {
        if (AttributeDecoder.TryDecode(reader, attribute, beforeMaterialize) is not { } decoded
            || decoded.FixedArguments.Length != 5
            || decoded.FixedArguments[0].Value is not byte scale
            || decoded.FixedArguments[1].Value is not byte sign
            || !TryGetUInt32(decoded.FixedArguments[2].Value, out var hi)
            || !TryGetUInt32(decoded.FixedArguments[3].Value, out var mid)
            || !TryGetUInt32(decoded.FixedArguments[4].Value, out var low)
            || scale > 28
            || sign > 1)
        {
            value = default;
            return false;
        }

        value = new decimal(
            unchecked((int)low),
            unchecked((int)mid),
            unchecked((int)hi),
            sign != 0,
            scale);
        return true;
    }

    private static bool TryGetUInt32(object? value, out uint result)
    {
        switch (value)
        {
            case uint unsigned:
                result = unsigned;
                return true;
            case int signed:
                result = unchecked((uint)signed);
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryReadDateTimeConstantAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out long ticks,
        Action<int>? beforeMaterialize = null)
    {
        if (AttributeDecoder.TryDecode(reader, attribute, beforeMaterialize) is { FixedArguments.Length: 1 } decoded
            && decoded.FixedArguments[0].Value is long value)
        {
            ticks = value;
            return true;
        }

        ticks = 0;
        return false;
    }

    private static object? ReadConstantValue(MetadataReader reader, Constant constant)
    {
        var blob = reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => blob.ReadBoolean(),
            ConstantTypeCode.Char => blob.ReadChar(),
            ConstantTypeCode.SByte => blob.ReadSByte(),
            ConstantTypeCode.Byte => blob.ReadByte(),
            ConstantTypeCode.Int16 => blob.ReadInt16(),
            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
            ConstantTypeCode.Int32 => blob.ReadInt32(),
            ConstantTypeCode.UInt32 => blob.ReadUInt32(),
            ConstantTypeCode.Int64 => blob.ReadInt64(),
            ConstantTypeCode.UInt64 => blob.ReadUInt64(),
            ConstantTypeCode.Single => blob.ReadSingle(),
            ConstantTypeCode.Double => blob.ReadDouble(),
            ConstantTypeCode.String => blob.ReadUTF16(blob.Length),
            ConstantTypeCode.NullReference => null,
            _ => null
        };
    }

    // `null` is a legal default only for a reference type or a Nullable<T> (a
    // value type that nonetheless accepts the `null` literal). A non-nullable
    // value type must spell its null constant `default`.
    private static bool AcceptsNullDefault(TypeNode node)
        => node.IsReferenceType
            || node.Render().StartsWith("System.Nullable<", StringComparison.Ordinal);

    private static string FormatDefaultValue(
        MetadataReader reader,
        object? value,
        string typeName,
        bool acceptsNullDefault,
        Action<int>? beforeDecodeWork = null)
    {
        // A null constant is `default(T)` for a non-nullable value-type parameter
        // (the only legal spelling — `T x = null` is CS1750), and a genuine `null`
        // for reference types and Nullable<T> (both accept `null` as a literal
        // default). value-vs-reference comes from the signature's element type
        // (ELEMENT_TYPE_VALUETYPE), already on the decoded type node.
        if (value == null)
            return acceptsNullDefault ? "null" : "default";

        if (TryFormatEnumDefaultValue(
                reader,
                value,
                typeName,
                beforeDecodeWork) is { } enumValue)
            return enumValue;

        if (!acceptsNullDefault
            && IsLikelyEnumDefaultType(typeName)
            && TryConvertEnumConstant(value, out var defaultValue))
        {
            return $"({typeName}){defaultValue.ToString(CultureInfo.InvariantCulture)}";
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            decimal d => FormatDecimalLiteral(d),
            string s => StringLiteral(s),
            char c => $"'{EscapeCharLiteral(c)}'",
            float f => f.ToString("G") + "f",
            double d => d.ToString("G"),
            _ => value.ToString() ?? "default"
        };
    }

    private static string? DefaultValueText(
        MetadataReader reader,
        object? value,
        string typeName,
        bool hasDefault,
        bool acceptsNullDefault,
        Action<int>? beforeDecodeWork = null)
    {
        if (!hasDefault || value is DateTimeConstantDefault)
            return null;
        return FormatDefaultValue(
            reader,
            value,
            typeName,
            acceptsNullDefault,
            beforeDecodeWork);
    }

    private static string EscapeCharLiteral(char c) => c switch
    {
        '\\' => "\\\\",
        '\'' => "\\'",
        '\0' => "\\0",
        '\a' => "\\a",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\v' => "\\v",
        // Bidi overrides are category Cf, so char.IsControl is false for them and
        // they would reach the terminal raw (issue #3319). No end-to-end gate
        // covers this particular escaper — every probe reached the sibling
        // escaper below instead — so treat it as unverified hardening that keeps
        // the two spellings consistent, not as a proven-reachable fix.
        _ when CSharpIdentifierCore.RequiresLiteralEscape(c) => $"\\u{(int)c:x4}",
        _ => c.ToString()
    };

    private static string FormatParameter(
        string type,
        string name,
        string? modifier,
        bool hasDefault,
        object? defaultValue,
        string? defaultValueText)
    {
        var escapedName = SanitizeIdentifier(name);
        var parameter = modifier is null ? $"{type} {escapedName}" : $"{modifier} {type} {escapedName}";
        if (!hasDefault)
            return parameter;

        if (defaultValue is DateTimeConstantDefault dateTime)
        {
            var ticks = FormatInt64Literal(dateTime.Ticks);
            return $"[{OptionalAttributeName}, {DateTimeConstantAttributeName}({ticks})] {parameter}";
        }

        return $"{parameter} = {defaultValueText}";
    }

    /// <summary>
    /// The spelling for a metadata name entering emitted C# declaration text.
    /// Keyword escaping alone leaves an unspellable name (one carrying a line
    /// terminator, say) intact, which lets it break out of the surrounding code
    /// fence or tree layout; sanitizing folds it to identifier characters
    /// instead (issue #3319). Byte-neutral for names that are already legal
    /// identifiers, which covers every well-formed assembly.
    /// </summary>
    /// <summary>
    /// The display spelling of a member name. A member name is not always a simple
    /// identifier — <c>.ctor</c>, and an explicit interface implementation spells
    /// <c>System.IConvertible.ToBoolean</c> — so this contains it rather than
    /// sanitizing it into one, which would mangle both.
    /// </summary>
    private static string SanitizeMemberDisplayName(string name)
        => CSharpIdentifierCore.ContainComposedName(name);

    private static string SanitizeIdentifier(string name)
        => CSharpIdentifierCore.ContainIdentifier(name, CSharpKeywords.RequiresDeclarationEscape);

    private static string FormatDecimalLiteral(decimal value)
        => value.ToString("G29", CultureInfo.InvariantCulture) + "m";

    private static string FormatInt64Literal(long value)
    {
        long minValue = long.MaxValue;
        minValue = -minValue - 1;
        return value == minValue
            ? "long.MinValue"
            : value.ToString(CultureInfo.InvariantCulture) + "L";
    }

    private static string StringLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when CSharpIdentifierCore.RequiresLiteralEscape(c) => $"\\u{(int)c:X4}",
                _ => c.ToString()
            });
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string? TryFormatEnumDefaultValue(
        MetadataReader reader,
        object value,
        string typeName,
        Action<int>? beforeDecodeWork = null)
    {
        if (!TryConvertEnumConstant(value, out var defaultValue))
            return null;

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            try
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                if (!IsEnum(reader, typeDef, beforeDecodeWork))
                    continue;

                if (MetadataTypeDefinitionNameReader.Read(
                        reader,
                        typeHandle,
                        beforeDecodeWork)
                    is not MetadataTypeDefinitionNameReadResult.Read resolvedEnumType)
                {
                    continue;
                }

                string enumTypeName = resolvedEnumType.Name.ToMetadataFullName();
                if (!string.Equals(typeName, enumTypeName, StringComparison.Ordinal))
                    continue;

                foreach (var fieldHandle in typeDef.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    if ((field.Attributes & FieldAttributes.Literal) == 0)
                        continue;
                    var constantHandle = field.GetDefaultValue();
                    if (constantHandle.IsNil)
                        continue;
                    var constant = reader.GetConstant(constantHandle);
                    if (TryReadEnumConstant(reader, constant, out var memberValue)
                        && memberValue == defaultValue)
                    {
                        return $"{typeName}.{DecodeString(reader, field.Name, beforeDecodeWork)}";
                    }
                }

                return $"({typeName}){defaultValue.ToString(CultureInfo.InvariantCulture)}";
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                continue;
            }
        }

        return null;
    }

    private static bool IsLikelyEnumDefaultType(string typeName)
        => typeName is not ("bool" or "char" or "sbyte" or "byte" or "short" or "ushort"
            or "int" or "uint" or "long" or "ulong" or "float" or "double" or "decimal"
            or "System.Boolean" or "System.Char" or "System.SByte" or "System.Byte"
            or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32"
            or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double"
            or "System.Decimal" or "System.DateTime");

    // Base types, interfaces, and events resolve to a display string via the
    // string-based TypeResolver, which has no DynamicAttribute context. Only a
    // generic instantiation (a TypeSpecification) can carry `dynamic`, so when
    // one does, re-decode it through the TypeNode tree and apply the flags. Every
    // other case (non-TypeSpec, or no DynamicAttribute) returns the string result
    // unchanged, so this never alters non-dynamic output.
    private static string ApplyDynamicView(
        MetadataReader reader,
        EntityHandle typeHandle,
        CustomAttributeHandleCollection attributes,
        GenericContext context,
        string fallback,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null)
    {
        if (typeHandle.Kind != HandleKind.TypeSpecification)
            return fallback;
        if (DynamicReader.GetDynamicFlags(
                reader,
                attributes,
                beforeDecodeWork) is not { } flags)
            return fallback;
        var node = GuardedProviderDecode.TypeSpec(
            reader,
            (TypeSpecificationHandle)typeHandle,
            beforeRetainText is null
                ? TypeNodeProvider.Instance
                : new TypeNodeProvider(beforeRetainText, beforeDecodeWork),
            context,
            (TypeNode)new DegradedTypeNode());
        // A rejected/degraded TypeSpec renders as a bare "object"/"dynamic", which would
        // obliterate the fully resolved string fallback. Keep failure visible: trust the
        // string resolver rather than silently collapsing the type.
        if (node.IsDegraded)
            return fallback;
        int position = 0;
        node.ApplyDynamic(flags, ref position);
        return node.Render();
    }

    private static string ResolveRequiredTypeName(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext? context = null,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null)
    {
        if (beforeDecodeWork is not null)
        {
            var provider =
                new TypeNodeProvider(beforeMaterialize: beforeDecodeWork);
            _ = handle.Kind switch
            {
                HandleKind.TypeDefinition => provider.GetTypeFromDefinition(
                    reader,
                    (TypeDefinitionHandle)handle,
                    rawTypeKind: 0),
                HandleKind.TypeReference => provider.GetTypeFromReference(
                    reader,
                    (TypeReferenceHandle)handle,
                    rawTypeKind: 0),
                HandleKind.TypeSpecification => GuardedProviderDecode.TypeSpec(
                    reader,
                    (TypeSpecificationHandle)handle,
                    provider,
                    context,
                    (TypeNode)new DegradedTypeNode()),
                _ => null,
            };
        }

        string resolved = TypeResolver.ResolveTypeName(reader, handle, context) switch
        {
            MetadataTypeNameResult.Resolved success => success.Value,
            MetadataTypeNameResult.Rejected rejected =>
                throw new MetadataRowRejectedException(
                    "type name",
                    rejected.Failure),
            MetadataTypeNameResult.Absent =>
                throw new MetadataRowRejectedException(
                    "type name",
                    MetadataTypeNameFailure.ForMechanism(
                        MetadataTypeNameFailureMechanism.Metadata,
                        handle,
                        "The metadata type name is absent.")),
            _ => throw new InvalidOperationException(
                "Unknown metadata type-name result."),
        };
        beforeRetainText?.Invoke(resolved);
        return resolved;
    }

    static ApiTypeReferenceIdentity? DecodeTypeDefinitionReference(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext context,
        Action<string>? beforeRetainText,
        Action<int>? beforeDecodeWork)
    {
        var provider = new TypeNodeProvider(
            beforeRetainText,
            beforeDecodeWork);
        TypeNode node = handle.Kind switch
        {
            HandleKind.TypeDefinition => provider.GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeReference => provider.GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeSpecification => GuardedProviderDecode.TypeSpec(
                reader,
                (TypeSpecificationHandle)handle,
                provider,
                context,
                (TypeNode)new DegradedTypeNode()),
            _ => new DegradedTypeNode(),
        };
        return node.IsDegraded
            ? null
            : node.DefinitionReference();
    }

    private static void AddInspectionFailure(
        ApiSurface surface,
        ExtractionBudget? budget,
        string operation,
        EntityHandle subject,
        MetadataTypeNameFailure failure,
        AssemblyReferenceIdentity? subjectAssembly = null,
        TypeDefinitionHandle owningType = default,
        MetadataTypeDefinitionName? owningTypeDefinition = null)
    {
        var retained = new ApiSurfaceInspectionFailure(
            operation,
            failure.SubjectToken ?? MetadataTokens.GetToken(subject),
            failure.Mechanism,
            failure.Kind,
            failure.Detail,
            subjectAssembly)
        {
            OwningTypeToken = owningType.IsNil
                ? null
                : MetadataTokens.GetToken(owningType),
            OwningTypeDefinition = owningTypeDefinition,
        };
        budget?.RetainInspectionFailure(retained);
        surface.InspectionFailures.Add(retained);
    }

    private static bool IsEnum(
        MetadataReader reader,
        TypeDefinition typeDef,
        Action<int>? beforeDecodeWork = null)
        => !typeDef.BaseType.IsNil
            && TypeResolver.GetTypeName(
                reader,
                typeDef.BaseType,
                context: null,
                beforeMaterialize: beforeDecodeWork) == "System.Enum";

    private sealed class MetadataRowRejectedException
        : InvalidOperationException
    {
        public MetadataRowRejectedException(
            string operation,
            MetadataTypeNameFailure failure)
            : base(
                $"Metadata row rejected during {operation} "
                + $"({failure.Mechanism}/{failure.Kind}): {failure.Detail}")
        {
            Operation = operation;
            Failure = failure;
        }

        public string Operation { get; }
        public MetadataTypeNameFailure Failure { get; }
    }

    private static bool TryReadEnumConstant(MetadataReader reader, Constant constant, out decimal value)
    {
        var blob = reader.GetBlobReader(constant.Value);
        switch (constant.TypeCode)
        {
            case ConstantTypeCode.SByte:
                return TryConvertEnumConstant(blob.ReadSByte(), out value);
            case ConstantTypeCode.Byte:
                return TryConvertEnumConstant(blob.ReadByte(), out value);
            case ConstantTypeCode.Int16:
                return TryConvertEnumConstant(blob.ReadInt16(), out value);
            case ConstantTypeCode.UInt16:
                return TryConvertEnumConstant(blob.ReadUInt16(), out value);
            case ConstantTypeCode.Int32:
                return TryConvertEnumConstant(blob.ReadInt32(), out value);
            case ConstantTypeCode.UInt32:
                return TryConvertEnumConstant(blob.ReadUInt32(), out value);
            case ConstantTypeCode.Int64:
                return TryConvertEnumConstant(blob.ReadInt64(), out value);
            case ConstantTypeCode.UInt64:
                return TryConvertEnumConstant(blob.ReadUInt64(), out value);
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryConvertEnumConstant(object value, out decimal converted)
    {
        switch (value)
        {
            case sbyte v:
                converted = v;
                return true;
            case byte v:
                converted = v;
                return true;
            case short v:
                converted = v;
                return true;
            case ushort v:
                converted = v;
                return true;
            case int v:
                converted = v;
                return true;
            case uint v:
                converted = v;
                return true;
            case long v:
                converted = v;
                return true;
            case ulong v:
                converted = v;
                return true;
            default:
                converted = 0;
                return false;
        }
    }

    private static (string Text, ApiSignature Model, bool IsDegraded) GetPropertySignature(
        MetadataReader reader,
        GenericContext context,
        PropertyDefinition prop,
        MethodDefinitionHandle getterHandle,
        MethodDefinitionHandle setterHandle,
        byte typeNullableContext,
        bool includeAll = false,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null,
        Action<int>? beforeAttributeMaterialize = null)
    {
        Action<int>? attributeMaterialize =
            beforeAttributeMaterialize ?? beforeDecodeWork;
        string name = DecodeString(
            reader,
            prop.Name,
            beforeDecodeWork);
        var typeNodeProvider = beforeRetainText is null
            ? TypeNodeProvider.Instance
            : new TypeNodeProvider(beforeRetainText, beforeDecodeWork);
        var treeSignature = GuardedProviderDecode.Property(
            reader,
            prop,
            typeNodeProvider,
            context,
            (TypeNode)new DegradedTypeNode());

        // Apply nullability to the property type
        var propBytes = NullabilityReader.GetNullableBytes(
            reader,
            prop.GetCustomAttributes(),
            beforeDecodeWork);
        int pos = 0;
        treeSignature.ReturnType.ApplyNullability(propBytes, ref pos, typeNullableContext);
        var propDynamicFlags = DynamicReader.GetDynamicFlags(
            reader,
            prop.GetCustomAttributes(),
            beforeDecodeWork);
        pos = 0;
        treeSignature.ReturnType.ApplyDynamic(propDynamicFlags, ref pos);
        treeSignature.ReturnType.ApplyTupleNames(
            TupleElementNamesReader.GetTupleElementNames(
                reader,
                prop.GetCustomAttributes(),
                beforeDecodeWork));

        // Determine accessor visibility
        MethodAttributes getterAccess = 0;
        MethodAttributes setterAccess = 0;
        bool hasGetter = !getterHandle.IsNil;
        bool hasSetter = !setterHandle.IsNil;

        if (hasGetter)
        {
            var getter = reader.GetMethodDefinition(getterHandle);
            getterAccess = getter.Attributes & MethodAttributes.MemberAccessMask;
        }

        if (hasSetter)
        {
            var setter = reader.GetMethodDefinition(setterHandle);
            setterAccess = setter.Attributes & MethodAttributes.MemberAccessMask;
        }

        bool hasPublicGetter = hasGetter && getterAccess == MethodAttributes.Public;
        bool hasPublicSetter = hasSetter && setterAccess == MethodAttributes.Public;

        // Build accessor string
        string accessorStr;
        var accessorModels = new List<ApiAccessor>();
        if (includeAll)
        {
            // Show explicit access levels for non-public accessors
            var getStr = hasGetter ? FormatAccessor("get", getterAccess, Math.Max((int)getterAccess, (int)setterAccess)) : null;
            var setStr = hasSetter ? FormatAccessor("set", setterAccess, Math.Max((int)getterAccess, (int)setterAccess)) : null;
            if (hasGetter)
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "get",
                    Accessibility = AccessorAccessibility(getterAccess, Math.Max((int)getterAccess, (int)setterAccess)),
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(getterHandle).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            if (hasSetter)
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    Accessibility = AccessorAccessibility(setterAccess, Math.Max((int)getterAccess, (int)setterAccess)),
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(setterHandle).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            accessorStr = (getStr, setStr) switch
            {
                (not null, not null) => $"{{ {getStr}; {setStr}; }}",
                (not null, null) => $"{{ {getStr}; }}",
                (null, not null) => $"{{ {setStr}; }}",
                _ => "{ get; }"
            };
        }
        else
        {
            if (hasPublicGetter && hasPublicSetter)
            {
                accessorStr = "{ get; set; }";
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "get",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(getterHandle).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(setterHandle).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            }
            else if (hasPublicGetter && hasSetter)
            {
                accessorStr = "{ get; private set; }";
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "get",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(getterHandle).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    Accessibility = "private",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(setterHandle).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            }
            else if (hasPublicGetter)
            {
                accessorStr = "{ get; }";
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "get",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(getterHandle).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            }
            else if (hasPublicSetter)
            {
                accessorStr = "{ set; }";
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(setterHandle).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            }
            else
            {
                accessorStr = "{ get; }"; // Fallback
                accessorModels.Add(new ApiAccessor { Kind = "get" });
            }
        }

        ApplyAccessorStructuralReturns(
            accessorModels,
            reader,
            kind => kind switch
            {
                "get" => getterHandle,
                "set" => setterHandle,
                _ => default,
            },
            typeNodeProvider,
            context,
            beforeRetainText,
            beforeDecodeWork);

        var requiredPrefix = AttributeReader.HasRequiredMemberAttribute(
                reader,
                prop.GetCustomAttributes(),
                beforeDecodeWork)
            ? "required "
            : "";
        var isRequired = requiredPrefix.Length > 0;

        MethodDefinitionHandle parameterAccessor = hasGetter
            ? getterHandle
            : setterHandle;
        var parameterAccessorMethod = parameterAccessor.IsNil
            ? default
            : reader.GetMethodDefinition(parameterAccessor);
        var paramHandles = parameterAccessor.IsNil
            ? default
            : parameterAccessorMethod.GetParameters();
        byte parameterNullableContext = parameterAccessor.IsNil
            ? typeNullableContext
            : NullabilityReader.GetNullableContext(
                    reader,
                    parameterAccessorMethod.GetCustomAttributes(),
                    beforeDecodeWork)
                ?? typeNullableContext;
        var paramTypes = treeSignature.ParameterTypes;
        List<string> indexerParameters = [];
        List<ApiParameter> parameterModels = [];
        for (var i = 0; i < paramTypes.Length; i++)
        {
            var paramBytes = NullabilityReader.GetParameterNullableBytes(
                reader,
                paramHandles,
                i + 1,
                beforeDecodeWork);
            pos = 0;
            paramTypes[i].ApplyNullability(paramBytes, ref pos, parameterNullableContext);
            var paramDynamicFlags = DynamicReader.GetParameterDynamicFlags(
                reader,
                paramHandles,
                i + 1,
                beforeDecodeWork);
            pos = 0;
            paramTypes[i].ApplyDynamic(paramDynamicFlags, ref pos);
            paramTypes[i].ApplyTupleNames(
                TupleElementNamesReader.GetParameterTupleElementNames(
                    reader,
                    paramHandles,
                    i + 1,
                    beforeDecodeWork));
            var paramType = paramTypes[i].Render();
            var canonicalParamType = paramTypes[i].RenderCanonical();
            var (paramName, isParams, refKind, hasDefault, defaultValue, attributes) =
                GetParameterInfo(
                    reader,
                    paramHandles,
                    i + 1,
                    beforeRetainText,
                    attributeMaterialize);
            paramName ??= $"arg{i}";

            var isByRef = paramType.StartsWith("ref ", StringComparison.Ordinal);
            if (isByRef)
            {
                paramType = paramType["ref ".Length..];
                canonicalParamType = canonicalParamType["ref ".Length..];
                refKind ??= "ref";
            }
            else
            {
                refKind = null;
            }

            var modifier = isParams ? "params" : refKind;
            bool acceptsNullDefault = AcceptsNullDefault(paramTypes[i]);
            string? defaultValueText = DefaultValueText(
                reader,
                defaultValue,
                paramType,
                hasDefault,
                acceptsNullDefault,
                beforeDecodeWork);
            var parameter = FormatParameter(
                paramType,
                paramName,
                modifier,
                hasDefault,
                defaultValue,
                defaultValueText);
            beforeRetainText?.Invoke(parameter);
            var parameterModel = new ApiParameter
            {
                Attributes = attributes,
                Name = paramName,
                Type = paramType,
                CanonicalType = canonicalParamType,
                StructuralType = paramTypes[i].HasStructuralPayload
                    ? paramTypes[i].StructuralIdentity()
                    : null,
                TypeReferences =
                    [.. paramTypes[i].ReferencedTypes().Distinct()],
                Modifier = modifier,
                HasDefault = hasDefault,
                DefaultValueText = defaultValueText
            };
            ObserveText(parameterModel, beforeRetainText);
            indexerParameters.Add(parameter);
            parameterModels.Add(parameterModel);
        }

        var returnType = FormatMethodReturnType(
            reader,
            treeSignature.ReturnType,
            paramHandles,
            beforeDecodeWork);
        var canonicalReturnType = FormatCanonicalMethodReturnType(
            reader,
            treeSignature.ReturnType,
            paramHandles,
            beforeDecodeWork);
        var model = new ApiSignature
        {
            ReturnType = returnType,
            CanonicalReturnType = canonicalReturnType,
            StructuralReturnType = treeSignature.ReturnType.HasStructuralPayload
                ? treeSignature.ReturnType.StructuralIdentity()
                : null,
            ReturnTypeReferences =
                [.. treeSignature.ReturnType.ReferencedTypes().Distinct()],
            ReturnTypeDefinitionReference =
                treeSignature.ReturnType.DefinitionReference(),
            ReturnTypeShape =
                ApiTypeShapeFactory.FromTypeNode(
                    treeSignature.ReturnType),
            MemberName = indexerParameters.Count > 0 ? "this[]" : name,
            IsRequired = isRequired,
            Parameters = parameterModels,
            Accessors = accessorModels
        };

        if (indexerParameters.Count > 0)
            return (
                $"{requiredPrefix}{returnType} this[{string.Join(", ", indexerParameters)}] {accessorStr}",
                model,
                treeSignature.ReturnType.IsDegraded
                    || treeSignature.ParameterTypes.Any(parameter => parameter.IsDegraded));

        return (
            $"{requiredPrefix}{returnType} {SanitizeIdentifier(name)} {accessorStr}",
            model,
            treeSignature.ReturnType.IsDegraded
                || treeSignature.ParameterTypes.Any(parameter => parameter.IsDegraded));
    }

    static void ApplyAccessorStructuralReturns(
        List<ApiAccessor> accessors,
        MetadataReader reader,
        Func<string, MethodDefinitionHandle> handleForKind,
        TypeNodeProvider provider,
        GenericContext context,
        Action<string>? beforeRetainText,
        Action<int>? beforeDecodeWork)
    {
        foreach (ApiAccessor accessor in accessors)
        {
            MethodDefinitionHandle handle = handleForKind(accessor.Kind);
            accessor.Name = MethodDefinitionName(reader, handle, beforeDecodeWork);
            if (accessor.Name is not null)
                beforeRetainText?.Invoke(accessor.Name);
            accessor.StructuralReturnType = MethodStructuralReturnType(
                reader,
                handle,
                provider,
                context,
                beforeRetainText);
        }
    }

    static string? MethodDefinitionName(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        Action<int>? beforeDecodeWork)
    {
        if (handle.IsNil)
            return null;

        return DecodeString(
            reader,
            reader.GetMethodDefinition(handle).Name,
            beforeDecodeWork);
    }

    static string? MethodStructuralReturnType(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        TypeNodeProvider provider,
        GenericContext context,
        Action<string>? beforeRetainText)
    {
        if (handle.IsNil)
            return null;

        var method = reader.GetMethodDefinition(handle);
        var signature = GuardedProviderDecode.Method(
            reader,
            method,
            provider,
            context,
            (TypeNode)new DegradedTypeNode());
        if (!signature.ReturnType.HasStructuralPayload)
            return null;

        string identity = signature.ReturnType.StructuralIdentity();
        beforeRetainText?.Invoke(identity);
        return identity;
    }

    /// <summary>
    /// Formats a property accessor with its access level prefix when it differs from the property's overall level.
    /// </summary>
    private static string FormatAccessor(string kind, MethodAttributes access, int bestAccess)
    {
        if ((int)access == bestAccess)
            return kind;
        var prefix = GetAccessibility(access);
        return prefix != null ? $"{prefix} {kind}" : kind;
    }

    private static string? AccessorAccessibility(MethodAttributes access, int bestAccess)
        => (int)access == bestAccess ? null : GetAccessibility(access);

    /// <summary>Gets the first parameter type for the lightweight extraction path.</summary>
    private static string? GetFirstParameterType(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var context = GenericContext.ForMethod(reader, typeDef, method);
        return GuardedSignatureText.MethodText(reader, method, context)
            .TryGetValue(out var signature)
                && signature.ParameterTypes.Length > 0
                    ? signature.ParameterTypes[0]
                    : null;
    }

    private static MetadataTypeDefinitionName? GetFirstParameterDefinitionName(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var context = GenericContext.ForMethod(reader, typeDef, method);
        MethodSignature<MetadataTypeDefinitionName?> signature =
            GuardedProviderDecode.Method(
                reader,
                method,
                DefinesPrimitiveTypes(reader)
                    ? ExtensionReceiverDefinitionProvider.WithLocalPrimitives
                    : ExtensionReceiverDefinitionProvider.WithoutLocalPrimitives,
                context,
                fallbackReturn: null);
        return signature.ParameterTypes.Length > 0
            ? signature.ParameterTypes[0]
            : null;
    }

    static bool DefinesPrimitiveTypes(MetadataReader reader)
        => PrimitiveDefinitionClassifications.GetValue(
            reader,
            static value => new(
                ComputeDefinesPrimitiveTypes(value)))
            .Value;

    static bool ComputeDefinesPrimitiveTypes(MetadataReader reader)
    {
        if (!reader.IsAssembly)
            return false;

        try
        {
            AssemblyDefinition definition =
                reader.GetAssemblyDefinition();
            string name = reader.GetString(definition.Name);
            if (name is not ("System.Private.CoreLib" or "mscorlib"))
                return false;
            if (reader.GetBlobReader(definition.PublicKey).Length
                > MetadataSafetyPolicy.MaxStructuralSignatureChars / 2)
            {
                return false;
            }

            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            return identity.PublicKeyToken is { } token
                && PlatformKeys.IsPlatform(token);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    sealed record PrimitiveDefinitionClassification(bool Value);

    internal static MetadataTypeDefinitionName? GetLocalPrimitiveDefinition(
        PrimitiveTypeCode typeCode)
    {
        if (typeCode is not (
            PrimitiveTypeCode.Void
            or PrimitiveTypeCode.Boolean
            or PrimitiveTypeCode.Char
            or PrimitiveTypeCode.SByte
            or PrimitiveTypeCode.Byte
            or PrimitiveTypeCode.Int16
            or PrimitiveTypeCode.UInt16
            or PrimitiveTypeCode.Int32
            or PrimitiveTypeCode.UInt32
            or PrimitiveTypeCode.Int64
            or PrimitiveTypeCode.UInt64
            or PrimitiveTypeCode.Single
            or PrimitiveTypeCode.Double
            or PrimitiveTypeCode.String
            or PrimitiveTypeCode.Object
            or PrimitiveTypeCode.IntPtr
            or PrimitiveTypeCode.UIntPtr
            or PrimitiveTypeCode.TypedReference))
        {
            return null;
        }

        return ((MetadataTypeDefinitionNameResult.Valid)
            MetadataTypeDefinitionName.Create(
                "System",
                [typeCode.ToString()])).Name;
    }

    sealed class ExtensionReceiverDefinitionProvider :
        ISignatureTypeProvider<MetadataTypeDefinitionName?, GenericContext?>
    {
        readonly bool primitivesAreLocal;

        ExtensionReceiverDefinitionProvider(bool primitivesAreLocal) =>
            this.primitivesAreLocal = primitivesAreLocal;

        public static ExtensionReceiverDefinitionProvider WithLocalPrimitives { get; } =
            new(primitivesAreLocal: true);

        public static ExtensionReceiverDefinitionProvider WithoutLocalPrimitives { get; } =
            new(primitivesAreLocal: false);

        public MetadataTypeDefinitionName? GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
            => MetadataTypeDefinitionNameReader.Read(reader, handle)
                is MetadataTypeDefinitionNameReadResult.Read read
                    ? read.Name
                    : null;

        public MetadataTypeDefinitionName? GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            Span<TypeReferenceHandle> rootToLeaf =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    rootToLeaf,
                    out _,
                    out EntityHandle terminal,
                    out _)
                || terminal.Kind != HandleKind.ModuleDefinition)
            {
                return null;
            }

            return MetadataTypeDefinitionNameReader.Read(reader, handle)
                is MetadataTypeDefinitionNameReadResult.Read read
                    ? read.Name
                    : null;
        }

        public MetadataTypeDefinitionName? GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return null;
            using (scope)
                return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        }

        public MetadataTypeDefinitionName? GetGenericInstantiation(
            MetadataTypeDefinitionName? genericType,
            ImmutableArray<MetadataTypeDefinitionName?> typeArguments)
            => genericType;

        public MetadataTypeDefinitionName? GetByReferenceType(
            MetadataTypeDefinitionName? elementType)
            => elementType;

        public MetadataTypeDefinitionName? GetModifiedType(
            MetadataTypeDefinitionName? modifier,
            MetadataTypeDefinitionName? unmodifiedType,
            bool isRequired)
            => unmodifiedType;

        public MetadataTypeDefinitionName? GetPinnedType(
            MetadataTypeDefinitionName? elementType)
            => elementType;

        public MetadataTypeDefinitionName? GetArrayType(
            MetadataTypeDefinitionName? elementType,
            ArrayShape shape)
            => null;

        public MetadataTypeDefinitionName? GetSZArrayType(
            MetadataTypeDefinitionName? elementType)
            => null;

        public MetadataTypeDefinitionName? GetPointerType(
            MetadataTypeDefinitionName? elementType)
            => null;

        public MetadataTypeDefinitionName? GetFunctionPointerType(
            MethodSignature<MetadataTypeDefinitionName?> signature)
            => null;

        public MetadataTypeDefinitionName? GetGenericMethodParameter(
            GenericContext? context,
            int index)
            => null;

        public MetadataTypeDefinitionName? GetGenericTypeParameter(
            GenericContext? context,
            int index)
            => null;

        public MetadataTypeDefinitionName? GetPrimitiveType(
            PrimitiveTypeCode typeCode)
            => primitivesAreLocal
                ? GetLocalPrimitiveDefinition(typeCode)
                : null;
    }

    static void AddFilteredJsonPropertyNameFact(
        ApiType type,
        FilteredJsonPropertyNameKind kind,
        string? associatedMemberName,
        int metadataToken,
        List<string?> propertyNames)
    {
        if (propertyNames.Count > 0)
        {
            type.FilteredJsonPropertyNameFacts.Add(
                new FilteredJsonPropertyNameFact(
                    kind,
                    associatedMemberName,
                    metadataToken,
                    propertyNames));
        }
    }

    static void RetainFilteredRuntimeJsExportFact(
        ApiType type,
        string methodName,
        MethodDefinitionHandle methodHandle,
        RuntimeJsExportAttributeEvidence evidence)
    {
        if (evidence.Count == 0 && !evidence.HasMalformedRow)
            return;

        type.FilteredRuntimeJsExportFacts.Add(new(
            methodName,
            MetadataTokens.GetToken(methodHandle),
            evidence.Count,
            evidence.HasValidRow,
            evidence.HasMalformedRow));
    }

    static void RetainFilteredRuntimeJsExportFacts(
        MetadataReader reader,
        TypeDefinition type,
        ApiSurface surface,
        ExtractionBudget? budget,
        Action<int>? observeDecodeWork)
    {
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            RuntimeJsExportAttributeEvidence evidence =
                AttributeReader.ReadRuntimeJsExportAttributes(
                    reader,
                    method.GetCustomAttributes(),
                    observeDecodeWork);
            if (evidence.Count == 0 && !evidence.HasMalformedRow)
                continue;

            string methodName = DecodeString(
                reader,
                method.Name,
                observeDecodeWork);
            var fact = new FilteredRuntimeJsExportFact(
                methodName,
                MetadataTokens.GetToken(methodHandle),
                evidence.Count,
                evidence.HasValidRow,
                evidence.HasMalformedRow);
            budget?.RetainSurfaceFilteredRuntimeJsExportFact(fact);
            surface.FilteredRuntimeJsExportFacts.Add(fact);
        }
    }

    /// <summary>
    /// Checks if a method signature contains unsafe constructs (pointers). This
    /// catches members whose signature renders a pointer; members declared
    /// <c>unsafe</c> with no pointer in the signature are detected separately via
    /// <see cref="AttributeReader.HasRequiresUnsafeAttribute"/>.
    /// </summary>
    private static bool HasUnsafeSignature(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        // Check for pointer types (e.g., int*, void*, byte*)
        // and function pointers (delegate*)
        return signature.Contains('*');
    }

    internal static long CountRetainedText(ApiType type)
    {
        long count = 0;
        AddText(ref count, type.Namespace);
        AddText(ref count, type.Name);
        AddText(ref count, type.MetadataName);
        AddText(ref count, type.DefinitionName);
        AddText(ref count, type.Accessibility);
        AddText(ref count, type.Kind);
        AddText(ref count, type.Attributes);
        AddText(ref count, type.EnumUnderlyingType);
        AddText(ref count, type.BaseType);
        AddText(ref count, type.BaseTypeReference?.Assembly);
        AddText(ref count, type.BaseTypeReference?.FullName);
        AddText(ref count, type.BaseTypeReference?.DefinitionName);
        foreach (ApiJsonSerializableRoot root
            in type.JsonSerializableRoots)
        {
            AddText(ref count, root.ElementType?.Assembly);
            AddText(ref count, root.ElementType?.FullName);
            AddText(ref count, root.ElementType?.DefinitionName);
            AddText(ref count, root.Type);
            AddText(ref count, root.UnsupportedReason);
            AddText(ref count, root.TypeInfoPropertyName);
        }
        AddText(ref count, type.Interfaces);
        foreach (FilteredJsonPropertyNameFact fact
            in type.FilteredJsonPropertyNameFacts)
        {
            AddText(ref count, fact.AssociatedMemberName);
            foreach (string? propertyName in fact.PropertyNames)
                AddText(ref count, propertyName);
        }
        foreach (FilteredRuntimeJsExportFact fact
            in type.FilteredRuntimeJsExportFacts)
        {
            AddText(ref count, fact.MethodName);
        }
        foreach (TypeParameter parameter in type.TypeParameters)
            AddText(ref count, parameter);
        return count;
    }

    internal static long CountRetainedText(ApiMember member)
    {
        long count = 0;
        AddText(ref count, member.Name);
        AddText(ref count, member.Kind);
        AddText(ref count, member.Attributes);
        AddText(ref count, member.ReturnType);
        AddText(ref count, member.Signature);
        AddText(ref count, member.CanonicalSignature);
        AddText(ref count, member.SignatureModel);
        AddText(ref count, member.Accessibility);
        AddText(ref count, member.ObsoleteMessage);
        AddText(ref count, member.ExtendedType);
        AddText(ref count, member.DeclaringType);
        AddText(ref count, member.DeclaringTypeCanonicalName);
        AddText(ref count, member.EnumValueLiteral);
        AddText(ref count, member.JsonPropertyName);
        AddText(ref count, member.GetterAccessibility);
        AddText(ref count, member.SetterAccessibility);
        foreach (string? propertyName
            in member.JsonPropertyNameAttributeValues)
        {
            AddText(ref count, propertyName);
        }
        foreach (string? enumMemberName
            in member.JsonStringEnumMemberNameAttributeValues)
        {
            AddText(ref count, enumMemberName);
        }
        return count;
    }

    static long CountRetainedText(ApiSurfaceInspectionFailure failure)
    {
        long count = 0;
        AddText(ref count, failure.Operation);
        AddText(ref count, failure.Kind);
        AddText(ref count, failure.Detail);
        return count;
    }

    static long CountRetainedText(TypeForwarder forwarder)
    {
        long count = 0;
        AddText(ref count, forwarder.DefinitionName);
        AddText(ref count, forwarder.TypeName);
        AddText(ref count, forwarder.TargetAssembly);
        return count;
    }

    static void ObserveText(ApiParameter parameter, Action<string>? observe)
    {
        if (observe is null)
            return;
        foreach (string attribute in parameter.Attributes)
            observe(attribute);
        ObserveText(parameter.Name, observe);
        ObserveText(parameter.Type, observe);
        ObserveText(parameter.CanonicalType, observe);
        ObserveText(parameter.StructuralType, observe);
        ObserveText(parameter.Modifier, observe);
        ObserveText(parameter.DefaultValueText, observe);
    }

    static void ObserveText(string? text, Action<string> observe)
    {
        if (text is not null)
            observe(text);
    }

    static string DecodeString(
        MetadataReader reader,
        StringHandle handle,
        Action<int>? beforeDecodeWork)
    {
        beforeDecodeWork?.Invoke(reader.GetBlobReader(handle).Length);
        return reader.GetString(handle);
    }

    static ApiAssemblyIdentity? ResolveTypeAssemblyIdentity(
        MetadataReader reader,
        EntityHandle type,
        ApiAssemblyIdentity? currentAssembly,
        Action<int>? beforeDecodeWork)
    {
        if (type.Kind == HandleKind.TypeDefinition)
            return currentAssembly;
        if (type.Kind != HandleKind.TypeReference)
            return null;

        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    (TypeReferenceHandle)type,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out _))
        {
            return null;
        }

        return terminal.Kind switch
        {
            HandleKind.AssemblyReference =>
                ApiAssemblyIdentity.FromReference(
                    reader,
                    (AssemblyReferenceHandle)terminal,
                    beforeDecodeWork),
            HandleKind.ModuleDefinition or HandleKind.ModuleReference =>
                currentAssembly,
            _ when terminal.IsNil => currentAssembly,
            _ => null,
        };
    }

    static void RetainAssemblyIdentity(
        ApiAssemblyIdentity? identity,
        Action<string>? observeText)
    {
        if (identity is null || observeText is null)
            return;

        observeText(identity.Name);
        if (identity.Culture is not null)
            observeText(identity.Culture);
        if (identity.PublicKeyToken is not null)
            observeText(identity.PublicKeyToken);
    }

    static void AddText(ref long count, ApiSignature? signature)
    {
        if (signature is null)
            return;
        AddText(ref count, signature.ReturnType);
        AddText(ref count, signature.CanonicalReturnType);
        AddText(ref count, signature.StructuralReturnType);
        AddText(ref count, signature.ReturnTypeShape);
        AddText(
            ref count,
            signature.ReturnTypeDefinitionReference?.Assembly);
        AddText(
            ref count,
            signature.ReturnTypeDefinitionReference?.FullName);
        AddText(
            ref count,
            signature.ReturnTypeDefinitionReference?.DefinitionName);
        foreach (ApiTypeReferenceIdentity reference
            in signature.ReturnTypeReferences)
        {
            AddText(ref count, reference.Assembly);
            AddText(ref count, reference.FullName);
            AddText(ref count, reference.DefinitionName);
        }
        AddText(ref count, signature.ReturnAttributes);
        AddText(ref count, signature.MemberName);
        AddText(ref count, signature.ExtensionReceiverType);
        foreach (TypeParameter parameter in signature.TypeParameters)
            AddText(ref count, parameter);
        foreach (ApiParameter parameter in signature.Parameters)
        {
            AddText(ref count, parameter.Attributes);
            AddText(ref count, parameter.Name);
            AddText(ref count, parameter.Type);
            AddText(ref count, parameter.CanonicalType);
            AddText(ref count, parameter.StructuralType);
            foreach (ApiTypeReferenceIdentity reference
                in parameter.TypeReferences)
            {
                AddText(ref count, reference.Assembly);
                AddText(ref count, reference.FullName);
                AddText(ref count, reference.DefinitionName);
            }
            AddText(ref count, parameter.Modifier);
            AddText(ref count, parameter.DefaultValueText);
        }
        foreach (ApiAccessor accessor in signature.Accessors)
        {
            AddText(ref count, accessor.Kind);
            AddText(ref count, accessor.Accessibility);
            AddText(ref count, accessor.ReturnAttributes);
            AddText(ref count, accessor.Name);
            AddText(ref count, accessor.StructuralReturnType);
        }
    }

    static void AddText(ref long count, ApiTypeShape? shape)
    {
        if (shape is null)
            return;

        var pending = new Stack<ApiTypeShape>();
        pending.Push(shape);
        while (pending.Count > 0)
        {
            ApiTypeShape current = pending.Pop();
            if (current.Definition is { } definition)
            {
                AddText(ref count, definition.Assembly);
                AddText(ref count, definition.FullName);
                AddText(ref count, definition.DefinitionName);
            }
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            for (int index = current.TypeArguments.Length - 1;
                index >= 0;
                index--)
            {
                pending.Push(current.TypeArguments[index]);
            }
        }
    }

    static void AddText(ref long count, TypeParameter parameter)
    {
        AddText(ref count, parameter.Name);
        AddText(ref count, parameter.Variance);
        AddText(ref count, parameter.Constraints);
        if (parameter.StructuredConstraints is not null)
        {
            foreach (TypeParameterConstraint constraint in parameter.StructuredConstraints)
                AddText(ref count, constraint.Value);
        }
    }

    static void AddText(ref long count, MetadataTypeDefinitionName? name)
    {
        if (name is null)
            return;
        AddText(ref count, name.Namespace);
        foreach (string segment in name.Segments)
            AddText(ref count, segment);
    }

    static void AddText(ref long count, IEnumerable<string> values)
    {
        foreach (string value in values)
            AddText(ref count, value);
    }

    static void AddText(
        ref long count,
        ApiAssemblyIdentity? identity)
    {
        if (identity is null)
            return;
        count = count > long.MaxValue
                - identity.RetainedCharacterCount
            ? long.MaxValue
            : count + identity.RetainedCharacterCount;
    }

    static void AddText(ref long count, string? value)
    {
        if (value is null)
            return;
        count = count > long.MaxValue - value.Length
            ? long.MaxValue
            : count + value.Length;
    }

    static string? TryOperatorPairingKey(
        MetadataReader reader,
        MethodDefinition method)
    {
        try
        {
            string signature =
                MethodStructuralSignature.BuildOperatorPairing(
                    reader,
                    method);
            return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(signature)));
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps MethodAttributes access level to C# keyword. Returns null for public.
    /// </summary>
    private static string? GetAccessibility(MethodAttributes access) => access switch
    {
        MethodAttributes.Private => "private",
        MethodAttributes.FamANDAssem => "private protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        _ => null // Public
    };

    /// <summary>
    /// Maps FieldAttributes access level to C# keyword. Returns null for public.
    /// </summary>
    private static string? GetFieldAccessibility(FieldAttributes access) => access switch
    {
        FieldAttributes.Private => "private",
        FieldAttributes.FamANDAssem => "private protected",
        FieldAttributes.Assembly => "internal",
        FieldAttributes.Family => "protected",
        FieldAttributes.FamORAssem => "protected internal",
        _ => null // Public
    };

    sealed class TypeParameterConstraintResolution
        : IOperatorTypeRelationshipResolver
    {
        readonly MetadataReader _reader;
        readonly ResolvedAssemblyReference _source;
        readonly List<Group> _groups = [];
        readonly List<(MethodDefinitionHandle Handle, ApiMember Member)>
            _operators = [];
        EntityHandle _operatorSubject;
        bool _hasUnauthenticatedTypeKindEvidence;

        internal TypeParameterConstraintResolution(
            MetadataReader reader,
            ResolvedAssemblyReference source,
            int maxTypeResolutionRequests)
        {
            _reader = reader;
            _source = source;
            Plan = new TypeParameterKindClassifier.ResolutionPlan(
                reader,
                source,
                maxTypeResolutionRequests);
        }

        internal TypeParameterKindClassifier.ResolutionPlan Plan { get; }

        internal IReadOnlyCollection<TypeResolutionRequest> Requests =>
            Plan.Requests;

        internal Checkpoint CreateCheckpoint() =>
            new(
                _groups.Count,
                _operators.Count,
                Plan.Checkpoint());

        internal void Rollback(Checkpoint checkpoint)
        {
            if (checkpoint.GroupCount < 0
                || checkpoint.GroupCount > _groups.Count
                || checkpoint.OperatorCount < 0
                || checkpoint.OperatorCount > _operators.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(checkpoint));
            }

            _groups.RemoveRange(
                checkpoint.GroupCount,
                _groups.Count - checkpoint.GroupCount);
            _operators.RemoveRange(
                checkpoint.OperatorCount,
                _operators.Count - checkpoint.OperatorCount);
            Plan.Rollback(checkpoint.RequestCheckpoint);
        }

        internal void Track(
            EntityHandle subject,
            List<(GenericParameterHandle Handle, TypeParameter Parameter)>
                group)
        {
            if (group.Count != 0)
                _groups.Add(new Group(subject, group));
        }

        internal void TrackOperator(
            MethodDefinitionHandle handle,
            ApiMember member)
            => _operators.Add((handle, member));

        internal void Apply(TypeResolutionContext context)
        {
            Plan.Bind(context);
            foreach (var (handle, member) in _operators)
            {
                OperatorMetadata.DeclarationClassification
                    classification = ClassifyOperator(handle);
                member.CSharpOperatorDeclaration = classification switch
                {
                    OperatorMetadata.DeclarationClassification.Yes =>
                        true,
                    OperatorMetadata.DeclarationClassification.No =>
                        false,
                    _ => null,
                };
            }
            foreach (var group in _groups)
            {
                var chain =
                    new TypeParameterKindClassifier.ChainState(
                        Plan,
                        group.Subject);
                foreach (var (handle, parameter) in group.Parameters)
                {
                    GenericParameter definition =
                        _reader.GetGenericParameter(handle);
                    GenericParameterAttributes attributes =
                        definition.Attributes;
                    parameter.TypeKind =
                        TypeParameterKindClassifier.Classify(
                            _reader,
                            handle,
                            hasValueTypeConstraint:
                                (attributes
                                    & GenericParameterAttributes
                                        .NotNullableValueTypeConstraint) != 0,
                            hasReferenceTypeConstraint:
                                (attributes
                                    & GenericParameterAttributes
                                        .ReferenceTypeConstraint) != 0,
                            chain);
                }
            }
        }

        internal readonly record struct Checkpoint(
            int GroupCount,
            int OperatorCount,
            TypeParameterKindClassifier.ResolutionPlan.RequestCheckpoint
                RequestCheckpoint);

        readonly record struct Group(
            EntityHandle Subject,
            List<(GenericParameterHandle Handle, TypeParameter Parameter)>
                Parameters);

        public OperatorMetadata.TypeRelationship ValueTypeRelationship(
            MetadataReader reader,
            OperatorMetadata.OperatorSignatureType type)
        {
            if (ResolveDefinition(reader, type, _source)
                is not { } definition)
            {
                return OperatorMetadata.TypeRelationship.Unknown;
            }
            if (!KnownKind(definition))
            {
                _hasUnauthenticatedTypeKindEvidence = true;
                return OperatorMetadata.TypeRelationship.Unknown;
            }
            return definition.IsValueType
                ? OperatorMetadata.TypeRelationship.Yes
                : OperatorMetadata.TypeRelationship.No;
        }

        public OperatorMetadata.TypeRelationship InterfaceRelationship(
            MetadataReader reader,
            OperatorMetadata.OperatorSignatureType type)
        {
            if (ResolveDefinition(reader, type, _source)
                is not { } definition)
            {
                return OperatorMetadata.TypeRelationship.Unknown;
            }
            if (!KnownKind(definition))
            {
                _hasUnauthenticatedTypeKindEvidence = true;
                return OperatorMetadata.TypeRelationship.Unknown;
            }
            return definition.IsInterface
                ? OperatorMetadata.TypeRelationship.Yes
                : OperatorMetadata.TypeRelationship.No;
        }

        public bool HasUnauthenticatedTypeKindEvidence =>
            _hasUnauthenticatedTypeKindEvidence;

        public bool IsResolutionComplete => Plan.Context is not null;

        public void BeginOperatorClassification()
            => _hasUnauthenticatedTypeKindEvidence = false;

        public OperatorMetadata.TypeRelationship
            SameOrDerivedRelationship(
                MetadataReader reader,
                OperatorMetadata.OperatorSignatureType candidate,
                OperatorMetadata.OperatorSignatureType requiredBase)
        {
            ResolvedAssemblyReference candidateOrigin = _source;
            MetadataReader candidateReader = reader;
            candidate = AddResolutionRequests(
                candidateReader,
                candidate,
                candidateOrigin);
            requiredBase = AddResolutionRequests(
                reader,
                requiredBase,
                _source);
            var visited = new HashSet<MetadataTypeDefinitionAddress>();
            for (int depth = 0; depth < 64; depth++)
            {
                OperatorMetadata.TypeRelationship same = SameType(
                    candidateReader,
                    candidate,
                    candidateOrigin,
                    reader,
                    requiredBase,
                    _source);
                if (same != OperatorMetadata.TypeRelationship.No)
                    return same;

                ResolvedTypeDefinition? definition = ResolveDefinition(
                    candidateReader,
                    candidate,
                    candidateOrigin);
                if (definition is null
                    || !KnownKind(definition)
                    || !visited.Add(definition.Address)
                    || Plan.Context is not { } context
                    || context.Open(
                        definition,
                        out TypeDefinitionHandle handle)
                        is not { } resolvedReader)
                {
                    return OperatorMetadata.TypeRelationship.Unknown;
                }

                TypeDefinition typeDefinition =
                    resolvedReader.GetTypeDefinition(handle);
                if (typeDefinition.BaseType.IsNil)
                    return OperatorMetadata.TypeRelationship.No;
                var baseType =
                    AddResolutionRequests(
                        resolvedReader,
                        OperatorMetadata.ReadSignatureType(
                            resolvedReader,
                            typeDefinition.BaseType),
                        definition.Assembly.Assembly);
                if (candidate.IsGenericInstantiation)
                {
                    baseType = baseType.Instantiate(
                        candidate.TypeArguments);
                }
                else if (typeDefinition.GetGenericParameters().Count != 0)
                {
                    return OperatorMetadata.TypeRelationship.Unknown;
                }

                candidate = baseType;
                candidateReader = resolvedReader;
                candidateOrigin = definition.Assembly.Assembly;
            }
            return OperatorMetadata.TypeRelationship.Unknown;
        }

        OperatorMetadata.TypeRelationship SameType(
            MetadataReader leftReader,
            OperatorMetadata.OperatorSignatureType left,
            ResolvedAssemblyReference leftOrigin,
            MetadataReader rightReader,
            OperatorMetadata.OperatorSignatureType right,
            ResolvedAssemblyReference rightOrigin)
        {
            if (left.IsTypeParameter || right.IsTypeParameter)
            {
                return left.IsTypeParameter
                    && right.IsTypeParameter
                    && left.IsMethodTypeParameter
                        == right.IsMethodTypeParameter
                    && left.TypeParameterIndex
                        == right.TypeParameterIndex
                    ? OperatorMetadata.TypeRelationship.Yes
                    : OperatorMetadata.TypeRelationship.No;
            }
            if (left.IsNonNamedType || right.IsNonNamedType)
                return OperatorMetadata.TypeRelationship.Unknown;
            if (left.IsGenericInstantiation
                != right.IsGenericInstantiation
                || left.TypeArguments.Length
                    != right.TypeArguments.Length)
            {
                return OperatorMetadata.TypeRelationship.No;
            }
            if (left.DefinitionAddress is { } leftAddress
                && right.DefinitionAddress is { } rightAddress
                && leftAddress != rightAddress)
            {
                return OperatorMetadata.TypeRelationship.No;
            }
            if (left.Identity.IsNil || right.Identity.IsNil)
            {
                if (left.Namespace is null
                    || left.Name is null
                    || right.Namespace is null
                    || right.Name is null)
                {
                    return OperatorMetadata.TypeRelationship.Unknown;
                }
                if (left.Namespace != right.Namespace
                    || left.Name != right.Name)
                {
                    return OperatorMetadata.TypeRelationship.No;
                }
                if (left.Identity.IsNil && right.Identity.IsNil)
                    return OperatorMetadata.TypeRelationship.Yes;
                OperatorMetadata.OperatorSignatureType named =
                    left.Identity.IsNil ? right : left;
                return named.IsTrustedCoreLibraryType
                    ? OperatorMetadata.TypeRelationship.Yes
                    : OperatorMetadata.TypeRelationship.No;
            }

            ResolvedTypeDefinition? leftDefinition =
                ResolveDefinition(
                    leftReader,
                    left,
                    leftOrigin);
            ResolvedTypeDefinition? rightDefinition =
                ResolveDefinition(
                    rightReader,
                    right,
                    rightOrigin);
            if (leftDefinition is null || rightDefinition is null)
                return OperatorMetadata.TypeRelationship.Unknown;
            if (leftDefinition.Address != rightDefinition.Address
                || !leftDefinition.Assembly.Assembly.Identity
                    .IsEquivalentTo(
                        rightDefinition.Assembly.Assembly.Identity))
            {
                return OperatorMetadata.TypeRelationship.No;
            }

            for (int index = 0;
                index < left.TypeArguments.Length;
                index++)
            {
                OperatorMetadata.TypeRelationship argument = SameType(
                    leftReader,
                    left.TypeArguments[index],
                    leftOrigin,
                    rightReader,
                    right.TypeArguments[index],
                    rightOrigin);
                if (argument != OperatorMetadata.TypeRelationship.Yes)
                    return argument;
            }
            return OperatorMetadata.TypeRelationship.Yes;
        }

        ResolvedTypeDefinition? ResolveDefinition(
            MetadataReader reader,
            OperatorMetadata.OperatorSignatureType type,
            ResolvedAssemblyReference origin)
        {
            TypeResolutionRequest? request =
                type.ResolutionRequest
                ?? CreateRequest(reader, type, origin);
            TypeResolutionOutcome? outcome =
                Plan.ResolveRequest(request, _operatorSubject);
            if (outcome is not null
                && outcome is not TypeResolutionOutcome.Resolved)
            {
                _hasUnauthenticatedTypeKindEvidence = true;
            }
            if (outcome is not TypeResolutionOutcome.Resolved resolved)
            {
                return null;
            }
            if (request is not null)
            {
                Plan.RecordDefinitionKindFailure(
                    request,
                    resolved.Definition,
                    _operatorSubject);
            }
            return resolved.Definition;
        }

        static OperatorMetadata.OperatorSignatureType AddResolutionRequests(
            MetadataReader reader,
            OperatorMetadata.OperatorSignatureType type,
            ResolvedAssemblyReference origin)
            => type with
            {
                ResolutionRequest =
                    type.ResolutionRequest
                    ?? CreateRequest(reader, type, origin),
                TypeArguments =
                [
                    .. type.TypeArguments.Select(
                        argument => AddResolutionRequests(
                            reader,
                            argument,
                            origin)),
                ],
            };

        static bool KnownKind(ResolvedTypeDefinition definition)
            => definition.HasKnownKind;

        internal OperatorMetadata.DeclarationClassification
            ClassifyOperator(MethodDefinitionHandle handle)
        {
            _operatorSubject = handle;
            try
            {
                return OperatorMetadata
                    .ClassifyCSharpOperatorDeclaration(
                        _reader,
                        _reader.GetMethodDefinition(handle),
                        this);
            }
            finally
            {
                _operatorSubject = default;
            }
        }

        static TypeResolutionRequest? CreateRequest(
            MetadataReader reader,
            OperatorMetadata.OperatorSignatureType type,
            ResolvedAssemblyReference origin)
        {
            if (type.Identity.Kind is not (
                HandleKind.TypeDefinition
                or HandleKind.TypeReference))
            {
                return null;
            }
            MetadataTypeDefinitionNameReadResult nameResult =
                type.Identity.Kind switch
                {
                    HandleKind.TypeDefinition =>
                        MetadataTypeDefinitionNameReader.Read(
                            reader,
                            (TypeDefinitionHandle)type.Identity),
                    HandleKind.TypeReference =>
                        MetadataTypeDefinitionNameReader.Read(
                            reader,
                            (TypeReferenceHandle)type.Identity),
                    _ => throw new UnreachableException(),
                };
            if (nameResult
                is not MetadataTypeDefinitionNameReadResult.Read read)
            {
                return null;
            }
            if (type.Identity.Kind == HandleKind.TypeDefinition)
            {
                return TypeResolutionRequest.FromAssembly(
                    origin,
                    AssemblyResolutionScope.Any,
                    read.Name);
            }

            Span<TypeReferenceHandle> chain =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal
                    .TryWalkTypeReferenceResolutionScope(
                        reader,
                        (TypeReferenceHandle)type.Identity,
                        chain,
                        out _,
                        out EntityHandle terminal,
                        out _))
            {
                return null;
            }

            return terminal.Kind switch
            {
                HandleKind.AssemblyReference =>
                    FromAssemblyReference(
                        reader,
                        origin,
                        (AssemblyReferenceHandle)terminal,
                        read.Name),
                HandleKind.ModuleDefinition =>
                    TypeResolutionRequest.FromAssembly(
                        origin,
                        AssemblyResolutionScope.Any,
                        read.Name),
                HandleKind.ModuleReference =>
                    TypeResolutionRequest.FromModule(
                        origin,
                        reader.GetString(
                            reader.GetModuleReference(
                                (ModuleReferenceHandle)terminal).Name),
                        read.Name),
                _ when terminal.IsNil =>
                    TypeResolutionRequest.FromAssembly(
                        origin,
                        AssemblyResolutionScope.Any,
                        read.Name),
                _ => null,
            };
        }

        static TypeResolutionRequest FromAssemblyReference(
            MetadataReader reader,
            ResolvedAssemblyReference origin,
            AssemblyReferenceHandle handle,
            MetadataTypeDefinitionName type)
        {
            AssemblyReferenceIdentity reference =
                AssemblyReferenceIdentity.From(reader, handle);
            AssemblyResolutionScope scope =
                PlatformKeys.IsPlatform(reference.PublicKeyToken)
                    ? AssemblyResolutionScope.Platform
                    : AssemblyResolutionScope.Any;
            return TypeResolutionRequest.FromReference(
                reference,
                AssemblyBindingOrigin.FromAssembly(origin),
                scope,
                type);
        }
    }

    /// <summary>
    /// The running retention count of one bounded extraction.
    /// </summary>
    /// <remarks>
    /// Members and retained text are counted as they are built but committed only when their type
    /// is, so a rejected type spends no retention budget. The exact retained total is gated by
    /// <c>ApiSurfaceExtractorBoundsTests.RetainedTextBudget_IsExact</c>. A separate monotonic
    /// extraction-wide decode-work estimate may reject allocation-amplifying input before its
    /// expanded model exists; the hostile-shape allocation tests in that class gate that safety
    /// boundary.
    /// </remarks>
    private sealed class ExtractionBudget(ApiSurfaceExtractionBounds bounds)
    {
        const int DecodeWorkWeight = 16;
        const int RetainedTextDecodeWorkCreditWeight = DecodeWorkWeight * 4;
        // Small exact retention budgets still need enough work room to decode one ordinary type.
        // It is granted once per extraction; retained model text then earns bounded additional
        // work so rejected or amplification-heavy candidates cannot rearm the floor.
        const int MinimumDecodeWorkLimit = 32_000_000;
        int _types;
        int _members;
        int _pendingMembers;
        int _inspectionFailures;
        int _typeForwarders;
        int _retainedTextCharacters;
        int _pendingTextCharacters;
        int _pendingObservedTextCharacters;
        long _decodeWork;

        public int MetadataRows { get; private set; }
        public int RetainedTextCharacters => _retainedTextCharacters;

        /// <summary>Refuses an image whose metadata shape exceeds the remaining walk budget.</summary>
        public void AdmitMetadataRows(MetadataReader reader)
        {
            foreach (TableIndex table in Enum.GetValues<TableIndex>())
            {
                int rows = reader.GetTableRowCount(table);
                if (rows > bounds.MaxMetadataRows - MetadataRows)
                {
                    throw new ExtractionBoundExceededException(
                        ApiSurfaceExtractionBound.MetadataRows);
                }
                MetadataRows += rows;
            }
        }

        /// <summary>Starts work that may determine whether a type is retained.</summary>
        public void BeginTypeCandidate()
        {
            _pendingMembers = 0;
            _pendingTextCharacters = 0;
            _pendingObservedTextCharacters = 0;
        }

        /// <summary>Admits a retained type before its model or members are built.</summary>
        public void BeginType()
        {
            if (_types >= bounds.MaxTypes)
                throw new ExtractionBoundExceededException(ApiSurfaceExtractionBound.Types);
        }

        /// <summary>Counts one member of the type currently being built.</summary>
        public void RetainMember(ApiMember member)
        {
            if (_members + _pendingMembers >= bounds.MaxMembers)
                throw new ExtractionBoundExceededException(ApiSurfaceExtractionBound.Members);
            RetainPendingText(CountRetainedText(member));
            _pendingMembers++;
        }

        /// <summary>Commits the type currently being built and its members.</summary>
        public void RetainType(ApiType type)
        {
            if (_types >= bounds.MaxTypes)
                throw new ExtractionBoundExceededException(ApiSurfaceExtractionBound.Types);
            RetainPendingText(CountRetainedText(type));
            _types++;
            _members += _pendingMembers;
            _retainedTextCharacters += _pendingTextCharacters;
            _pendingMembers = 0;
            _pendingTextCharacters = 0;
            _pendingObservedTextCharacters = 0;
        }

        /// <summary>Counts one member attached to a type that is already committed.</summary>
        public void RetainAttachedMember(ApiMember member)
        {
            if (_members >= bounds.MaxMembers)
                throw new ExtractionBoundExceededException(ApiSurfaceExtractionBound.Members);
            RetainCommittedText(CountRetainedText(member));
            _members++;
        }

        public void RetainSurfaceFilteredRuntimeJsExportFact(
            FilteredRuntimeJsExportFact fact) =>
            RetainCommittedText(fact.MethodName);

        /// <summary>Counts one retained metadata-row rejection.</summary>
        public void RetainInspectionFailure(ApiSurfaceInspectionFailure failure)
        {
            if (_inspectionFailures >= bounds.MaxInspectionFailures)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.InspectionFailures);
            }
            RetainCommittedText(CountRetainedText(failure));
            _inspectionFailures++;
        }

        /// <summary>Refuses before a type-forwarder model is built.</summary>
        public void BeginTypeForwarder()
        {
            if (_typeForwarders >= bounds.MaxTypeForwarders)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.TypeForwarders);
            }
        }

        /// <summary>Counts one retained type forwarder.</summary>
        public void RetainTypeForwarder(TypeForwarder forwarder)
        {
            if (_typeForwarders >= bounds.MaxTypeForwarders)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.TypeForwarders);
            }
            RetainCommittedText(CountRetainedText(forwarder));
            _typeForwarders++;
        }

        public void RetainCommittedText(string text) => RetainCommittedText(text.Length);

        public void ObservePendingText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            ObservePendingText(text.Length);
        }

        public void ObservePendingDecodeWork(int encodedCharacters)
        {
            if (encodedCharacters < 0)
                throw new ArgumentOutOfRangeException(nameof(encodedCharacters));
            long next =
                (long)encodedCharacters * DecodeWorkWeight + _decodeWork;
            long creditedCharacters =
                (long)_retainedTextCharacters + _pendingTextCharacters;
            long limit =
                MinimumDecodeWorkLimit
                + creditedCharacters * RetainedTextDecodeWorkCreditWeight;
            if (next > limit || next < 0)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.RetainedTextCharacters);
            }
            _decodeWork = next;
        }

        void RetainPendingText(long characters)
        {
            long next = characters + _pendingTextCharacters;
            if (next > bounds.MaxRetainedTextCharacters - _retainedTextCharacters
                || next < 0)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.RetainedTextCharacters);
            }
            _pendingTextCharacters += (int)characters;
        }

        void ObservePendingText(long characters)
        {
            long next = characters + _pendingObservedTextCharacters;
            if (next > bounds.MaxRetainedTextCharacters - _retainedTextCharacters
                || next < 0)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.RetainedTextCharacters);
            }
            _pendingObservedTextCharacters += (int)characters;
        }

        void RetainCommittedText(long characters)
        {
            if (characters > bounds.MaxRetainedTextCharacters - _retainedTextCharacters)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.RetainedTextCharacters);
            }
            _retainedTextCharacters += (int)characters;
        }
    }

    /// <summary>
    /// The abandonment signal of a bounded extraction. It is private to this extractor and caught
    /// by <see cref="ExtractBounded"/>, so a bound never surfaces as an exception to a caller.
    /// </summary>
    private sealed class ExtractionBoundExceededException(ApiSurfaceExtractionBound bound)
        : Exception("The API-surface extraction exceeded a declared retention bound.")
    {
        public ApiSurfaceExtractionBound Bound { get; } = bound;
    }
}
