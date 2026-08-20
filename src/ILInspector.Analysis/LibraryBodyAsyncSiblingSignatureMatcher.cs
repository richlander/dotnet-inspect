using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Owns stateless async-sibling signature decoding, identity, compatibility,
/// and bounded display policy.
/// </summary>
internal static class LibraryBodyAsyncSiblingSignatureMatcher
{
    internal static bool HasSupportedAsyncSiblingSignature(MemberRef method)
        => IsSupportedAsyncSiblingType(method.ReturnType)
            && method.ParameterTypes.All(
                IsSupportedAsyncSiblingType)
            && HasSupportedMethodSignatureHeader(method)
            && (method.RequiredParameterCount < 0
                || method.RequiredParameterCount
                    == method.ParameterTypes.Length);

    static bool HasSupportedMethodSignatureHeader(
        MemberRef method)
    {
        const byte Generic = 0x10;
        const byte HasThis = 0x20;
        const byte Supported = Generic | HasThis;
        byte header = method.SignatureHeader;
        return (header & ~Supported) == 0
            && ((header & Generic) != 0)
                == (method.GenericArity > 0)
            && ((header & HasThis) != 0)
                == method.HasThis;
    }

    internal static bool IsSupportedAsyncSiblingType(TypeRef type)
    {
        var pending = new Stack<TypeRef>();
        var visited = new HashSet<TypeRef>(
            ReferenceEqualityComparer.Instance);
        pending.Push(type);
        while (pending.Count > 0)
        {
            TypeRef current = pending.Pop();
            if (!visited.Add(current))
                continue;
            if (visited.Count
                > MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                throw new BadImageFormatException(
                    "The constructed type classification exceeds the metadata relationship limit.");
            }
            if (current.Kind == TypeRefKind.Unsupported)
                return false;
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            foreach (TypeRef argument in current.TypeArguments)
                pending.Push(argument);
        }
        return true;
    }

