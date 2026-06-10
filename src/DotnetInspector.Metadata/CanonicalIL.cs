using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DotnetInspector.Metadata;

/// <summary>
/// Selects how IL operands are rendered by <see cref="ILDisassembler"/>.
/// </summary>
public enum ILSyntax
{
    /// <summary>Human-readable C#-style names (the IL section's historical format).</summary>
    Display,

    /// <summary>
    /// Canonical ilasm syntax — assembly-qualified type names, IL primitive names,
    /// return types and calling conventions on member refs — suitable for feeding
    /// back through an IL assembler.
    /// </summary>
    Canonical,
}

/// <summary>
/// Renders signature types in IL assembler syntax: <c>int32</c>, <c>float64</c>,
/// <c>class [asm]Ns.Type</c>, <c>valuetype [asm]Ns.E</c>, <c>!0</c>/<c>!!0</c>.
/// Generic parameters are never substituted — canonical IL refers to them by index.
/// </summary>
public sealed class ILSignatureTypeProvider : ISignatureTypeProvider<string, GenericContext?>
{
    public static ILSignatureTypeProvider Instance { get; } = new();

    const byte ValueTypeRawKind = 0x11; // ELEMENT_TYPE_VALUETYPE
    const byte ClassRawKind = 0x12;     // ELEMENT_TYPE_CLASS

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.SByte => "int8",
        PrimitiveTypeCode.Byte => "uint8",
        PrimitiveTypeCode.Int16 => "int16",
        PrimitiveTypeCode.UInt16 => "uint16",
        PrimitiveTypeCode.Int32 => "int32",
        PrimitiveTypeCode.UInt32 => "uint32",
        PrimitiveTypeCode.Int64 => "int64",
        PrimitiveTypeCode.UInt64 => "uint64",
        PrimitiveTypeCode.Single => "float32",
        PrimitiveTypeCode.Double => "float64",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.IntPtr => "native int",
        PrimitiveTypeCode.UIntPtr => "native uint",
        PrimitiveTypeCode.TypedReference => "typedref",
        _ => "object"
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => Prefix(rawTypeKind) + CanonicalIL.QualifiedName(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        => Prefix(rawTypeKind) + CanonicalIL.QualifiedName(reader, handle);

    public string GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, context);

    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', Math.Max(shape.Rank - 1, 0))}]";
    public string GetByReferenceType(string elementType) => $"{elementType}&";
    public string GetPointerType(string elementType) => $"{elementType}*";
    public string GetPinnedType(string elementType) => $"{elementType} pinned";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => $"{genericType}<{string.Join(", ", typeArguments)}>";

    public string GetGenericMethodParameter(GenericContext? context, int index) => $"!!{index}";
    public string GetGenericTypeParameter(GenericContext? context, int index) => $"!{index}";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetFunctionPointerType(MethodSignature<string> signature)
        => $"method {signature.ReturnType} *({string.Join(", ", signature.ParameterTypes)})";

    static string Prefix(byte rawTypeKind) => rawTypeKind switch
    {
        ValueTypeRawKind => "valuetype ",
        ClassRawKind => "class ",
        _ => ""
    };
}

