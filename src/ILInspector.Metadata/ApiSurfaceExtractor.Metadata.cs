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

public static partial class ApiSurfaceExtractor
{

    private static void CountSummaryMembers(
        MetadataReader reader,
        TypeDefinition typeDef,
        ApiType? apiType,
        ApiSurface surface,
        bool isExtensionClass,
        Dictionary<ApiMember, MetadataTypeDefinitionName>?
            extensionReceiverDefinitions)
    {
        var explicitImplementationBodies = GetExplicitImplementationBodies(reader, typeDef);
        var accessorMethods = GetSemanticAccessorMethods(reader, typeDef);
        bool isEnum = IsEnum(reader, typeDef);

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var methodAccess = method.Attributes & MethodAttributes.MemberAccessMask;
            bool isExplicitImplementation = explicitImplementationBodies.Contains(methodHandle);
            if (methodAccess != MethodAttributes.Public && !isExplicitImplementation)
                continue;

            string methodName = reader.GetString(method.Name);
            if ((accessorMethods.TryGetValue(
                        methodHandle,
                        out ApiMethodSemanticsKind methodSemantics)
                    && IsCSharpAccessor(methodSemantics)
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

            bool isStatic =
                (method.Attributes & MethodAttributes.Static) != 0;
            if (isExtensionClass
                && isStatic
                && AttributeReader.HasExtensionAttribute(
                    reader,
                    method.GetCustomAttributes()))
            {
                int token = MetadataTokens.GetToken(methodHandle);
                string? extendedType =
                    GetFirstParameterType(reader, typeDef, method);
                MetadataTypeDefinitionName? receiverDefinition =
                    GetFirstParameterDefinitionName(
                        reader,
                        typeDef,
                        method);
                if (apiType is not null)
                {
                    var member = new ApiMember
                    {
                        Name = methodName,
                        Kind = "method",
                        IsStatic = true,
                        IsExtension = true,
                        ExtendedType = extendedType,
                        DeclaringType = apiType.FullName,
                        MetadataToken = token,
                        Signature = token.ToString(
                            "X8",
                            CultureInfo.InvariantCulture),
                    };
                    apiType.Members.Add(member);
                    if (receiverDefinition is not null)
                    {
                        extensionReceiverDefinitions!.Add(
                            member,
                            receiverDefinition);
                    }
                }
            }
            else if (apiType is not null)
            {
                apiType.Members.Add(new ApiMember
                {
                    Name = methodName,
                    Kind = "method",
                    IsStatic = isStatic,
                });
            }
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

            apiType?.Members.Add(new ApiMember
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
            if ((isEnum && fieldName == "value__")
                || !IsSurfaceableFieldName(fieldName, includeCompilerGenerated: false)
                || AttributeReader.HasEditorBrowsableNeverAttribute(reader, field.GetCustomAttributes()))
            {
                continue;
            }

            apiType?.Members.Add(new ApiMember
            {
                Name = fieldName,
                Kind = "field",
            });
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

            apiType?.Members.Add(new ApiMember
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
                            exportedTypeHandle) switch
                        {
                            MetadataTypeDefinitionNameReadResult.Read read =>
                                read.Name,
                            MetadataTypeDefinitionNameReadResult.Rejected rejected =>
                                throw new MetadataRowRejectedException(
                                    ApiSurfaceInspectionFailure
                                        .TypeForwarderIdentityOperation,
                                    rejected.Failure),
                            _ => throw new InvalidOperationException(
                                "Unknown exported-type name result."),
                        };
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
            var constraintTypeDefinitionNames =
                new List<MetadataTypeDefinitionName>();
            bool constraintTypeDefinitionNamesAvailable = true;

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
                ConstraintTypeDefinitionNameReadResult? constraintIdentity =
                    ConstraintTypeDefinitionNameReader.Read(
                        reader,
                        constraint.Type,
                        context,
                        allowUnmanagedValueTypeEncoding: isUnmanaged);
                if (IsExactPseudoConstraint(
                        constraintTypeName,
                        attrs,
                        isUnmanaged,
                        constraintIdentity))
                {
                    continue;
                }
                if (constraintIdentity is null
                    || constraintIdentity.IsCoreLibraryPseudoConstraint)
                {
                    constraintTypeDefinitionNamesAvailable = false;
                }
                else
                {
                    constraintTypeDefinitionNames.AddRange(
                        constraintIdentity.DefinitionNames);
                }
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
            typeParam.ConstraintTypeDefinitionNames =
                constraintTypeDefinitionNamesAvailable
                ? [.. constraintTypeDefinitionNames.Distinct()]
                : null;
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

    private static bool IsExactPseudoConstraint(
        string constraintTypeName,
        GenericParameterAttributes attributes,
        bool isUnmanaged,
        ConstraintTypeDefinitionNameReadResult? constraintIdentity)
    {
        if (constraintIdentity is not
            {
                IsCoreLibraryPseudoConstraint: true,
                DefinitionNames: [var definitionName],
            }
            || definitionName.Namespace != "System"
            || definitionName.Segments is not [var simpleName])
        {
            return false;
        }

        if ((constraintTypeName, simpleName) is
            ("System.Object", "Object"))
        {
            return true;
        }

        const GenericParameterAttributes StandardValueTypeFlags =
            GenericParameterAttributes.NotNullableValueTypeConstraint
                | GenericParameterAttributes.DefaultConstructorConstraint;
        const GenericParameterAttributes SpecialConstraintFlags =
            GenericParameterAttributes.ReferenceTypeConstraint
                | GenericParameterAttributes.NotNullableValueTypeConstraint
                | GenericParameterAttributes.DefaultConstructorConstraint;
        return (constraintTypeName, simpleName) is
            ("System.ValueType", "ValueType")
            && (attributes & SpecialConstraintFlags) == StandardValueTypeFlags
            && isUnmanaged
                == constraintIdentity.IsUnmanagedValueTypeEncoding;
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
    /// Property and event semantic methods from
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
    private static Dictionary<MethodDefinitionHandle, ApiMethodSemanticsKind>
        GetSemanticAccessorMethods(
        MetadataReader reader,
        TypeDefinition typeDef)
    {
        Dictionary<MethodDefinitionHandle, ApiMethodSemanticsKind> accessors = [];
        foreach (PropertyDefinitionHandle propertyHandle in typeDef.GetProperties())
        {
            PropertyAccessors propertyAccessors =
                reader.GetPropertyDefinition(propertyHandle).GetAccessors();
            Add(propertyAccessors.Getter, ApiMethodSemanticsKind.PropertyGetter);
            Add(propertyAccessors.Setter, ApiMethodSemanticsKind.PropertySetter);
            foreach (MethodDefinitionHandle other in propertyAccessors.Others)
                Add(other, ApiMethodSemanticsKind.PropertyOther);
        }

        foreach (EventDefinitionHandle eventHandle in typeDef.GetEvents())
        {
            EventAccessors eventAccessors =
                reader.GetEventDefinition(eventHandle).GetAccessors();
            Add(eventAccessors.Adder, ApiMethodSemanticsKind.EventAdder);
            Add(eventAccessors.Remover, ApiMethodSemanticsKind.EventRemover);
            Add(eventAccessors.Raiser, ApiMethodSemanticsKind.EventRaiser);
            foreach (MethodDefinitionHandle other in eventAccessors.Others)
                Add(other, ApiMethodSemanticsKind.EventOther);
        }

        return accessors;

        void Add(
            MethodDefinitionHandle accessor,
            ApiMethodSemanticsKind semantics)
        {
            if (accessor.IsNil)
                return;

            accessors.TryGetValue(
                accessor,
                out ApiMethodSemanticsKind existing);
            accessors[accessor] = existing | semantics;
        }
    }

    static bool IsCSharpAccessor(ApiMethodSemanticsKind semantics)
        => (semantics
            & (ApiMethodSemanticsKind.PropertyGetter
                | ApiMethodSemanticsKind.PropertySetter
                | ApiMethodSemanticsKind.EventAdder
                | ApiMethodSemanticsKind.EventRemover)) != 0;

    internal static HashSet<MethodDefinitionHandle>
        GetExplicitImplementationBodies(
            MetadataReader reader,
            TypeDefinition typeDef,
            Action<int>? beforeDecodeWork = null)
    {
        HashSet<MethodDefinitionHandle> handles = [];
        foreach (var implementationHandle in typeDef.GetMethodImplementations())
        {
            beforeDecodeWork?.Invoke(16);
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
    internal static bool IsFinalizerMethod(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle,
        Action<int>? beforeDecodeWork = null)
    {
        var method = reader.GetMethodDefinition(methodHandle);
        if (!string.Equals(
                DecodeString(
                    reader,
                    method.Name,
                    beforeDecodeWork),
                "Finalize",
                StringComparison.Ordinal))
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
                && ReferencesObjectFinalize(
                    reader,
                    implementation.MethodDeclaration,
                    beforeDecodeWork))
            {
                return true;
            }
        }

        // No MethodImpl: fall back to the implicit-slot shape the VB.NET compiler emits.
        return IsImplicitObjectFinalizeOverride(
            reader,
            typeHandle,
            method,
            beforeDecodeWork);
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

    internal static string ClassifyMethodKind(
        string methodName,
        bool isFinalizer,
        bool isExplicitInterfaceImplementation)
        => methodName switch
        {
            ".ctor" => "constructor",
            _ when IsOperatorMethodName(methodName) => "operator",
            // A finalizer's MethodImpl also makes it look explicit. Preserve
            // production precedence so its ordinary selector remains stable.
            _ when isFinalizer => "finalizer",
            _ when isExplicitInterfaceImplementation =>
                "explicit-interface-implementation",
            _ => "method",
        };

    private static bool IsOperatorMethodName(string methodName) =>
        methodName.StartsWith(
            "op_",
            StringComparison.Ordinal);
}
