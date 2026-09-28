using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

internal static class MetadataLibrarySignatureUseInspection
{
    internal static MetadataLibrarySignatureUseOutcome Execute(
        PEReader image,
        MetadataLibrarySignatureUseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        MetadataImageFormatResult format =
            MetadataImageFormatClassifier.Classify(image);
        if (format is not MetadataImageFormatResult.SupportedEcma335)
        {
            return new MetadataLibrarySignatureUseOutcome.Rejected(
                MetadataLibrarySignatureUseRejectionKind.UnsupportedImage,
                format switch
                {
                    MetadataImageFormatResult.NoMetadata =>
                        "The selected image contains no managed metadata.",
                    MetadataImageFormatResult.UnsupportedWindowsMetadata =>
                        "Windows Metadata is not a supported signature-use input.",
                    MetadataImageFormatResult.MalformedRoot =>
                        "The selected image has a malformed metadata root.",
                    _ => "The selected image format is unavailable.",
                },
                format);
        }

        MetadataReader reader;
        try
        {
            reader = image.GetMetadataReader(MetadataReaderOptions.None);
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or OverflowException)
        {
            return new MetadataLibrarySignatureUseOutcome.Rejected(
                MetadataLibrarySignatureUseRejectionKind.MalformedImage,
                exception.Message,
                new MetadataImageFormatResult.MalformedRoot(
                    MetadataRootMalformedReason
                        .UnmappableMetadataDirectory));
        }

        AssemblyReferenceIdentity assembly;
        Guid moduleVersionId;
        try
        {
            if (!reader.IsAssembly)
            {
                return new MetadataLibrarySignatureUseOutcome.Rejected(
                    MetadataLibrarySignatureUseRejectionKind
                        .MissingAssemblyIdentity,
                    "A Library signature-use population requires an assembly manifest.");
            }

            assembly =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            moduleVersionId =
                reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (moduleVersionId == Guid.Empty)
            {
                return new MetadataLibrarySignatureUseOutcome.Rejected(
                    MetadataLibrarySignatureUseRejectionKind
                        .MissingAssemblyIdentity,
                    "The metadata image has an empty module version identifier.");
            }
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or OverflowException)
        {
            return new MetadataLibrarySignatureUseOutcome.Rejected(
                MetadataLibrarySignatureUseRejectionKind.MalformedImage,
                exception.Message);
        }

        using var operation =
            new MetadataOperationContext(request.Policy);
        MetadataImageAdmissionResult admission =
            operation.AdmitImage(reader);
        if (admission is MetadataImageAdmissionResult.Rejected rejected)
        {
            return new MetadataLibrarySignatureUseOutcome.Rejected(
                MetadataLibrarySignatureUseRejectionKind.Limit,
                "The metadata image exceeds the operation row budget.",
                Counters: rejected.Failure.Counters);
        }

        AssemblyTypeDeclarationInventoryOutcome inventoryOutcome =
            AssemblyTypeDeclarationInventoryReader.Read(
                image,
                MaximumInt(request.Policy.MaxStructuredNodes),
                MaximumInt(request.Policy.MaxRetainedText));
        if (inventoryOutcome
            is AssemblyTypeDeclarationInventoryOutcome.Incomplete incomplete)
        {
            return new MetadataLibrarySignatureUseOutcome.Rejected(
                MetadataLibrarySignatureUseRejectionKind.Limit,
                $"The Type inventory exceeded {incomplete.Bound}.",
                Counters: operation.Counters);
        }
        if (inventoryOutcome
            is AssemblyTypeDeclarationInventoryOutcome.Rejected
                inventoryRejected)
        {
            return new MetadataLibrarySignatureUseOutcome.Rejected(
                MetadataLibrarySignatureUseRejectionKind.TypeInventory,
                inventoryRejected.Failure.Detail,
                Counters: operation.Counters);
        }

        var inventory =
            ((AssemblyTypeDeclarationInventoryOutcome.Read)inventoryOutcome)
                .Inventory;
        try
        {
            operation.Charge(
                MetadataOperationDimension.StructuredNodes,
                inventory.Definitions.Length);
            operation.Charge(
                MetadataOperationDimension.RetainedText,
                inventory.RetainedTextCharacters);
            return new Operation(
                image,
                reader,
                assembly,
                moduleVersionId,
                inventory,
                operation,
                cancellationToken).Execute();
        }
        catch (MetadataOperationBudgetExceededException exception)
        {
            return new MetadataLibrarySignatureUseOutcome.Rejected(
                MetadataLibrarySignatureUseRejectionKind.Limit,
                $"The Type inventory exceeded {exception.Dimension}.",
                Counters: exception.Counters);
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return new MetadataLibrarySignatureUseOutcome.Rejected(
                MetadataLibrarySignatureUseRejectionKind.MalformedImage,
                exception.Message,
                Counters: operation.Counters);
        }
    }