    internal static MemberRef? DecodeAsyncSibling(
        MetadataReader reader,
        TypeDefinition declaringDefinition,
        MethodDefinition methodDefinition,
        MemberRef callee,
        bool requireAsyncReturn = true)
    {
        var scope = new GenericScope(
            GenericParameterNames(
                reader,
                declaringDefinition.GetGenericParameters()),
            GenericParameterNames(
                reader,
                methodDefinition.GetGenericParameters()));
        if (!SignatureBlobGuard.IsSafeToDecode(
                reader,
                methodDefinition.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            return null;
        }

        var signature = methodDefinition.DecodeSignature(
            TypeRefDecoder.Instance,
            scope);
        bool metadataIsInstance =
            (methodDefinition.Attributes
                & MethodAttributes.Static) == 0;
        if (signature.Header.IsInstance
                != metadataIsInstance
            || !HasExactGenericParameters(
                reader,
                methodDefinition.GetGenericParameters(),
                signature.GenericParameterCount))
        {
            return null;
        }
        ImmutableArray<TypeRef> typeArguments =
            callee.DeclaringType.Kind
                == TypeRefKind.GenericInstance
                    ? callee.DeclaringType.TypeArguments
                    : [];
        ImmutableArray<TypeRef> methodArguments =
            callee.TypeArguments;
        var candidate = new MemberRef(
            callee.DeclaringType,
            reader.GetString(methodDefinition.Name),
            [.. signature.ParameterTypes.Select(
                parameter => parameter.Instantiate(
                    typeArguments,
                    methodArguments))],
            signature.ReturnType.Instantiate(
                typeArguments,
                methodArguments),
            MemberKind.Method)
        {
            TypeArguments = methodArguments,
            OpenParameterTypes = signature.ParameterTypes,
            OpenReturnType = signature.ReturnType,
            HasThis = signature.Header.IsInstance,
            SignatureHeader = signature.Header.RawValue,
            RequiredParameterCount =
                signature.RequiredParameterCount,
            TrailingParameterCanBeOmitted =
                TrailingParameterCanBeOmitted(
                    reader,
                    methodDefinition,
                    signature.ParameterTypes.Length),
            ParameterDirections =
                MemberResolver.ParameterDirections(
                    reader,
                    methodDefinition,
                    signature.ParameterTypes),
            GenericArity = signature.GenericParameterCount,
        };
        return candidate.HasThis == callee.HasThis
            && candidate.GenericArity == callee.GenericArity
            && (!requireAsyncReturn
                || AsyncReturnMatches(
                    callee.ReturnType,
                    candidate.ReturnType))
                ? candidate
                : null;
    }

    static bool HasExactGenericParameters(
        MetadataReader reader,
        GenericParameterHandleCollection parameters,
        int signatureCount)
    {
        if (parameters.Count != signatureCount)
            return false;

        int expectedIndex = 0;
        foreach (var handle in parameters)
        {
            if (reader.GetGenericParameter(handle).Index
                != expectedIndex++)
            {
                return false;
            }
        }
        return true;
    }

    internal static ImmutableArray<string> GenericParameterNames(
        MetadataReader reader,
        GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(
            handles.Count);
        foreach (var handle in handles)
        {
            names.Add(
                reader.GetString(
                    reader.GetGenericParameter(handle).Name));
        }
        return names.MoveToImmutable();
    }

    internal static bool ParametersMatchAsyncSibling(
        MemberRef synchronous,
        MemberRef asynchronous)
    {
        int synchronousCount =
            synchronous.ParameterTypes.Length;
        int asynchronousCount =
            asynchronous.ParameterTypes.Length;
        if (asynchronousCount != synchronousCount
                && asynchronousCount != synchronousCount + 1)
        {
            return false;
        }

        for (int i = 0; i < synchronousCount; i++)
        {
            if (!AsyncSiblingTypesMatch(
                    synchronous.ParameterTypes[i],
                    asynchronous.ParameterTypes[i])
                || !ParameterDirectionsMatch(
                    synchronous,
                    asynchronous,
                    i))
            {
                return false;
            }
        }
        return asynchronousCount == synchronousCount
            || IsCancellationToken(
                asynchronous.ParameterTypes[^1])
                && asynchronous
                    .TrailingParameterCanBeOmitted;
    }

    static bool ParameterDirectionsMatch(
        MemberRef left,
        MemberRef right,
        int index)
    {
        ParameterDirection leftDirection =
            ParameterDirectionAt(left, index);
        ParameterDirection rightDirection =
            ParameterDirectionAt(right, index);
        return leftDirection
                != ParameterDirection.UnknownByRef
            && rightDirection
                != ParameterDirection.UnknownByRef
            && leftDirection == rightDirection;
    }

    static ParameterDirection ParameterDirectionAt(
        MemberRef member,
        int index)
    {
        if (member.ParameterTypes[index].Kind
            != TypeRefKind.ByRef)
        {
            return ParameterDirection.Value;
        }
        return member.ParameterDirections.Length
                == member.ParameterTypes.Length
            ? member.ParameterDirections[index]
            : ParameterDirection.UnknownByRef;
    }

    internal static bool AsyncSiblingMethodMatchesSource(
        MemberRef candidate,
        MethodIdentity method)
        => SameTypeDefinition(
                candidate.DeclaringType,
                method.DeclaringType)
            && candidate.Name == method.Name
            && AsyncSiblingTypesMatch(
                SourceFrameParameters(candidate),
                method.ParameterTypes)
            && AsyncSiblingTypesMatch(
                SourceFrameReturn(candidate),
                method.ReturnType)
            && candidate.HasThis == !method.IsStatic
            && candidate.GenericArity
                == method.GenericArity
            && candidate.SignatureHeader
                == method.SignatureHeader
            && candidate.RequiredParameterCount
                == method.RequiredParameterCount;

    internal static bool AsyncSiblingDeclarationsMatch(
        MemberRef left,
        MemberRef right)
        => SameTypeDefinition(
                left.DeclaringType,
                right.DeclaringType)
            && left.Name == right.Name
            && AsyncSiblingTypesMatch(
                SourceFrameParameters(left),
                SourceFrameParameters(right))
            && AsyncSiblingTypesMatch(
                SourceFrameReturn(left),
                SourceFrameReturn(right))
            && left.HasThis == right.HasThis
            && left.GenericArity == right.GenericArity
            && left.SignatureHeader
                == right.SignatureHeader
            && left.RequiredParameterCount
                == right.RequiredParameterCount;

    internal static ImmutableArray<TypeRef>
        SourceFrameParameters(MemberRef member)
    {
        ImmutableArray<TypeRef> typeArguments =
            member.DeclaringType.Kind
                == TypeRefKind.GenericInstance
                    ? member.DeclaringType.TypeArguments
                    : [];
        return
        [
            .. member.OpenSignatureParameters.Select(
                parameter => parameter.Instantiate(
                    typeArguments,
                    [])),
        ];
    }

    internal static TypeRef SourceFrameReturn(MemberRef member)
    {
        ImmutableArray<TypeRef> typeArguments =
            member.DeclaringType.Kind
                == TypeRefKind.GenericInstance
                    ? member.DeclaringType.TypeArguments
                    : [];
        return member.OpenSignatureReturn.Instantiate(
            typeArguments,
            []);
    }

    internal static bool AsyncSiblingMethodsMatch(
        MemberRef left,
        MemberRef right)
        => SameTypeDefinition(
                left.DeclaringType,
                right.DeclaringType)
            && left.Name == right.Name
            && AsyncSiblingTypesMatch(
                left.ParameterTypes,
                right.ParameterTypes)
            && AsyncSiblingTypesMatch(
                left.ReturnType,
                right.ReturnType)
            && AsyncSiblingTypesMatch(
                left.TypeArguments,
                right.TypeArguments)
            && AsyncSiblingTypesMatch(
                left.OpenSignatureParameters,
                right.OpenSignatureParameters)
            && AsyncSiblingTypesMatch(
                left.OpenSignatureReturn,
                right.OpenSignatureReturn)
            && left.HasThis == right.HasThis
            && left.GenericArity == right.GenericArity
            && left.SignatureHeader
                == right.SignatureHeader
            && left.RequiredParameterCount
                == right.RequiredParameterCount;

    internal static bool TrailingParameterCanBeOmitted(
        MetadataReader reader,
        MethodDefinition method,
        int parameterCount)
    {
        if (parameterCount == 0)
            return false;

        ParameterHandle trailing = default;
        foreach (var handle in method.GetParameters())
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber != parameterCount)
                continue;

            if (!trailing.IsNil)
                return false;
            trailing = handle;
        }
        if (trailing.IsNil)
            return false;

