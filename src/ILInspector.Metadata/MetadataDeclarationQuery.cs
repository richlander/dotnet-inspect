using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using CSharpText;

namespace ILInspector.Metadata;

public sealed record MetadataMethodDeclaration(
    string MetadataName,
    string CSharpName,
    string Accessibility,
    bool IsPublicOrProtected,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsSealed,
    ApiSignature Signature,
    IReadOnlyList<string> Attributes)
{
    public SignatureDecodeStatus? SignatureDecodeStatus { get; init; }
}

public sealed record MetadataPropertyDeclaration(
    string MetadataName,
    string CSharpName,
    string Accessibility,
    bool IsPublicOrProtected,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsSealed,
    ApiSignature Signature,
    IReadOnlyList<string> Attributes,
    MethodDefinitionHandle Getter,
    MethodDefinitionHandle Setter)
{
    public SignatureDecodeStatus? SignatureDecodeStatus { get; init; }
}

public sealed record MetadataFieldDeclaration(
    string MetadataName,
    string CSharpName,
    string Accessibility,
    bool IsStatic,
    bool IsReadOnly,
    bool IsConst,
    string? ReturnType,
    IReadOnlyList<string> Attributes)
{
    public SignatureDecodeStatus? SignatureDecodeStatus { get; init; }
}

/// <summary>
/// A same-assembly virtual slot inherited by an overriding method.
/// </summary>
public sealed record MetadataOverrideSlot(
    TypeDefinitionHandle DeclaringType,
    MethodDefinitionHandle Method);

/// <summary>
/// Handle-based declaration questions over SRM metadata. This layer is safe for
/// NativeAOT and compile-back source composition: it does not load inspected
/// assemblies and does not depend on Roslyn or the decompiler.
/// </summary>
public static class MetadataDeclarationQuery
{
    public static MetadataTypeDefinitionName GetTypeDefinitionName(
        MetadataReader reader,
        TypeDefinitionHandle handle)
        => MetadataTypeDefinitionNameReader.Read(reader, handle) switch
        {
            MetadataTypeDefinitionNameReadResult.Read read => read.Name,
            MetadataTypeDefinitionNameReadResult.Rejected rejected =>
                throw new BadImageFormatException(rejected.Failure.Detail),
            _ => throw new InvalidOperationException(
                "Unknown metadata type-name result."),
        };

    const string DegradedType = "object";
    static readonly MethodSignature<string> DegradedMethodSignature =
        new(default, DegradedType, requiredParameterCount: 0, genericParameterCount: 0, []);

    public static ApiType GetTypeSurface(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        bool includeNonPublicMembers = false)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var attributes = typeDef.Attributes;
        var (ns, name) = GetApiTypeNameParts(reader, typeHandle);
        MetadataTypeDefinitionName definitionName =
            MetadataTypeDefinitionNameReader.Read(reader, typeHandle) switch
            {
                MetadataTypeDefinitionNameReadResult.Read read => read.Name,
                MetadataTypeDefinitionNameReadResult.Rejected rejected =>
                    throw new BadImageFormatException(rejected.Failure.Detail),
                _ => throw new InvalidOperationException(
                    "Unexpected metadata type-name result."),
            };
        var type = new ApiType
        {
            Namespace = ns,
            Name = name,
            DefinitionName = definitionName,
            IntroducedTypeParameterCounts =
                GetIntroducedTypeParameterCounts(reader, typeHandle),
            Accessibility = TypeAccessibility(typeDef),
            IsSealed = (attributes & TypeAttributes.Sealed) != 0,
            IsAbstract = (attributes & TypeAttributes.Abstract) != 0,
            Attributes = AttributeReader.RenderAttributes(reader, typeDef.GetCustomAttributes(), qualifyNames: true),
            TypeParameters = TypeParameters(reader, typeDef.GetGenericParameters(), GenericContext.ForType(reader, typeDef)).ToList(),
        };

        if ((attributes & TypeAttributes.Interface) != 0)
        {
            type.Kind = "interface";
        }
        else if (!typeDef.BaseType.IsNil)
        {
            var baseTypeName = TypeResolver.GetTypeName(reader, typeDef.BaseType);
            type.BaseType = baseTypeName;
            type.Kind = baseTypeName switch
            {
                "System.Enum" => "enum",
                "System.ValueType" => "struct",
                "System.Delegate" or "System.MulticastDelegate" => "delegate",
                _ => "class",
            };
        }
        else
        {
            type.Kind = "class";
        }

        type.IsStatic = type.IsSealed && type.IsAbstract;
        var accessorMethods = new HashSet<MethodDefinitionHandle>();
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var declaration = GetProperty(reader, typeDef, property);
            if (!includeNonPublicMembers && declaration.Accessibility != "public")
                continue;

            if (!declaration.Getter.IsNil)
                accessorMethods.Add(declaration.Getter);
            if (!declaration.Setter.IsNil)
                accessorMethods.Add(declaration.Setter);