    private static int MaximumInt(long value) =>
        value >= int.MaxValue ? int.MaxValue : checked((int)value);

    private sealed class Operation
    {
        private readonly PEReader _image;
        private readonly MetadataReader _reader;
        private readonly AssemblyReferenceIdentity _assembly;
        private readonly Guid _moduleVersionId;
        private readonly AssemblyTypeDeclarationInventory _inventory;
        private readonly MetadataOperationContext _operation;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<
            MetadataTypeDefinitionName,
            TypeEntry> _typesByName = [];
        private readonly TypeEntry?[] _typesByRow;
        private readonly MetadataLibraryTypeClassification[]
            _inheritanceClassification;
        private readonly bool[] _inheritanceSettled;
        private readonly bool[] _baseSiteExamined;
        private readonly int[] _inheritanceVisitGeneration;
        private readonly List<TypeDefinitionHandle> _inheritancePath = [];
        private readonly List<TypeEntry> _types = [];
        private readonly ImmutableArray<
            MetadataLibrarySignatureUseOccurrence>.Builder _occurrences =
                ImmutableArray.CreateBuilder<
                    MetadataLibrarySignatureUseOccurrence>();
        private readonly ImmutableArray<
            MetadataLibrarySignatureUseDiagnostic>.Builder _diagnostics =
                ImmutableArray.CreateBuilder<
                    MetadataLibrarySignatureUseDiagnostic>();
        private int _considered;
        private int _examined;
        private int _unavailable;
        private int _siteLimited;
        private bool _limited;
        private int _currentInheritanceGeneration;

        internal Operation(
            PEReader image,
            MetadataReader reader,
            AssemblyReferenceIdentity assembly,
            Guid moduleVersionId,
            AssemblyTypeDeclarationInventory inventory,
            MetadataOperationContext operation,
            CancellationToken cancellationToken)
        {
            _image = image;
            _reader = reader;
            _assembly = assembly;
            _moduleVersionId = moduleVersionId;
            _inventory = inventory;
            _operation = operation;
            _cancellationToken = cancellationToken;
            int typeRowCapacity =
                checked(reader.TypeDefinitions.Count + 1);
            _typesByRow = new TypeEntry?[typeRowCapacity];
            _inheritanceClassification =
                new MetadataLibraryTypeClassification[typeRowCapacity];
            _inheritanceSettled = new bool[typeRowCapacity];
            _baseSiteExamined = new bool[typeRowCapacity];
            _inheritanceVisitGeneration = new int[typeRowCapacity];
        }

        internal MetadataLibrarySignatureUseOutcome Execute()
        {
            BuildTypeInventory();
            ScanSites();
            ClassifyInheritance();
            int considered = _limited
                ? CountSites()
                : _considered;
            int limited = _limited
                ? considered - _examined - _unavailable
                : _siteLimited;
            if (!_limited
                && _examined + _unavailable + _siteLimited
                    != considered)
            {
                throw new InvalidOperationException(
                    "Library signature-use coverage did not settle every site.");
            }

            ImmutableArray<MetadataLibrarySignatureType> types =
                [.. _types.Select(
                    static entry =>
                        new MetadataLibrarySignatureType(
                            entry.Address,
                            entry.Name,
                            entry.DefinitionKind,
                            entry.Classification))];
            MetadataLibrarySignatureUseDisposition disposition =
                _diagnostics.Count == 0
                    ? MetadataLibrarySignatureUseDisposition.Complete
                    : MetadataLibrarySignatureUseDisposition.Partial;
            return new MetadataLibrarySignatureUseOutcome.Available(
                new(
                    new(
                        _moduleVersionId,
                        _assembly,
                        _operation.Counters),
                    disposition,
                    types,
                    _occurrences.ToImmutable(),
                    new(
                        considered,
                        _examined,
                        _unavailable,
                        limited),
                    _diagnostics.ToImmutable()));
        }

        private void BuildTypeInventory()
        {
            bool isCoreLibrary =
                CoreLibraryRootAuthentication
                    .DeclaresUniqueTopLevelCoreLibraryRoot(_reader);
            foreach (AssemblyTypeDeclaration declaration
                in _inventory.Declarations)
            {
                if (declaration.Kind
                        != AssemblyTypeDeclarationKind.Definition
                    || declaration.DefinitionToken is not { } token
                    || declaration.DefinitionKind is not { } definitionKind)
                {
                    continue;
                }

                EntityHandle entity =
                    MetadataTokens.EntityHandle(token.Value);
                if (entity.Kind != HandleKind.TypeDefinition)
                {
                    throw new BadImageFormatException(
                        "A Type inventory definition token is not a TypeDef.");
                }

                TypeDefinitionHandle handle =
                    (TypeDefinitionHandle)entity;
                var entry = new TypeEntry(
                    handle,
                    MetadataTypeDefinitionAddress.FromHandle(
                        _reader,
                        handle),
                    declaration.Name,
                    definitionKind,
                    SafeInitialClassification(
                        handle,
                        declaration.Name,
                        isCoreLibrary));
                if (!_typesByName.TryAdd(entry.Name, entry))
                {
                    throw new BadImageFormatException(
                        "More than one TypeDef has the same exact metadata name.");
                }
                int row = TypeRow(entry.Handle);
                if (_typesByRow[row] is not null)
                {
                    throw new BadImageFormatException(
                        "More than one Type inventory entry has the same TypeDef.");
                }
                _typesByRow[row] = entry;
                _types.Add(entry);
            }
        }

