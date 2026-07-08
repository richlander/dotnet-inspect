using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Collections.Immutable;

namespace ILInspector.AnalysisHarness;

/// <summary>
/// Shared SRM member/type-name resolution for the measurement-only harness sensors (leak
/// actionability, MemoryPool lifecycle). Given a metadata token for a call target it returns the
/// declaring type name and member name, resolving <see cref="HandleKind.MethodSpecification"/> and
/// generic-instance (<see cref="HandleKind.TypeSpecification"/>) parents. These sensors run over
/// arbitrary corpus assemblies, so every decode is bounded and fails closed rather than throwing.
/// </summary>
internal static class HarnessMemberResolution
{
    // SRM's SignatureDecoder.DecodeType recurses on the NATIVE stack once per nested blob element
    // (e.g. each SZARRAY prefix) BEFORE any provider callback runs, so a pathologically deep blob
    // StackOverflows uncatchably and kills the whole process. Blob length bounds that native depth
    // (each level costs >= 1 byte), so refuse over-long blobs pre-decode. Real single-type TypeSpec
    // blobs are tiny (CoreLib's largest is 57 bytes), so 1024 only trips on malformed/adversarial
    // input. Mirrors ILInspector.Analysis.TypeRefDecoder's MaxSignatureBlobLength guard.
    internal const int MaxTypeSpecBlobLength = 1024;

    public static (string Type, string Name) ResolveToken(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                return ResolveMethodDef(reader, (MethodDefinitionHandle)handle);
            case HandleKind.MemberReference:
                return ResolveMemberRef(reader, (MemberReferenceHandle)handle);
            case HandleKind.MethodSpecification:
            {
                // MethodSpec.Method is a MethodDefOrRef coded index (MethodDef or MemberRef only -
                // never another MethodSpec), so resolve it directly rather than recursing.
                var method = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
                return method.Kind switch
                {
                    HandleKind.MethodDefinition => ResolveMethodDef(reader, (MethodDefinitionHandle)method),
                    HandleKind.MemberReference => ResolveMemberRef(reader, (MemberReferenceHandle)method),
                    _ => ("", $"(methodspec:{method.Kind})"),
                };
            }
            default:
                return ("", $"(token:{handle.Kind})");
        }
    }

    /// <summary>How a call consumes the owner value loaded immediately before it.</summary>
    public enum ReceiverCallKind
    {
        /// <summary>Owner is an argument, or the call shape is unmodeled: treat as an escape.</summary>
        Other,
        /// <summary>Parameterless instance call with the owner as receiver, not <c>Dispose</c>
        /// (e.g. <c>get_Memory</c>/<c>get_Span</c>): a non-consuming read - the owner stays owned.</summary>
        ReceiverRead,
        /// <summary>Parameterless instance <c>Dispose()</c> with the owner as receiver.</summary>
        Dispose,
    }

    /// <summary>
    /// Classify a call whose single relevant stack input is the owner value loaded right before it.
    /// A parameterless <b>instance</b> method has the owner as its receiver: <c>Dispose()</c>
    /// disposes it, any other parameterless instance call merely reads it (the owner stays owned).
    /// A static or multi-arg call takes the owner as an argument (potential ownership transfer), so
    /// it is <see cref="ReceiverCallKind.Other"/>. Fails closed to <see cref="ReceiverCallKind.Other"/>.
    /// </summary>
    public static ReceiverCallKind ClassifyReceiverCall(MetadataReader reader, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.MethodSpecification)
                handle = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;

            string name;
            SignatureHeader header;
            int parameterCount;
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var md = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    var sig = md.DecodeSignature(SignatureTypeNameProvider.Instance, genericContext: null);
                    name = reader.GetString(md.Name);
                    header = sig.Header;
                    parameterCount = sig.ParameterTypes.Length;
                    break;
                }
                case HandleKind.MemberReference:
                {
                    var mr = reader.GetMemberReference((MemberReferenceHandle)handle);
                    if (mr.GetKind() != MemberReferenceKind.Method)
                        return ReceiverCallKind.Other;
                    var sig = mr.DecodeMethodSignature(SignatureTypeNameProvider.Instance, genericContext: null);
                    name = reader.GetString(mr.Name);
                    header = sig.Header;
                    parameterCount = sig.ParameterTypes.Length;
                    break;
                }
                default:
                    return ReceiverCallKind.Other;
            }

            if (!header.IsInstance || parameterCount != 0)
                return ReceiverCallKind.Other;
            return name == "Dispose" ? ReceiverCallKind.Dispose : ReceiverCallKind.ReceiverRead;
        }
        catch
        {
            return ReceiverCallKind.Other;
        }
    }

    static (string Type, string Name) ResolveMethodDef(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var md = reader.GetMethodDefinition(handle);
        return (TypeName(reader, md.GetDeclaringType()), reader.GetString(md.Name));
    }

    static (string Type, string Name) ResolveMemberRef(MetadataReader reader, MemberReferenceHandle handle)
    {
        var mr = reader.GetMemberReference(handle);
        return (ParentName(reader, mr.Parent), reader.GetString(mr.Name));
    }

    static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var td = reader.GetTypeDefinition(handle);
        string ns = reader.GetString(td.Namespace), name = reader.GetString(td.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    public static string ParentName(MetadataReader reader, EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeReference => TypeRefName(reader, (TypeReferenceHandle)parent),
        HandleKind.TypeDefinition => TypeName(reader, (TypeDefinitionHandle)parent),
        HandleKind.TypeSpecification => TypeSpecName(reader, (TypeSpecificationHandle)parent),
        _ => $"({parent.Kind})",
    };

    // A generic type instance (e.g. Span<byte>, MemoryPool<byte>) names its declaring type through a
    // TypeSpecification blob; decode it to the underlying generic type name (Span`1, MemoryPool`1)
    // so callers see a real type, not "(typespec)". Bounded pre-decode against the SRM native-stack
    // StackOverflow described on MaxTypeSpecBlobLength.
    static string TypeSpecName(MetadataReader reader, TypeSpecificationHandle handle)
    {
        var typeSpec = reader.GetTypeSpecification(handle);
        if (reader.GetBlobReader(typeSpec.Signature).Length > MaxTypeSpecBlobLength)
            return "(typespec)";
        try
        {
            return typeSpec.DecodeSignature(SignatureTypeNameProvider.Instance, genericContext: null);
        }
        catch
        {
            return "(typespec)";
        }
    }

    static string TypeRefName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var tr = reader.GetTypeReference(handle);
        string ns = reader.GetString(tr.Namespace), name = reader.GetString(tr.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }
}