            type.Members.Add(new ApiMember
            {
                Name = declaration.MetadataName,
                Kind = "property",
                SignatureModel = declaration.Signature,
                Signature = PropertySignatureText(declaration),
                SignatureDecodeStatus = declaration.SignatureDecodeStatus,
                IsStatic = declaration.IsStatic,
                IsAbstract = declaration.IsAbstract,
                IsVirtual = declaration.IsVirtual,
                IsOverride = declaration.IsOverride,
                IsSealed = declaration.IsSealed,
                Accessibility = NonPublicAccessibility(declaration.Accessibility),
                Attributes = declaration.Attributes.ToList(),
                GetterToken = declaration.Getter.IsNil ? null : MetadataTokens.GetToken(declaration.Getter),
                SetterToken = declaration.Setter.IsNil ? null : MetadataTokens.GetToken(declaration.Setter),
            });
        }

        foreach (var methodHandle in typeDef.GetMethods())
        {
            if (accessorMethods.Contains(methodHandle))
                continue;

            var method = reader.GetMethodDefinition(methodHandle);
            var methodName = reader.GetString(method.Name);
            if (methodName.StartsWith('<'))
                continue;

            var declaration = GetMethod(reader, typeDef, method);
            if (!includeNonPublicMembers && declaration.Accessibility != "public")
                continue;

            type.Members.Add(new ApiMember
            {
                Name = declaration.MetadataName,
                Kind = declaration.MetadataName == ".ctor" ? "constructor" : "method",
                SignatureModel = declaration.Signature,
                Signature = MethodSignatureText(declaration),
                SignatureDecodeStatus = declaration.SignatureDecodeStatus,
                MetadataToken = MetadataTokens.GetToken(methodHandle),
                IsStatic = declaration.IsStatic,
                IsAbstract = declaration.IsAbstract,
                IsVirtual = declaration.IsVirtual,
                IsOverride = declaration.IsOverride,
                IsSealed = declaration.IsSealed,
                Accessibility = NonPublicAccessibility(declaration.Accessibility),
                Attributes = declaration.Attributes.ToList(),
            });
        }

        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            var fieldName = reader.GetString(field.Name);
            if (fieldName.StartsWith("<", StringComparison.Ordinal))
                continue;

            var declaration = GetField(reader, typeDef, field);
            if (!includeNonPublicMembers && declaration.Accessibility != "public")
                continue;

            type.Members.Add(new ApiMember
            {
                Name = declaration.MetadataName,
                Kind = "field",
                ReturnType = declaration.ReturnType,
                SignatureModel = declaration.ReturnType is null
                    ? null
                    : new ApiSignature { ReturnType = declaration.ReturnType, MemberName = declaration.CSharpName },
                SignatureDecodeStatus = declaration.SignatureDecodeStatus,
                IsStatic = declaration.IsStatic,
                IsReadOnly = declaration.IsReadOnly,
                IsConst = declaration.IsConst,
                Accessibility = NonPublicAccessibility(declaration.Accessibility),
                Attributes = declaration.Attributes.ToList(),
            });
        }

        return type;
    }

    public static List<int> GetIntroducedTypeParameterCounts(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                typeHandle,
                chain,
                out int consumed,
                out EntityHandle terminal,
                out RelationshipTraversalRejection? rejection)
            || consumed == 0
            || !terminal.IsNil)
        {
            throw new BadImageFormatException(
                rejection?.Detail
                    ?? "The type has an invalid declaring-type chain.");
        }

        var counts = new List<int>(consumed);
        int enclosingCount = 0;
        for (int index = 0; index < consumed; index++)
        {
            if (!MetadataTypeDeclarationProbe.TryGetGenericParameterCount(
                    reader,
                    chain[index],
                    out int cumulativeCount))
            {
                throw new BadImageFormatException(
                    "Generic parameter indices must be contiguous and ordered.");
            }
            counts.Add(GetIntroducedTypeParameterCount(
                cumulativeCount,
                enclosingCount));
            enclosingCount = cumulativeCount;
        }
        return counts;
    }

    internal static int GetIntroducedTypeParameterCount(
        int cumulativeCount,
        int enclosingCount)
    {
        if (cumulativeCount < enclosingCount)
        {
            throw new BadImageFormatException(
                "A nested type has fewer generic parameters than its declaring type.");
        }
        return cumulativeCount - enclosingCount;
    }

    /// <summary>
    /// The C#-declaration type parameters for a type — its own parameters only,
    /// excluding any inherited from an enclosing generic type (which a nested
    /// type's C# declaration does not repeat). Constraint and variance tokens are
    /// produced from metadata facts; C# spelling of the resulting names is the
    /// printer's responsibility.
    /// </summary>
    public static IReadOnlyList<TypeParameter> GetTypeParameters(MetadataReader reader, TypeDefinition typeDef)
    {
        var handles = typeDef.GetGenericParameters();
        GenericContext.ValidateParameterIndices(reader, handles);
        int inheritedCount = 0;
        int childCount = handles.Count;
        var seen = new HashSet<TypeDefinitionHandle>();
        var declaringType = typeDef.GetDeclaringType();
        for (int depth = 0; !declaringType.IsNil; depth++)
        {
            if (depth >= MetadataSafetyPolicy.MaxRelationshipNodes
                || !seen.Add(declaringType))
            {
                throw new BadImageFormatException(
                    "The type has an invalid declaring-type chain.");
            }

            TypeDefinition declaringDefinition =
                reader.GetTypeDefinition(declaringType);
            GenericParameterHandleCollection declaringHandles =
                declaringDefinition.GetGenericParameters();
            GenericContext.ValidateParameterIndices(
                reader,
                declaringHandles);
            if (childCount < declaringHandles.Count)
            {
                throw new BadImageFormatException(
                    "A nested type has fewer generic parameters than its declaring type.");
            }
            if (depth == 0)
                inheritedCount = declaringHandles.Count;
            childCount = declaringHandles.Count;
            declaringType = declaringDefinition.GetDeclaringType();
        }

        return TypeParameters(
            reader,
            handles.Skip(inheritedCount),
            GenericContext.ForType(reader, typeDef),
            expectedIndex: inheritedCount);
    }

    public static MetadataMethodDeclaration GetMethod(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var result = GuardedSignatureText.MethodText(
            reader,
            method,
            GenericContext.ForMethod(reader, typeDef, method));
        var signature = ProjectDecode(result, DegradedMethodSignature, out var status);
        return GetMethod(reader, typeDef, method, signature) with
        {
            SignatureDecodeStatus = status,
        };
    }

    public static string GetMethodReturnType(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var signature = GuardedSignatureText.MethodText(
            reader,
            method,
            GenericContext.ForMethod(reader, typeDef, method))
            .GetValueOrThrow();
        return FormatMethodReturnType(reader, signature.ReturnType, method.GetParameters());
    }

    public static bool TryGetCliInstanceConstructorSignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        out MethodSignature<string> signature)
    {
        signature = default;
        const MethodAttributes constructorFlags =
            MethodAttributes.SpecialName
            | MethodAttributes.RTSpecialName;
        if ((method.Attributes & MethodAttributes.Static) != 0
            || (method.Attributes & constructorFlags) != constructorFlags
            || !reader.StringComparer.Equals(
                method.Name,
                ".ctor")
            || method.GetGenericParameters().Count != 0)
        {
            return false;
        }

        try
        {
            signature = GuardedSignatureText.MethodText(
                reader,
                method,
                GenericContext.ForMethod(
                    reader,
                    typeDef,
                    method))
                .GetValueOrThrow();
        }
        catch (Exception ex)
            when (ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException)
        {
            signature = default;
            return false;
        }

        return signature.Header.Kind
                == SignatureKind.Method
            && signature.Header.CallingConvention
                == SignatureCallingConvention.Default
            && signature.Header.IsInstance
            && !signature.Header.IsGeneric
            && !signature.Header.HasExplicitThis
            && string.Equals(
                signature.ReturnType,
                "void",
                StringComparison.Ordinal);
    }

    public static MetadataMethodDeclaration GetMethod(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        MethodSignature<string> signature)
    {
        var name = reader.GetString(method.Name);
        var attributes = method.Attributes;
        var access = attributes & MethodAttributes.MemberAccessMask;
        var isPublicOrProtected = IsPublicOrProtected(access);
        var isSourceDeclarable = IsSourceDeclarableAccessibility(access);
        var isVirtual = (attributes & MethodAttributes.Virtual) != 0;
        var isNewSlot = (attributes & MethodAttributes.NewSlot) != 0;
        var methodHandle = FindMethodDefinitionHandle(reader, typeDef, method);
        var hasClassMethodImplOverride = isNewSlot
            && !methodHandle.IsNil
            && GetAuthenticatedClassMethodImplOverrideSlot(
                reader,
                method.GetDeclaringType(),
                methodHandle) is not null;
        var isOverride = isSourceDeclarable
            && isVirtual
            && (!isNewSlot || hasClassMethodImplOverride)
            && (attributes & MethodAttributes.Static) == 0
            && (typeDef.Attributes & TypeAttributes.Interface) == 0;
        var typeParameters = MethodTypeParameters(reader, typeDef, method);
        var csharpName = SanitizeIdentifier(name);
        var methodName = typeParameters.Count == 0
            ? csharpName
            : $"{csharpName}<{string.Join(", ", typeParameters.Select(parameter => SanitizeIdentifier(parameter.Name)))}>";

        return new MetadataMethodDeclaration(
            name,
            csharpName,
            AccessibilityKeyword(access),
            isPublicOrProtected,
            (attributes & MethodAttributes.Static) != 0,
            isSourceDeclarable && (attributes & MethodAttributes.Abstract) != 0,
            isSourceDeclarable
                && isVirtual
                && (attributes & MethodAttributes.Abstract) == 0
                && (attributes & MethodAttributes.Final) == 0
                && isNewSlot
                && !hasClassMethodImplOverride,
            isOverride,
            isOverride && (attributes & MethodAttributes.Final) != 0,
            new ApiSignature
            {
                ReturnType = FormatMethodReturnType(reader, signature.ReturnType, method.GetParameters()),
                ReturnAttributes = ReturnAttributes(reader, method.GetParameters()).ToList(),
                MemberName = methodName,
                TypeParameters = typeParameters.ToList(),
                Parameters = MethodParameters(reader, method, signature).ToList(),
            },
            RenderMemberAttributes(reader, method.GetCustomAttributes()));
    }

    /// <summary>
    /// The ordered same-image base classes of
    /// <paramref name="derivedTypeHandle"/>, nearest first. A constructed
    /// generic base is followed through its <c>TypeSpec</c> to the definition
    /// it instantiates, which a walk restricted to <c>TypeDef</c> bases cannot
    /// do. The walk stops at the first base that leaves this image, is not a
    /// constructed instantiation of a same-image definition, cannot be
    /// decoded, or repeats, and is bounded by
    /// <see cref="MetadataSafetyPolicy.MaxRelationshipNodes"/>.
    /// </summary>
    public static IReadOnlyList<TypeDefinitionHandle> GetSameAssemblyBaseChain(
        MetadataReader reader,
        TypeDefinitionHandle derivedTypeHandle)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return
        [
            .. OverrideBaseChain
                .SameAssemblyBases(reader, derivedTypeHandle)
                .Select(step => step.Definition),
        ];
    }

    /// <summary>
    /// The same-image class definition instantiated by
    /// <paramref name="derivedType"/>'s constructed generic base, when it has
    /// one.
    ///
    /// A compiler encodes <c>Derived : Base&lt;string&gt;</c> and
    /// <c>Derived&lt;T&gt; : Base&lt;T&gt;</c> as a <c>TypeSpec</c> base, so a
    /// consumer that only reads a <c>TypeDef</c> base cannot see the definition
    /// the base instantiates. This resolves exactly that one step and keeps the
    /// exact definition token; it never matches a rendered name.
    ///
    /// Fails closed: a base that is not a <c>TypeSpec</c>, a <c>TypeSpec</c>
    /// that is not a generic instantiation of a definition in this image, and
    /// an undecodable or over-budget signature all return
    /// <see langword="false"/>.
    /// </summary>
    /// <summary>
    /// True when <paramref name="typeDef"/> declares a member that occupies a
    /// virtual slot it did not introduce: a virtual method that is not
    /// <see cref="MethodAttributes.NewSlot"/>, or the body of a
    /// <c>MethodImpl</c> row. A base type owns such a slot, so a shell that
    /// drops the base must drop the member's <c>override</c> with it.
    ///
    /// Read from method attribute flags and <c>MethodImpl</c> rows only; no
    /// name or rendered signature participates. A malformed row fails closed to
    /// <see langword="false"/>, which is the drop-the-base answer.
    /// </summary>
    public static bool ReusesInheritedVirtualSlot(MetadataReader reader, TypeDefinition typeDef)
    {
        ArgumentNullException.ThrowIfNull(reader);
        try
        {
            if (typeDef.GetMethodImplementations().Count != 0)
                return true;

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var attributes = reader.GetMethodDefinition(methodHandle).Attributes;
                if ((attributes & MethodAttributes.Virtual) != 0
                    && (attributes & MethodAttributes.NewSlot) == 0)
                {
                    return true;
                }
            }
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
            return false;
        }

        return false;
    }

    public static bool TryGetSameAssemblyConstructedBaseDefinition(
        MetadataReader reader,
        TypeDefinition derivedType,
        out TypeDefinitionHandle baseTypeHandle)
    {
        ArgumentNullException.ThrowIfNull(reader);
        baseTypeHandle = default;
        if (derivedType.BaseType.Kind != HandleKind.TypeSpecification)
            return false;

        return OverrideBaseChain.TryReadConstructedBase(
            reader,
            (TypeSpecificationHandle)derivedType.BaseType,
            derivedType,
            substitution: null,
            out baseTypeHandle,
            out _);
    }

    /// <summary>
    /// True when <paramref name="methodHandle"/> reuses a virtual slot that
    /// authenticated inheritance evidence proves is declared by
    /// <c>System.Object</c>.
    ///
    /// Three facts must all hold. The method reuses an inherited slot rather
    /// than declaring a new one; its signature is exactly one of the three
    /// object intrinsics, read from primitive element types rather than from
    /// any rendered or referenced type name; and every base link from its
    /// declaring type up to <c>System.Object</c> stays inside this image, with
    /// the root authenticated as the real <c>System.Object</c> of a recognized
    /// core library.
    ///
    /// The chain requirement is the load-bearing one. Any external base on the
    /// chain may declare its own <c>NewSlot</c> virtual with the same name and
    /// signature, so a name-and-signature match alone would silently rebind
    /// that base's slot to <c>System.Object</c> whenever a consumer flattens
    /// the external base away. Local metadata cannot prove which slot such a
    /// method occupies, so this refuses rather than guessing.
    /// </summary>
    public static bool IsAuthenticatedObjectSlotOverride(
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinitionHandle methodHandle)
    {
        ArgumentNullException.ThrowIfNull(reader);
        MethodDefinition method = reader.GetMethodDefinition(methodHandle);
        if ((method.Attributes & MethodAttributes.Static) != 0
            || (method.Attributes & MethodAttributes.Virtual) == 0
            || (method.Attributes & MethodAttributes.NewSlot) != 0
            || method.GetGenericParameters().Count != 0)
        {
            return false;
        }

        if (!MatchesObjectIntrinsicSlot(reader, declaringTypeHandle, method))
            return false;

        return OverrideBaseChain.ReachesAuthenticatedObjectRoot(
            reader,
            declaringTypeHandle);
    }

    /// <summary>
    /// True when <paramref name="method"/> has the exact name and signature of
    /// one of <c>System.Object</c>'s three overridable members. Every type
    /// position is read as a primitive element type, so a hostile image cannot
    /// satisfy this with a type reference that merely renders as
    /// <c>string</c>, <c>int</c>, <c>bool</c>, or <c>object</c>.
    /// </summary>
    static bool MatchesObjectIntrinsicSlot(
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinition method)
    {
        GuardedProviderDecode.DecodeResult<MethodSignature<TypeNode>> decoded =
            GuardedProviderDecode.MethodResult(
                reader,
                method,
                new TypeNodeProvider(
                    scopeNamedTypeIdentity: true,
                    requireScopedNamedTypeIdentity: true),
                GenericContext.ForMethod(
                    reader,
                    reader.GetTypeDefinition(declaringTypeHandle),
                    method),
                (TypeNode)new DegradedTypeNode());
        if (decoded.IsDegraded)
            return false;

        MethodSignature<TypeNode> signature = decoded.Value;
        return reader.GetString(method.Name) switch
        {
            "ToString" => signature.ParameterTypes.Length == 0
                && IsPrimitive(signature.ReturnType, "string"),
            "GetHashCode" => signature.ParameterTypes.Length == 0
                && IsPrimitive(signature.ReturnType, "int"),
            "Equals" => signature.ParameterTypes.Length == 1
                && IsPrimitive(signature.ReturnType, "bool")
                && IsPrimitive(signature.ParameterTypes[0], "object"),
            _ => false,
        };

        static bool IsPrimitive(TypeNode node, string name)
            => node is PrimitiveTypeNode primitive
                && primitive.Name == name;
    }

    /// <summary>
    /// Locates the nearest same-assembly virtual slot reused by
    /// <paramref name="methodHandle"/>, including a source override encoded as
    /// <c>NewSlot</c> with a class <c>MethodImpl</c>. Returns
    /// <see langword="null"/> for an actual new slot or a base outside this
    /// metadata reader.
    /// </summary>
    public static MetadataOverrideSlot? GetSameAssemblyOverrideSlot(
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinitionHandle methodHandle)
    {
        var method = reader.GetMethodDefinition(methodHandle);
        if ((method.Attributes & MethodAttributes.Static) != 0
            || (method.Attributes & MethodAttributes.Virtual) == 0)
        {
            return null;
        }

        var declaringType = reader.GetTypeDefinition(declaringTypeHandle);
        if ((declaringType.Attributes & TypeAttributes.Interface) != 0
            || !IsSourceDeclarableAccessibility(method.Attributes & MethodAttributes.MemberAccessMask))
        {
            return null;
        }

        if ((method.Attributes & MethodAttributes.NewSlot) != 0)
        {
            return GetAuthenticatedClassMethodImplOverrideSlot(
                reader,
                declaringTypeHandle,
                methodHandle);
        }

        var methodName = reader.GetString(method.Name);
        var methodAccess = method.Attributes & MethodAttributes.MemberAccessMask;
        if (!GuardedSignatureText.MethodText(
            reader,
            method,
            PositionalGenericContext(declaringType, method))
            .TryGetValue(out var methodSignature))
        {
            return null;
        }

        var methodShape = GetOverrideSlotShape(reader, method, methodSignature);

        foreach (OverrideBaseInstantiation baseStep
            in OverrideBaseChain.SameAssemblyBases(reader, declaringTypeHandle))
        {
            var baseDefinitionHandle = baseStep.Definition;
            var baseDefinition = reader.GetTypeDefinition(baseDefinitionHandle);
            foreach (var candidateHandle in baseDefinition.GetMethods())
            {
                var candidate = reader.GetMethodDefinition(candidateHandle);
                if (reader.GetString(candidate.Name) != methodName
                    || (candidate.Attributes & MethodAttributes.Virtual) == 0
                    || (candidate.Attributes & MethodAttributes.Final) != 0
                    || (candidate.Attributes & MethodAttributes.Static) != 0
                    || (candidate.Attributes & MethodAttributes.MemberAccessMask) != methodAccess
                    || !IsSourceDeclarableAccessibility(candidate.Attributes & MethodAttributes.MemberAccessMask)
                    || candidate.GetGenericParameters().Count != method.GetGenericParameters().Count)
                {
                    continue;
                }

                if (!GuardedSignatureText.MethodText(
                    reader,
                    candidate,
                    PositionalGenericContext(baseDefinition, candidate))
                    .TryGetValue(out var candidateSignature))
                {
                    continue;
                }

                if (!TryGetSubstitutedOverrideSlotShape(
                        reader,
                        candidate,
                        candidateSignature,
                        baseStep.TypeArguments,
                        declaringTypeHandle,
                        out OverrideSlotShape candidateShape))
                {
                    continue;
                }

                if (MatchesOverrideSlotShape(
                    reader,
                    methodShape,
                    candidateShape))
                {
                    return new MetadataOverrideSlot(baseDefinitionHandle, candidateHandle);
                }
            }
        }

        return null;
    }

    static MethodDefinitionHandle FindMethodDefinitionHandle(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        foreach (var candidateHandle in typeDef.GetMethods())
        {
            if (reader.GetMethodDefinition(candidateHandle).Equals(method))
                return candidateHandle;
        }

        return default;
    }

    static MetadataOverrideSlot? GetAuthenticatedClassMethodImplOverrideSlot(
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinitionHandle methodHandle)
    {
        var declaringType = reader.GetTypeDefinition(declaringTypeHandle);
        var method = reader.GetMethodDefinition(methodHandle);
        var methodName = reader.GetString(method.Name);
        var methodAccess = method.Attributes & MethodAttributes.MemberAccessMask;
        if (!GuardedSignatureText.MethodText(
            reader,
            method,
            PositionalGenericContext(declaringType, method))
            .TryGetValue(out var methodSignature))
        {
            return null;
        }

        var methodShape = GetOverrideSlotShape(reader, method, methodSignature);
        List<OverrideBaseInstantiation> bases =
            OverrideBaseChain.SameAssemblyBases(reader, declaringTypeHandle);
        MetadataOverrideSlot? match = null;
        foreach (var implementationHandle in declaringType.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody != methodHandle)
                continue;

            if (!TryResolveSameAssemblyOverrideDeclaration(
                    reader,
                    implementation.MethodDeclaration,
                    declaringType,
                    bases,
                    out MethodDefinitionHandle declarationHandle,
                    out ImmutableArray<TypeNode>? substitution))
            {
                continue;
            }

            var declaration = reader.GetMethodDefinition(declarationHandle);
            var declarationTypeHandle = declaration.GetDeclaringType();
            if ((reader.GetTypeDefinition(declarationTypeHandle).Attributes & TypeAttributes.Interface) != 0
                || reader.GetString(declaration.Name) != methodName
                || (declaration.Attributes & MethodAttributes.Virtual) == 0
                || (declaration.Attributes & MethodAttributes.Final) != 0
                || (declaration.Attributes & MethodAttributes.Static) != 0
                || (declaration.Attributes & MethodAttributes.MemberAccessMask) != methodAccess
                || !IsSourceDeclarableAccessibility(
                    declaration.Attributes & MethodAttributes.MemberAccessMask)
                || declaration.GetGenericParameters().Count != method.GetGenericParameters().Count)
            {
                continue;
            }

            var declarationType = reader.GetTypeDefinition(declarationTypeHandle);
            if (!GuardedSignatureText.MethodText(
                reader,
                declaration,
                PositionalGenericContext(declarationType, declaration))
                .TryGetValue(out var declarationSignature)
                || !TryGetSubstitutedOverrideSlotShape(
                    reader,
                    declaration,
                    declarationSignature,
                    substitution,
                    declaringTypeHandle,
                    out OverrideSlotShape declarationShape)
                || !MatchesOverrideSlotShape(
                    reader,
                    methodShape,
                    declarationShape))
            {
                continue;
            }

            if (match is not null)
                return null;

            match = new MetadataOverrideSlot(declarationTypeHandle, declarationHandle);
        }

        return match;
    }

    /// <summary>
    /// Resolves a <c>MethodImpl</c> declaration token to the exact same-image
    /// base <c>MethodDef</c> it names, together with the instantiation of the
    /// base that the derived type actually extends. A <c>MethodDef</c>
    /// declaration must name a type on the authenticated base chain. A
    /// <c>MemberRef</c> declaration must be rooted in a constructed generic
    /// <c>TypeSpec</c> whose definition token and whose exact generic
    /// arguments both equal a chain step's, which is what authenticates the
    /// slot rather than the spelling of the reference; the referenced member
    /// is then resolved to a unique <c>MethodDef</c> by structural signature
    /// correspondence. Any other token kind, any off-chain base, any
    /// mismatched instantiation, and any ambiguity are refused.
    /// </summary>
    static bool TryResolveSameAssemblyOverrideDeclaration(
        MetadataReader reader,
        EntityHandle declarationToken,
        TypeDefinition declaringType,
        List<OverrideBaseInstantiation> bases,
        out MethodDefinitionHandle declarationHandle,
        out ImmutableArray<TypeNode>? substitution)
    {
        declarationHandle = default;
        substitution = null;
        if (declarationToken.Kind == HandleKind.MethodDefinition)
        {
            var candidate = (MethodDefinitionHandle)declarationToken;
            TypeDefinitionHandle owner =
                reader.GetMethodDefinition(candidate).GetDeclaringType();
            foreach (OverrideBaseInstantiation step in bases)
            {
                if (step.Definition != owner)
                    continue;

                declarationHandle = candidate;
                substitution = step.TypeArguments;
                return true;
            }

            return false;
        }

        if (declarationToken.Kind != HandleKind.MemberReference)
            return false;

        MemberReference reference =
            reader.GetMemberReference((MemberReferenceHandle)declarationToken);
        if (reference.GetKind() != MemberReferenceKind.Method
            || reference.Parent.Kind != HandleKind.TypeSpecification)
        {
            return false;
        }

        TypeDefinition? containing = null;
        foreach (OverrideBaseInstantiation step in bases)
        {
            if (step.TypeArguments is not { } stepArguments
                || !OverrideBaseChain.TryReadConstructedBase(
                    reader,
                    (TypeSpecificationHandle)reference.Parent,
                    declaringType,
                    null,
                    out TypeDefinitionHandle referencedDefinition,
                    out ImmutableArray<TypeNode> referencedArguments))
            {
                continue;
            }

            if (referencedDefinition != step.Definition
                || referencedArguments.Length != stepArguments.Length)
            {
                continue;
            }

            bool argumentsMatch = true;
            for (int index = 0; index < stepArguments.Length; index++)
            {
                if (!TypeNodesCorrespond(
                    reader,
                    referencedArguments[index],
                    stepArguments[index]))
                {
                    argumentsMatch = false;
                    break;
                }
            }

            if (!argumentsMatch)
                continue;

            containing = reader.GetTypeDefinition(step.Definition);
            substitution = stepArguments;
            break;
        }

        if (containing is not { } containingType)
            return false;

        return TryResolveMemberReferenceToUniqueMethod(
            reader,
            reference,
            containingType,
            out declarationHandle);
    }

    /// <summary>
    /// Finds the single <c>MethodDef</c> in <paramref name="containingType"/>
    /// that <paramref name="reference"/> names. The reference signature is
    /// written in the generic type definition's own scope, so both sides are
    /// decoded positionally and compared structurally; no rendered name
    /// participates beyond the exact metadata member name. Ambiguity or an
    /// undecodable signature fails closed.
    /// </summary>
    static bool TryResolveMemberReferenceToUniqueMethod(
        MetadataReader reader,
        MemberReference reference,
        TypeDefinition containingType,
        out MethodDefinitionHandle resolved)
    {
        resolved = default;
        string referenceName = reader.GetString(reference.Name);
        int referenceMethodArity = GuardedProviderDecode.MemberRefMethod(
            reader,
            reference,
            new SignatureArityProbe(),
            default(GenericContext?),
            (byte)0)
            .GenericParameterCount;
        MethodSignature<TypeNode> referenceSignature =
            GuardedProviderDecode.MemberRefMethod(
                reader,
                reference,
                new TypeNodeProvider(
                    scopeNamedTypeIdentity: true,
                    requireScopedNamedTypeIdentity: true),
                PositionalGenericContext(
                    containingType.GetGenericParameters().Count,
                    referenceMethodArity),
                (TypeNode)new DegradedTypeNode());

        if (referenceSignature.ReturnType.IsDegraded
            || referenceSignature.ParameterTypes.Any(
                parameter => parameter.IsDegraded))
        {
            return false;
        }

        bool found = false;
        foreach (MethodDefinitionHandle candidateHandle
            in containingType.GetMethods())
        {
            MethodDefinition candidate =
                reader.GetMethodDefinition(candidateHandle);
            if (reader.GetString(candidate.Name) != referenceName)
                continue;

            MethodSignature<TypeNode> candidateSignature =
                GuardedProviderDecode.Method(
                    reader,
                    candidate,
                    new TypeNodeProvider(
                        scopeNamedTypeIdentity: true,
                        requireScopedNamedTypeIdentity: true),
                    PositionalGenericContext(
                        containingType.GetGenericParameters().Count,
                        candidate.GetGenericParameters().Count),
                    (TypeNode)new DegradedTypeNode());
            if (!MethodSignaturesCorrespond(
                reader,
                referenceSignature,
                candidateSignature))
            {
                continue;
            }

            if (found)
                return false;

            found = true;
            resolved = candidateHandle;
        }

        return found;
    }

    static bool MethodSignaturesCorrespond(
        MetadataReader reader,
        MethodSignature<TypeNode> reference,
        MethodSignature<TypeNode> candidate)
    {
        if (reference.Header.RawValue != candidate.Header.RawValue
            || reference.GenericParameterCount
                != candidate.GenericParameterCount
            || reference.RequiredParameterCount
                != candidate.RequiredParameterCount
            || reference.ParameterTypes.Length
                != candidate.ParameterTypes.Length
            || candidate.ReturnType.IsDegraded
            || !TypeNodesCorrespond(
                reader,
                reference.ReturnType,
                candidate.ReturnType))
        {
            return false;
        }

        for (int index = 0;
            index < reference.ParameterTypes.Length;
            index++)
        {
            if (candidate.ParameterTypes[index].IsDegraded
                || !TypeNodesCorrespond(
                    reader,
                    reference.ParameterTypes[index],
                    candidate.ParameterTypes[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads only the arity of a method signature blob so a
    /// <c>MemberRef</c> can be decoded with a positional context of the right
    /// size. Every type position collapses to a single sentinel because no
    /// type identity is consumed from this probe.
    /// </summary>
    sealed class SignatureArityProbe
        : ISignatureTypeProvider<byte, GenericContext?>
    {
        public byte GetArrayType(byte elementType, ArrayShape shape) => 0;
        public byte GetByReferenceType(byte elementType) => 0;
        public byte GetFunctionPointerType(MethodSignature<byte> signature) => 0;
        public byte GetGenericInstantiation(
            byte genericType,
            ImmutableArray<byte> typeArguments) => 0;
        public byte GetGenericMethodParameter(
            GenericContext? context,
            int index) => 0;
        public byte GetGenericTypeParameter(
            GenericContext? context,
            int index) => 0;
        public byte GetModifiedType(
            byte modifier,
            byte unmodifiedType,
            bool isRequired) => 0;
        public byte GetPinnedType(byte elementType) => 0;
        public byte GetPointerType(byte elementType) => 0;
        public byte GetPrimitiveType(PrimitiveTypeCode typeCode) => 0;
        public byte GetSZArrayType(byte elementType) => 0;
        public byte GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind) => 0;
        public byte GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind) => 0;
        public byte GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind) => 0;
    }

    static GenericContext PositionalGenericContext(
        int typeParameterCount,
        int methodParameterCount)
        => new(
            [.. Enumerable.Range(0, typeParameterCount)
                .Select(index => $"!{index}")],
            [.. Enumerable.Range(0, methodParameterCount)
                .Select(index => $"!!{index}")]);

    static GenericContext PositionalGenericContext(
        TypeDefinition type,
        MethodDefinition method)
        => new(
            Enumerable.Range(0, type.GetGenericParameters().Count)
                .Select(index => $"!{index}")
                .ToArray(),
            Enumerable.Range(0, method.GetGenericParameters().Count)
                .Select(index => $"!!{index}")
                .ToArray());

    enum OverrideModifier
    {
        None,
        Ref,
        RefReadOnly,
        In,
        Out,
        Params,
    }

    readonly record struct OverrideTypeIdentity(
        string Exact,
        string PlatformNormalized,
        bool IsDegraded);

    readonly record struct OverrideParameterShape(
        OverrideTypeIdentity Type,
        TypeNode TypeNode,
        OverrideModifier Modifier);

    readonly record struct OverrideTypeContext(
        GenericContext GenericContext,
        IReadOnlyList<GenericParameterHandle> TypeParameters,
        IReadOnlyList<GenericParameterHandle> MethodParameters);

    readonly record struct OverrideSlotShape(
        OverrideTypeIdentity ReturnType,
        TypeNode ReturnTypeNode,
        OverrideModifier ReturnModifier,
        OverrideTypeContext TypeContext,
        IReadOnlyList<OverrideParameterShape> Parameters,
        bool IsDegraded);

    static OverrideSlotShape GetOverrideSlotShape(
        MetadataReader reader,
        MethodDefinition method,
        MethodSignature<string> textSignature)
    {
        var typeDef = reader.GetTypeDefinition(method.GetDeclaringType());
        GenericContext context =
            GenericContext.ForMethod(reader, typeDef, method);
        var nodeSignature = GuardedProviderDecode.Method(
            reader,
            method,
            new TypeNodeProvider(
                scopeNamedTypeIdentity: true,
                requireScopedNamedTypeIdentity: true),
            context,
            (TypeNode)new DegradedTypeNode());
        return BuildOverrideSlotShape(
            reader,
            method,
            textSignature,
            nodeSignature,
            context,
            typeDef);
    }

    /// <summary>
    /// Produces the slot shape of a base declaration as the derived type sees
    /// it. When the base is reached through a constructed generic
    /// <c>TypeSpec</c>, every type-parameter position in the declaration's
    /// signature is replaced by the exact argument that instantiates it, so
    /// the resulting shape is expressed in the derived type's own generic
    /// scope and compares against the deriving method without any name
    /// matching. Returns <see langword="false"/> when the recorded
    /// instantiation does not match the declaring definition's arity.
    /// </summary>
    static bool TryGetSubstitutedOverrideSlotShape(
        MetadataReader reader,
        MethodDefinition method,
        MethodSignature<string> textSignature,
        ImmutableArray<TypeNode>? substitution,
        TypeDefinitionHandle derivedTypeHandle,
        out OverrideSlotShape shape)
    {
        if (substitution is not { } arguments)
        {
            shape = GetOverrideSlotShape(reader, method, textSignature);
            return true;
        }

        TypeDefinition declarationType =
            reader.GetTypeDefinition(method.GetDeclaringType());
        if (declarationType.GetGenericParameters().Count
            != arguments.Length)
        {
            shape = default;
            return false;
        }

        TypeDefinition derivedType =
            reader.GetTypeDefinition(derivedTypeHandle);
        var nodeSignature = GuardedProviderDecode.Method(
            reader,
            method,
            SubstitutedTypeParameterProvider.Create(arguments),
            GenericContext.ForMethod(reader, declarationType, method),
            (TypeNode)new DegradedTypeNode());
        shape = BuildOverrideSlotShape(
            reader,
            method,
            textSignature,
            nodeSignature,
            GenericContext.ForMethod(reader, derivedType, method),
            derivedType);
        return true;
    }

    static OverrideSlotShape BuildOverrideSlotShape(
        MetadataReader reader,
        MethodDefinition method,
        MethodSignature<string> textSignature,
        MethodSignature<TypeNode> nodeSignature,
        GenericContext context,
        TypeDefinition typeDef)
    {
        IReadOnlyList<ApiParameter> parameters =
            MethodParameters(reader, method, textSignature);
        bool isDegraded =
            nodeSignature.ReturnType.IsDegraded
            || nodeSignature.ParameterTypes.Any(
                parameter => parameter.IsDegraded)
            || nodeSignature.ParameterTypes.Length
                != parameters.Count;
        var parameterShapes =
            new List<OverrideParameterShape>(
                nodeSignature.ParameterTypes.Length);
        for (int index = 0;
            index < nodeSignature.ParameterTypes.Length;
            index++)
        {
            parameterShapes.Add(
                new OverrideParameterShape(
                    TypeIdentity(
                        nodeSignature.ParameterTypes[index]),
                    nodeSignature.ParameterTypes[index],
                    index < parameters.Count
                        ? ParameterModifier(
                            parameters[index].Modifier)
                        : OverrideModifier.None));
        }
        return new OverrideSlotShape(
            TypeIdentity(nodeSignature.ReturnType),
            nodeSignature.ReturnType,
            ReturnModifier(
                reader,
                method,
                nodeSignature.ReturnType),
            new OverrideTypeContext(
                context,
                [.. typeDef.GetGenericParameters()],
                [.. method.GetGenericParameters()]),
            parameterShapes,
            isDegraded);
    }

    /// <summary>
    /// Deterministic cumulative work and recursion-depth accounting for one
    /// override-slot authentication. Active-handle cycle detection alone
    /// bounds neither a constraint DAG, whose distinct paths grow
    /// exponentially in its width, nor a constraint chain, which recurses once
    /// per link on the native stack. Exhaustion is sticky and fails closed:
    /// every comparison declines and the caller refuses the slot outright, so
    /// an over-budget decision can never be read as a retained relationship.
    /// </summary>
    sealed class OverrideCompatibilityBudget
    {
        int remainingWork =
            MetadataSafetyPolicy.MaxOverrideCompatibilityWork;
        int depth;

        internal bool IsExhausted { get; private set; }

        internal bool TryEnter()
        {
            if (IsExhausted
                || remainingWork == 0
                || depth
                    >= MetadataSafetyPolicy.MaxOverrideCompatibilityDepth)
            {
                IsExhausted = true;
                return false;
            }

            remainingWork--;
            depth++;
            return true;
        }

        internal void Exit() => depth--;

        internal bool TryCharge()
        {
            if (IsExhausted || remainingWork == 0)
            {
                IsExhausted = true;
                return false;
            }

            remainingWork--;
            return true;
        }
    }

    static bool MatchesOverrideSlotShape(
        MetadataReader reader,
        OverrideSlotShape method,
        OverrideSlotShape candidate)
    {
        if (method.IsDegraded || candidate.IsDegraded)
            return false;

        var budget = new OverrideCompatibilityBudget();
        bool matches = ParametersMatch(
                reader,
                method.Parameters,
                candidate.Parameters,
                budget)
            && ReturnTypesAreOverrideCompatible(
                reader,
                method,
                candidate,
                budget);
        return matches && !budget.IsExhausted;
    }

    static bool ParametersMatch(
        MetadataReader reader,
        IReadOnlyList<OverrideParameterShape> methodParameters,
        IReadOnlyList<OverrideParameterShape> candidateParameters,
        OverrideCompatibilityBudget budget)
    {
        if (methodParameters.Count != candidateParameters.Count)
            return false;

        for (var index = 0; index < methodParameters.Count; index++)
        {
            if (!budget.TryCharge()
                || methodParameters[index].Modifier
                    != candidateParameters[index].Modifier
                || !TypeNodesCorrespond(
                    reader,
                    methodParameters[index].TypeNode,
                    candidateParameters[index].TypeNode))
            {
                return false;
            }
        }

        return true;
    }

    static bool ReturnTypesAreOverrideCompatible(
        MetadataReader reader,
        OverrideSlotShape method,
        OverrideSlotShape candidate,
        OverrideCompatibilityBudget budget)
    {
        if (method.ReturnModifier != candidate.ReturnModifier)
            return false;

        if (TypeNodesCorrespond(
                reader,
                method.ReturnTypeNode,
                candidate.ReturnTypeNode))
        {
            return true;
        }

        if (method.ReturnModifier != OverrideModifier.None
            || method.ReturnTypeNode.IsDegraded
            || candidate.ReturnTypeNode.IsDegraded)
        {
            return false;
        }

        if (method.ReturnTypeNode is GenericParameterNode
            methodParameter)
        {
            return CompareGenericParameterReturn(
                    reader,
                    methodParameter,
                    method.TypeContext,
                    candidate.ReturnTypeNode,
                    candidate.TypeContext,
                    [],
                    budget)
                != OverrideCompatibility.Incompatible;
        }

        if (candidate.ReturnTypeNode
            is GenericParameterNode)
        {
            return false;
        }

        if (!method.ReturnTypeNode.IsReferenceType
            || !candidate.ReturnTypeNode.IsReferenceType)
        {
            return false;
        }

        if (IsObject(candidate.ReturnTypeNode))
            return true;
        if (IsObject(method.ReturnTypeNode))
            return false;

        OverrideCompatibility structuredCompatibility =
            CompareStructuredReturnTypes(
                reader,
                method.ReturnTypeNode,
                method.TypeContext,
                candidate.ReturnTypeNode,
                candidate.TypeContext,
                budget);
        if (structuredCompatibility
            != OverrideCompatibility.Unknown)
        {
            return structuredCompatibility
                == OverrideCompatibility.Compatible;
        }

        if (HaveSameDefinitionNameDifferentScope(
                method.ReturnTypeNode,
                candidate.ReturnTypeNode))
        {
            return false;
        }

        var hasMethodReturnDefinition =
            TryFindExactLocalTypeDefinition(
                reader,
                method.ReturnTypeNode,
                out var methodReturnHandle);
        var hasCandidateReturnDefinition =
            TryFindExactLocalTypeDefinition(
                reader,
                candidate.ReturnTypeNode,
                out var candidateReturnHandle);
        if (HasUnavailableOrAmbiguousExactLocalDefinition(
                reader,
                method.ReturnTypeNode)
            || HasUnavailableOrAmbiguousExactLocalDefinition(
                reader,
                candidate.ReturnTypeNode))
        {
            return false;
        }
        if (!hasMethodReturnDefinition
            || !hasCandidateReturnDefinition)
        {
            // The MethodImpl already authenticates the exact base slot. If either
            // reference-type return lives outside this image, local metadata cannot
            // prove or disprove covariance; preserve the slot and let the C#
            // compiler validate the referenced hierarchy during compile-back.
            return true;
        }

        return IsSameOrDerivedOrImplements(
            reader,
            method.ReturnTypeNode,
            methodReturnHandle,
            candidate.ReturnTypeNode,
            candidateReturnHandle,
            budget);
    }

    enum OverrideCompatibility
    {
        Unknown,
        Compatible,
        Incompatible,
    }

    static OverrideCompatibility CompareStructuredReturnTypes(
        MetadataReader reader,
        TypeNode method,
        OverrideTypeContext methodContext,
        TypeNode candidate,
        OverrideTypeContext candidateContext,
        OverrideCompatibilityBudget budget)
    {
        if (!budget.TryEnter())
            return OverrideCompatibility.Incompatible;

        try
        {
            return CompareStructuredReturnTypesCore(
                reader,
                method,
                methodContext,
                candidate,
                candidateContext,
                budget);
        }
        finally
        {
            budget.Exit();
        }
    }

    static OverrideCompatibility CompareStructuredReturnTypesCore(
        MetadataReader reader,
        TypeNode method,
        OverrideTypeContext methodContext,
        TypeNode candidate,
        OverrideTypeContext candidateContext,
        OverrideCompatibilityBudget budget)
    {
        if (TypeNodesCorrespond(
                reader,
                method,
                candidate))
        {
            return OverrideCompatibility.Compatible;
        }

        if (method is ModifiedTypeNode
            || candidate is ModifiedTypeNode)
        {
            return method is ModifiedTypeNode methodModified
                && candidate is ModifiedTypeNode candidateModified
                && methodModified.IsRequired
                    == candidateModified.IsRequired
                && TypeNodesCorrespond(
                    reader,
                    methodModified.Modifier,
                    candidateModified.Modifier)
                ? CompareStructuredReturnTypes(
                    reader,
                    methodModified.Inner,
                    methodContext,
                    candidateModified.Inner,
                    candidateContext,
                    budget)
                : OverrideCompatibility.Incompatible;
        }

        if (method is PinnedTypeNode
            || candidate is PinnedTypeNode)
        {
            return method is PinnedTypeNode methodPinned
                && candidate is PinnedTypeNode candidatePinned
                ? CompareStructuredReturnTypes(
                    reader,
                    methodPinned.Inner,
                    methodContext,
                    candidatePinned.Inner,
                    candidateContext,
                    budget)
                : OverrideCompatibility.Incompatible;
        }

        if (method is GenericParameterNode
            methodParameter)
        {
            return CompareGenericParameterReturn(
                reader,
                methodParameter,
                methodContext,
                candidate,
                candidateContext,
                [],
                budget);
        }

        if (candidate is GenericParameterNode)
            return OverrideCompatibility.Incompatible;

        if (method is SZArrayTypeNode
            || candidate is SZArrayTypeNode)
        {
            if (method is not SZArrayTypeNode methodSz
                || candidate is not SZArrayTypeNode candidateSz
                || !TypeIsAuthenticatedReferenceType(
                    reader,
                    methodSz.ElementType,
                    methodContext)
                || !TypeIsAuthenticatedReferenceType(
                    reader,
                    candidateSz.ElementType,
                    candidateContext))
            {
                return OverrideCompatibility.Incompatible;
            }
            return CompareVariantTypeArguments(
                reader,
                methodSz.ElementType,
                methodContext,
                candidateSz.ElementType,
                candidateContext,
                budget);
        }

        if (method is MDArrayTypeNode
            || candidate is MDArrayTypeNode)
        {
            return method is MDArrayTypeNode methodMd
                && candidate is MDArrayTypeNode candidateMd
                && ArrayShapesCorrespond(
                    methodMd.Shape,
                    candidateMd.Shape)
                && TypeIsAuthenticatedReferenceType(
                    reader,
                    methodMd.ElementType,
                    methodContext)
                && TypeIsAuthenticatedReferenceType(
                    reader,
                    candidateMd.ElementType,
                    candidateContext)
                ? CompareVariantTypeArguments(
                    reader,
                    methodMd.ElementType,
                    methodContext,
                    candidateMd.ElementType,
                    candidateContext,
                    budget)
                : OverrideCompatibility.Incompatible;
        }

        if (method is not GenericTypeNode methodGeneric
            || candidate is not GenericTypeNode candidateGeneric
            || !GenericDefinitionsCorrespond(
               methodGeneric,
               candidateGeneric)
            || methodGeneric.Arguments.Length
               != candidateGeneric.Arguments.Length
            || !TryFindExactLocalTypeDefinition(
               reader,
               methodGeneric,
               out TypeDefinitionHandle genericDefinitionHandle))
        {
            return OverrideCompatibility.Unknown;
        }

        GenericParameterHandleCollection genericParameterHandles =
            reader
                .GetTypeDefinition(genericDefinitionHandle)
                .GetGenericParameters();
        try
        {
            GenericContext.ValidateParameterIndices(
                reader,
                genericParameterHandles);
        }
        catch (BadImageFormatException)
        {
            return OverrideCompatibility.Unknown;
        }
        var genericParameters = genericParameterHandles
            .Select(reader.GetGenericParameter)
            .ToArray();
        if (genericParameters.Length
            != methodGeneric.Arguments.Length)
        {
            return OverrideCompatibility.Unknown;
        }

        bool hasUnknown = false;
        for (int index = 0;
            index < genericParameters.Length;
            index++)
        {
            TypeNode methodArgument =
                methodGeneric.Arguments[index];
            TypeNode candidateArgument =
                candidateGeneric.Arguments[index];
            if (!budget.TryCharge())
                return OverrideCompatibility.Incompatible;

            GenericParameterAttributes variance =
                genericParameters[index].Attributes
                & GenericParameterAttributes.VarianceMask;
            OverrideCompatibility argumentCompatibility =
                variance switch
                {
                    GenericParameterAttributes.None =>
                        TypeNodesCorrespond(
                            reader,
                            methodArgument,
                            candidateArgument)
                            ? OverrideCompatibility.Compatible
                            : OverrideCompatibility.Incompatible,
                    GenericParameterAttributes.Covariant =>
                        CompareVariantTypeArguments(
                            reader,
                            methodArgument,
                            methodContext,
                            candidateArgument,
                            candidateContext,
                            budget),
                    GenericParameterAttributes.Contravariant =>
                        CompareVariantTypeArguments(
                            reader,
                            candidateArgument,
                            candidateContext,
                            methodArgument,
                            methodContext,
                            budget),
                    _ => OverrideCompatibility.Incompatible,
                };
            if (argumentCompatibility
                == OverrideCompatibility.Incompatible)
            {
                return OverrideCompatibility.Incompatible;
            }
            hasUnknown |= argumentCompatibility
                == OverrideCompatibility.Unknown;
        }

        return hasUnknown
            ? OverrideCompatibility.Unknown
            : OverrideCompatibility.Compatible;
    }

    static OverrideCompatibility CompareVariantTypeArguments(
        MetadataReader reader,
        TypeNode method,
        OverrideTypeContext methodContext,
        TypeNode candidate,
        OverrideTypeContext candidateContext,
        OverrideCompatibilityBudget budget)
    {
        if (!budget.TryCharge())
            return OverrideCompatibility.Incompatible;

        OverrideCompatibility structured =
            CompareStructuredReturnTypes(
                reader,
                method,
                methodContext,
                candidate,
                candidateContext,
                budget);
        if (structured != OverrideCompatibility.Unknown)
            return structured;

        bool hasMethodDefinition =
            TryFindExactLocalTypeDefinition(
                reader,
                method,
                out TypeDefinitionHandle methodHandle);
        bool hasCandidateDefinition =
            TryFindExactLocalTypeDefinition(
                reader,
                candidate,
                out TypeDefinitionHandle candidateHandle);
        if (HasUnavailableOrAmbiguousExactLocalDefinition(
                reader,
                method)
            || HasUnavailableOrAmbiguousExactLocalDefinition(
                reader,
                candidate))
        {
            return OverrideCompatibility.Incompatible;
        }
        if (!hasMethodDefinition
            || !hasCandidateDefinition)
        {
            return OverrideCompatibility.Unknown;
        }

        return IsSameOrDerivedOrImplements(
                reader,
                method,
                methodHandle,
                candidate,
                candidateHandle,
                budget)
            ? OverrideCompatibility.Compatible
            : OverrideCompatibility.Incompatible;
    }

    static OverrideCompatibility CompareGenericParameterReturn(
        MetadataReader reader,
        GenericParameterNode method,
        OverrideTypeContext methodContext,
        TypeNode candidate,
        OverrideTypeContext candidateContext,
        HashSet<GenericParameterHandle> visited,
        OverrideCompatibilityBudget budget)
    {
        if (!budget.TryEnter())
            return OverrideCompatibility.Incompatible;

        try
        {
            return CompareGenericParameterReturnCore(
                reader,
                method,
                methodContext,
                candidate,
                candidateContext,
                visited,
                budget);
        }
        finally
        {
            budget.Exit();
        }
    }

    static OverrideCompatibility CompareGenericParameterReturnCore(
        MetadataReader reader,
        GenericParameterNode method,
        OverrideTypeContext methodContext,
        TypeNode candidate,
        OverrideTypeContext candidateContext,
        HashSet<GenericParameterHandle> visited,
        OverrideCompatibilityBudget budget)
    {
        if (TypeNodesCorrespond(
                reader,
                method,
                candidate))
        {
            return OverrideCompatibility.Compatible;
        }

        if (!TryGetGenericParameterHandle(
                method,
                methodContext,
                out GenericParameterHandle parameterHandle))
        {
            return OverrideCompatibility.Incompatible;
        }

        if (IsObject(candidate))
        {
            return TypeParameterKindClassifier.Classify(
                    reader,
                    parameterHandle,
                    method.HasValueTypeConstraint,
                    method.HasReferenceTypeConstraint,
                    new TypeParameterKindClassifier
                        .ChainState())
                    == TypeParameterTypeKind
                        .ReferenceType
                ? OverrideCompatibility.Compatible
                : OverrideCompatibility.Incompatible;
        }

        if (!visited.Add(parameterHandle))
            return OverrideCompatibility.Incompatible;

        try
        {
            GenericParameter parameter =
                reader.GetGenericParameter(parameterHandle);
            if (!TryDecodeConstraintSet(
                    reader,
                    parameter,
                    methodContext,
                    budget,
                    out List<TypeNode> constraintTypes))
            {
                return OverrideCompatibility.Incompatible;
            }

            bool candidateHasUnavailableDefinition =
                HasUnavailableOrAmbiguousExactLocalDefinition(
                    reader,
                    candidate);
            bool hasUnknownConstraintCompatibility = false;
            foreach (TypeNode constraintType in constraintTypes)
            {
                if (constraintType is GenericParameterNode
                    constrainedParameter)
                {
                    if (CompareGenericParameterReturn(
                            reader,
                            constrainedParameter,
                            methodContext,
                            candidate,
                            candidateContext,
                            visited,
                            budget)
                            == OverrideCompatibility.Compatible)
                    {
                        return OverrideCompatibility.Compatible;
                    }
                    continue;
                }

                OverrideCompatibility constraintCompatibility =
                    CompareStructuredReturnTypes(
                        reader,
                        constraintType,
                        methodContext,
                        candidate,
                        candidateContext,
                        budget);
                if (constraintCompatibility
                    == OverrideCompatibility.Compatible)
                {
                    return OverrideCompatibility.Compatible;
                }
                if (candidateHasUnavailableDefinition)
                    continue;

                bool candidateHasGenericShape =
                    HasGenericShape(candidate);
                bool hasConstraintDefinition =
                    TryFindExactLocalTypeDefinition(
                        reader,
                        constraintType,
                        out TypeDefinitionHandle constraintDefinition);
                bool hasCandidateDefinition =
                    TryFindExactLocalTypeDefinition(
                        reader,
                        candidate,
                        out TypeDefinitionHandle candidateDefinition);
                if (constraintCompatibility
                        == OverrideCompatibility.Unknown
                    && candidateHasGenericShape
                    && (!hasConstraintDefinition
                        || !hasCandidateDefinition))
                {
                    hasUnknownConstraintCompatibility = true;
                }
                if (constraintCompatibility
                        == OverrideCompatibility.Incompatible
                    || candidateHasGenericShape)
                {
                    // A constructed or raw generic target needs the argument
                    // correspondence the structured comparison above decides.
                    continue;
                }

                if (hasConstraintDefinition
                    && hasCandidateDefinition
                    && IsSameOrDerivedOrImplements(
                        reader,
                        constraintType,
                        constraintDefinition,
                        candidate,
                        candidateDefinition,
                        budget))
                {
                    return OverrideCompatibility.Compatible;
                }
            }

            return hasUnknownConstraintCompatibility
                ? OverrideCompatibility.Unknown
                : OverrideCompatibility.Incompatible;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
            return OverrideCompatibility.Incompatible;
        }
        finally
        {
            visited.Remove(parameterHandle);
        }
    }

    /// <summary>
    /// Decodes every constraint on one generic parameter before any of them
    /// may authenticate a conversion.
    ///
    /// The constraint set is existential among constraints this image fully
    /// decodes, so degraded evidence has to be decided for the whole set
    /// first: skipping a degraded constraint and then accepting a later valid
    /// one would let metadata order decide whether malformed current-image
    /// evidence is fail-closed. A degraded decode, a constraint whose
    /// current-image definition does not resolve uniquely, and an exhausted
    /// comparison budget therefore refuse the whole set. Gated by
    /// <c>SameAssemblyOverrideSlot_DeclinesMixedDegradedAndValidConstraintSet</c>,
    /// which runs both metadata orders, and
    /// <c>SameAssemblyOverrideSlot_DeclinesDegradedOnlyConstraint</c>, whose
    /// non-vacuity control is
    /// <c>SameAssemblyOverrideSlot_AllowsValidOnlyExplicitConstraintSet</c>.
    /// </summary>
    static bool TryDecodeConstraintSet(
        MetadataReader reader,
        GenericParameter parameter,
        OverrideTypeContext methodContext,
        OverrideCompatibilityBudget budget,
        out List<TypeNode> constraintTypes)
    {
        constraintTypes = [];
        foreach (GenericParameterConstraintHandle constraintHandle
            in parameter.GetConstraints())
        {
            if (!budget.TryCharge())
                return false;

            GenericParameterConstraint constraint =
                reader.GetGenericParameterConstraint(constraintHandle);
            TypeNode constraintType = DecodeConstraintType(
                reader,
                constraint.Type,
                methodContext);
            if (constraintType.IsDegraded
                || HasUnavailableOrAmbiguousExactLocalDefinition(
                    reader,
                    constraintType))
            {
                return false;
            }

            constraintTypes.Add(constraintType);
        }

        return true;
    }

    static OverrideTypeIdentity TypeIdentity(
        TypeNode type)
        => new(
            type.StructuralIdentity(),
            type.PlatformNormalizedStructuralIdentity(),
            type.IsDegraded);

    static bool TypeIdentitiesCorrespond(
        OverrideTypeIdentity left,
        OverrideTypeIdentity right)
        => !left.IsDegraded
            && !right.IsDegraded
            && (string.Equals(
                    left.Exact,
                    right.Exact,
                    StringComparison.Ordinal)
                || string.Equals(
                    left.PlatformNormalized,
                    right.PlatformNormalized,
                    StringComparison.Ordinal));

    static bool TypeNodesCorrespond(
        MetadataReader reader,
        TypeNode left,
        TypeNode right)
        => !HasUnavailableOrAmbiguousExactLocalDefinition(
                reader,
                left)
            && !HasUnavailableOrAmbiguousExactLocalDefinition(
                reader,
                right)
            && TypeIdentitiesCorrespond(
                TypeIdentity(left),
                TypeIdentity(right));

    static OverrideModifier ParameterModifier(
        string? modifier)
        => modifier switch
        {
            null or "" => OverrideModifier.None,
            "ref" => OverrideModifier.Ref,
            "in" => OverrideModifier.In,
            "out" => OverrideModifier.Out,
            "params" => OverrideModifier.Params,
            _ => OverrideModifier.None,
        };

    static OverrideModifier ReturnModifier(
        MetadataReader reader,
        MethodDefinition method,
        TypeNode returnType)
        => returnType is not ByRefTypeNode
            ? OverrideModifier.None
            : ReturnIsReadOnlyRef(
                reader,
                method.GetParameters())
                ? OverrideModifier.RefReadOnly
                : OverrideModifier.Ref;

    static bool IsObject(TypeNode type)
        => type is PrimitiveTypeNode
        {
            Name: "object" or "System.Object",
        };

    static bool TypeIsAuthenticatedReferenceType(
        MetadataReader reader,
        TypeNode type,
        OverrideTypeContext context)
    {
        if (type.IsDegraded)
            return false;

        if (type is PassthroughTypeNode passthrough)
            return TypeIsAuthenticatedReferenceType(
                reader,
                passthrough.Inner,
                context);

        if (type is not GenericParameterNode parameter)
            return type.IsReferenceType;

        return TryGetGenericParameterHandle(
                parameter,
                context,
                out GenericParameterHandle parameterHandle)
            && TypeParameterKindClassifier.Classify(
                    reader,
                    parameterHandle,
                    parameter.HasValueTypeConstraint,
                    parameter.HasReferenceTypeConstraint,
                    new TypeParameterKindClassifier
                        .ChainState())
                == TypeParameterTypeKind
                    .ReferenceType;
    }

    static bool ArrayShapesCorrespond(
        ArrayShape left,
        ArrayShape right)
        => left.Rank == right.Rank
            && left.Sizes.SequenceEqual(right.Sizes)
            && left.LowerBounds.SequenceEqual(
                right.LowerBounds);

    static bool TryGetGenericParameterHandle(
        GenericParameterNode parameter,
        OverrideTypeContext context,
        out GenericParameterHandle handle)
    {
        IReadOnlyList<GenericParameterHandle> parameters =
            parameter.IsMethodParameter
                ? context.MethodParameters
                : context.TypeParameters;
        if ((uint)parameter.Index
            >= (uint)parameters.Count)
        {
            handle = default;
            return false;
        }

        handle = parameters[parameter.Index];
        return !handle.IsNil;
    }

    static TypeNode DecodeConstraintType(
        MetadataReader reader,
        EntityHandle handle,
        OverrideTypeContext context)
    {
        var provider = new TypeNodeProvider(
            scopeNamedTypeIdentity: true,
            requireScopedNamedTypeIdentity: true);
        return handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                provider.GetTypeFromDefinition(
                    reader,
                    (TypeDefinitionHandle)handle,
                    rawTypeKind: 0x12),
            HandleKind.TypeReference =>
                provider.GetTypeFromReference(
                    reader,
                    (TypeReferenceHandle)handle,
                    rawTypeKind: 0x12),
            HandleKind.TypeSpecification =>
                GuardedProviderDecode.TypeSpec(
                    reader,
                    (TypeSpecificationHandle)handle,
                    provider,
                    context.GenericContext,
                    (TypeNode)new DegradedTypeNode()),
            _ => new DegradedTypeNode(),
        };
    }

    static bool GenericDefinitionsCorrespond(
        GenericTypeNode left,
        GenericTypeNode right)
        => TypeDefinitionsCorrespond(left, right);

    static bool HasGenericShape(TypeNode type)
        => type is GenericTypeNode
            || TryGetNamedDefinition(
                    type,
                    out MetadataTypeNameParts? name,
                    out _)
                && name.IntroducedTypeParameterCounts
                    is { } counts
                && counts.Any(count => count != 0);

    static bool TypeDefinitionsCorrespond(
        TypeNode left,
        TypeNode right)
        => TryGetNamedDefinition(
                left,
                out MetadataTypeNameParts? leftName,
                out ScopedNamedTypeIdentity? leftScope)
            && TryGetNamedDefinition(
                right,
                out MetadataTypeNameParts? rightName,
                out ScopedNamedTypeIdentity? rightScope)
            && NamesCorrespond(leftName, rightName)
            && ScopesCorrespond(leftScope, rightScope);

    static bool HaveSameDefinitionNameDifferentScope(
        TypeNode left,
        TypeNode right)
        => TryGetNamedDefinition(
                left,
                out MetadataTypeNameParts? leftName,
                out ScopedNamedTypeIdentity? leftScope)
            && TryGetNamedDefinition(
                right,
                out MetadataTypeNameParts? rightName,
                out ScopedNamedTypeIdentity? rightScope)
            && NamesCorrespond(leftName, rightName)
            && !ScopesCorrespond(leftScope, rightScope);

    static bool HasUnavailableOrAmbiguousExactLocalDefinition(
        MetadataReader reader,
        TypeNode type)
    {
        if (TryGetNamedDefinition(
                type,
                out _,
                out ScopedNamedTypeIdentity? scope)
            && string.Equals(
                scope.Scope,
                "current",
                StringComparison.Ordinal)
            && !TryFindExactLocalTypeDefinition(
                reader,
                type,
                out _))
        {
            return true;
        }

        foreach (TypeNode child in StructuralChildren(type))
        {
            if (HasUnavailableOrAmbiguousExactLocalDefinition(
                    reader,
                    child))
            {
                return true;
            }
        }

        return false;
    }

    static IEnumerable<TypeNode> StructuralChildren(
        TypeNode type)
        => type switch
        {
            ModifiedTypeNode modified =>
                [modified.Modifier, modified.Inner],
            GenericTypeNode generic =>
                generic.Arguments,
            SZArrayTypeNode array =>
                [array.ElementType],
            MDArrayTypeNode array =>
                [array.ElementType],
            PointerTypeNode pointer =>
                [pointer.ElementType],
            ByRefTypeNode byRef =>
                [byRef.ElementType],
            FunctionPointerTypeNode functionPointer =>
                functionPointer.ChildTypes,
            PinnedTypeNode pinned =>
                [pinned.Inner],
            PassthroughTypeNode passthrough =>
                [passthrough.Inner],
            _ => [],
        };

    static bool TryGetNamedDefinition(
        TypeNode type,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out MetadataTypeNameParts? name,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out ScopedNamedTypeIdentity? scope)
    {
        switch (type)
        {
            case NamedTypeNode
            {
                MetadataName: { } named,
                ScopedIdentity: { } namedScope,
            }:
                name = named;
                scope = namedScope;
                return true;
            case GenericTypeNode
            {
                MetadataName: { } generic,
                ScopedIdentity: { } genericScope,
            }:
                name = generic;
                scope = genericScope;
                return true;
            default:
                name = null;
                scope = null;
                return false;
        }
    }

    static bool NamesCorrespond(
        MetadataTypeNameParts left,
        MetadataTypeNameParts right)
        => string.Equals(
                left.Namespace,
                right.Namespace,
                StringComparison.Ordinal)
            && left.Segments.SequenceEqual(
                right.Segments,
                StringComparer.Ordinal);

    static bool ScopesCorrespond(
        ScopedNamedTypeIdentity left,
        ScopedNamedTypeIdentity right)
        => string.Equals(
                left.Scope,
                right.Scope,
                StringComparison.Ordinal)
            || string.Equals(
                left.PlatformNormalizedScope,
                right.PlatformNormalizedScope,
                StringComparison.Ordinal);

    static bool TryFindExactLocalTypeDefinition(
        MetadataReader reader,
        TypeNode type,
        out TypeDefinitionHandle handle)
    {
        handle = default;
        if (!TryGetNamedDefinition(
                type,
                out MetadataTypeNameParts? requested,
                out ScopedNamedTypeIdentity? scope)
            || !string.Equals(
                scope.Scope,
                "current",
                StringComparison.Ordinal)
            || requested.IntroducedTypeParameterCounts
                is not { } requestedCounts
            || requestedCounts.Count
                != requested.Segments.Count)
        {
            return false;
        }

        if (type is GenericTypeNode generic
            && requestedCounts.Sum()
                != generic.Arguments.Length)
        {
            return false;
        }

        TypeDefinitionHandle match = default;
        foreach (TypeDefinitionHandle candidateHandle
            in reader.TypeDefinitions)
        {
            try
            {
                MetadataTypeNameParts candidate =
                    TypeResolver
                        .GetTypeNamePartsFromDefinition(
                            reader,
                            candidateHandle)
                        .WithIntroducedTypeParameterCounts(
                            GetIntroducedTypeParameterCounts(
                                reader,
                                candidateHandle));
                if (!NamesCorrespond(
                        requested,
                        candidate)
                    || !requestedCounts.SequenceEqual(
                        candidate
                            .IntroducedTypeParameterCounts!))
                {
                    continue;
                }
            }
            catch (Exception exception)
                when (exception
                    is BadImageFormatException
                    or ArgumentException
                    or InvalidOperationException)
            {
                return false;
            }

            if (!match.IsNil)
                return false;
            match = candidateHandle;
        }

        handle = match;
        return !handle.IsNil;
    }

    /// <summary>
    /// True when the implementation type is the declaration type, or derives
    /// from or implements it, proved from same-image base and interface rows.
    ///
    /// The walk is a metadata walk, not a <c>TypeDef</c> walk. A compiler
    /// writes <c>Dog : Middle&lt;int&gt;</c> and <c>Middle&lt;T&gt; :
    /// IContract&lt;T&gt;</c> as <c>TypeSpec</c> rows, so a walk restricted to
    /// <c>TypeDef</c> ancestors stops at the first constructed step and cannot
    /// see the ancestor a covariant return actually reaches. Every step
    /// carries its exact generic arguments, substituted into the next row, and
    /// a match requires both the exact definition token and the exact
    /// instantiation: <c>Dog : Middle&lt;int&gt;</c> never satisfies a
    /// declaration returning <c>Middle&lt;string&gt;</c>. Variance is not
    /// applied here; a variance-bearing pair with corresponding definitions is
    /// decided by <see cref="CompareStructuredReturnTypes"/> before this
    /// predicate is consulted.
    ///
    /// Fails closed. A supertype outside this image, an unrecorded
    /// instantiation, a degraded or undecodable row, a cycle, more than
    /// <see cref="MetadataSafetyPolicy.MaxRelationshipNodes"/> distinct
    /// ancestors, and an exhausted comparison budget all decline rather than
    /// authenticate. Gated by
    /// <c>SameAssemblyOverrideSlot_AuthenticatesCovariantReturnThroughConstructedGenericAncestry</c>,
    /// <c>SameAssemblyOverrideSlot_DeclinesConstructedGenericAncestryWithDifferentArgument</c>,
    /// and
    /// <c>SameAssemblyOverrideSlot_CyclicConstructedAncestryFailsClosed</c>.
    /// </summary>
    static bool IsSameOrDerivedOrImplements(
        MetadataReader reader,
        TypeNode implementationType,
        TypeDefinitionHandle implementationHandle,
        TypeNode declarationType,
        TypeDefinitionHandle declarationHandle,
        OverrideCompatibilityBudget budget)
    {
        if (!TryGetExactInstantiation(
                reader,
                implementationType,
                implementationHandle,
                out ImmutableArray<TypeNode>? implementationArguments)
            || !TryGetExactInstantiation(
                reader,
                declarationType,
                declarationHandle,
                out ImmutableArray<TypeNode>? declarationArguments))
        {
            return false;
        }

        var pending = new Queue<OverrideBaseInstantiation>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var supertypes = new List<OverrideBaseInstantiation>();
        pending.Enqueue(
            new OverrideBaseInstantiation(
                implementationHandle,
                implementationArguments));
        while (pending.Count != 0)
        {
            if (!budget.TryCharge()
                || visited.Count
                    >= MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                return false;
            }

            OverrideBaseInstantiation current = pending.Dequeue();
            if (!visited.Add(InstantiationKey(current)))
                continue;

            if (current.Definition == declarationHandle
                && InstantiationsCorrespond(
                    reader,
                    current.TypeArguments,
                    declarationArguments,
                    budget))
            {
                return true;
            }

            supertypes.Clear();
            OverrideBaseChain.AddDirectSameAssemblySupertypes(
                reader,
                current,
                supertypes);
            foreach (OverrideBaseInstantiation supertype in supertypes)
                pending.Enqueue(supertype);
        }

        return false;
    }

    /// <summary>
    /// The exact instantiation a named or constructed node carries for the
    /// definition it resolved to. A constructed node must supply exactly the
    /// definition's arity, and a non-generic definition must carry no
    /// arguments. A raw generic name with no recorded arguments, an arity
    /// disagreement, and an unreadable row all fail closed, because ancestry
    /// is only ever proved instantiation-exactly.
    /// </summary>
    static bool TryGetExactInstantiation(
        MetadataReader reader,
        TypeNode type,
        TypeDefinitionHandle handle,
        out ImmutableArray<TypeNode>? arguments)
    {
        arguments = null;
        int arity;
        try
        {
            arity = reader
                .GetTypeDefinition(handle)
                .GetGenericParameters()
                .Count;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
            return false;
        }

        if (type is GenericTypeNode generic)
        {
            if (arity == 0
                || generic.Arguments.Length != arity)
            {
                return false;
            }

            arguments = generic.Arguments;
            return true;
        }

        return arity == 0;
    }

    static bool InstantiationsCorrespond(
        MetadataReader reader,
        ImmutableArray<TypeNode>? left,
        ImmutableArray<TypeNode>? right,
        OverrideCompatibilityBudget budget)
    {
        if (left is not { } leftArguments)
            return right is null;

        if (right is not { } rightArguments
            || leftArguments.Length != rightArguments.Length)
        {
            return false;
        }

        for (int index = 0; index < leftArguments.Length; index++)
        {
            if (!budget.TryCharge()
                || !TypeNodesCorrespond(
                    reader,
                    leftArguments[index],
                    rightArguments[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Identifies one ancestry step by its exact definition token and the
    /// exact structural identity of every argument, so a diamond is visited
    /// once while two different instantiations of the same definition stay
    /// distinct. A self-referential base that keeps producing new
    /// instantiations is bounded by the caller's node cap and comparison
    /// budget rather than by this set.
    /// </summary>
    static string InstantiationKey(OverrideBaseInstantiation type)
    {
        var builder = new StringBuilder();
        builder.Append(
            MetadataTokens.GetToken(type.Definition)
                .ToString(CultureInfo.InvariantCulture));
        if (type.TypeArguments is { } arguments)
        {
            foreach (TypeNode argument in arguments)
            {
                builder.Append('|');
                builder.Append(argument.StructuralIdentity());
            }
        }

        return builder.ToString();
    }

    public static MetadataPropertyDeclaration GetProperty(
        MetadataReader reader,
        TypeDefinition typeDef,
        PropertyDefinition property)
    {
        var result = GuardedSignatureText.PropertyText(
            reader,
            property,
            GenericContext.ForType(reader, typeDef));
        var signature = ProjectDecode(result, DegradedMethodSignature, out var status);
        return GetProperty(reader, typeDef, property, signature) with
        {
            SignatureDecodeStatus = status,
        };
    }

    static MetadataPropertyDeclaration GetProperty(
        MetadataReader reader,
        TypeDefinition typeDef,
        PropertyDefinition property,
        MethodSignature<string> signature)
    {
        var accessors = property.GetAccessors();
        var getter = accessors.Getter.IsNil ? default : reader.GetMethodDefinition(accessors.Getter);
        var setter = accessors.Setter.IsNil ? default : reader.GetMethodDefinition(accessors.Setter);

        var bestAccess = BestAccessorAccess(getter, setter, accessors);
        var hasClassMethodImplOverride =
            TryGetAuthenticatedOverridePropertyAccess(
                reader,
                accessors,
                out var authenticatedPropertyAccess);
        if (hasClassMethodImplOverride)
            bestAccess = authenticatedPropertyAccess;
        var primaryAccessor = !accessors.Getter.IsNil ? getter : setter;
        var accessorAttributes = !accessors.Getter.IsNil || !accessors.Setter.IsNil
            ? primaryAccessor.Attributes
            : default;
        var isVirtual = (accessorAttributes & MethodAttributes.Virtual) != 0;
        var isNewSlot = (accessorAttributes & MethodAttributes.NewSlot) != 0;
        var isSourceDeclarable = IsSourceDeclarableAccessibility(bestAccess);
        var isOverride = isSourceDeclarable
            && isVirtual
            && (!isNewSlot || hasClassMethodImplOverride)
            && (accessorAttributes & MethodAttributes.Static) == 0
            && (typeDef.Attributes & TypeAttributes.Interface) == 0;
        var isPublicOrProtected = IsPublicOrProtected(bestAccess);

        var accessorParameters = !accessors.Getter.IsNil
            ? getter.GetParameters()
            : !accessors.Setter.IsNil
                ? setter.GetParameters()
                : default;

        var accessorModels = new List<ApiAccessor>();
        if (!accessors.Getter.IsNil)
        {
            var getterAccess = getter.Attributes & MethodAttributes.MemberAccessMask;
            accessorModels.Add(new ApiAccessor
            {
                Kind = "get",
                Accessibility = AccessorAccessibility(getterAccess, bestAccess),
                ReturnAttributes = ReturnAttributes(reader, getter.GetParameters()).ToList(),
            });
        }

        if (!accessors.Setter.IsNil)
        {
            accessorModels.Add(new ApiAccessor
            {
                Kind = "set",
                Accessibility = AccessorAccessibility(setter.Attributes & MethodAttributes.MemberAccessMask, bestAccess),
                ReturnAttributes = ReturnAttributes(reader, setter.GetParameters()).ToList(),
            });
        }

        var parameters = PropertyParameters(reader, accessorParameters, signature).ToList();
        var name = reader.GetString(property.Name);
        var csharpName = SanitizeIdentifier(name);
        return new MetadataPropertyDeclaration(
            name,
            csharpName,
            AccessibilityKeyword(bestAccess),
            isPublicOrProtected,
            !accessors.Getter.IsNil || !accessors.Setter.IsNil
                ? (accessorAttributes & MethodAttributes.Static) != 0
                : false,
            isSourceDeclarable && (accessorAttributes & MethodAttributes.Abstract) != 0,
            isSourceDeclarable
                && isVirtual
                && (accessorAttributes & MethodAttributes.Abstract) == 0
                && (accessorAttributes & MethodAttributes.Final) == 0
                && isNewSlot
                && !hasClassMethodImplOverride,
            isOverride,
            isOverride && (accessorAttributes & MethodAttributes.Final) != 0,
            new ApiSignature
            {
                ReturnType = signature.ReturnType,
                ReturnAttributes = !accessors.Getter.IsNil
                    ? ReturnAttributes(reader, getter.GetParameters()).ToList()
                    : [],
                MemberName = parameters.Count == 0 ? csharpName : "this[]",
                Parameters = parameters,
                Accessors = accessorModels,
            },
            RenderMemberAttributes(reader, property.GetCustomAttributes()),
            accessors.Getter,
            accessors.Setter);
    }

    static bool TryGetAuthenticatedOverridePropertyAccess(
        MetadataReader reader,
        PropertyAccessors accessors,
        out MethodAttributes access)
    {
        access = default;
        bool followedOverride = false;
        var visited = new HashSet<MethodDefinitionHandle>();
        while (true)
        {
            if (accessors.Getter.IsNil
                && accessors.Setter.IsNil)
            {
                return false;
            }

            MethodDefinition getter =
                accessors.Getter.IsNil
                    ? default
                    : reader.GetMethodDefinition(
                        accessors.Getter);
            MethodDefinition setter =
                accessors.Setter.IsNil
                    ? default
                    : reader.GetMethodDefinition(
                        accessors.Setter);
            MethodAttributes bestAccess =
                BestAccessorAccess(
                    getter,
                    setter,
                    accessors);
            if (accessors.Getter.IsNil
                == accessors.Setter.IsNil)
            {
                if (!followedOverride)
                    return false;

                access = bestAccess;
                return true;
            }

            var accessorHandle = accessors.Getter.IsNil
                ? accessors.Setter
                : accessors.Getter;
            if (!visited.Add(accessorHandle)
                || visited.Count
                    > MetadataSafetyPolicy
                        .MaxRelationshipNodes)
            {
                return false;
            }

            MethodDefinition accessor =
                reader.GetMethodDefinition(
                    accessorHandle);
            if (GetSameAssemblyOverrideSlot(
                    reader,
                    accessor.GetDeclaringType(),
                    accessorHandle) is not { } slot
                || !TryGetPropertyForAccessor(
                    reader,
                    slot.DeclaringType,
                    slot.Method,
                    out PropertyDefinition baseProperty))
            {
                if (!followedOverride)
                    return false;

                access = bestAccess;
                return true;
            }

            followedOverride = true;
            accessors =
                baseProperty.GetAccessors();
        }
    }

    static bool TryGetPropertyForAccessor(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle accessorHandle,
        out PropertyDefinition property)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var candidate = reader.GetPropertyDefinition(propertyHandle);
            var accessors = candidate.GetAccessors();
            if (accessors.Getter == accessorHandle || accessors.Setter == accessorHandle)
            {
                property = candidate;
                return true;
            }
        }

        property = default;
        return false;
    }

    public static MetadataFieldDeclaration GetField(
        MetadataReader reader,
        TypeDefinition typeDef,
        FieldDefinition field)
    {
        var result = GuardedSignatureText.FieldText(
            reader,
            field,
            GenericContext.ForType(reader, typeDef));
        var fieldType = ProjectDecode(result, DegradedType, out var status);
        return GetField(reader, typeDef, field, fieldType) with
        {
            SignatureDecodeStatus = status,
        };
    }

    static MetadataFieldDeclaration GetField(
        MetadataReader reader,
        TypeDefinition typeDef,
        FieldDefinition field,
        string fieldType)
    {
        var attributes = field.Attributes;
        var access = attributes & FieldAttributes.FieldAccessMask;
        return new MetadataFieldDeclaration(
            reader.GetString(field.Name),
            SanitizeIdentifier(reader.GetString(field.Name)),
            AccessibilityKeyword(access),
            (attributes & FieldAttributes.Static) != 0,
            (attributes & FieldAttributes.InitOnly) != 0,
            (attributes & FieldAttributes.Literal) != 0,
            fieldType,
            RenderMemberAttributes(reader, field.GetCustomAttributes()));
    }

    /// <summary>
    /// Whether <paramref name="field"/> is declared <c>volatile</c> — its signature carries
    /// <c>modreq(System.Runtime.CompilerServices.IsVolatile)</c>. Reads the same custom-modifier
    /// evidence (<see cref="TypeNode.HasRequiredModifier"/>) that the API surface uses for
    /// <c>in</c>/<c>ref readonly</c>, so callers do not re-decode the signature to spot the modifier.
    /// </summary>
    public static bool IsVolatileField(MetadataReader reader, FieldDefinition field, GenericContext context)
        => GuardedProviderDecode.Field(reader, field, TypeNodeProvider.Instance, context, (TypeNode)new NamedTypeNode("object", isReferenceType: true))
            .HasRequiredModifier("System.Runtime.CompilerServices", "IsVolatile");

    /// <summary>
    /// The C# generic-constraint clause body for each in-scope generic parameter of
    /// <paramref name="method"/> — its own parameters and its declaring type's —
    /// keyed by parameter name (for example <c>"TOther"</c> to
    /// <c>"INumberBase&lt;TOther&gt;"</c>). Each body already honors the C# ordering
    /// and redundancy rules (<c>class</c>/<c>struct</c>, then base/interface
    /// constraints, then <c>new()</c>; <c>struct</c> implies <c>new()</c> and drops
    /// the <c>ValueType</c> base). Type parameters win over method parameters on a
    /// name clash, since both share one scope inside a method shell. A parameter with
    /// no constraints is omitted. This is the product-owned source of constraint
    /// declaration facts, so consumers do not re-derive the C# rules from metadata.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetGenericConstraintClauses(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var clauses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in TypeParameters(reader, typeDef.GetGenericParameters(), GenericContext.ForType(reader, typeDef)))
            if (SpellableConstraintClause(parameter) is { } clause)
                clauses[parameter.Name] = clause;
        foreach (var parameter in MethodTypeParameters(reader, typeDef, method))
            if (SpellableConstraintClause(parameter) is { } clause)
                clauses.TryAdd(parameter.Name, clause);
        return clauses;
    }

    /// <summary>
    /// The spellable <c>where</c> clause body for a parameter, or null when nothing
    /// remains to spell. C# forbids an explicit <c>System.Object</c> constraint
    /// (<c>CS0702: Constraint cannot be special class 'object'</c>) — and it is
    /// semantically vacuous — so it is dropped. Non-C# compilers can emit it even
    /// though Roslyn does not.
    /// </summary>
    internal static string? SpellableConstraintClause(TypeParameter parameter)
    {
        var structured = parameter.StructuredConstraints;
        List<string> spellable = [];
        for (int i = 0; i < parameter.Constraints.Count; i++)
        {
            var value = parameter.Constraints[i];

            // Without the structured kind we cannot tell an attribute-derived special
            // constraint (class/struct/new()/…) from a type name, so preserve the
            // legacy verbatim spelling (only the vacuous object constraint is dropped).
            // The real callers (GetGenericConstraintClauses) always populate the kind.
            if (structured is not { } kinds || i >= kinds.Count)
            {
                if (value is not ("System.Object" or "Object" or "object"))
                    spellable.Add(value);
                continue;
            }

            // Special constraints are spelled verbatim; type-name constraints are
            // subject to reserved-keyword escaping so a type literally named like a
            // keyword renders as an escaped identifier (global "class" -> "@class").
            if (!kinds[i].IsTypeName)
            {
                spellable.Add(value);
                continue;
            }

            // C# forbids an explicit System.Object constraint (CS0702) and it is
            // vacuous, so drop it. Non-C# compilers can emit it though Roslyn does not.
            if (value is "System.Object" or "Object" or "object")
                continue;
            spellable.Add(EscapeReservedKeywordSegments(value));
        }
        return spellable.Count > 0 ? string.Join(", ", spellable) : null;
    }

    // Escapes every reserved-keyword identifier segment inside a (possibly qualified
    // or generic) constraint type name, so a type literally named like a keyword
    // renders as an escaped identifier (global "class" -> "@class", "N.class" ->
    // "N.@class"). Segments already prefixed with '@' are left untouched.
    static string EscapeReservedKeywordSegments(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            if (char.IsLetter(text[i]) || text[i] == '_')
            {
                int start = i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    i++;
                string token = text[start..i];
                bool alreadyEscaped = start > 0 && text[start - 1] == '@';
                builder.Append(!alreadyEscaped && CSharpKeywords.RequiresDeclarationEscape(token) ? "@" + token : token);
                continue;
            }
            builder.Append(text[i++]);
        }
        return builder.ToString();
    }

    public static string SelfTypeSignature(MetadataReader reader, TypeDefinition typeDef)
    {
        MetadataTypeNameParts name =
            TypeResolver.GetTypeNameParts(reader, typeDef);
        var directTypeParameters = TypeParameterNames(reader, typeDef);
        var typeParameters = directTypeParameters.Count
                >= GenericArity(name.Segments)
            ? directTypeParameters
            : TypeAndDeclaringTypeParameters(reader, typeDef);
        string typeName = TypeResolver.ApplyGenericArguments(
            name.Segments,
            typeParameters);
        return name.Namespace.Length == 0
            ? typeName
            : $"{name.Namespace}.{typeName}";
    }

    public static IReadOnlyList<string> RenderMemberAttributes(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes)
        => AttributeReader.RenderAttributes(reader, attributes, qualifyNames: true);

    public static bool IsPublicOrProtected(MethodDefinition method)
        => IsPublicOrProtected(method.Attributes & MethodAttributes.MemberAccessMask);

    public static bool IsAbstractMethod(MethodDefinition method)
        => IsPublicOrProtected(method)
            && (method.Attributes & MethodAttributes.Abstract) != 0;

    public static bool IsVirtualMethod(MethodDefinition method)
        => IsPublicOrProtected(method)
            && (method.Attributes & MethodAttributes.Virtual) != 0
            && (method.Attributes & MethodAttributes.Abstract) == 0
            && (method.Attributes & MethodAttributes.Final) == 0
            && (method.Attributes & MethodAttributes.NewSlot) != 0;

    /// <summary>
    /// True when the method flags can be represented by a C# abstract member
    /// declaration, including non-API internal accessibility.
    /// </summary>
    public static bool IsSourceDeclarableAbstractMethod(MethodDefinition method)
        => IsSourceDeclarableAccessibility(method.Attributes & MethodAttributes.MemberAccessMask)
            && (method.Attributes & MethodAttributes.Abstract) != 0;

    /// <summary>
    /// True when the method flags can be represented by a C# new-slot virtual
    /// declaration, including non-API internal accessibility.
    /// </summary>
    public static bool IsSourceDeclarableVirtualMethod(MethodDefinition method)
        => IsSourceDeclarableAccessibility(method.Attributes & MethodAttributes.MemberAccessMask)
            && (method.Attributes & MethodAttributes.Virtual) != 0
            && (method.Attributes & MethodAttributes.Abstract) == 0
            && (method.Attributes & MethodAttributes.Final) == 0
            && (method.Attributes & MethodAttributes.NewSlot) != 0;

    public static string AccessibilityKeyword(MethodDefinition method)
        => AccessibilityKeyword(method.Attributes & MethodAttributes.MemberAccessMask);

    internal static string? TypeAccessibility(TypeDefinition type)
        => (type.Attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.NotPublic => "internal",
            TypeAttributes.NestedPrivate => "private",
            TypeAttributes.NestedFamily => "protected",
            TypeAttributes.NestedAssembly => "internal",
            TypeAttributes.NestedFamANDAssem => "private protected",
            TypeAttributes.NestedFamORAssem => "protected internal",
            _ => null,
        };

    static IReadOnlyList<ApiParameter> MethodParameters(
        MetadataReader reader,
        MethodDefinition method,
        MethodSignature<string> signature)
        => Parameters(reader, method.GetParameters(), signature.ParameterTypes);

    static IReadOnlyList<ApiParameter> PropertyParameters(
        MetadataReader reader,
        ParameterHandleCollection accessorParameters,
        MethodSignature<string> signature)
        => Parameters(reader, accessorParameters, signature.ParameterTypes);

    static IReadOnlyList<ApiParameter> Parameters(
        MetadataReader reader,
        ParameterHandleCollection parameterHandles,
        IReadOnlyList<string> parameterTypes)
    {
        var parameters = new List<ApiParameter>();
        for (var index = 0; index < parameterTypes.Count; index++)
        {
            var parameterInfo = GetParameterInfo(reader, parameterHandles, index + 1);
            var name = parameterInfo.Name is { Length: > 0 } parameterName
                ? parameterName
                : $"arg{index}";
            var type = parameterTypes[index];
            string? modifier = null;
            if (type.StartsWith("ref ", StringComparison.Ordinal))
            {
                type = type["ref ".Length..];
                modifier = parameterInfo.RefKind ?? "ref";
            }

            if (parameterInfo.IsParams)
                modifier = "params";

            var attributes = parameterInfo.Attributes.ToList();
            string? defaultValueText = null;
            var hasDefault = false;
            if (parameterInfo.CustomAttributes is { } customAttributes
                && TryFormatAttributedParameterDefault(
                    reader,
                    customAttributes,
                    out var attributedDefaultValue,
                    out var attributedDefaultAttributes))
            {
                hasDefault = true;
                foreach (var attribute in attributedDefaultAttributes)
                {
                    if (!attributes.Contains(attribute, StringComparer.Ordinal))
                        attributes.Add(attribute);
                }

                if (attributedDefaultValue.Length == 0)
                {
                    if (!attributes.Contains("System.Runtime.InteropServices.Optional", StringComparer.Ordinal))
                        attributes.Add("System.Runtime.InteropServices.Optional");
                }
                else
                {
                    defaultValueText = attributedDefaultValue;
                }
            }
            else if (parameterInfo.DefaultParameter is { } defaultParameter)
            {
                hasDefault = true;
                if (TryFormatParameterDefault(reader, defaultParameter, type, out var formattedDefault))
                {
                    defaultValueText = formattedDefault;
                }
                else if (!attributes.Contains("System.Runtime.InteropServices.Optional", StringComparer.Ordinal))
                {
                    attributes.Add("System.Runtime.InteropServices.Optional");
                }
            }

            parameters.Add(new ApiParameter
            {
                Attributes = attributes,
                Name = name,
                Type = type,
                Modifier = modifier,
                HasDefault = hasDefault,
                DefaultValueText = hasDefault ? defaultValueText : null,
            });
        }

        return parameters;
    }

    sealed record ParameterInfo(
        string? Name,
        bool IsParams,
        string? RefKind,
        IReadOnlyList<string> Attributes,
        Parameter? DefaultParameter,
        CustomAttributeHandleCollection? CustomAttributes);

    static ParameterInfo GetParameterInfo(MetadataReader reader, ParameterHandleCollection handles, int sequenceNumber)
    {
        foreach (var handle in handles)
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber != sequenceNumber)
                continue;

            var attributes = parameter.GetCustomAttributes();
            var isParams = AttributeReader.HasAttribute(reader, attributes, "System.ParamArrayAttribute")
                || AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.ParamCollectionAttribute);
            var isOut = (parameter.Attributes & ParameterAttributes.Out) != 0;
            var isIn = (parameter.Attributes & ParameterAttributes.In) != 0;
            var refKind = isOut && !isIn
                ? "out"
                : isIn && !isOut
                    ? "in"
                    : null;
            var defaultParameter = (parameter.Attributes & ParameterAttributes.HasDefault) != 0
                ? parameter
                : (Parameter?)null;
            return new ParameterInfo(
                reader.GetString(parameter.Name),
                isParams,
                refKind,
                AttributeReader.RenderParameterAttributes(reader, handle),
                defaultParameter,
                attributes);
        }

        return new ParameterInfo(null, false, null, [], null, null);
    }

    static IReadOnlyList<string> ReturnAttributes(MetadataReader reader, ParameterHandleCollection handles)
    {
        foreach (var handle in handles)
        {
            if (reader.GetParameter(handle).SequenceNumber == 0)
                return AttributeReader.RenderParameterAttributes(reader, handle);
        }

        return [];
    }

    static string FormatMethodReturnType(MetadataReader reader, string returnType, ParameterHandleCollection handles)
        => returnType.StartsWith("ref ", StringComparison.Ordinal)
            && ReturnIsReadOnlyRef(reader, handles)
            ? $"ref readonly {returnType["ref ".Length..]}"
            : returnType;

    static bool ReturnIsReadOnlyRef(MetadataReader reader, ParameterHandleCollection handles)
    {
        foreach (var handle in handles)
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber == 0
                && (AttributeReader.HasAttribute(reader, parameter.GetCustomAttributes(), KnownAttributeNames.IsReadOnlyAttribute)
                    || AttributeReader.HasAttribute(reader, parameter.GetCustomAttributes(), KnownAttributeNames.RequiresLocationAttribute)))
            {
                return true;
            }
        }
        return false;
    }

    static IReadOnlyList<TypeParameter> MethodTypeParameters(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var context = GenericContext.ForMethod(reader, typeDef, method);
        return TypeParameters(reader, method.GetGenericParameters(), context);
    }

    static IReadOnlyList<TypeParameter> TypeParameters(
        MetadataReader reader,
        IEnumerable<GenericParameterHandle> handles,
        GenericContext context,
        int expectedIndex = 0)
    {
        var parameters = new List<TypeParameter>();
        GenericContext.ValidateParameterIndices(
            reader,
            handles,
            expectedIndex);

        // Shared across the list for the same reason `ApiSurfaceExtractor` shares one:
        // `where T : U` chains run through it, and answering each parameter from scratch
        // rewalks the chain's whole tail, which is quadratic in the number of parameters.
        var chain = new TypeParameterKindClassifier.ChainState();
        foreach (var handle in handles)
        {
            var parameter = reader.GetGenericParameter(handle);
            var constraints = new List<string>();
            var structured = new List<TypeParameterConstraint>();
            var attributes = parameter.Attributes;
            var isStruct = (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
            if (GenericConstraintKeywords.PrimaryKeyword(attributes, nullableFlag: 0, isUnmanaged: false) is { } primaryKeyword)
            {
                constraints.Add(primaryKeyword);
                structured.Add(new TypeParameterConstraint(primaryKeyword, IsTypeName: false));
            }

            foreach (var constraintHandle in parameter.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                if (ConstraintTypeName(reader, constraint.Type, context) is { Length: > 0 } constraintName)
                {
                    if (isStruct && constraintName is "System.ValueType" or "ValueType")
                        continue;
                    constraints.Add(constraintName);
                    structured.Add(new TypeParameterConstraint(constraintName, IsTypeName: true));
                }
            }

            if (GenericConstraintKeywords.NewConstraintKeyword(attributes) is { } newConstraint)
            {
                constraints.Add(newConstraint);
                structured.Add(new TypeParameterConstraint(newConstraint, IsTypeName: false));
            }

            parameters.Add(new TypeParameter
            {
                Name = reader.GetString(parameter.Name),
                Constraints = constraints,
                StructuredConstraints = structured,
                Variance = GenericConstraintKeywords.VarianceKeyword(attributes),
                TypeKind = TypeParameterKindClassifier.Classify(
                    reader,
                    handle,
                    hasValueTypeConstraint: isStruct,
                    hasReferenceTypeConstraint: (attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0,
                    chain),
            });
        }

        return parameters;
    }

    static string? ConstraintTypeName(MetadataReader reader, EntityHandle handle, GenericContext context)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition => TypeResolver.GetFullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
            HandleKind.TypeReference => TypeResolver.GetTypeNameFromReference(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => GuardedSignatureText.TypeSpecText(
                reader,
                (TypeSpecificationHandle)handle,
                context).TryGetValue(out var typeName)
                    ? typeName
                    : null,
            _ => null,
        };

    static bool IsPublicOrProtected(MethodAttributes access)
        => access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    static bool IsSourceDeclarableAccessibility(MethodAttributes access)
        => access is not MethodAttributes.Private
            and not MethodAttributes.PrivateScope;

    static MethodAttributes BestAccessorAccess(
        MethodDefinition getter,
        MethodDefinition setter,
        PropertyAccessors accessors)
    {
        MethodAttributes best = 0;
        if (!accessors.Getter.IsNil)
            best = getter.Attributes & MethodAttributes.MemberAccessMask;
        if (!accessors.Setter.IsNil)
        {
            var setterAccess = setter.Attributes & MethodAttributes.MemberAccessMask;
            if ((int)setterAccess > (int)best)
                best = setterAccess;
        }

        return best;
    }

    static string? AccessorAccessibility(MethodAttributes access, MethodAttributes bestAccess)
        => access == bestAccess ? null : NonPublicAccessibility(access);

    static MethodAttributes AccessibilityValue(string accessibility)
        => accessibility switch
        {
            "private" => MethodAttributes.Private,
            "private protected" => MethodAttributes.FamANDAssem,
            "internal" => MethodAttributes.Assembly,
            "protected" => MethodAttributes.Family,
            "protected internal" => MethodAttributes.FamORAssem,
            _ => MethodAttributes.Public,
        };

    static string AccessibilityKeyword(MethodAttributes access)
        => NonPublicAccessibility(access) ?? "public";

    static string AccessibilityKeyword(FieldAttributes access)
        => NonPublicAccessibility(access) ?? "public";

    static string? NonPublicAccessibility(MethodAttributes access) => access switch
    {
        MethodAttributes.PrivateScope => "private",
        MethodAttributes.Private => "private",
        MethodAttributes.FamANDAssem => "private protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        _ => null,
    };

    static string? NonPublicAccessibility(FieldAttributes access) => access switch
    {
        FieldAttributes.PrivateScope => "private",
        FieldAttributes.Private => "private",
        FieldAttributes.FamANDAssem => "private protected",
        FieldAttributes.Assembly => "internal",
        FieldAttributes.Family => "protected",
        FieldAttributes.FamORAssem => "protected internal",
        _ => null,
    };

    static string? NonPublicAccessibility(string accessibility)
        => accessibility == "public" ? null : accessibility;

    static T ProjectDecode<T>(
        SignatureDecodeResult<T> result,
        T degradedValue,
        out SignatureDecodeStatus? status)
        where T : notnull
    {
        if (result.TryGetValue(out var value))
        {
            status = null;
            return value;
        }

        status = SignatureDecodeStatus.Degraded;
        return degradedValue;
    }

    static (string? Namespace, string Name) GetApiTypeNameParts(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var chain = MetadataRelationshipTraversal
            .WalkTypeDefinitionDeclaringChain(reader, handle)
            .GetValueOrThrow();
        var fullName = TypeResolver.ResolveTypeNameFromDefinition(
            reader,
            handle).GetValueOrThrow();
        var rootNamespace = reader.GetString(
            reader.GetTypeDefinition(chain.Handles[0]).Namespace);
        if (rootNamespace.Length == 0)
            return (null, fullName);

        var prefix = rootNamespace + ".";
        return fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? (rootNamespace, fullName[prefix.Length..])
            : (rootNamespace, fullName);
    }

    static string MethodSignatureText(MetadataMethodDeclaration declaration)
    {
        var parameters = $"({string.Join(", ", declaration.Signature.Parameters.Select(ParameterDeclaration))})";
        var returnType = declaration.Signature.ReturnType ?? "void";
        var name = declaration.Signature.MemberName ?? declaration.MetadataName;
        return $"{returnType} {SanitizeMemberDisplayName(name)}{parameters}";
    }

    static string PropertySignatureText(MetadataPropertyDeclaration declaration)
    {
        var returnType = declaration.Signature.ReturnType ?? "void";
        var accessors = declaration.Signature.Accessors.Count == 0
            ? "{ get; }"
            : "{ " + string.Join(" ", declaration.Signature.Accessors.Select(AccessorText)) + " }";
        return declaration.Signature.Parameters.Count == 0
            ? $"{returnType} {declaration.CSharpName} {accessors}"
            : $"{returnType} this[{string.Join(", ", declaration.Signature.Parameters.Select(ParameterDeclaration))}] {accessors}";
    }

    static string ParameterDeclaration(ApiParameter parameter)
    {
        var attributes = parameter.Attributes.Count == 0
            ? ""
            : $"[{string.Join(", ", parameter.Attributes)}] ";
        string type = EscapeCompatibilityTypeKeywords(parameter.Type);
        string typeWithModifier = string.IsNullOrEmpty(parameter.Modifier)
            ? type
            : $"{parameter.Modifier} {type}";
        var head = string.IsNullOrWhiteSpace(parameter.Name)
            ? typeWithModifier
            : $"{typeWithModifier} {SanitizeIdentifier(parameter.Name)}";
        var declaration = parameter.HasDefault && parameter.DefaultValueText is { Length: > 0 }
            ? $"{head} = {parameter.DefaultValueText}"
            : head;
        return EscapeCompatibilityQualifiedKeywordSegments(attributes + declaration);
    }

    internal static string EscapeCompatibilityTypeKeywords(string type)
    {
        var builder = new StringBuilder(type.Length);
        for (int index = 0; index < type.Length;)
        {
            if (!(char.IsLetter(type[index]) || type[index] == '_'))
            {
                builder.Append(type[index++]);
                continue;
            }

            int end = index + 1;
            while (end < type.Length
                   && (char.IsLetterOrDigit(type[end]) || type[end] == '_'))
            {
                end++;
            }

            string identifier = type[index..end];
            bool isTypeSyntaxKeyword = IsTypeSyntaxKeyword(type, identifier, index, end);
            if ((index == 0 || type[index - 1] != '@')
                && SanitizeIdentifier(identifier) != identifier
                && !isTypeSyntaxKeyword)
            {
                builder.Append('@');
            }
            builder.Append(identifier);
            index = end;
        }
        return builder.ToString();
    }

    static bool IsTypeSyntaxKeyword(string type, string identifier, int start, int end)
    {
        if (identifier is "bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "object" or "short" or "ushort" or "string" or "void")
        {
            return end == type.Length || type[end] != '.';
        }

        if (identifier == "delegate")
            return end < type.Length && type[end] == '*';

        if (type.StartsWith("delegate*", StringComparison.Ordinal)
            && identifier is "ref" or "in" or "out" or "readonly" or "unmanaged")
        {
            return true;
        }

        return start == 0
            && end < type.Length
            && char.IsWhiteSpace(type[end])
            && identifier is "ref" or "in" or "out" or "params" or "readonly" or "scoped";
    }

    // ApiMember.Signature is a legacy compatibility string. Keep its qualified
    // keyword escaping local rather than restoring declaration ownership to Metadata.
    static string EscapeCompatibilityQualifiedKeywordSegments(string signature)
    {
        var builder = new StringBuilder(signature.Length);
        bool inString = false;
        bool inChar = false;
        bool escapedCharacter = false;
        for (int index = 0; index < signature.Length; index++)
        {
            char character = signature[index];
            builder.Append(character);
            if (inString || inChar)
            {
                if (escapedCharacter)
                {
                    escapedCharacter = false;
                    continue;
                }
                if (character == '\\')
                {
                    escapedCharacter = true;
                    continue;
                }
                if (inString && character == '"')
                    inString = false;
                else if (inChar && character == '\'')
                    inChar = false;
                continue;
            }
            if (character == '"')
            {
                inString = true;
                continue;
            }
            if (character == '\'')
            {
                inChar = true;
                continue;
            }
            if (character != '.'
                || index + 1 >= signature.Length
                || signature[index + 1] == '@'
                || !(char.IsLetter(signature[index + 1]) || signature[index + 1] == '_'))
            {
                continue;
            }

            int start = index + 1;
            int end = start + 1;
            while (end < signature.Length
                   && (char.IsLetterOrDigit(signature[end]) || signature[end] == '_'))
            {
                end++;
            }

            string segment = signature[start..end];
            string escaped = SanitizeIdentifier(segment);
            if (escaped != segment)
            {
                builder.Append(escaped);
                index = end - 1;
            }
        }
        return builder.ToString();
    }

    static string AccessorText(ApiAccessor accessor)
        => accessor.Accessibility is { Length: > 0 }
            ? $"{accessor.Accessibility} {accessor.Kind};"
            : $"{accessor.Kind};";

    static bool TryFormatAttributedParameterDefault(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out string defaultValueText,
        out IReadOnlyList<string> defaultAttributes)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            var attributeTypeName = AttributeReader.GetAttributeTypeName(reader, attribute.Constructor);
            if (attributeTypeName == KnownAttributeNames.DecimalConstantAttribute
                && TryReadDecimalConstantAttribute(reader, attribute, out var decimalValue))
            {
                defaultValueText = FormatDecimalDefault(decimalValue);
                defaultAttributes = [];
                return true;
            }

            if (attributeTypeName == KnownAttributeNames.DateTimeConstantAttribute
                && TryReadDateTimeConstantAttribute(reader, attribute, out var ticks))
            {
                defaultValueText = "";
                defaultAttributes =
                [
                    "System.Runtime.InteropServices.Optional",
                    $"System.Runtime.CompilerServices.DateTimeConstant({FormatInt64Default(ticks)})",
                ];
                return true;
            }
        }

        defaultValueText = "";
        defaultAttributes = [];
        return false;
    }

    static bool TryReadDateTimeConstantAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out long ticks)
    {
        if (AttributeDecoder.TryDecode(reader, attribute) is { FixedArguments.Length: 1 } decoded
            && decoded.FixedArguments[0].Value is long value)
        {
            ticks = value;
            return true;
        }

        ticks = 0;
        return false;
    }

    static bool TryReadDecimalConstantAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out decimal value)
    {
        if (AttributeDecoder.TryDecode(reader, attribute) is not { } decoded
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

    static bool TryGetUInt32(object? value, out uint result)
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

    static string FormatDecimalDefault(decimal value)
        => value.ToString("G29", CultureInfo.InvariantCulture) + "m";

    static string FormatInt64Default(long value)
    {
        long minValue = long.MaxValue;
        minValue = -minValue - 1;
        return value == minValue
            ? "long.MinValue"
            : value.ToString(CultureInfo.InvariantCulture) + "L";
    }

    static bool TryFormatParameterDefault(
        MetadataReader reader,
        Parameter parameter,
        string parameterType,
        out string defaultValueText)
    {
        defaultValueText = "";
        if ((parameter.Attributes & ParameterAttributes.HasDefault) == 0)
            return false;

        var constantHandle = parameter.GetDefaultValue();
        if (constantHandle.IsNil)
            return false;

        var constant = reader.GetConstant(constantHandle);
        var blob = reader.GetBlobReader(constant.Value);
        defaultValueText = constant.TypeCode switch
        {
            ConstantTypeCode.Boolean when IsDefaultType(parameterType, "bool") => blob.ReadBoolean() ? "true" : "false",
            ConstantTypeCode.Char when IsDefaultType(parameterType, "char") => $"'{EscapeCharLiteral(blob.ReadChar())}'",
            ConstantTypeCode.SByte when IsDefaultType(parameterType, "sbyte") => blob.ReadSByte().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Byte when IsDefaultType(parameterType, "byte") => blob.ReadByte().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Int16 when IsDefaultType(parameterType, "short") => blob.ReadInt16().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.UInt16 when IsDefaultType(parameterType, "ushort") => blob.ReadUInt16().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Int32 when IsDefaultType(parameterType, "int") => blob.ReadInt32().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Int32 when IsLikelyEnumDefaultType(parameterType) => FormatEnumParameterDefault(reader, blob.ReadInt32(), parameterType),
            ConstantTypeCode.UInt32 when IsDefaultType(parameterType, "uint") => blob.ReadUInt32().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.UInt32 when IsLikelyEnumDefaultType(parameterType) => FormatEnumParameterDefault(reader, blob.ReadUInt32(), parameterType),
            ConstantTypeCode.Int64 when IsDefaultType(parameterType, "long") => blob.ReadInt64().ToString(CultureInfo.InvariantCulture) + "L",
            ConstantTypeCode.Int64 when IsLikelyEnumDefaultType(parameterType) => FormatEnumParameterDefault(reader, blob.ReadInt64(), parameterType),
            ConstantTypeCode.UInt64 when IsDefaultType(parameterType, "ulong") => blob.ReadUInt64().ToString(CultureInfo.InvariantCulture) + "UL",
            ConstantTypeCode.UInt64 when IsLikelyEnumDefaultType(parameterType) => FormatEnumParameterDefault(reader, blob.ReadUInt64(), parameterType),
            ConstantTypeCode.Single when IsDefaultType(parameterType, "float") => FormatParameterSingleDefault(blob.ReadSingle()),
            ConstantTypeCode.Double when IsDefaultType(parameterType, "double") => FormatParameterDoubleDefault(blob.ReadDouble()),
            ConstantTypeCode.String when IsDefaultType(parameterType, "string") => StringLiteral(blob.ReadUTF16(blob.Length)),
            ConstantTypeCode.NullReference when AcceptsNullParameterDefault(parameterType) => "null",
            _ => "",
        };
        return defaultValueText.Length != 0;
    }

    static bool IsDefaultType(string parameterType, string expected)
        => string.Equals(parameterType, expected, StringComparison.Ordinal);

    static bool AcceptsNullParameterDefault(string parameterType)
        => parameterType is not ("bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "short" or "ushort" or "System.DateTime");

    static bool IsLikelyEnumDefaultType(string parameterType)
        => parameterType is not ("bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "short" or "ushort" or "string" or "object" or "System.Boolean" or "System.Byte"
            or "System.SByte" or "System.Char" or "System.Decimal" or "System.Double" or "System.Single"
            or "System.Int32" or "System.UInt32" or "System.IntPtr" or "System.UIntPtr" or "System.Int64"
            or "System.UInt64" or "System.Int16" or "System.UInt16" or "System.String" or "System.Object"
            or "System.DateTime");

    static string FormatEnumParameterDefault(MetadataReader reader, object value, string parameterType)
    {
        if (!TryConvertEnumConstant(value, out var defaultValue))
            return "";

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (TypeResolver.GetTypeName(reader, typeDef.BaseType) != "System.Enum"
                || !string.Equals(TypeResolver.GetFullName(reader, typeDef), parameterType, StringComparison.Ordinal))
            {
                continue;
            }

            string escapedType = EscapeCompatibilityTypeKeywords(parameterType);
            return $"({escapedType}){defaultValue.ToString(CultureInfo.InvariantCulture)}";
        }

        return "";
    }

    static bool TryConvertEnumConstant(object value, out decimal converted)
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

    static string FormatParameterSingleDefault(float value)
    {
        if (float.IsNaN(value))
            return "float.NaN";
        if (float.IsPositiveInfinity(value))
            return "float.PositiveInfinity";
        if (float.IsNegativeInfinity(value))
            return "float.NegativeInfinity";
        return value.ToString("R", CultureInfo.InvariantCulture) + "f";
    }

    static string FormatParameterDoubleDefault(double value)
    {
        if (double.IsNaN(value))
            return "double.NaN";
        if (double.IsPositiveInfinity(value))
            return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(value))
            return "double.NegativeInfinity";
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    static string StringLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char ch in value)
            sb.Append(ch == '"' ? "\\\"" : EscapeCharLiteral(ch));
        sb.Append('"');
        return sb.ToString();
    }

    static string EscapeCharLiteral(char ch) => ch switch
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
        // Bidi overrides are Unicode category Cf, so char.IsControl is false for
        // them and they would reach rendered output raw (issue #3319).
        _ when CSharpIdentifierCore.RequiresLiteralEscape(ch) => $"\\u{(int)ch:x4}",
        _ => ch.ToString(),
    };

    static IReadOnlyList<string> TypeAndDeclaringTypeParameters(MetadataReader reader, TypeDefinition typeDef)
    {
        var parameters = new List<string>();
        TypeDefinitionHandle declaringType = typeDef.GetDeclaringType();
        if (!declaringType.IsNil)
        {
            RelationshipChain<TypeDefinitionHandle> chain =
                MetadataRelationshipTraversal
                    .WalkTypeDefinitionDeclaringChain(
                        reader,
                        declaringType)
                    .GetValueOrThrow();
            foreach (TypeDefinitionHandle handle in chain.Handles)
            {
                parameters.AddRange(
                    TypeParameterNames(
                        reader,
                        reader.GetTypeDefinition(handle)));
            }
        }
        parameters.AddRange(TypeParameterNames(reader, typeDef));
        return parameters;
    }

    static IReadOnlyList<string> TypeParameterNames(
        MetadataReader reader,
        TypeDefinition typeDef)
    {
        GenericParameterHandleCollection handles =
            typeDef.GetGenericParameters();
        GenericContext.ValidateParameterIndices(reader, handles);
        return handles
            .Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name))
            .ToArray();
    }

    /// <summary>
    /// The cumulative arity a metadata full name declares across its components.
    /// Only a canonical <c>`N</c> counts (<see cref="MetadataNameArity"/>), so a
    /// digit run that is name text does not inflate the count.
    /// </summary>
    static int GenericArity(IReadOnlyList<string> metadataNameSegments)
    {
        int arity = 0;
        foreach (string segment in metadataNameSegments)
            arity += MetadataNameArity.OfSegment(segment);

        return arity;
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
    static string SanitizeMemberDisplayName(string name)
        => CSharpIdentifierCore.ContainComposedName(name);

    static string SanitizeIdentifier(string name)
        => CSharpIdentifierCore.ContainIdentifier(name, CSharpKeywords.RequiresDeclarationEscape);
}
