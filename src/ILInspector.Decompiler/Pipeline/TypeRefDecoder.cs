using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>Generic parameter names in scope while decoding a signature.</summary>
internal sealed record GenericScope(ImmutableArray<string> TypeParameters, ImmutableArray<string> MethodParameters)
{
    public static readonly GenericScope Empty = new([], []);
}

/// <summary>
/// Decodes metadata signatures into <see cref="TypeRef"/>s. Primitives and
/// corelib-resolved types canonicalize to <see cref="TypeRef.CoreLibrary"/>
/// so identity does not depend on which facade spelled the reference.
/// Shapes outside the supported core (function pointers, custom modifiers)
/// decode to <see cref="TypeRefKind.Unsupported"/> — honest, fidelity-lowering,
/// never a guess.
/// </summary>
internal sealed class TypeRefDecoder : ISignatureTypeProvider<TypeRef, GenericScope>
{
    public static readonly TypeRefDecoder Instance = new();

    public TypeRef GetPrimitiveType(PrimitiveTypeCode typeCode)
        => TypeRef.CoreLib("System", typeCode switch
        {
            PrimitiveTypeCode.Boolean => "Boolean",
            PrimitiveTypeCode.Byte => "Byte",
            PrimitiveTypeCode.SByte => "SByte",
            PrimitiveTypeCode.Char => "Char",
            PrimitiveTypeCode.Int16 => "Int16",
            PrimitiveTypeCode.UInt16 => "UInt16",
            PrimitiveTypeCode.Int32 => "Int32",
            PrimitiveTypeCode.UInt32 => "UInt32",
            PrimitiveTypeCode.Int64 => "Int64",
            PrimitiveTypeCode.UInt64 => "UInt64",
            PrimitiveTypeCode.Single => "Single",
            PrimitiveTypeCode.Double => "Double",
            PrimitiveTypeCode.IntPtr => "IntPtr",
            PrimitiveTypeCode.UIntPtr => "UIntPtr",
            PrimitiveTypeCode.String => "String",
            PrimitiveTypeCode.Object => "Object",
            PrimitiveTypeCode.Void => "Void",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            _ => typeCode.ToString(),
        });

