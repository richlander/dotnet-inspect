using System.Reflection.Metadata;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

/// <summary>
/// Execution-scoped logical-owner attribution for Library body use. It is the
/// Roslyn-exact fast path described in
/// <c>docs/design/analysis-library-body-use.md#logical-ownership</c>: it reads
/// Roslyn's emission shape directly and does not authenticate it.
/// </summary>
/// <remarks>
/// Work is linear in the image's metadata. Each state-machine host Type is
/// scanned once, so each method's custom attributes are read at most once;
/// each claimed state-machine Type's <c>MethodImpl</c> rows are read once;
/// each attribute value blob is parsed once, within the type-name bound;
/// each lifted-method name is classified once, within the type-name bound;
/// attribute constructors and Type attribute answers are memoized; and a
/// declaring-chain walk is bounded by
/// <see cref="MetadataSafetyPolicy.MaxRelationshipNodes"/>. No other body is
/// decoded.
/// </remarks>
internal sealed class BodyUseOwnerAttribution(MetadataReader reader)
{
    const string CompilerServices = "System.Runtime.CompilerServices";

    static readonly string[] AsyncRoles = ["MoveNext", "SetStateMachine"];
    static readonly string[] AsyncIteratorRoles =
        ["MoveNext", "SetStateMachine", "MoveNextAsync", "DisposeAsync"];
    static readonly string[] IteratorRoles = ["MoveNext", "Dispose"];

    readonly MetadataReader _reader = reader;
    readonly Dictionary<EntityHandle, string[]?> _stateMachineConstructors = [];
    readonly Dictionary<EntityHandle, bool> _compilerGeneratedConstructors = [];
    readonly Dictionary<BlobHandle, string?> _stateMachineLeaves = [];
    readonly Dictionary<StringHandle, LiftedMethodNameKind> _liftedMethodNames = [];
    readonly Dictionary<TypeDefinitionHandle, bool> _compilerGeneratedTypes = [];
    readonly HashSet<TypeDefinitionHandle> _scannedHosts = [];
    readonly HashSet<TypeDefinitionHandle> _malformedHosts = [];
    readonly Dictionary<MethodDefinitionHandle, MethodDefinitionHandle> _kickoffByRole = [];

    /// <summary>
    /// The body's logical source Type, or <see cref="BodyUseOwner.PhysicalOnly"/>
    /// when the body has no Roslyn-shaped logical owner.
    /// </summary>
    internal BodyUseOwner Attribute(MethodDefinitionHandle method)
    {
        MethodDefinition definition = _reader.GetMethodDefinition(method);
        MethodDefinitionHandle owner = method;
        if (KickoffOf(method, definition.GetDeclaringType()) is { } kickoff)
        {
            owner = kickoff;
            definition = _reader.GetMethodDefinition(kickoff);
        }

        TypeDefinitionHandle ownerType = definition.GetDeclaringType();
        if (_reader.StringComparer.StartsWith(definition.Name, "<"))
        {
            LiftedMethodNameKind name = ClassifyLiftedMethodName(definition.Name);
            if (name == LiftedMethodNameKind.Canonical)
            {
                return TryLiftedHost(ownerType, out TypeDefinitionHandle host)
                    ? BodyUseOwner.Logical(host)
                    : BodyUseOwner.Rejected;
            }
            if (name == LiftedMethodNameKind.Rejected)
                return BodyUseOwner.Rejected;
        }

        return IsCompilerGenerated(definition.GetCustomAttributes())
            || IsCompilerGeneratedType(ownerType)
            ? BodyUseOwner.PhysicalOnly
            : BodyUseOwner.Logical(ownerType);
    }

