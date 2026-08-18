using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Reads <c>DynamicAttribute</c> transform flags from assembly custom-attribute
/// collections. The flags record which <c>System.Object</c> positions in a
/// signature were authored as <c>dynamic</c>, so the type view can render
/// <c>dynamic</c> instead of <c>object</c>.
/// </summary>
/// <remarks>
/// Unlike nullability there is no <c>DynamicContextAttribute</c>: a position is
/// dynamic only when <c>DynamicAttribute</c> is present and its flag is set.
/// Flags are returned as a <c>byte[]</c> of 0/1 so the same preorder walk that
/// <see cref="TypeNode.ApplyNullability"/> uses can consume them via
/// <c>ApplyDynamic</c>. The marker form <c>[Dynamic]</c> (no constructor
/// argument) is emitted only for a bare <c>dynamic</c> and is returned as a
/// single-element array, which the walk consumes at the single (bare object)
/// position without broadcasting to inner nodes.
/// </remarks>
public static class DynamicReader
{
    enum ConstructorKind
    {
        Marker,
        TransformFlags,
    }

    /// <summary>
    /// Gets the DynamicAttribute transform-flags array (0 = object, 1 = dynamic)
    /// from custom attributes. Returns null when the attribute is not present or
    /// its custom-attribute encoding is malformed. The no-argument marker form
    /// returns a one-element array.
    /// </summary>
    public static byte[]? GetDynamicFlags(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = AttributeReader.GetAttributeTypeName(
                reader,
                attr.Constructor,
                beforeMaterialize);
            if (attrTypeName != KnownAttributeNames.DynamicAttribute) continue;
            if (GetConstructorKind(
                    reader,
                    attr.Constructor,
                    beforeMaterialize) is not { } constructorKind)
                return null;

            var blob = reader.GetBlobReader(attr.Value);
            if (blob.Length < 2) return null;
            if (blob.ReadUInt16() != 1) return null;

            // DynamicAttribute():        prolog(2) + namedArgs(2) = 4          -> marker form
            // DynamicAttribute(bool[]):  prolog(2) + count(4) + N bytes + namedArgs(2) = 8+N
            if (constructorKind == ConstructorKind.Marker && blob.RemainingBytes == 2)
            {
                // Marker form: the whole (bare object) type is dynamic.
                return blob.ReadUInt16() == 0 ? [1] : null;
            }

            if (constructorKind == ConstructorKind.TransformFlags && blob.RemainingBytes >= 6)
            {
                int count = blob.ReadInt32();
                if (count < 0 || blob.RemainingBytes != count + 2) return null;
                beforeMaterialize?.Invoke(blob.Length);
                var flags = new byte[count];
                for (int i = 0; i < count; i++)
                {
                    byte flag = blob.ReadByte();
                    if (flag > 1) return null;
                    flags[i] = flag;
                }
                return blob.ReadUInt16() == 0 ? flags : null;
            }

            return null;
        }
        return null;
    }

    static ConstructorKind? GetConstructorKind(
        MetadataReader reader,
        EntityHandle constructor,
        Action<int>? beforeMaterialize)
    {
        try
        {
            MethodSignature<TypeNode> signature;
            switch (constructor.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var method = reader.GetMethodDefinition((MethodDefinitionHandle)constructor);
                    if (!reader.StringComparer.Equals(method.Name, ".ctor"))
                        return null;
                    if (!IsSupportedConstructorSignature(
                        reader,
                        method.Signature))
                        return null;
                    if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                        reader,
                        method.Signature,
                        SignatureBlobGuard.Kind.Method))
                        return null;
                    signature = method.DecodeSignature(
                        new TypeNodeProvider(
                            beforeMaterialize: beforeMaterialize),
                        genericContext: null);
                    break;
                }
                case HandleKind.MemberReference:
                {
                    var member = reader.GetMemberReference((MemberReferenceHandle)constructor);
                    if (!reader.StringComparer.Equals(member.Name, ".ctor"))
                        return null;
                    if (!IsSupportedConstructorSignature(
                        reader,
                        member.Signature))
                        return null;
                    if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                        reader,
                        member.Signature,
                        SignatureBlobGuard.Kind.Method))
                        return null;
                    signature = member.DecodeMethodSignature(
                        new TypeNodeProvider(
                            beforeMaterialize: beforeMaterialize),
                        genericContext: null);
                    break;
                }
                default:
                    return null;
            }

            if (!signature.Header.IsInstance
                || signature.GenericParameterCount != 0
                || signature.ReturnType is not PrimitiveTypeNode { Name: "void" })
            {
                return null;
            }
            return signature.ParameterTypes switch
            {
                [] => ConstructorKind.Marker,
                [SZArrayTypeNode { ElementType: PrimitiveTypeNode { Name: "bool" } }]
                    => ConstructorKind.TransformFlags,
                _ => null,
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    static bool IsSupportedConstructorSignature(
        MetadataReader reader,
        BlobHandle signatureHandle)
    {
        var blob = reader.GetBlobReader(signatureHandle);
        SignatureHeader header = blob.ReadSignatureHeader();
        if (header.Kind != SignatureKind.Method
            || header.CallingConvention
                != SignatureCallingConvention.Default
            || !header.IsInstance
            || header.HasExplicitThis
            || header.IsGeneric)
        {
            return false;
        }

        int parameterCount = blob.ReadCompressedInteger();
        if (parameterCount is not (0 or 1))
            return false;

        // A constructor returns void, with no custom modifiers. Rejecting any
        // other return encoding here — before a provider ever materializes it —
        // is what keeps a hostile return type from being decoded in full only
        // to be discarded: nested TypeSpec decodes each get their own blob
        // budget, so a return type carrying thousands of modifier arguments
        // that all point at one shallow TypeSpec multiplies out far past any
        // single-blob bound.
        const byte ElementTypeVoid = 0x01;
        return blob.RemainingBytes > 0 && blob.ReadByte() == ElementTypeVoid;
    }

    /// <summary>
    /// True when a transform-flags array marks the top-level (outermost) type
    /// position as <c>dynamic</c>. The flags are a preorder walk over the type;
    /// index 0 is the whole type, so only a bare <c>dynamic</c> (a
    /// <c>System.Object</c> authored as <c>dynamic</c>) sets it — a nested
    /// position such as <c>Func&lt;dynamic&gt;</c> leaves index 0 false. Null
    /// (attribute absent) means the position is a plain object.
    /// </summary>
    public static bool IsTopLevelDynamic(byte[]? dynamicFlags)
        => dynamicFlags is { Length: > 0 } flags && flags[0] == 1;

    /// <summary>
    /// True when a transform-flags array marks the <em>element</em> of a by-ref
    /// type as <c>dynamic</c>. The flags are a preorder walk over the type; for a
    /// by-ref signature (<c>ref</c>/<c>in</c>/<c>out</c>) index 0 is the ByRef
    /// modifier itself (never dynamic) and the referenced element sits at index 1,
    /// so <c>ref dynamic</c> emits <c>{ false, true }</c>. Callers must only use
    /// this for a position whose type is by-ref; for a non-by-ref position use
    /// <see cref="IsTopLevelDynamic"/>. Null (attribute absent) means the element
    /// is a plain object.
    /// </summary>
    public static bool IsByRefElementDynamic(byte[]? dynamicFlags)
        => dynamicFlags is { Length: > 1 } flags && flags[1] == 1;

    /// <summary>
    /// True when a transform-flags array marks the element of an array type as
    /// <c>dynamic</c>. The array occupies index 0 and its element index 1.
    /// </summary>
    public static bool IsArrayElementDynamic(byte[]? dynamicFlags)
        => dynamicFlags is { Length: > 1 } flags && flags[1] == 1;

    /// <summary>
    /// Gets DynamicAttribute transform flags for a specific parameter by sequence
    /// number. Sequence 0 = return type, 1+ = parameters.
    /// </summary>
    public static byte[]? GetParameterDynamicFlags(
        MetadataReader reader,
        ParameterHandleCollection paramHandles,
        int sequenceNumber,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var handle in paramHandles)
        {
            var param = reader.GetParameter(handle);
            if (param.SequenceNumber == sequenceNumber)
                return GetDynamicFlags(
                    reader,
                    param.GetCustomAttributes(),
                    beforeMaterialize);
        }
        return null;
    }
}