/// <summary>
/// Resolves IL operand tokens to canonical ilasm syntax (see <see cref="ILSyntax.Canonical"/>).
/// Counterpart of <see cref="ILTokenResolver"/>, which renders for human display.
/// </summary>
public static class CanonicalIL
{
    public static string ResolveString(MetadataReader reader, int token)
    {
        try
        {
            var handle = MetadataTokens.UserStringHandle(token);
            return $"\"{EscapeString(reader.GetUserString(handle))}\"";
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    /// <summary>
    /// Escapes a string for an ilasm QSTRING literal: standard backslash escapes
    /// plus octal escapes for remaining control characters.
    /// </summary>
    public static string EscapeString(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case < ' ' or '\x7f': sb.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0')); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    public static string ResolveType(MetadataReader reader, int token)
    {
        try
        {
            return ResolveTypeHandle(reader, MetadataTokens.EntityHandle(token));
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    /// <summary>
    /// Renders a type handle in operand position: qualified name for defs/refs
    /// (no class/valuetype keyword, matching ildasm), decoded signature for specs.
    /// </summary>
    public static string ResolveTypeHandle(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => QualifiedName(reader, (TypeDefinitionHandle)handle),
        HandleKind.TypeReference => QualifiedName(reader, (TypeReferenceHandle)handle),
        HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle)
            .DecodeSignature(ILSignatureTypeProvider.Instance, null),
        _ => $"0x{MetadataTokens.GetToken(handle):X8}"
    };

    public static string ResolveMethod(MetadataReader reader, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => FormatMethodDef(reader, (MethodDefinitionHandle)handle),
                HandleKind.MemberReference => FormatMemberRef(reader, (MemberReferenceHandle)handle),
                HandleKind.MethodSpecification => FormatMethodSpec(reader, (MethodSpecificationHandle)handle),
                _ => $"0x{token:X8}"
            };
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    public static string ResolveField(MetadataReader reader, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.FieldDefinition => FormatFieldDef(reader, (FieldDefinitionHandle)handle),
                HandleKind.MemberReference => FormatFieldMemberRef(reader, (MemberReferenceHandle)handle),
                _ => $"0x{token:X8}"
            };
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    /// <summary>Resolves an <c>ldtoken</c> operand (type, method, or field).</summary>
    public static string ResolveToken(MetadataReader reader, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    => ResolveTypeHandle(reader, handle),
                HandleKind.MethodDefinition => $"method {FormatMethodDef(reader, (MethodDefinitionHandle)handle)}",
                HandleKind.MemberReference when reader.GetMemberReference((MemberReferenceHandle)handle).GetKind() == MemberReferenceKind.Method
                    => $"method {FormatMemberRef(reader, (MemberReferenceHandle)handle)}",
                HandleKind.MemberReference => $"field {FormatFieldMemberRef(reader, (MemberReferenceHandle)handle)}",
                HandleKind.FieldDefinition => $"field {FormatFieldDef(reader, (FieldDefinitionHandle)handle)}",
                _ => $"0x{token:X8}"
            };
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    /// <summary>
    /// Quotes an identifier segment for ilasm when it contains characters outside
    /// the grammar's identifier set (e.g. hyphens in assembly names like
    /// <c>xunit.v3.mtp-v1</c>, angle brackets in compiler-generated names).
    /// </summary>
    public static string QuoteName(string name)
    {
        bool needsQuote = name.Length == 0 || char.IsAsciiDigit(name[0]);
        bool anyUpper = false;
        if (!needsQuote)
        {
            foreach (char c in name)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '$' or '@' or '#' or '?' or '`'))
                {
                    needsQuote = true;
                    break;
                }
                anyUpper |= char.IsAsciiLetterUpper(c);
            }
        }

        // ilasm keywords (type, value, method, handler, ...) are all lowercase;
        // quoting every all-lowercase name sidesteps the entire collision class
        // (over-quoting is always legal).
        if (!needsQuote && !anyUpper)
            needsQuote = true;

        // Exactly-two-hex-character names (enum members D0, F1, ...) lex as the
        // assembler's HEXBYTE token rather than as identifiers.
        if (!needsQuote && name.Length == 2
            && char.IsAsciiHexDigit(name[0]) && char.IsAsciiHexDigit(name[1]))
        {
            needsQuote = true;
        }

        return needsQuote
            ? $"'{name.Replace("\\", "\\\\").Replace("'", "\\'")}'"
            : name;
    }

    /// <summary>Quotes each dot-separated segment of a name as needed.</summary>
    public static string QuoteDottedName(string dotted)
        => dotted.Contains('.') ? string.Join(".", dotted.Split('.').Select(QuoteName)) : QuoteName(dotted);

    /// <summary>Dotted (and slash-nested) name for a type definition — no assembly qualifier.</summary>
    internal static string QualifiedName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var td = reader.GetTypeDefinition(handle);
        string name = QuoteName(reader.GetString(td.Name));
        var declaring = td.GetDeclaringType();
        if (!declaring.IsNil)
            return $"{QualifiedName(reader, declaring)}/{name}";
        string ns = QuoteDottedName(reader.GetString(td.Namespace));
        return ns.Length > 0 ? $"{ns}.{name}" : name;
    }

