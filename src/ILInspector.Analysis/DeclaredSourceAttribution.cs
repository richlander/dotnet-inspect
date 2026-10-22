using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// The primary-image services that authenticate which declared source method a
/// compiler-generated body belongs to: the declared-source, async state-machine,
/// and lifted-owner resolvers, with the metadata resolvers and classifiers they
/// read. It is composed from the subject's readers alone, so Method Query
/// Source can attribute generated bodies without a Library Body Analysis
/// builder, and the builder reuses the same composition.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/method-query-source.md#declared-expansion</c>. The
/// authentication semantics stay with the resolvers it composes.
/// </remarks>
internal sealed class DeclaredSourceAttribution
{
    readonly MetadataReader _reader;
    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle>? _localTypeDefinitions;

    internal DeclaredSourceAttribution(
        string path,
        MetadataReader reader,
        PEReader peReader,
        Action<MethodDefinitionHandle>? methodBodyReferenceIndexed = null,
        Action<MethodDefinitionHandle>? stableReceiverGetterClassified = null,
        Action<MethodDefinitionHandle, int>? methodReferenceResolved = null,
        Action<TypeDefinitionHandle>? sourceGeneratedTypeClassified = null,
        Action? typeDefinitionIndexBuilt = null,
        Action? stateMachineExecutionMethodsBuilt = null,
        Action? targetedStateMachineClaimsBuilt = null,
        Action? liftedDeclaringTypeChainWalked = null,
        Action? asyncStateMachineTypesBuilt = null,
        ImplementationMetricWorkBudget? implementationMetricWork = null,
        ImplementationMetricExecutionRecorder?
            implementationMetricRecorder = null,
        MethodDefinitionGeneratedExpansionWork?
            generatedExpansionWork = null,
        MethodAnalysisLookup? methodAnalysis = null,
        Action<TypeDefinitionHandle>?
            declaredSourceTypeDefinitionRead = null,
        Action? declaredSourceAttributeRead = null,
        Action<TypeDefinitionHandle>?
            declaredSourceRelationshipRead = null)
    {
        _reader = reader;
        MethodAnalysis =
            methodAnalysis
            ?? new(
                path,
                reader,
                peReader,
                stableReceiverGetterClassified,
                methodReferenceResolved,
                asyncStateMachineTypesBuilt);
        AssemblyName = MethodAnalysis.AssemblyName;
        Mvid = MethodAnalysis.Mvid;
        AssemblyIdentity = reader.IsAssembly
            ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
            : new AssemblyReferenceIdentity(
                AssemblyName,
                null,
                null,
                null);
        GeneratedExpansionWork = generatedExpansionWork;
        GeneratedProvenanceClassifier =
            new LibraryBodyGeneratedProvenanceClassifier(
                reader,
                PrimaryMetadataResolver.HasGeneratedCodeAttribute,
                sourceGeneratedTypeClassified,
                declaredSourceTypeDefinitionRead,
                declaredSourceAttributeRead,
                declaredSourceRelationshipRead);
        Func<TypeDefinitionHandle, bool>
            isCompilerGeneratedTypeOrEnclosing =
                handle => PrimaryMetadataResolver
                    .IsCompilerGeneratedTypeOrEnclosing(
                        handle,
                        declaredSourceTypeDefinitionRead,
                        declaredSourceAttributeRead,
                        declaredSourceRelationshipRead);
        StateMachineSourceResolver =
            new StateMachineSourceResolver(
                reader,
                AssemblyIdentity,
                PrimaryMetadataResolver,
                GeneratedProvenanceClassifier
                    .IsSourceGeneratedTypeOrEnclosing,
                isCompilerGeneratedTypeOrEnclosing,
                TypeFromEntity,
                // The state-machine relationship index owns the TypeDef name
                // index that claim resolution uses.
                relationshipIndexBuilt: typeDefinitionIndexBuilt,
                stateMachineExecutionMethodsBuilt,
                targetedStateMachineClaimsBuilt);
        LiftedSourceOwnerResolver =
            new LiftedSourceOwnerResolver(
                reader,
                peReader,
                PrimaryMetadataResolver,
                isCompilerGeneratedTypeOrEnclosing,
                MethodReferenceResolver,
                StateMachineSourceResolver,
                methodBodyReferenceIndexed,
                implementationMetricWork,
                implementationMetricRecorder,
                generatedExpansionWork,
                liftedDeclaringTypeChainWalked);
        DeclaredSourceResolver =
            new DeclaredSourceResolver(
                reader,
                PrimaryMetadataResolver,
                LiftedSourceOwnerResolver,
                StateMachineSourceResolver);
        ImplementationMetricWork = implementationMetricWork;
    }

    internal string AssemblyName { get; }