    public TypeRef GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var typeDef = reader.GetTypeDefinition(handle);
        string name = reader.GetString(typeDef.Name);
        string ns = reader.GetString(typeDef.Namespace);
        if (typeDef.IsNested)
        {
            var declaring = GetTypeFromDefinition(reader, typeDef.GetDeclaringType(), 0);
            return TypeRef.Definition(
                declaring.Assembly,
                declaring.Namespace,
                $"{declaring.Name}+{name}",
                HintFrom(rawTypeKind),
                InlineArrayFact(reader, typeDef));
        }
        string assembly = reader.IsAssembly
            ? Canonical(reader.GetString(reader.GetAssemblyDefinition().Name))
            : "";
        return TypeRef.Definition(assembly, ns, name, HintFrom(rawTypeKind), InlineArrayFact(reader, typeDef));
    }

    public TypeRef GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        string name = reader.GetString(typeRef.Name);
        string ns = reader.GetString(typeRef.Namespace);
        switch (typeRef.ResolutionScope.Kind)
        {
            case HandleKind.AssemblyReference:
                var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope);
                return TypeRef.Definition(Canonical(reader.GetString(assembly.Name)), ns, name, HintFrom(rawTypeKind));
            case HandleKind.TypeReference:
                var declaring = GetTypeFromReference(reader, (TypeReferenceHandle)typeRef.ResolutionScope, 0);
                return TypeRef.Definition(declaring.Assembly, declaring.Namespace, $"{declaring.Name}+{name}", HintFrom(rawTypeKind));
            default:
                return TypeRef.Definition("", ns, name, HintFrom(rawTypeKind));
        }
    }

    public TypeRef GetTypeFromSpecification(MetadataReader reader, GenericScope genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public TypeRef GetSZArrayType(TypeRef elementType) => TypeRef.SzArray(elementType);

    public TypeRef GetArrayType(TypeRef elementType, ArrayShape shape) => TypeRef.MdArray(elementType, shape.Rank);

    public TypeRef GetByReferenceType(TypeRef elementType) => TypeRef.ByRef(elementType);

    public TypeRef GetPointerType(TypeRef elementType) => TypeRef.Pointer(elementType);

    public TypeRef GetPinnedType(TypeRef elementType) => TypeRef.Pinned(elementType);

    public TypeRef GetGenericInstantiation(TypeRef genericType, ImmutableArray<TypeRef> typeArguments)
        => TypeRef.GenericInstance(genericType, typeArguments);

    public TypeRef GetGenericTypeParameter(GenericScope genericContext, int index)
        => TypeRef.GenericParameter(index, NameAt(genericContext.TypeParameters, index));

    public TypeRef GetGenericMethodParameter(GenericScope genericContext, int index)
        => TypeRef.MethodGenericParameter(index, NameAt(genericContext.MethodParameters, index));

    public TypeRef GetFunctionPointerType(MethodSignature<TypeRef> signature)
        => TypeRef.FunctionPointer(signature.ReturnType, signature.ParameterTypes, ConventionText(signature.Header.CallingConvention));

    /// <summary>The C# calling-convention spelling for a function pointer: empty for a managed pointer, the <c>unmanaged</c> keyword (with the specific convention in brackets) otherwise.</summary>
    public static string ConventionText(SignatureCallingConvention convention) => convention switch
    {
        SignatureCallingConvention.Default => "",
        SignatureCallingConvention.CDecl => "unmanaged[Cdecl]",
        SignatureCallingConvention.StdCall => "unmanaged[Stdcall]",
        SignatureCallingConvention.ThisCall => "unmanaged[Thiscall]",
        SignatureCallingConvention.FastCall => "unmanaged[Fastcall]",
        _ => "unmanaged",
    };

    /// <summary>
    /// Custom modifiers are seen through to the unmodified type. The three that
    /// occur in practice — <c>modreq(InAttribute)</c> (an <c>in</c>/<c>ref readonly</c>
    /// parameter or return), <c>modreq(IsVolatile)</c> (a <c>volatile</c> field),
    /// and <c>modreq(IsExternalInit)</c> (an <c>init</c> accessor) — are
    /// declaration-site concerns the signature renderer reads from metadata; they
    /// never appear in a method body, where types surface only as local
    /// declarations, casts, and call arguments over the *unmodified* type. Seeing
    /// through keeps the underlying shape intact (an <c>in T</c> stays
    /// <c>ByRef(T)</c>, so every <c>ByRef</c>/<c>Pointer</c> unwrap site still
    /// matches) and lets a fully-representable body import at
    /// <see cref="DecompilationFidelity.Full"/> instead of being capped by a
    /// modifier that the C# never spells here.
    ///
    /// This is the "no infrastructure without a customer" choice
    /// (docs/decompiler.md): the design contract has type identity carry
    /// modifiers through the tree, but no body consumer reads them today, and
    /// wrapping the byref of an <c>in</c> parameter would break the structural
    /// <c>Kind == ByRef</c> checks. When an IR-based signature renderer needs the
    /// distinction, model it then.
    /// </summary>
    public TypeRef GetModifiedType(TypeRef modifier, TypeRef unmodifiedType, bool isRequired)
        => unmodifiedType;

    static string NameAt(ImmutableArray<string> names, int index)
        => index >= 0 && index < names.Length ? names[index] : "";

    // ECMA-335 II.23.1.16 signature element types.
    const byte ElementTypeValueType = 0x11;
    const byte ElementTypeClass = 0x12;

    /// <summary>Maps the signature's CLASS/VALUETYPE byte to a hint; anything else (a type reached outside a signature) is Unknown.</summary>
    static ValueTypeHint HintFrom(byte rawTypeKind) => rawTypeKind switch
    {
        ElementTypeValueType => ValueTypeHint.ValueType,
        ElementTypeClass => ValueTypeHint.ReferenceType,
        _ => ValueTypeHint.Unknown,
    };

    static MetadataFactState InlineArrayFact(MetadataReader reader, TypeDefinition typeDef)
        => MethodDefinitionFacts.HasInlineArrayAttribute(reader, typeDef)
            ? MetadataFactState.Yes
            : MetadataFactState.No;

    /// <summary>Canonicalizes corelib spellings so facade choice never affects identity.</summary>
    internal static string Canonical(string assemblyName) => assemblyName is
        "System.Private.CoreLib" or "System.Runtime" or "mscorlib" or "netstandard" or "System.Runtime.Extensions"
        ? TypeRef.CoreLibrary
        : assemblyName;
}