        private void ClassifyInheritance()
        {
            foreach (TypeEntry entry in _types)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                entry.Classification |=
                    SafeInheritanceClassification(entry);
            }
        }

        private MetadataLibraryTypeClassification
            SafeInitialClassification(
                TypeDefinitionHandle handle,
                MetadataTypeDefinitionName name,
                bool isCoreLibrary)
        {
            try
            {
                return InitialClassification(
                    handle,
                    name,
                    isCoreLibrary);
            }
            catch (Exception exception)
                when (IsMalformedClassificationEvidence(exception))
            {
                return MetadataLibraryTypeClassification.None;
            }
        }

        private MetadataLibraryTypeClassification
            SafeInheritanceClassification(TypeEntry entry)
        {
            try
            {
                return InheritanceClassification(entry);
            }
            catch (Exception exception)
                when (IsMalformedClassificationEvidence(exception))
            {
                return MetadataLibraryTypeClassification.None;
            }
        }

        private static bool IsMalformedClassificationEvidence(
            Exception exception) =>
            exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException
                or OverflowException;

        private MetadataLibraryTypeClassification InheritanceClassification(
            TypeEntry entry)
        {
            const MetadataLibraryTypeClassification inheritedFlags =
                MetadataLibraryTypeClassification.Attribute
                | MetadataLibraryTypeClassification.Exception;
            _inheritancePath.Clear();
            int visitGeneration =
                checked(++_currentInheritanceGeneration);
            TypeDefinitionHandle current = entry.Handle;
            MetadataLibraryTypeClassification result;
            while (true)
            {
                int row = TypeRow(current);
                if (_inheritanceSettled[row])
                {
                    result = _inheritanceClassification[row];
                    break;
                }
                if (_inheritanceVisitGeneration[row] == visitGeneration)
                {
                    result = MetadataLibraryTypeClassification.None;
                    break;
                }
                _inheritanceVisitGeneration[row] = visitGeneration;
                _inheritancePath.Add(current);
                if (_typesByRow[row] is { } currentEntry)
                {
                    result =
                        currentEntry.Classification & inheritedFlags;
                    if (result != MetadataLibraryTypeClassification.None)
                        break;
                }

                if (!_baseSiteExamined[row])
                {
                    result = MetadataLibraryTypeClassification.None;
                    break;
                }

                EntityHandle baseType =
                    _reader.GetTypeDefinition(current).BaseType;
                if (baseType.IsNil)
                {
                    result = MetadataLibraryTypeClassification.None;
                    break;
                }

                if (baseType.Kind == HandleKind.TypeDefinition)
                {
                    current = (TypeDefinitionHandle)baseType;
                    continue;
                }

                if (baseType.Kind == HandleKind.TypeSpecification)
                {
                    TypeSpecificationRootReadResult rootResult =
                        TypeSpecificationRoot.Read(
                            _reader,
                            (TypeSpecificationHandle)baseType);
                    if (rootResult
                            is not TypeSpecificationRootReadResult.Read
                                rootRead
                        || rootRead.Root is not
                            {
                                Kind:
                                    TypeSpecificationRootKind.NamedType,
                                RawTypeKind: 0x12,
                            } root)
                    {
                        result =
                            MetadataLibraryTypeClassification.None;
                        break;
                    }

                    if (root.Type.Kind
                        == HandleKind.TypeDefinition)
                    {
                        TypeDefinitionHandle localHandle =
                            (TypeDefinitionHandle)root.Type;
                        if (!HasGenericArity(
                                localHandle,
                                root.GenericArgumentCount))
                        {
                            result =
                                MetadataLibraryTypeClassification.None;
                            break;
                        }
                        current = localHandle;
                        continue;
                    }
                    if (root.Type.Kind
                        != HandleKind.TypeReference)
                    {
                        result =
                            MetadataLibraryTypeClassification.None;
                        break;
                    }

                    TypeReference specificationReference =
                        _reader.GetTypeReference(
                            (TypeReferenceHandle)root.Type);
                    MetadataTypeDefinitionName specificationName =
                        ReadName((TypeReferenceHandle)root.Type);
                    if (IsCurrentImage(
                            (TypeReferenceHandle)root.Type)
                        && _typesByName.TryGetValue(
                            specificationName,
                            out TypeEntry? specificationEntry))
                    {
                        if (!HasGenericArity(
                                specificationEntry.Handle,
                                root.GenericArgumentCount))
                        {
                            result =
                                MetadataLibraryTypeClassification.None;
                            break;
                        }
                        current = specificationEntry.Handle;
                        continue;
                    }

                    result =
                        root.GenericArgumentCount == 0
                        && ApiSurfaceExtractor
                            .ResolvesThroughCoreLibrary(
                                _reader,
                                specificationReference.ResolutionScope)
                            ? CoreBaseClassification(specificationName)
                            : MetadataLibraryTypeClassification.None;
                    break;
                }

                if (baseType.Kind != HandleKind.TypeReference)
                {
                    result = MetadataLibraryTypeClassification.None;
                    break;
                }

                TypeReference reference =
                    _reader.GetTypeReference(
                        (TypeReferenceHandle)baseType);
                MetadataTypeDefinitionName name =
                    ReadName((TypeReferenceHandle)baseType);
                if (IsCurrentImage(
                        (TypeReferenceHandle)baseType)
                    && _typesByName.TryGetValue(
                        name,
                        out TypeEntry? localEntry))
                {
                    current = localEntry.Handle;
                    continue;
                }

                result = ApiSurfaceExtractor.ResolvesThroughCoreLibrary(
                        _reader,
                        reference.ResolutionScope)
                    ? CoreBaseClassification(name)
                    : MetadataLibraryTypeClassification.None;
                break;
            }

            foreach (TypeDefinitionHandle handle in _inheritancePath)
            {
                int row = TypeRow(handle);
                _inheritanceClassification[row] = result;
                _inheritanceSettled[row] = true;
            }
            return result;
        }