        var trailingParameter =
            reader.GetParameter(trailing);
        ConstantHandle defaultHandle =
            trailingParameter.GetDefaultValue();
        const ParameterAttributes required =
            ParameterAttributes.Optional
            | ParameterAttributes.HasDefault;
        if ((trailingParameter.Attributes & required)
                != required
            || defaultHandle.IsNil)
        {
            return false;
        }

        Constant value = reader.GetConstant(
            defaultHandle);
        if (value.TypeCode
            != ConstantTypeCode.NullReference)
        {
            return false;
        }
        byte[] bytes = reader.GetBlobBytes(value.Value);
        return bytes.Length == sizeof(int)
            && bytes.All(value => value == 0);
    }

    internal static bool SameTypeDefinition(TypeRef left, TypeRef right)
    {
        TypeRef leftDefinition = left.Kind
            == TypeRefKind.GenericInstance
                ? left.ElementType ?? left
                : left;
        TypeRef rightDefinition = right.Kind
            == TypeRefKind.GenericInstance
                ? right.ElementType ?? right
                : right;
        return AsyncSiblingTypesMatch(
            leftDefinition,
            rightDefinition);
    }

    static ResolvableTypeReference? DefinitionResolution(
        TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? (type.ElementType ?? type).Resolution
            : type.Resolution;

    internal static string ExactAsyncSiblingMemberIdentity(
        MemberRef member)
    {
        var identity = new System.Text.StringBuilder();
        AppendAsyncSiblingTypeIdentity(
            identity,
            member.DeclaringType);
        identity.Append('|');
        AppendIdentityField(identity, member.Name);
        identity.Append('|').Append(member.SignatureHeader)
            .Append('|').Append(member.RequiredParameterCount)
            .Append('|').Append(member.GenericArity)
            .Append('|').Append(member.HasThis);
        foreach (TypeRef type in member.ParameterTypes)
        {
            identity.Append('|');
            AppendAsyncSiblingTypeIdentity(identity, type);
        }
        identity.Append('|');
        AppendAsyncSiblingTypeIdentity(
            identity,
            member.ReturnType);
        foreach (TypeRef type in member.TypeArguments)
        {
            identity.Append('|');
            AppendAsyncSiblingTypeIdentity(identity, type);
        }
        foreach (ParameterDirection direction
            in member.ParameterDirections)
        {
            identity.Append('|')
                .Append((int)direction);
        }
        return identity.ToString();
    }

    internal static void AppendAsyncSiblingTypeIdentity(
        System.Text.StringBuilder identity,
        TypeRef type)
    {
        var visited = new Dictionary<TypeRef, int>(
            ReferenceEqualityComparer.Instance);
        AppendAsyncSiblingTypeIdentity(
            identity,
            type,
            visited);
    }

    internal static string AsyncSiblingTypeIdentity(
        TypeRef type)
    {
        var identity = new StringBuilder();
        AppendAsyncSiblingTypeIdentity(identity, type);
        return identity.ToString();
    }

    static void AppendAsyncSiblingTypeIdentity(
        System.Text.StringBuilder identity,
        TypeRef type,
        Dictionary<TypeRef, int> visited)
    {
        if (visited.TryGetValue(type, out int prior))
        {
            identity.Append('#').Append(prior).Append(';');
            return;
        }
        if (visited.Count
            >= MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            throw new BadImageFormatException(
                "The constructed type identity exceeds the metadata relationship limit.");
        }
        int nodeId = visited.Count;
        visited.Add(type, nodeId);
        identity.Append('{').Append(nodeId).Append(':');
        identity.Append((int)type.Kind).Append(';');
        AppendIdentityField(identity, type.Assembly);
        AppendIdentityField(identity, type.Namespace);
        AppendIdentityField(identity, type.Name);
        identity.Append(type.Rank).Append(';')
            .Append(type.GenericParameterIndex)
            .Append(';')
            .Append(type.RawTypeKind)
            .Append(';');
        AppendIdentityValues(identity, type.ArraySizes);
        AppendIdentityValues(
            identity,
            type.ArrayLowerBounds);
        AppendIdentityField(
            identity,
            type.UnsupportedReason);
        if (type.Resolution is { } resolution)
        {
            identity.Append('@');
            AppendIdentityField(
                identity,
                resolution.Type.Namespace);
            foreach (string segment
                in resolution.Type.Segments)
            {
                AppendIdentityField(identity, segment);
            }
            switch (resolution.Origin)
            {
                case TypeReferenceOrigin.AssemblyReference assembly:
                    identity.Append("@A");
                    AppendIdentityField(
                        identity,
                        assembly.Assembly.Name);
                    AppendIdentityField(
                        identity,
                        assembly.Assembly.Version?.ToString());
                    AppendIdentityField(
                        identity,
                        assembly.Assembly.Culture);
                    AppendIdentityField(
                        identity,
                        assembly.Assembly.PublicKeyToken);
                    break;
                case TypeReferenceOrigin.CurrentAssembly current:
                    identity.Append("@C");
                    AppendIdentityField(
                        identity,
                        current.Assembly?.Name);
                    AppendIdentityField(
                        identity,
                        current.Assembly?.Version
                            ?.ToString());
                    AppendIdentityField(
                        identity,
                        current.Assembly?.Culture);
                    AppendIdentityField(
                        identity,
                        current.Assembly
                            ?.PublicKeyToken);
                    break;
                case TypeReferenceOrigin.IntrinsicCoreLibrary:
                    identity.Append("@I");
                    break;
                case TypeReferenceOrigin.ModuleReference module:
                    identity.Append("@M");
                    AppendIdentityField(
                        identity,
                        module.ModuleName);
                    break;
            }
        }
        if (type.ElementType is not null)
        {
            identity.Append('[');
            AppendAsyncSiblingTypeIdentity(
                identity,
                type.ElementType,
                visited);
            identity.Append(']');
        }
        foreach (TypeRef argument in type.TypeArguments)
        {
            identity.Append('<');
            AppendAsyncSiblingTypeIdentity(
                identity,
                argument,
                visited);
            identity.Append('>');
        }
        identity.Append('}');
    }

    static void AppendIdentityValues(
        System.Text.StringBuilder identity,
        ImmutableArray<int> values)
    {
        identity.Append(values.Length).Append(':');
        foreach (int value in values)
            identity.Append(value).Append(',');
        identity.Append(';');
    }

    static void AppendIdentityField(
        System.Text.StringBuilder identity,
        string? value)
    {
        if (value is null)
        {
            identity.Append("-1:");
            return;
        }
        identity.Append(value.Length)
            .Append(':')
            .Append(value);
    }

    internal static bool AsyncSiblingTypesMatch(
        ImmutableArray<TypeRef> left,
        ImmutableArray<TypeRef> right)
    {
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (!AsyncSiblingTypesMatch(left[i], right[i]))
                return false;
        }
        return true;
    }

    internal static bool AsyncSiblingTypesMatch(
        TypeRef left,
        TypeRef right)
    {
        var pending = new Stack<(TypeRef Left, TypeRef Right)>();
        var visited = new HashSet<(TypeRef Left, TypeRef Right)>(
            TypeRefPairReferenceComparer.Instance);
        pending.Push((left, right));
        while (pending.Count > 0)
        {
            (TypeRef currentLeft, TypeRef currentRight) =
                pending.Pop();
            if (!visited.Add((currentLeft, currentRight)))
                continue;
            if (visited.Count
                > MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                throw new BadImageFormatException(
                    "The constructed type comparison exceeds the metadata relationship limit.");
            }
            if (currentLeft.Kind != currentRight.Kind
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    currentLeft.Assembly,
                    currentRight.Assembly)
                || currentLeft.Namespace != currentRight.Namespace
                || currentLeft.Name != currentRight.Name
                || currentLeft.Rank != currentRight.Rank
                || currentLeft.RawTypeKind
                    != currentRight.RawTypeKind
                || !currentLeft.ArraySizes.AsSpan()
                    .SequenceEqual(
                        currentRight.ArraySizes.AsSpan())
                || !currentLeft.ArrayLowerBounds.AsSpan()
                    .SequenceEqual(
                        currentRight.ArrayLowerBounds.AsSpan())
                || currentLeft.GenericParameterIndex
                    != currentRight.GenericParameterIndex
                || currentLeft.UnsupportedReason
                    != currentRight.UnsupportedReason
                || currentLeft.TypeArguments.Length
                    != currentRight.TypeArguments.Length
                || (currentLeft.ElementType is null)
                    != (currentRight.ElementType is null))
            {
                return false;
            }

            ResolvableTypeReference? leftResolution =
                DefinitionResolution(currentLeft);
            ResolvableTypeReference? rightResolution =
                DefinitionResolution(currentRight);
            if (leftResolution is not null
                && rightResolution is not null
                && leftResolution.Type
                    != rightResolution.Type)
            {
                return false;
            }

            TypeRef leftDefinition =
                DefinitionType(currentLeft);
            TypeRef rightDefinition =
                DefinitionType(currentRight);
            bool coreLibraryType =
                leftDefinition.Assembly
                    == TypeRef.CoreLibrary
                && rightDefinition.Assembly
                    == TypeRef.CoreLibrary;
            if (coreLibraryType
                && !AreTrustedCoreLibraryFacades(
                    currentLeft,
                    currentRight))
            {
                return false;
            }
            if (!coreLibraryType
                && !ExactNonCoreOriginsMatch(
                    leftResolution,
                    rightResolution))
            {
                return false;
            }

            if (currentLeft.ElementType is not null)
            {
                pending.Push((
                    currentLeft.ElementType,
                    currentRight.ElementType!));
            }
            for (int i = 0;
                i < currentLeft.TypeArguments.Length;
                i++)
            {
                pending.Push((
                    currentLeft.TypeArguments[i],
                    currentRight.TypeArguments[i]));
            }
        }
        return true;
    }

    static bool ExactNonCoreOriginsMatch(
        ResolvableTypeReference? left,
        ResolvableTypeReference? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return (left.Origin, right.Origin) switch
        {
            (TypeReferenceOrigin.AssemblyReference l,
                TypeReferenceOrigin.AssemblyReference r) =>
                l.Assembly.IsEquivalentTo(r.Assembly),
            (TypeReferenceOrigin.CurrentAssembly l,
                TypeReferenceOrigin.CurrentAssembly r) =>
                l.Assembly is not null
                && r.Assembly is not null
                && l.Assembly.IsEquivalentTo(r.Assembly),
            (TypeReferenceOrigin.CurrentAssembly l,
                TypeReferenceOrigin.AssemblyReference r) =>
                l.Assembly is not null
                && l.Assembly.IsEquivalentTo(r.Assembly),
            (TypeReferenceOrigin.AssemblyReference l,
                TypeReferenceOrigin.CurrentAssembly r) =>
                r.Assembly is not null
                && l.Assembly.IsEquivalentTo(r.Assembly),
            (TypeReferenceOrigin.ModuleReference l,
                TypeReferenceOrigin.ModuleReference r) =>
                l.ModuleName == r.ModuleName,
            _ => false,
        };
    }

    sealed class TypeRefPairReferenceComparer
        : IEqualityComparer<(TypeRef Left, TypeRef Right)>
    {
        internal static TypeRefPairReferenceComparer Instance
            { get; } = new();

        public bool Equals(
            (TypeRef Left, TypeRef Right) x,
            (TypeRef Left, TypeRef Right) y)
            => ReferenceEquals(x.Left, y.Left)
                && ReferenceEquals(x.Right, y.Right);

        public int GetHashCode(
            (TypeRef Left, TypeRef Right) pair)
            => HashCode.Combine(
                RuntimeHelpers.GetHashCode(pair.Left),
                RuntimeHelpers.GetHashCode(pair.Right));
    }

    static bool AreTrustedCoreLibraryFacades(
        TypeRef left,
        TypeRef right)
    {
        TypeRef leftDefinition = DefinitionType(left);
        TypeRef rightDefinition = DefinitionType(right);
        return leftDefinition.Assembly == TypeRef.CoreLibrary
            && rightDefinition.Assembly == TypeRef.CoreLibrary
            && leftDefinition.TrustedFrameworkAssembly
            && rightDefinition.TrustedFrameworkAssembly;
    }

    internal static TypeRef DefinitionType(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;

    static bool IsCancellationToken(TypeRef type)
    {
        TypeRef definition = type.Kind
            == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
        return FrameworkIdentity.IsKnownFrameworkType(
            definition,
            "System.Threading",
            "System.Threading",
            "CancellationToken");
    }

    internal static bool IsAsyncReturnType(TypeRef type)
    {
        TypeRef definition = type.Kind
            == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
        return IsTaskContractType(definition, "Task")
            || IsTaskContractType(definition, "Task`1")
            || IsTaskContractType(definition, "ValueTask")
            || IsTaskContractType(definition, "ValueTask`1")
            || IsAsyncEnumerableContractType(
                definition,
                "IAsyncEnumerable`1");
    }

    static bool AsyncReturnMatches(
        TypeRef synchronous,
        TypeRef asynchronous)
    {
        TypeRef definition = asynchronous.Kind
            == TypeRefKind.GenericInstance
                ? asynchronous.ElementType ?? asynchronous
                : asynchronous;
        if (IsTaskContractType(definition, "Task")
            || IsTaskContractType(
                definition,
                "ValueTask"))
        {
            return FrameworkIdentity.IsCoreLibraryType(
                synchronous,
                "System",
                "Void");
        }

        if (asynchronous.Kind != TypeRefKind.GenericInstance
            || asynchronous.TypeArguments.Length != 1)
        {
            return false;
        }

        if (IsTaskContractType(definition, "Task`1")
            || IsTaskContractType(
                definition,
                "ValueTask`1"))
        {
            return AsyncSiblingTypesMatch(
                synchronous,
                asynchronous.TypeArguments[0]);
        }

        if (!IsAsyncEnumerableContractType(
                definition,
                "IAsyncEnumerable`1")
            || synchronous.Kind
                != TypeRefKind.GenericInstance
            || synchronous.TypeArguments.Length != 1)
        {
            return false;
        }
        TypeRef synchronousDefinition =
            synchronous.ElementType ?? synchronous;
        return IsEnumerableContractType(
                synchronousDefinition)
            && AsyncSiblingTypesMatch(
                synchronous.TypeArguments[0],
                asynchronous.TypeArguments[0]);
    }

    static bool IsTaskContractType(
        TypeRef type,
        string name)
        => FrameworkIdentity.IsKnownFrameworkType(
                type,
                "System.Threading.Tasks",
                "System.Threading.Tasks",
                name)
            || name.StartsWith(
                    "ValueTask",
                    StringComparison.Ordinal)
                && FrameworkIdentity.IsKnownFrameworkType(
                    type,
                    "System.Threading.Tasks.Extensions",
                    "System.Threading.Tasks",
                    name);

    static bool IsAsyncEnumerableContractType(
        TypeRef type,
        string name)
        => FrameworkIdentity.IsCoreLibraryType(
                type,
                "System.Collections.Generic",
                name)
            || FrameworkIdentity.IsKnownFrameworkType(
                type,
                "Microsoft.Bcl.AsyncInterfaces",
                "System.Collections.Generic",
                name);

    static bool IsEnumerableContractType(TypeRef type)
        => FrameworkIdentity.IsCoreLibraryType(
                type,
                "System.Collections.Generic",
                "IEnumerable`1")
            || FrameworkIdentity.IsKnownFrameworkType(
                type,
                "System.Collections",
                "System.Collections.Generic",
                "IEnumerable`1");

    internal static string FormatMember(MemberRef member)
    {
        EnsureAsyncSiblingDisplayIsBounded(member);
        return $"{member.DeclaringType.ToQualifiedDisplayString()}"
            + $"::{member.Name}("
            + string.Join(
                ", ",
                member.ParameterTypes.Select(
                    parameter =>
                        parameter.ToQualifiedDisplayString()))
            + ")";
    }

    internal static void EnsureAsyncSiblingDisplayIsBounded(TypeRef type)
    {
        long characters = 0;
        EnsureAsyncSiblingTypeDisplayIsBounded(
            type,
            ref characters);
    }

    internal static void EnsureAsyncSiblingDisplayIsBounded(
        MemberRef member)
    {
        long characters =
            member.Name.Length
            + (long)member.ParameterTypes.Length * 2
            + 4;
        EnsureAsyncSiblingTypeDisplayIsBounded(
            member.DeclaringType,
            ref characters);
        foreach (TypeRef parameter in member.ParameterTypes)
        {
            EnsureAsyncSiblingTypeDisplayIsBounded(
                parameter,
                ref characters);
        }
    }

    static void EnsureAsyncSiblingTypeDisplayIsBounded(
        TypeRef type,
        ref long characters)
    {
        const int MaxDisplayCharacters = 64 * 1024;
        var pending = new Stack<TypeRef>();
        pending.Push(type);
        int nodes = 0;
        while (pending.Count > 0)
        {
            TypeRef current = pending.Pop();
            long arrayRankCharacters =
                current.Kind == TypeRefKind.Array
                    ? current.Rank
                    : 0;
            if (++nodes
                    > MetadataSafetyPolicy.MaxRelationshipNodes
                || current.Kind == TypeRefKind.Array
                    && current.Rank <= 0
                || characters
                    > MaxDisplayCharacters
                        - current.Namespace.Length
                        - current.Name.Length
                        - 16
                        - arrayRankCharacters)
            {
                throw new BadImageFormatException(
                    "The constructed type display exceeds the analysis output limit.");
            }
            characters += current.Namespace.Length
                + current.Name.Length
                + 16
                + arrayRankCharacters;
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            foreach (TypeRef argument in current.TypeArguments)
                pending.Push(argument);
        }
    }

}