    /// <summary>Assembly-qualified (and slash-nested) name for a type reference.</summary>
    internal static string QualifiedName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var tr = reader.GetTypeReference(handle);
        string name = QuoteName(reader.GetString(tr.Name));
        string ns = QuoteDottedName(reader.GetString(tr.Namespace));
        string dotted = ns.Length > 0 ? $"{ns}.{name}" : name;
        return tr.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference =>
                $"[{QuoteDottedName(reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name))}]{dotted}",
            HandleKind.TypeReference =>
                $"{QualifiedName(reader, (TypeReferenceHandle)tr.ResolutionScope)}/{dotted}",
            _ => dotted
        };
    }

    static string FormatMethodDef(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        string name = reader.GetString(method.Name);
        string parent = QualifiedName(reader, method.GetDeclaringType());
        var sig = method.DecodeSignature(ILSignatureTypeProvider.Instance, genericContext: null);
        return FormatCall(sig, parent, name, genericArgs: null);
    }

    static string FormatMemberRef(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberRef = reader.GetMemberReference(handle);
        string name = reader.GetString(memberRef.Name);
        string parent = FormatMemberRefParent(reader, memberRef.Parent);
        var sig = memberRef.DecodeMethodSignature(ILSignatureTypeProvider.Instance, genericContext: null);
        return FormatCall(sig, parent, name, genericArgs: null);
    }

    static string FormatMethodSpec(MetadataReader reader, MethodSpecificationHandle handle)
    {
        var spec = reader.GetMethodSpecification(handle);
        var typeArgs = spec.DecodeSignature(ILSignatureTypeProvider.Instance, genericContext: null);
        string genericArgs = $"<{string.Join(", ", typeArgs)}>";

        switch (spec.Method.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)spec.Method);
                var sig = method.DecodeSignature(ILSignatureTypeProvider.Instance, genericContext: null);
                return FormatCall(sig, QualifiedName(reader, method.GetDeclaringType()), reader.GetString(method.Name), genericArgs);
            }
            case HandleKind.MemberReference:
            {
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)spec.Method);
                var sig = memberRef.DecodeMethodSignature(ILSignatureTypeProvider.Instance, genericContext: null);
                return FormatCall(sig, FormatMemberRefParent(reader, memberRef.Parent), reader.GetString(memberRef.Name), genericArgs);
            }
            default:
                return $"0x{MetadataTokens.GetToken(spec.Method):X8}";
        }
    }

    static string FormatCall(MethodSignature<string> sig, string parent, string name, string? genericArgs)
    {
        string instance = sig.Header.IsInstance ? "instance " : "";
        // .ctor/.cctor are grammar keywords; every other member name may need
        // quoting (compiler-generated names like <get_X>b__16_0).
        string methodName = name is ".ctor" or ".cctor" ? name : QuoteName(name);
        return $"{instance}{sig.ReturnType} {parent}::{methodName}{genericArgs}({string.Join(", ", sig.ParameterTypes)})";
    }

    static string FormatFieldDef(MetadataReader reader, FieldDefinitionHandle handle)
    {
        var field = reader.GetFieldDefinition(handle);
        string fieldType = field.DecodeSignature(ILSignatureTypeProvider.Instance, genericContext: null);
        return $"{fieldType} {QualifiedName(reader, field.GetDeclaringType())}::{QuoteName(reader.GetString(field.Name))}";
    }

    static string FormatFieldMemberRef(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberRef = reader.GetMemberReference(handle);
        string fieldType = memberRef.DecodeFieldSignature(ILSignatureTypeProvider.Instance, genericContext: null);
        return $"{fieldType} {FormatMemberRefParent(reader, memberRef.Parent)}::{QuoteName(reader.GetString(memberRef.Name))}";
    }

    /// <summary>
    /// Parent of a member reference: plain qualified name for defs/refs; for
    /// generic instantiations (type specs) the decoded signature, which carries
    /// the required <c>class</c>/<c>valuetype</c> keyword.
    /// </summary>
    static string FormatMemberRefParent(MetadataReader reader, EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeDefinition => QualifiedName(reader, (TypeDefinitionHandle)parent),
        HandleKind.TypeReference => QualifiedName(reader, (TypeReferenceHandle)parent),
        HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)parent)
            .DecodeSignature(ILSignatureTypeProvider.Instance, null),
        HandleKind.MethodDefinition => "?", // varargs call-site refs — not produced by Roslyn
        _ => $"0x{MetadataTokens.GetToken(parent):X8}"
    };
}