        private bool HasGenericArity(
            TypeDefinitionHandle handle,
            int expected) =>
            _reader.GetTypeDefinition(handle)
                .GetGenericParameters().Count == expected;

        private int TypeRow(TypeDefinitionHandle handle)
        {
            int row = MetadataTokens.GetRowNumber(handle);
            if (row <= 0 || row >= _typesByRow.Length)
            {
                throw new BadImageFormatException(
                    "A TypeDef handle is outside the admitted Type table.");
            }
            return row;
        }

        private int CountSites()
        {
            int count = 0;
            foreach (TypeEntry entry in _types)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                TypeDefinition definition =
                    _reader.GetTypeDefinition(entry.Handle);
                if (!definition.BaseType.IsNil)
                    count = checked(count + 1);
                count = checked(
                    count
                    + definition.GetInterfaceImplementations().Count
                    + definition.GetFields().Count
                    + definition.GetProperties().Count
                    + definition.GetEvents().Count
                    + definition.GetMethods().Count);
                count = checked(
                    count
                    + CountConstraints(
                        definition.GetGenericParameters()));
                foreach (MethodDefinitionHandle methodHandle
                    in definition.GetMethods())
                {
                    count = checked(
                        count
                        + CountConstraints(
                            _reader.GetMethodDefinition(methodHandle)
                                .GetGenericParameters()));
                }
            }
            return count;
        }

        private int CountConstraints(
            GenericParameterHandleCollection parameters)
        {
            int count = 0;
            foreach (GenericParameterHandle parameterHandle in parameters)
            {
                count = checked(
                    count
                    + _reader.GetGenericParameter(parameterHandle)
                        .GetConstraints().Count);
            }
            return count;
        }

        private void ScanSites()
        {
            foreach (TypeEntry source in _types)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (_limited)
                    return;

                TypeDefinition definition =
                    _reader.GetTypeDefinition(source.Handle);
                if (!definition.BaseType.IsNil)
                {
                    int examinedBefore = _examined;
                    ScanEntitySite(
                        source,
                        definition.BaseType,
                        MetadataLibrarySignatureUseSiteKind.BaseType,
                        MetadataTokens.GetToken(source.Handle));
                    _baseSiteExamined[TypeRow(source.Handle)] =
                        _examined != examinedBefore;
                }

                foreach (InterfaceImplementationHandle implementationHandle
                    in definition.GetInterfaceImplementations())
                {
                    if (_limited)
                        return;
                    ScanSite(
                        source,
                        MetadataTokens.GetToken(implementationHandle),
                        (pending, provider) =>
                        {
                            _operation.Charge(
                                MetadataOperationDimension
                                    .InterfaceImplementationRows);
                            InterfaceImplementation implementation =
                                _reader.GetInterfaceImplementation(
                                    implementationHandle);
                            Collect(
                                DecodeEntity(
                                    implementation.Interface,
                                    provider),
                                MetadataLibrarySignatureUseSiteKind
                                    .Interface,
                                pending);
                        });
                }

                ScanConstraints(
                    source,
                    definition.GetGenericParameters(),
                    MetadataLibrarySignatureUseSiteKind.TypeConstraint);

                foreach (FieldDefinitionHandle fieldHandle
                    in definition.GetFields())
                {
                    ScanSite(
                        source,
                        MetadataTokens.GetToken(fieldHandle),
                        (pending, provider) =>
                        {
                            FieldDefinition field =
                                _reader.GetFieldDefinition(fieldHandle);
                            RequireSafe(
                                field.Signature,
                                SignatureBlobGuard.Kind.Field,
                                provider);
                            Collect(
                                field.DecodeSignature(
                                    provider,
                                    genericContext: (object?)null),
                                MetadataLibrarySignatureUseSiteKind
                                    .FieldType,
                                pending);
                        });
                }

                foreach (PropertyDefinitionHandle propertyHandle
                    in definition.GetProperties())
                {
                    ScanSite(
                        source,
                        MetadataTokens.GetToken(propertyHandle),
                        (pending, provider) =>
                        {
                            PropertyDefinition property =
                                _reader.GetPropertyDefinition(
                                    propertyHandle);
                            RequireSafe(
                                property.Signature,
                                SignatureBlobGuard.Kind.Property,
                                provider);
                            MethodSignature<
                                ImmutableArray<
                                    SignatureNamedTypeOccurrence>>
                                signature =
                                    property.DecodeSignature(
                                        provider,
                                        genericContext: (object?)null);
                            Collect(
                                signature.ReturnType,
                                MetadataLibrarySignatureUseSiteKind
                                    .PropertyType,
                                pending);
                            foreach (ImmutableArray<
                                SignatureNamedTypeOccurrence> parameter
                                in signature.ParameterTypes)
                            {
                                Collect(
                                    parameter,
                                    MetadataLibrarySignatureUseSiteKind
                                        .PropertyParameter,
                                    pending);
                            }
                        });
                }

                foreach (EventDefinitionHandle eventHandle
                    in definition.GetEvents())
                {
                    ScanSite(
                        source,
                        MetadataTokens.GetToken(eventHandle),
                        (pending, provider) =>
                        {
                            EventDefinition @event =
                                _reader.GetEventDefinition(eventHandle);
                            Collect(
                                DecodeEntity(@event.Type, provider),
                                MetadataLibrarySignatureUseSiteKind
                                    .EventType,
                                pending);
                        });
                }

                foreach (MethodDefinitionHandle methodHandle
                    in definition.GetMethods())
                {
                    MethodDefinition method =
                        _reader.GetMethodDefinition(methodHandle);
                    ScanSite(
                        source,
                        MetadataTokens.GetToken(methodHandle),
                        (pending, provider) =>
                        {
                            RequireSafe(
                                method.Signature,
                                SignatureBlobGuard.Kind.Method,
                                provider);
                            MethodSignature<
                                ImmutableArray<
                                    SignatureNamedTypeOccurrence>>
                                signature =
                                    method.DecodeSignature(
                                        provider,
                                        genericContext: (object?)null);
                            Collect(
                                signature.ReturnType,
                                MetadataLibrarySignatureUseSiteKind
                                    .MethodReturn,
                                pending);
                            foreach (ImmutableArray<
                                SignatureNamedTypeOccurrence> parameter
                                in signature.ParameterTypes)
                            {
                                Collect(
                                    parameter,
                                    MetadataLibrarySignatureUseSiteKind
                                        .MethodParameter,
                                    pending);
                            }
                        });
                    ScanConstraints(
                        source,
                        method.GetGenericParameters(),
                        MetadataLibrarySignatureUseSiteKind
                            .MethodConstraint);
                }
            }
        }

        private void ScanConstraints(
            TypeEntry source,
            GenericParameterHandleCollection parameters,
            MetadataLibrarySignatureUseSiteKind kind)
        {
            foreach (GenericParameterHandle parameterHandle in parameters)
            {
                GenericParameter parameter =
                    _reader.GetGenericParameter(parameterHandle);
                foreach (GenericParameterConstraintHandle constraintHandle
                    in parameter.GetConstraints())
                {
                    if (_limited)
                        return;
                    ScanSite(
                        source,
                        MetadataTokens.GetToken(constraintHandle),
                        (pending, provider) =>
                        {
                            GenericParameterConstraint constraint =
                                _reader.GetGenericParameterConstraint(
                                    constraintHandle);
                            Collect(
                                DecodeEntity(
                                    constraint.Type,
                                    provider),
                                kind,
                                pending);
                        });
                }
            }
        }

        private void ScanEntitySite(
            TypeEntry source,
            EntityHandle target,
            MetadataLibrarySignatureUseSiteKind kind,
            int metadataToken) =>
            ScanSite(
                source,
                metadataToken,
                (pending, provider) =>
                {
                    Collect(
                        DecodeEntity(target, provider),
                        kind,
                        pending);
                });

        private void ScanSite(
            TypeEntry source,
            int metadataToken,
            Action<PendingSite, SignatureOccurrenceProvider> decode)
        {
            if (_limited)
                return;
            _cancellationToken.ThrowIfCancellationRequested();
            _considered++;
            try
            {
                _operation.Charge(
                    MetadataOperationDimension.DeclarationCandidates);
                var pending = new PendingSite();
                var provider = new SignatureOccurrenceProvider(
                    _image,
                    new(
                        SignatureOccurrenceLimits.Default,
                        metrics: null,
                        operation: _operation));
                decode(pending, provider);
                _operation.EnsureCanCharge(
                    MetadataOperationDimension.RelationshipEdges,
                    pending.Occurrences.Count);
                _operation.Charge(
                    MetadataOperationDimension.RelationshipEdges,
                    pending.Occurrences.Count);
                foreach (PendingOccurrence occurrence
                    in pending.Occurrences)
                {
                    _occurrences.Add(
                        new(
                            source.Address,
                            source.Name,
                            occurrence.Target.Address,
                            occurrence.Target.Name,
                            occurrence.Kind,
                            metadataToken,
                            occurrence.Ordinal));
                }
                _examined++;
            }
            catch (SiteUnavailableException exception)
            {
                _diagnostics.Add(
                    new(
                        exception.Kind,
                        metadataToken,
                        exception.Message));
                _unavailable++;
            }
            catch (SiteLimitException exception)
            {
                _diagnostics.Add(
                    new(
                        MetadataLibrarySignatureUseDiagnosticKind.Limit,
                        metadataToken,
                        exception.Message,
                        exception.Dimension,
                        exception.Limit,
                        exception.AttemptedCharge));
                _siteLimited++;
            }
            catch (SignatureOccurrenceRejectedException exception)
            {
                if (exception.Dimension is { } dimension
                    && exception.Limit is { } limit
                    && exception.AttemptedCharge is { } attemptedCharge)
                {
                    _diagnostics.Add(
                        new(
                            MetadataLibrarySignatureUseDiagnosticKind.Limit,
                            metadataToken,
                            "The signature occurrence exceeded "
                                + $"{exception.Reason}.",
                            dimension,
                            limit,
                            attemptedCharge));
                    _siteLimited++;
                    return;
                }
                _diagnostics.Add(
                    new(
                        RejectionDiagnosticKind(exception.Reason),
                        metadataToken,
                        "The signature occurrence was rejected: "
                            + $"{exception.Reason}."));
                _unavailable++;
            }
            catch (MetadataOperationBudgetExceededException exception)
            {
                _diagnostics.Add(
                    new(
                        MetadataLibrarySignatureUseDiagnosticKind.Limit,
                        metadataToken,
                        $"The signature-use population exceeded "
                            + $"{exception.Dimension}.",
                        exception.Dimension,
                        exception.Limit,
                        exception.AttemptedCharge));
                _limited = true;
            }
            catch (Exception exception)
                when (exception is BadImageFormatException
                    or ArgumentException
                    or InvalidOperationException
                    or OverflowException)
            {
                _diagnostics.Add(
                    new(
                        MetadataLibrarySignatureUseDiagnosticKind
                            .MalformedMetadata,
                        metadataToken,
                        exception.Message));
                _unavailable++;
            }
        }

        private static MetadataLibrarySignatureUseDiagnosticKind
            RejectionDiagnosticKind(
                SignatureOccurrenceRejectionReason reason) =>
            reason switch
            {
                SignatureOccurrenceRejectionReason.MalformedMetadata
                    or SignatureOccurrenceRejectionReason.UnsafeSignature =>
                    MetadataLibrarySignatureUseDiagnosticKind
                        .MalformedMetadata,
                SignatureOccurrenceRejectionReason.TypeSpecificationBudget
                    or SignatureOccurrenceRejectionReason.TypeNameBudget
                    or SignatureOccurrenceRejectionReason.NodeBudget
                    or SignatureOccurrenceRejectionReason
                        .OccurrenceCopyBudget
                    or SignatureOccurrenceRejectionReason.WorkBudget =>
                    MetadataLibrarySignatureUseDiagnosticKind.Limit,
                _ =>
                    MetadataLibrarySignatureUseDiagnosticKind
                        .UnsupportedShape,
            };

        private void Collect(
            ImmutableArray<SignatureNamedTypeOccurrence> occurrences,
            MetadataLibrarySignatureUseSiteKind kind,
            PendingSite pending)
        {
            foreach (SignatureNamedTypeOccurrence occurrence
                in occurrences)
            {
                if (occurrence.Reference.Scope
                    is MetadataTypeReferenceScope.IntrinsicCoreLibrary)
                {
                    continue;
                }
                int currentOrdinal = pending.NextOrdinal++;
                if (occurrence.Participates
                    && TryBind(
                        occurrence.Reference,
                        out TypeEntry? target))
                {
                    pending.Occurrences.Add(
                        new(target, kind, currentOrdinal));
                }
            }
        }

        private bool TryBind(
            MetadataNamedTypeReference reference,
            out TypeEntry target)
        {
            target = null!;
            bool local =
                reference.Scope
                    is MetadataTypeReferenceScope.CurrentAssembly;
            bool selfReference =
                reference.Scope
                    is MetadataTypeReferenceScope.AssemblyReference
                    assembly
                && assembly.Assembly.IsEquivalentTo(_assembly);
            if (!local && !selfReference)
                return false;

            return _typesByName.TryGetValue(
                reference.Type,
                out target!);
        }

        private bool IsCurrentImage(TypeReferenceHandle handle)
        {
            Span<TypeReferenceHandle> chain =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal
                    .TryWalkTypeReferenceResolutionScope(
                        _reader,
                        handle,
                        chain,
                        out _,
                        out EntityHandle terminal,
                        out _))
            {
                return false;
            }

            if (terminal.IsNil
                || terminal.Kind == HandleKind.ModuleDefinition)
            {
                return true;
            }
            return terminal.Kind == HandleKind.AssemblyReference
                && AssemblyReferenceIdentity.From(
                        _reader,
                        (AssemblyReferenceHandle)terminal)
                    .IsEquivalentTo(_assembly);
        }

        private MetadataTypeDefinitionName ReadName(
            TypeDefinitionHandle handle) =>
            RequireName(
                MetadataTypeDefinitionNameReader.Read(
                    _reader,
                    handle));

        private MetadataTypeDefinitionName ReadName(
            TypeReferenceHandle handle) =>
            RequireName(
                MetadataTypeDefinitionNameReader.Read(
                    _reader,
                    handle));

        private static MetadataTypeDefinitionName RequireName(
            MetadataTypeDefinitionNameReadResult result) =>
            result is MetadataTypeDefinitionNameReadResult.Read read
                ? read.Name
                : throw new BadImageFormatException(
                    ((MetadataTypeDefinitionNameReadResult.Rejected)result)
                        .Failure.Detail);

        private ImmutableArray<SignatureNamedTypeOccurrence> DecodeEntity(
            EntityHandle entity,
            SignatureOccurrenceProvider provider) =>
            entity.Kind switch
            {
                HandleKind.TypeDefinition =>
                    provider.GetTypeFromDefinition(
                        _reader,
                        (TypeDefinitionHandle)entity,
                        rawTypeKind: 0x12),
                HandleKind.TypeReference =>
                    provider.GetTypeFromReference(
                        _reader,
                        (TypeReferenceHandle)entity,
                        rawTypeKind: 0x12),
                HandleKind.TypeSpecification =>
                    provider.GetTypeFromSpecification(
                        _reader,
                        genericContext: null,
                        (TypeSpecificationHandle)entity,
                        rawTypeKind: 0x12),
                _ => throw new SiteUnavailableException(
                    "A signature-use endpoint has an unsupported handle."),
            };

        private void RequireSafe(
            BlobHandle signature,
            SignatureBlobGuard.Kind kind,
            SignatureOccurrenceProvider provider)
        {
            int length = _reader.GetBlobReader(signature).Length;
            _operation.Charge(
                MetadataOperationDimension.SignatureBytes,
                length);
            SignatureBlobGuard.CompleteValidationResult validation =
                SignatureBlobGuard.ValidateCompleteDetailed(
                    _reader,
                    signature,
                    kind,
                    out SignatureBlobGuardMeasurements measurements);
            provider.ObserveGuard(measurements);
            if (validation.Kind
                == SignatureBlobGuard.CompleteValidationKind
                    .DepthBudgetExceeded)
            {
                throw new SiteLimitException(
                    MetadataOperationDimension.StructuredNodes,
                    validation.BudgetLimit!.Value,
                    validation.AttemptedCharge!.Value,
                    "The signature exceeded the structural-depth limit.");
            }
            if (validation.Kind
                == SignatureBlobGuard.CompleteValidationKind
                    .NodeBudgetExceeded)
            {
                throw new SiteLimitException(
                    MetadataOperationDimension.StructuredNodes,
                    validation.BudgetLimit!.Value,
                    validation.AttemptedCharge!.Value,
                    "The signature exceeded the structural-node limit.");
            }
            if (validation.Kind
                != SignatureBlobGuard.CompleteValidationKind.Valid)
            {
                throw new SiteUnavailableException(
                    MetadataLibrarySignatureUseDiagnosticKind
                        .MalformedMetadata,
                    "The signature is not safe and complete to decode.");
            }
        }

        private MetadataLibraryTypeClassification
            InitialClassification(
                TypeDefinitionHandle handle,
                MetadataTypeDefinitionName name,
                bool isCoreLibrary)
        {
            MetadataLibraryTypeClassification classification =
                AuthenticatesCoreBase(
                    handle,
                    isCoreLibrary,
                    "Enum")
                        ? MetadataLibraryTypeClassification.Enum
                        : AuthenticatesCoreBase(
                            handle,
                            isCoreLibrary,
                            "Delegate",
                            "MulticastDelegate")
                                ? MetadataLibraryTypeClassification
                                    .Delegate
                                : MetadataLibraryTypeClassification.None;
            if (isCoreLibrary
                && IsTopLevelSystemType(
                    name,
                    "Object",
                    "ValueType",
                    "Enum",
                    "Delegate",
                    "MulticastDelegate"))
            {
                classification |=
                    MetadataLibraryTypeClassification.UniversalBase;
            }
            if (isCoreLibrary)
                classification |= CoreBaseClassification(name);
            return classification;
        }

        private bool AuthenticatesCoreBase(
            TypeDefinitionHandle handle,
            bool isCoreLibrary,
            params string[] names)
        {
            EntityHandle baseType =
                _reader.GetTypeDefinition(handle).BaseType;
            if (baseType.IsNil)
                return false;
            if (baseType.Kind == HandleKind.TypeDefinition)
            {
                return isCoreLibrary
                    && IsTopLevelSystemType(
                        ReadName((TypeDefinitionHandle)baseType),
                        names);
            }
            if (baseType.Kind != HandleKind.TypeReference)
                return false;

            TypeReferenceHandle referenceHandle =
                (TypeReferenceHandle)baseType;
            TypeReference reference =
                _reader.GetTypeReference(referenceHandle);
            return ApiSurfaceExtractor.ResolvesThroughCoreLibrary(
                    _reader,
                    reference.ResolutionScope)
                && IsTopLevelSystemType(
                    ReadName(referenceHandle),
                    names);
        }

        private static MetadataLibraryTypeClassification
            CoreBaseClassification(
                MetadataTypeDefinitionName name)
        {
            if (IsTopLevelSystemType(name, "Attribute"))
                return MetadataLibraryTypeClassification.Attribute;
            if (IsTopLevelSystemType(name, "Exception"))
                return MetadataLibraryTypeClassification.Exception;
            return MetadataLibraryTypeClassification.None;
        }

        private static bool IsTopLevelSystemType(
            MetadataTypeDefinitionName name,
            params string[] names) =>
            name.Namespace == "System"
            && name.Segments is [var segment]
            && names.Contains(segment, StringComparer.Ordinal);

        private sealed class TypeEntry(
            TypeDefinitionHandle handle,
            MetadataTypeDefinitionAddress address,
            MetadataTypeDefinitionName name,
            AssemblyTypeDefinitionKind definitionKind,
            MetadataLibraryTypeClassification classification)
        {
            internal TypeDefinitionHandle Handle { get; } = handle;
            internal MetadataTypeDefinitionAddress Address { get; } =
                address;
            internal MetadataTypeDefinitionName Name { get; } = name;
            internal AssemblyTypeDefinitionKind DefinitionKind { get; } =
                definitionKind;
            internal MetadataLibraryTypeClassification Classification
            {
                get;
                set;
            } = classification;
        }

        private sealed record PendingOccurrence(
            TypeEntry Target,
            MetadataLibrarySignatureUseSiteKind Kind,
            int Ordinal);

        private sealed class PendingSite
        {
            internal List<PendingOccurrence> Occurrences { get; } = [];
            internal int NextOrdinal { get; set; }
        }

        private sealed class SiteUnavailableException : Exception
        {
            internal SiteUnavailableException(string detail)
                : this(
                    MetadataLibrarySignatureUseDiagnosticKind
                        .UnsupportedShape,
                    detail)
            {
            }

            internal SiteUnavailableException(
                MetadataLibrarySignatureUseDiagnosticKind kind,
                string detail)
                : base(detail) =>
                Kind = kind;

            internal MetadataLibrarySignatureUseDiagnosticKind Kind
            {
                get;
            }
        }

        private sealed class SiteLimitException(
            MetadataOperationDimension dimension,
            long limit,
            long attemptedCharge,
            string detail) : Exception(detail)
        {
            internal MetadataOperationDimension Dimension { get; } =
                dimension;
            internal long Limit { get; } = limit;
            internal long AttemptedCharge { get; } = attemptedCharge;
        }
    }
}