    internal Guid Mvid { get; }

    internal AssemblyReferenceIdentity AssemblyIdentity { get; }

    internal MethodDefinitionGeneratedExpansionWork? GeneratedExpansionWork
    { get; }

    internal MethodAnalysisLookup MethodAnalysis { get; }

    internal LibraryBodyMethodReferenceResolver MethodReferenceResolver
        => MethodAnalysis.MethodReferenceResolver;

    internal LibraryBodyGenericConstraintClassifier GenericConstraintClassifier
        => MethodAnalysis.GenericConstraintClassifier;

    internal LibraryBodyStableReceiverGetterClassifier
        StableReceiverGetterClassifier
        => MethodAnalysis.StableReceiverGetterClassifier;

    internal LibraryBodyPrimaryMetadataResolver PrimaryMetadataResolver
        => MethodAnalysis.PrimaryMetadataResolver;

    internal LibraryBodyGeneratedProvenanceClassifier
        GeneratedProvenanceClassifier
    { get; }

    internal StateMachineSourceResolver StateMachineSourceResolver { get; }

    internal LiftedSourceOwnerResolver LiftedSourceOwnerResolver { get; }

    internal DeclaredSourceResolver DeclaredSourceResolver { get; }

    internal ImplementationMetricWorkBudget? ImplementationMetricWork { get; }

    /// <summary>
    /// Adds the authenticated generated bodies of
    /// <paramref name="directMethods"/> in the requested
    /// <paramref name="roles"/> under the configured expansion work, and
    /// classifies the methods of compiler-generated types nested under
    /// <paramref name="reachFromTypes"/>.
    /// </summary>
    internal MethodDefinitionGeneratedExpansionResult
        ExpandGeneratedExecutionBodies(
            System.Collections.Immutable.ImmutableArray<MethodDefinitionHandle>
                directMethods,
            MethodDefinitionGeneratedBodyRoles roles =
                MethodDefinitionGeneratedBodyRoles.Execution,
            System.Collections.Immutable.ImmutableArray<TypeDefinitionHandle>
                reachFromTypes = default,
            bool includeUnauthenticatedCandidates = false)
    {
        if (GeneratedExpansionWork is null)
        {
            throw new InvalidOperationException(
                "Generated expansion work was not configured.");
        }

        return DeclaredSourceResolver.ExpandGeneratedExecutionBodies(
            directMethods,
            GeneratedExpansionWork,
            roles,
            reachFromTypes,
            includeUnauthenticatedCandidates);
    }

    /// <summary>
    /// Records a traversed body's call and function-pointer operands for the
    /// all-definition relation, which settles at source completion.
    /// </summary>
    internal void RecordTraversedBodyReferences(
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        MethodBodyBlock? body,
        bool acquiredByRelation)
    {
        if (GeneratedExpansionWork is null)
        {
            throw new InvalidOperationException(
                "Generated expansion work was not configured.");
        }

        if (body is not null)
        {
            GeneratedExpansionWork.RecordTraversalReferenceBody(
                body.GetILReader().Length,
                acquiredByRelation);
        }
        LiftedSourceOwnerResolver.RecordTraversedBodyReferences(
            methodHandle,
            method,
            body);
    }

    /// <summary>
    /// The module's TypeDefs by full metadata name, built once; a name defined
    /// more than once maps to a nil handle.
    /// </summary>
    internal IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle> LocalTypeDefinitions()
    {
        if (_localTypeDefinitions is not null)
            return _localTypeDefinitions;

        var definitions = new Dictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle>();
        foreach (TypeDefinitionHandle handle in _reader.TypeDefinitions)
        {
            TypeRef type = TypeRefDecoder.Instance.GetTypeFromDefinition(
                _reader,
                handle,
                0);
            if (type.Resolution?.Type is { } name)
            {
                if (!definitions.TryAdd(name, handle))
                    definitions[name] = default;
            }
        }
        _localTypeDefinitions = definitions;
        return definitions;
    }

    internal TypeRef TypeFromEntity(EntityHandle handle)
    {
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition =>
                    TypeRefDecoder.Instance.GetTypeFromDefinition(
                        _reader,
                        (TypeDefinitionHandle)handle,
                        0),
                HandleKind.TypeReference =>
                    TypeRefDecoder.Instance.GetTypeFromReference(
                        _reader,
                        (TypeReferenceHandle)handle,
                        0),
                HandleKind.TypeSpecification =>
                    TypeRefDecoder.Instance.GetTypeFromSpecification(
                        _reader,
                        new GenericScope([], []),
                        (TypeSpecificationHandle)handle,
                        0),
                _ => TypeRef.Unsupported("interface implementation"),
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException)
        {
            return TypeRef.Unsupported("interface implementation");
        }
    }
}