/// <summary>A minimal signature decoder that yields type names as strings, used to name the
/// declaring type behind a generic-instance parent (e.g. <c>Span`1</c>, <c>MemoryPool`1</c>).
/// It keeps the outer generic type name and ignores type arguments - callers only need the
/// declaring type, not its instantiation.</summary>
sealed class SignatureTypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public static readonly SignatureTypeNameProvider Instance = new();

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var td = reader.GetTypeDefinition(handle);
        string ns = reader.GetString(td.Namespace), name = reader.GetString(td.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        string ns = reader.GetString(tr.Namespace), name = reader.GetString(tr.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    // A nested TypeSpecification reference (e.g. a custom modifier or a CLASS/VALUETYPE token that
    // itself resolves to a TypeSpec) re-enters DecodeSignature. Each re-entry is a MANAGED frame
    // here plus SRM native DecodeType frames, so a self-referential or long TypeSpec CHAIN - whose
    // individual blobs each stay under MaxTypeSpecBlobLength - would recurse until an uncatchable
    // StackOverflow kills the process. The outer TypeSpecName length cap only bounds the ENTRY
    // blob's native depth, not this cross-TypeSpec re-entry, so bound both the re-entry depth and
    // each nested blob's length here (mirrors ILInspector.Analysis.TypeRefDecoder). Callers never
    // use a nested TypeSpec's decoded name (modifiers/type-args are discarded), so failing closed
    // to "(typespec)" past the cap is loss-free.
    const int MaxTypeSpecDepth = 16;

    [ThreadStatic]
    static int s_typeSpecDepth;

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var typeSpec = reader.GetTypeSpecification(handle);
        if (s_typeSpecDepth >= MaxTypeSpecDepth ||
            reader.GetBlobReader(typeSpec.Signature).Length > HarnessMemberResolution.MaxTypeSpecBlobLength)
            return "(typespec)";
        s_typeSpecDepth++;
        try
        {
            return typeSpec.DecodeSignature(this, genericContext);
        }
        catch
        {
            return "(typespec)";
        }
        finally
        {
            s_typeSpecDepth--;
        }
    }

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
    public string GetSZArrayType(string elementType) => elementType;
    public string GetArrayType(string elementType, ArrayShape shape) => elementType;
    public string GetByReferenceType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "(fnptr)";
}
