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
                    Kind = "class",
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
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindingPolicy);

        var constraintResolution =
            new TypeParameterConstraintResolution(
                MetadataFormatAdmission.GetMetadataReader(peReader),
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

        using TypeResolutionContext context =
            catalog.CreateApiSurfaceContext(
                bindingPolicy,
                [source],
                constraintResolution.Requests);
        constraintResolution.Apply(context);
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
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        var surface = new ApiSurface();
        var reader = MetadataFormatAdmission.GetMetadataReader(peReader);
        Guid moduleVersionId = reader.GetGuid(
            reader.GetModuleDefinition().Mvid);
        var extensionReceiverDefinitions =
            new Dictionary<ApiMember, MetadataTypeDefinitionName>();
        budget?.AdmitMetadataRows(reader);
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
                MemorySafety = typesOnly
                    ? null
                    : new ApiModuleMemorySafetyFacts(
                        moduleVersionId, GetMemorySafetyIndex().Rules),
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
                if (accessorMethods.Contains(methodHandle)
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
                var isOperator = IsOperatorMethodName(methodName);
                var isVirtual = (methodAttributes & MethodAttributes.Virtual) != 0;
                var isNewSlot = (methodAttributes & MethodAttributes.NewSlot) != 0;
                var isOverride = isVirtual && !isNewSlot && !isExplicitInterfaceImplementation;

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
                    Kind = methodName switch
                    {
                        ".ctor" => "constructor",
                        _ when isOperator => "operator",
                        // A finalizer compiles to a `Finalize` method carrying an
                        // explicit `.override System.Object::Finalize` MethodImpl,
                        // so it also lands in `explicitImplementationBodies`. Classify
                        // it as its own kind before the explicit-interface arm so it
                        // is not filed under Explicit Interface Implementations; the
                        // MethodImpl still (correctly) suppresses its accessibility.
                        _ when isFinalizer => "finalizer",
                        _ when isExplicitInterfaceImplementation => "explicit-interface-implementation",
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
                        : MetadataTokens.GetToken(accessors.Remover)
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

    private static void CountSummaryMembers(
        MetadataReader reader,
        TypeDefinition typeDef,
        ApiType apiType,
        ApiSurface surface,
        bool isExtensionClass,
        Dictionary<ApiMember, MetadataTypeDefinitionName> extensionReceiverDefinitions)
    {
        var explicitImplementationBodies = GetExplicitImplementationBodies(reader, typeDef);
        var accessorMethods = GetSemanticAccessorMethods(reader, typeDef);

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var methodAccess = method.Attributes & MethodAttributes.MemberAccessMask;
            bool isExplicitImplementation = explicitImplementationBodies.Contains(methodHandle);
            if (methodAccess != MethodAttributes.Public && !isExplicitImplementation)
                continue;

            string methodName = reader.GetString(method.Name);
            if ((accessorMethods.Contains(methodHandle)
                    && !(isExplicitImplementation
                        && methodAccess == MethodAttributes.Private))
                || methodName.StartsWith('<'))
            {
                continue;
            }

            if (!isExplicitImplementation
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
            MethodAttributes bestAccess = 0;
            if (!accessors.Getter.IsNil)
            {
                bestAccess = reader.GetMethodDefinition(accessors.Getter).Attributes
                    & MethodAttributes.MemberAccessMask;
            }
            if (!accessors.Setter.IsNil)
            {
                var setterAccess = reader.GetMethodDefinition(accessors.Setter).Attributes
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
            if (accessors.Adder.IsNil)
                continue;

            var adder = reader.GetMethodDefinition(accessors.Adder);
            if ((adder.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                || AttributeReader.HasEditorBrowsableNeverAttribute(reader, evt.GetCustomAttributes()))
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

                if (!exportedType.IsForwarder)
                {
                    if (exportedType.Implementation.Kind
                        == HandleKind.AssemblyReference)
                    {
                        throw new MetadataRowRejectedException(
                            ApiSurfaceInspectionFailure
                                .TypeForwarderIdentityOperation,
                            MetadataTypeNameFailure.Malformed(
                                exportedTypeHandle,
                                ApiSurfaceInspectionFailure
                                    .UnmarkedAssemblyForwarderDetail));
                    }

                    continue;
                }

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
                                ApiSurfaceInspectionFailure
                                    .TypeForwarderIdentityOperation,
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
                    ApiSurfaceInspectionFailure.TypeForwarderRowOperation,
                    exportedTypeHandle,
                    MetadataTypeNameFailure.Malformed(exportedTypeHandle, ex.Message));
            }
        }
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

    /// <summary>
    /// Property getter/setter and event adder/remover bodies from
    /// <c>MethodSemantics</c>. Ordinary accessors are represented by their
    /// property or event row; raiser and Other semantic methods have no
    /// <see cref="ApiMember"/> token slots, so they stay methods.
    /// </summary>
    /// <remarks>
    /// <c>ApiSurfaceEmitSetTests</c> is the gate: ordinary <c>get_*</c> methods
    /// remain methods, ordinary semantic accessors do not, and a private
    /// MethodImpl accessor remains <c>explicit-interface-implementation</c>
    /// because its property or event row does not represent the public contract.
    /// A public MethodImpl accessor is represented by that public row.
    /// </remarks>
    private static HashSet<MethodDefinitionHandle> GetSemanticAccessorMethods(
        MetadataReader reader,
        TypeDefinition typeDef)
    {
        HashSet<MethodDefinitionHandle> accessors = [];
        foreach (PropertyDefinitionHandle propertyHandle in typeDef.GetProperties())
        {
            PropertyAccessors propertyAccessors =
                reader.GetPropertyDefinition(propertyHandle).GetAccessors();
            Add(propertyAccessors.Getter);
            Add(propertyAccessors.Setter);
        }

        foreach (EventDefinitionHandle eventHandle in typeDef.GetEvents())
        {
            EventAccessors eventAccessors =
                reader.GetEventDefinition(eventHandle).GetAccessors();
            Add(eventAccessors.Adder);
            Add(eventAccessors.Remover);
        }

        return accessors;

        void Add(MethodDefinitionHandle accessor)
        {
            if (!accessor.IsNil)
                accessors.Add(accessor);
        }
    }

    private static HashSet<MethodDefinitionHandle> GetExplicitImplementationBodies(
        MetadataReader reader, TypeDefinition typeDef)
    {
        HashSet<MethodDefinitionHandle> handles = [];
        foreach (var implementationHandle in typeDef.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody.Kind == HandleKind.MethodDefinition)
                handles.Add((MethodDefinitionHandle)implementation.MethodBody);
        }

        return handles;
    }

    /// <summary>
    /// The set of methods on <paramref name="typeDef"/> whose explicit
    /// <c>.override</c> MethodImpl targets <c>System.Object::Finalize</c> — the
    /// slot a C# <c>~Type()</c> destructor compiles to. Keying on the overridden
    /// declaration (not the method's own name/slot/signature) is what lets the
    /// C# writer spell <c>~Type()</c> for real finalizers while excluding a
    /// same-named override of an unrelated <c>Finalize</c> slot or an explicit
    /// interface implementation.
    /// </summary>
    private static HashSet<MethodDefinitionHandle> GetObjectFinalizeOverrides(
        MetadataReader reader,
        TypeDefinition typeDef,
        Action<int>? beforeDecodeWork = null)
    {
        HashSet<MethodDefinitionHandle> handles = [];
        foreach (var implementationHandle in typeDef.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody.Kind != HandleKind.MethodDefinition)
                continue;
            if (ReferencesObjectFinalize(
                    reader,
                    implementation.MethodDeclaration,
                    beforeDecodeWork))
                handles.Add((MethodDefinitionHandle)implementation.MethodBody);
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
        var method = reader.GetMethodDefinition(methodHandle);
        if (!string.Equals(reader.GetString(method.Name), "Finalize", StringComparison.Ordinal))
            return false;
        if (method.GetGenericParameters().Count != 0)
            return false;

        var typeHandle = method.GetDeclaringType();
        var typeDef = reader.GetTypeDefinition(typeHandle);
        foreach (var implementationHandle in typeDef.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody.Kind == HandleKind.MethodDefinition
                && (MethodDefinitionHandle)implementation.MethodBody == methodHandle
                && ReferencesObjectFinalize(reader, implementation.MethodDeclaration))
            {
                return true;
            }
        }

        // No MethodImpl: fall back to the implicit-slot shape the VB.NET compiler emits.
        return IsImplicitObjectFinalizeOverride(reader, typeHandle, method);
    }

    // A malformed or adversarial base-type chain can be arbitrarily long or cyclic; the visited-set
    // below stops in-assembly cycles, and this cap stops an unbounded walk through a long legitimate
    // (or degenerate) hierarchy. Real finalizer-bearing hierarchies are far shallower than this.
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
    /// <item>a base that leaves the assembly without resolving to <c>System.Object</c> — including a
    /// generic (<see cref="TypeSpecification"/>) base — cannot be proven and rejects conservatively,
    /// so no guessed <c>~Type()</c> is spelled for an unresolvable chain.</item>
    /// </list>
    /// </summary>
    private static bool IsImplicitObjectFinalizeOverride(
        MetadataReader reader,
        TypeDefinitionHandle typeDefHandle,
        MethodDefinition method,
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
        if (!HasVoidNullaryInstanceSignature(reader, method))
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

            switch (baseHandle.Kind)
            {
                case HandleKind.TypeReference:
                    // Reached a cross-assembly base: only System.Object roots the object.Finalize slot.
                    return IsSystemObjectType(
                        reader,
                        baseHandle,
                        beforeDecodeWork);
                case HandleKind.TypeDefinition:
                    var baseTypeHandle = (TypeDefinitionHandle)baseHandle;
                    if (!visited.Add(baseTypeHandle))
                        return false; // cyclic base chain in malformed metadata
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
                    continue;
                default:
                    // TypeSpecification (generic base) or any other shape: the slot root cannot be
                    // proven from the handle alone, so reject rather than guess.
                    return false;
            }
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
                && HasVoidNullaryInstanceSignature(reader, method))
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
    private static bool HasVoidNullaryInstanceSignature(MetadataReader reader, MethodDefinition method)
    {
        try
        {
            var blob = reader.GetBlobReader(method.Signature);
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
            return blob.ReadSignatureTypeCode() == SignatureTypeCode.Void;
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

    /// <summary>
    /// <c>.override</c> MethodImpl) names <c>Finalize</c> on <c>System.Object</c>.
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

    private static bool IsOperatorMethodName(string methodName) =>
        methodName.StartsWith("op_", StringComparison.Ordinal);

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
                MetadataTypeDefinitionName declaringTypeDefinitionName =
                    declaringType.DefinitionName
                    ?? throw new InvalidOperationException(
                        "An extension declaration must retain exact Type identity before projection.");
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
                    MemorySafety = extension.MemorySafety,
                    IsExtension = true,
                    ExtendedType = extension.ExtendedType,
                    DeclaringType = declaringType.FullName,
                    DeclaringTypeCanonicalName =
                        declaringTypeCanonicalName,
                    DeclaringTypeDefinitionName =
                        declaringTypeDefinitionName,
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
        int PropertyToken,
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
                        MetadataTokens.GetToken(propertyHandle),
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

    static ImmutableArray<ApiMemberMemorySafetyFacts> ReadAccessorMemorySafety(
        MetadataReader reader,
        MemorySafetyMetadataIndex index,
        Guid moduleVersionId,
        MethodDefinitionHandle[] handles)
        => [.. handles.Where(handle => !handle.IsNil).Distinct()
            .Select(handle => ApiMemorySafetyFacts.Read(
                reader, index, moduleVersionId, handle))];

    static Dictionary<int, ApiBackingStorageAssociation> ReadBackingStorageAssociations(
        MetadataReader reader,
        TypeDefinition type,
        GenericContext context,
        Guid moduleVersionId,
        Dictionary<string, AutoPropertyBackingField>? properties,
        HashSet<string>? eventNames,
        Action<string>? beforeRetainText,
        Action<int>? beforeDecodeWork)
    {
        var results = new Dictionary<int, ApiBackingStorageAssociation>();
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        var ambiguousPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propertyHandle in type.GetProperties())
        {
            string name = DecodeString(
                reader, reader.GetPropertyDefinition(propertyHandle).Name, beforeDecodeWork);
            if (!propertyNames.Add(name))
                ambiguousPropertyNames.Add(name);
            results.Add(
                MetadataTokens.GetToken(propertyHandle),
                Unknown(ApiBackingStorageConvention.AutoProperty));
        }
        var events = new Dictionary<string, EventDefinitionHandle>(StringComparer.Ordinal);
        foreach (var eventHandle in type.GetEvents())
        {
            var @event = reader.GetEventDefinition(eventHandle);
            string name = DecodeString(reader, @event.Name, beforeDecodeWork);
            if (!events.TryAdd(name, eventHandle))
                events[name] = default;
            results.Add(
                MetadataTokens.GetToken(eventHandle),
                Unknown(ApiBackingStorageConvention.FieldLikeEvent));
        }
        if (results.Count == 0)
            return results;

        var fields = new Dictionary<string, List<FieldDefinitionHandle>>(StringComparer.Ordinal);
        foreach (var fieldHandle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            string name = DecodeString(reader, field.Name, beforeDecodeWork);
            if (!fields.TryGetValue(name, out var sameNamedFields))
                fields.Add(name, sameNamedFields = []);
            sameNamedFields.Add(fieldHandle);
        }

        if (properties is not null)
        {
            foreach (var (name, descriptor) in properties)
            {
                if (ambiguousPropertyNames.Contains(descriptor.PropertyName))
                    continue;
                results[descriptor.PropertyToken] = Match(
                    name,
                    ApiBackingStorageConvention.AutoProperty,
                    field =>
                    {
                        if (((field.Attributes & FieldAttributes.Static) != 0) != descriptor.IsStatic
                            || !AttributeReader.HasAttribute(
                                reader, field.GetCustomAttributes(),
                                KnownAttributeNames.CompilerGeneratedAttribute, beforeDecodeWork))
                        {
                            return false;
                        }
                        return MatchBackingType(
                            field, MetadataTokens.EntityHandle(descriptor.PropertyToken));
                    });
            }
        }
        foreach (var (name, eventHandle) in events)
        {
            if (eventHandle.IsNil || eventNames?.Contains(name) != true)
                continue;
            var @event = reader.GetEventDefinition(eventHandle);
            var adder = reader.GetMethodDefinition(@event.GetAccessors().Adder);
            bool isStatic = (adder.Attributes & MethodAttributes.Static) != 0;
            results[MetadataTokens.GetToken(eventHandle)] = Match(
                name,
                ApiBackingStorageConvention.FieldLikeEvent,
                field =>
                {
                    if (((field.Attributes & FieldAttributes.Static) != 0) != isStatic
                        || !IsFieldLikeEventBackingField(
                            reader, field, name, eventNames, beforeDecodeWork))
                    {
                        return false;
                    }
                    return MatchBackingType(field, eventHandle);
                });
        }
        return results;

        ApiBackingStorageAssociation Unknown(ApiBackingStorageConvention convention) =>
            new(moduleVersionId, convention, ApiBackingStorageState.Unknown, []);

        bool? MatchBackingType(FieldDefinition field, EntityHandle declaration)
        {
            TypeNode node = GuardedProviderDecode.Field(
                reader, field,
                new TypeNodeProvider(beforeRetainText, beforeDecodeWork),
                context, (TypeNode)new DegradedTypeNode());
            if (node.IsDegraded)
                return null;

            // Exact encoding is sufficient within this module, including token scope,
            // generic positions and shape. Alternate encodings remain unproven.
            BlobReader fieldType = reader.GetBlobReader(field.Signature);
            beforeDecodeWork?.Invoke(fieldType.Length);
            if (fieldType.ReadSignatureHeader().Kind != SignatureKind.Field)
                return null;

            BlobReader declaredType;
            if (declaration.Kind == HandleKind.PropertyDefinition)
            {
                declaredType = reader.GetBlobReader(
                    reader.GetPropertyDefinition((PropertyDefinitionHandle)declaration).Signature);
                beforeDecodeWork?.Invoke(declaredType.Length);
                SignatureHeader header = declaredType.ReadSignatureHeader();
                if (header.Kind != SignatureKind.Property || header.IsGeneric
                    || declaredType.ReadCompressedInteger() != 0)
                {
                    return null;
                }
            }
            else
            {
                EntityHandle eventType = reader.GetEventDefinition(
                    (EventDefinitionHandle)declaration).Type;
                if (eventType.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference)
                {
                    return fieldType.ReadSignatureTypeCode() == SignatureTypeCode.TypeHandle
                        && fieldType.ReadTypeHandle() == eventType
                        && fieldType.RemainingBytes == 0;
                }
                if (eventType.Kind != HandleKind.TypeSpecification)
                    return null;
                declaredType = reader.GetBlobReader(
                    reader.GetTypeSpecification((TypeSpecificationHandle)eventType).Signature);
                beforeDecodeWork?.Invoke(declaredType.Length);
            }

            if (fieldType.RemainingBytes != declaredType.RemainingBytes)
                return false;
            while (fieldType.RemainingBytes > 0)
            {
                if (fieldType.ReadByte() != declaredType.ReadByte())
                    return false;
            }
            return true;
        }

        ApiBackingStorageAssociation Match(
            string name,
            ApiBackingStorageConvention convention,
            Func<FieldDefinition, bool?> matches)
        {
            if (!fields.TryGetValue(name, out var candidates))
                return Unknown(convention);
            var evidence = ImmutableArray.CreateBuilder<ApiBackingFieldEvidence>();
            bool incomplete = false;
            foreach (var candidate in candidates)
            {
                var field = reader.GetFieldDefinition(candidate);
                bool? match;
                try
                {
                    match = matches(field);
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentException
                        or InvalidOperationException)
                {
                    match = null;
                }
                incomplete |= match is null;
                if (match == true)
                {
                    evidence.Add(new(
                        MetadataTokens.GetToken(candidate),
                        name,
                        (field.Attributes & FieldAttributes.Static) != 0));
                }
            }
            return new(
                moduleVersionId,
                convention,
                evidence.Count > 1
                    ? ApiBackingStorageState.Ambiguous
                    : evidence.Count == 1 && !incomplete
                        ? ApiBackingStorageState.Associated
                        : ApiBackingStorageState.Unknown,
                evidence.ToImmutable());
        }
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
        var parameterInfos = Enumerable.Range(1, paramTypes.Length)
            .Select(sequenceNumber => GetParameterInfo(
                reader,
                paramHandles,
                sequenceNumber,
                beforeRetainText,
                attributeMaterialize))
            .ToArray();
        string[] parameterNames = CSharpParameterNames.Allocate(
            parameterInfos.Select(info => info.name).ToArray(),
            context.MethodParameters);
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
            var (_, isParams, refKind, hasDefault, defaultValue, attributes) =
                parameterInfos[i];
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
                parameterNames[i],
                modifier,
                hasDefault,
                defaultValue,
                defaultValueText);

            beforeRetainText?.Invoke(paramStr);
            var parameterModel = new ApiParameter
            {
                Attributes = attributes,
                Name = parameterNames[i],
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
        Action<int>? beforeDecodeWork = null,
        Action<TypeNode>? captureTypeNode = null)
    {
        if (beforeDecodeWork is not null || captureTypeNode is not null)
        {
            var provider =
                new TypeNodeProvider(beforeMaterialize: beforeDecodeWork);
            TypeNode? typeNode = handle.Kind switch
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
            if (typeNode is not null)
                captureTypeNode?.Invoke(typeNode);
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
        PropertyAccessors accessors,
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
        bool hasGetter = !accessors.Getter.IsNil;
        bool hasSetter = !accessors.Setter.IsNil;

        if (hasGetter)
        {
            var getter = reader.GetMethodDefinition(accessors.Getter);
            getterAccess = getter.Attributes & MethodAttributes.MemberAccessMask;
        }

        if (hasSetter)
        {
            var setter = reader.GetMethodDefinition(accessors.Setter);
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
                        reader.GetMethodDefinition(accessors.Getter).GetParameters(),
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
                        reader.GetMethodDefinition(accessors.Setter).GetParameters(),
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
                        reader.GetMethodDefinition(accessors.Getter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Setter).GetParameters(),
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
                        reader.GetMethodDefinition(accessors.Getter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    Accessibility = "private",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Setter).GetParameters(),
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
                        reader.GetMethodDefinition(accessors.Getter).GetParameters(),
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
                        reader.GetMethodDefinition(accessors.Setter).GetParameters(),
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
                "get" => accessors.Getter,
                "set" => accessors.Setter,
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
            ? accessors.Getter
            : accessors.Setter;
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
        var parameterInfos = Enumerable.Range(1, paramTypes.Length)
            .Select(sequenceNumber => GetParameterInfo(
                reader,
                paramHandles,
                sequenceNumber,
                beforeRetainText,
                attributeMaterialize))
            .ToArray();
        string[] parameterNames = CSharpParameterNames.Allocate(
            parameterInfos.Select(info => info.name).ToArray());
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
            var (_, isParams, refKind, hasDefault, defaultValue, attributes) =
                parameterInfos[i];
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
                parameterNames[i],
                modifier,
                hasDefault,
                defaultValue,
                defaultValueText);
            beforeRetainText?.Invoke(parameter);
            var parameterModel = new ApiParameter
            {
                Attributes = attributes,
                Name = parameterNames[i],
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

        for (int i = 0; i < signature.Length; i++)
        {
            if (signature[i] == '*'
                && (i == 0
                    || i == signature.Length - 1
                    || signature[i - 1] != '['
                    || signature[i + 1] != ']'))
            {
                return true;
            }
        }

        return false;
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
        if (type.MemorySafety is { } memorySafety)
        {
            foreach (var observation in memorySafety.Rules.Observations)
                AddText(ref count, observation.Detail);
            if (memorySafety.Rules is MemorySafetyRulesResult.Unavailable unavailable)
                AddText(ref count, unavailable.Failure.Detail);
        }
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
        AddText(ref count, member.DeclaringTypeDefinitionName);
        AddText(ref count, member.EnumValueLiteral);
        AddText(ref count, member.JsonPropertyName);
        AddText(ref count, member.GetterAccessibility);
        AddText(ref count, member.SetterAccessibility);
        AddMemorySafetyText(ref count, member.MemorySafety);
        if (member.AccessorMemorySafety is { } accessors)
        {
            foreach (var accessor in accessors)
                AddMemorySafetyText(ref count, accessor);
        }
        if (member.BackingStorage is { } backing)
        {
            foreach (var candidate in backing.Candidates)
                AddText(ref count, candidate.MatchedName);
        }
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

    static void AddMemorySafetyText(
        ref long count,
        ApiMemberMemorySafetyFacts? facts)
    {
        if (facts?.CallerContract is MemorySafetyMemberContractResult.Unavailable unavailable)
            AddText(ref count, unavailable.Failure.Detail);
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
    {
        readonly MetadataReader _reader;
        readonly List<Group> _groups = [];

        internal TypeParameterConstraintResolution(
            MetadataReader reader,
            ResolvedAssemblyReference source,
            int maxTypeResolutionRequests)
        {
            _reader = reader;
            Plan = new TypeParameterKindClassifier.ResolutionPlan(
                reader,
                source,
                maxTypeResolutionRequests);
        }

        internal TypeParameterKindClassifier.ResolutionPlan Plan { get; }

        internal IReadOnlyCollection<TypeResolutionRequest> Requests =>
            Plan.Requests;

        internal Checkpoint CreateCheckpoint() =>
            new(_groups.Count, Plan.Checkpoint());

        internal void Rollback(Checkpoint checkpoint)
        {
            if (checkpoint.GroupCount < 0
                || checkpoint.GroupCount > _groups.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(checkpoint));
            }

            _groups.RemoveRange(
                checkpoint.GroupCount,
                _groups.Count - checkpoint.GroupCount);
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

        internal void Apply(TypeResolutionContext context)
        {
            Plan.Bind(context);
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
            TypeParameterKindClassifier.ResolutionPlan.RequestCheckpoint
                RequestCheckpoint);

        readonly record struct Group(
            EntityHandle Subject,
            List<(GenericParameterHandle Handle, TypeParameter Parameter)>
                Parameters);
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