    LiftedMethodNameKind ClassifyLiftedMethodName(StringHandle handle)
    {
        if (_liftedMethodNames.TryGetValue(
                handle,
                out LiftedMethodNameKind kind))
        {
            return kind;
        }

        int encodedBytes = _reader.GetBlobReader(handle).Length;
        if (encodedBytes
            > MetadataSafetyPolicy.MaxTypeNameCharacters * 3)
        {
            kind = LiftedMethodNameKind.Rejected;
        }
        else
        {
            string name = _reader.GetString(handle);
            if (name.Length > MetadataSafetyPolicy.MaxTypeNameCharacters)
            {
                kind = LiftedMethodNameKind.Rejected;
            }
            else if (CompilerGeneratedNames.IsLocalFunctionOrLambda(name))
            {
                kind = LiftedMethodNameKind.Canonical;
            }
            else
            {
                kind = CompilerGeneratedNames.HasLiftedMethodMarker(name)
                    ? LiftedMethodNameKind.Rejected
                    : LiftedMethodNameKind.Ordinary;
            }
        }

        _liftedMethodNames.Add(handle, kind);
        return kind;
    }

    // Roslyn nests a lifted body's closure Types (<>c, <>c__DisplayClass...)
    // inside the Type that declares the source method, so the owner is the
    // innermost declaring Type whose name does not start with "<>".
    bool TryLiftedHost(
        TypeDefinitionHandle type,
        out TypeDefinitionHandle host)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                _reader,
                type,
                chain,
                out int count,
                out _,
                out _)
            || count == 0)
        {
            host = default;
            return false;
        }

        int index = count - 1;
        while (index > 0
            && _reader.StringComparer.StartsWith(
                _reader.GetTypeDefinition(chain[index]).Name,
                "<>"))
        {
            index--;
        }
        host = chain[index];
        return true;
    }

    // Roslyn, for C# and Visual Basic alike, nests a state machine directly
    // in its kickoff's declaring Type, names it in the kickoff's state-machine
    // attribute, and implements each interface role through a MethodImpl. The
    // state machine's own name is language-specific (<M>d__N in C#,
    // VB$StateMachine_N_M in Visual Basic), so no name is assumed here.
    MethodDefinitionHandle? KickoffOf(
        MethodDefinitionHandle method,
        TypeDefinitionHandle type)
    {
        TypeDefinition definition = _reader.GetTypeDefinition(type);
        if (definition.GetMethodImplementations().Count == 0)
            return null;
        TypeDefinitionHandle host = definition.GetDeclaringType();
        if (host.IsNil)
            return null;
        if (_scannedHosts.Add(host))
        {
            try
            {
                ScanHost(host);
            }
            catch (Exception exception)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(exception))
            {
                _malformedHosts.Add(host);
            }
        }
        if (_malformedHosts.Contains(host))
        {
            // Every generated body of an unreadable host fails visibly.
            throw new BadImageFormatException(
                "The state-machine host's metadata could not be read.");
        }
        return _kickoffByRole.TryGetValue(method, out MethodDefinitionHandle kickoff)
            ? kickoff
            : null;
    }

    void ScanHost(TypeDefinitionHandle host)
    {
        TypeDefinition hostDefinition = _reader.GetTypeDefinition(host);
        Dictionary<string, TypeDefinitionHandle>? nested = null;
        var claims = new Dictionary<TypeDefinitionHandle, (MethodDefinitionHandle Kickoff, string[] Roles)>();
        HashSet<TypeDefinitionHandle>? ambiguous = null;
        foreach (MethodDefinitionHandle kickoff in hostDefinition.GetMethods())
        {
            foreach (CustomAttributeHandle handle
                in _reader.GetMethodDefinition(kickoff).GetCustomAttributes())
            {
                CustomAttribute attribute = _reader.GetCustomAttribute(handle);
                if (StateMachineRoles(attribute.Constructor) is not { } roles
                    || !TryReadStateMachineLeaf(attribute.Value, out string? leaf))
                {
                    continue;
                }
                nested ??= NestedTypes(hostDefinition);
                if (!nested.TryGetValue(leaf, out TypeDefinitionHandle stateMachine)
                    || stateMachine.IsNil)
                {
                    continue;
                }
                if (!claims.TryAdd(stateMachine, (kickoff, roles)))
                    (ambiguous ??= []).Add(stateMachine);
            }
        }

        foreach ((TypeDefinitionHandle stateMachine, (MethodDefinitionHandle kickoff, string[] roles))
            in claims)
        {
            if (ambiguous?.Contains(stateMachine) != true)
                MapRoles(stateMachine, kickoff, roles);
        }
    }

    Dictionary<string, TypeDefinitionHandle> NestedTypes(
        TypeDefinition host)
    {
        var nested = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        foreach (TypeDefinitionHandle handle in host.GetNestedTypes())
        {
            StringHandle name = _reader.GetTypeDefinition(handle).Name;
            // A duplicated name claims no Type.
            if (!nested.TryAdd(_reader.GetString(name), handle))
                nested[_reader.GetString(name)] = default;
        }
        return nested;
    }

    void MapRoles(
        TypeDefinitionHandle stateMachine,
        MethodDefinitionHandle kickoff,
        string[] roles)
    {
        foreach (MethodImplementationHandle handle
            in _reader.GetTypeDefinition(stateMachine).GetMethodImplementations())
        {
            MethodImplementation implementation =
                _reader.GetMethodImplementation(handle);
            if (implementation.MethodBody.Kind != HandleKind.MethodDefinition)
                continue;
            StringHandle declaration = implementation.MethodDeclaration.Kind switch
            {
                HandleKind.MethodDefinition => _reader.GetMethodDefinition(
                    (MethodDefinitionHandle)implementation.MethodDeclaration).Name,
                HandleKind.MemberReference => _reader.GetMemberReference(
                    (MemberReferenceHandle)implementation.MethodDeclaration).Name,
                _ => default,
            };
            if (declaration.IsNil || !IsRole(declaration, roles))
                continue;
            var body = (MethodDefinitionHandle)implementation.MethodBody;
            if (_reader.GetMethodDefinition(body).GetDeclaringType() == stateMachine)
                _kickoffByRole.TryAdd(body, kickoff);
        }
    }

    bool IsRole(StringHandle name, string[] roles)
    {
        foreach (string role in roles)
        {
            if (_reader.StringComparer.Equals(name, role))
                return true;
        }
        return false;
    }

    string[]? StateMachineRoles(EntityHandle constructor)
    {
        if (_stateMachineConstructors.TryGetValue(constructor, out string[]? roles))
            return roles;
        roles = null;
        if (TryAttributeTypeName(constructor, out StringHandle ns, out StringHandle name)
            && _reader.StringComparer.Equals(ns, CompilerServices))
        {
            if (_reader.StringComparer.Equals(name, "AsyncStateMachineAttribute"))
                roles = AsyncRoles;
            else if (_reader.StringComparer.Equals(name, "AsyncIteratorStateMachineAttribute"))
                roles = AsyncIteratorRoles;
            else if (_reader.StringComparer.Equals(name, "IteratorStateMachineAttribute"))
                roles = IteratorRoles;
        }
        _stateMachineConstructors[constructor] = roles;
        return roles;
    }

    // The attribute's single System.Type argument, serialized as a reflection
    // type name. Roslyn names the nested state machine in the kickoff's
    // declaring Type, so only the innermost '+' segment is matched. Each value
    // blob is parsed once however many attributes share it, and a name longer
    // than MaxTypeNameCharacters fails the host visibly.
    bool TryReadStateMachineLeaf(
        BlobHandle value,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? leaf)
    {
        if (!_stateMachineLeaves.TryGetValue(value, out leaf))
        {
            leaf = ParseStateMachineLeaf(value);
            _stateMachineLeaves[value] = leaf;
        }
        return leaf is not null;
    }

    string? ParseStateMachineLeaf(BlobHandle value)
    {
        BlobReader blob = _reader.GetBlobReader(value);
        // Prolog, a compressed length of at most 4 bytes, and UTF-8 text of
        // at most 3 bytes per UTF-16 character.
        if (blob.Length > 6 + (3 * MetadataSafetyPolicy.MaxTypeNameCharacters))
        {
            throw new BadImageFormatException(
                "A state-machine attribute value exceeds the type-name bound.");
        }

        string? serialized;
        try
        {
            if (blob.Length < 3 || blob.ReadUInt16() != 1)
                return null;
            serialized = blob.ReadSerializedString();
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        if (string.IsNullOrEmpty(serialized))
            return null;
        if (serialized.Length > MetadataSafetyPolicy.MaxTypeNameCharacters)
        {
            throw new BadImageFormatException(
                "A state-machine attribute type name exceeds the type-name bound.");
        }

        int end = serialized.Length;
        int start = -1;
        for (int i = 0; i < serialized.Length; i++)
        {
            char c = serialized[i];
            if (c == '\\')
            {
                i++;
                continue;
            }
            if (c == ',')
            {
                end = i;
                break;
            }
            if (c == '+')
                start = i + 1;
        }
        if (start <= 0 || start >= end)
            return null;

        var text = new System.Text.StringBuilder(end - start);
        for (int i = start; i < end; i++)
        {
            if (serialized[i] == '\\' && i + 1 < end)
                i++;
            text.Append(serialized[i]);
        }
        return text.ToString();
    }

    bool IsCompilerGeneratedType(TypeDefinitionHandle type)
    {
        if (!_compilerGeneratedTypes.TryGetValue(type, out bool generated))
        {
            generated = IsCompilerGenerated(
                _reader.GetTypeDefinition(type).GetCustomAttributes());
            _compilerGeneratedTypes[type] = generated;
        }
        return generated;
    }

    bool IsCompilerGenerated(CustomAttributeHandleCollection attributes)
    {
        foreach (CustomAttributeHandle handle in attributes)
        {
            EntityHandle constructor = _reader.GetCustomAttribute(handle).Constructor;
            if (!_compilerGeneratedConstructors.TryGetValue(constructor, out bool generated))
            {
                generated =
                    TryAttributeTypeName(constructor, out StringHandle ns, out StringHandle name)
                    && _reader.StringComparer.Equals(name, "CompilerGeneratedAttribute")
                    && _reader.StringComparer.Equals(ns, CompilerServices);
                _compilerGeneratedConstructors[constructor] = generated;
            }
            if (generated)
                return true;
        }
        return false;
    }

    // The constructor's declaring type name as the legacy attribute-name
    // reader spells it: a TypeRef parent, or a MethodDef's declaring TypeDef.
    bool TryAttributeTypeName(
        EntityHandle constructor,
        out StringHandle ns,
        out StringHandle name)
    {
        if (constructor.Kind == HandleKind.MemberReference
            && _reader.GetMemberReference((MemberReferenceHandle)constructor).Parent
                is { Kind: HandleKind.TypeReference } parent)
        {
            TypeReference type = _reader.GetTypeReference((TypeReferenceHandle)parent);
            ns = type.Namespace;
            name = type.Name;
            return true;
        }
        if (constructor.Kind == HandleKind.MethodDefinition)
        {
            TypeDefinition type = _reader.GetTypeDefinition(
                _reader.GetMethodDefinition((MethodDefinitionHandle)constructor)
                    .GetDeclaringType());
            ns = type.Namespace;
            name = type.Name;
            return true;
        }
        ns = default;
        name = default;
        return false;
    }
}

enum LiftedMethodNameKind
{
    Ordinary,
    Canonical,
    Rejected,
}

/// <summary>A body's logical-owner attribution.</summary>
internal readonly record struct BodyUseOwner(
    TypeDefinitionHandle Source,
    BodyUseOwnerStatus Status)
{
    internal static BodyUseOwner Logical(TypeDefinitionHandle source) =>
        new(source, BodyUseOwnerStatus.Logical);

    internal static BodyUseOwner PhysicalOnly =>
        new(default, BodyUseOwnerStatus.PhysicalOnly);

    internal static BodyUseOwner Rejected =>
        new(default, BodyUseOwnerStatus.Rejected);
}

internal enum BodyUseOwnerStatus
{
    Logical,
    PhysicalOnly,
    Rejected,
}
