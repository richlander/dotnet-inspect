using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DotnetInspector.Metadata;

/// <summary>
/// Resolves IL operand metadata tokens (strings, types, methods, fields) to human-readable text.
/// Shared by <see cref="ILDisassembler"/> (the IL section) and the decompiler's annotated-IL emitter
/// so a given call/field/type operand renders identically in both — method operands always carry
/// their full parameter list and generic type arguments.
///
/// Type resolution accepts an optional <see cref="GenericContext"/> so callers that have the
/// enclosing method's generic parameters (the annotated emitter) can resolve <c>!0</c>/<c>!!0</c>
/// references; callers without one (the standalone disassembler) pass null and get the same output
/// as before.
/// </summary>
public static class ILTokenResolver
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

    public static string ResolveType(MetadataReader reader, int token, GenericContext? genericContext = null)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return TypeResolver.GetTypeName(reader, handle, genericContext) ?? $"0x{token:X8}";
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

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
                HandleKind.MemberReference => FormatMemberRef(reader, (MemberReferenceHandle)handle),
                _ => $"0x{token:X8}"
            };
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    /// <summary>Resolves an <c>ldtoken</c> operand, which may reference a type, method, or field.</summary>
    public static string ResolveToken(MetadataReader reader, int token, GenericContext? genericContext = null)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    => TypeResolver.GetTypeName(reader, handle, genericContext) ?? $"0x{token:X8}",
                HandleKind.MethodDefinition => FormatMethodDef(reader, (MethodDefinitionHandle)handle),
                HandleKind.MemberReference => FormatMemberRef(reader, (MemberReferenceHandle)handle),
                HandleKind.FieldDefinition => FormatFieldDef(reader, (FieldDefinitionHandle)handle),
                _ => $"0x{token:X8}"
            };
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    public static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    static string FormatMethodDef(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        string methodName = reader.GetString(method.Name);
        string typeName = TypeResolver.GetTypeName(reader, method.GetDeclaringType()) ?? "?";
        string baseName = $"{typeName}::{methodName}";

        try
        {
            var sig = method.DecodeSignature(SignatureDecoder.Instance, genericContext: null);
            return $"{baseName}({string.Join(", ", sig.ParameterTypes)})";
        }
        catch
        {
            return baseName;
        }
    }

    static string FormatMemberRef(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberRef = reader.GetMemberReference(handle);
        string name = reader.GetString(memberRef.Name);
        string? parentName = TypeResolver.GetTypeName(reader, memberRef.Parent);
        string baseName = parentName is not null ? $"{parentName}::{name}" : name;

        try
        {
            if (memberRef.GetKind() == MemberReferenceKind.Method)
            {
                var genericCtx = BuildGenericContext(reader, memberRef);
                var sig = memberRef.DecodeMethodSignature(SignatureDecoder.Instance, genericCtx);
                return $"{baseName}({string.Join(", ", sig.ParameterTypes)})";
            }
        }
        catch
        {
            // Fall through to baseName (field member refs and undecodable signatures).
        }

        return baseName;
    }

    static string FormatMethodSpec(MetadataReader reader, MethodSpecificationHandle handle)
    {
        var spec = reader.GetMethodSpecification(handle);
        string baseName = spec.Method.Kind switch
        {
            HandleKind.MethodDefinition => FormatMethodDef(reader, (MethodDefinitionHandle)spec.Method),
            HandleKind.MemberReference => FormatMemberRef(reader, (MemberReferenceHandle)spec.Method),
            _ => $"0x{MetadataTokens.GetToken(spec.Method):X8}"
        };

        try
        {
            var typeArgs = spec.DecodeSignature(SignatureDecoder.Instance, genericContext: null);
            return $"{baseName}<{string.Join(", ", typeArgs)}>";
        }
        catch
        {
            return baseName;
        }
    }

    static string FormatFieldDef(MetadataReader reader, FieldDefinitionHandle handle)
    {
        var field = reader.GetFieldDefinition(handle);
        string fieldName = reader.GetString(field.Name);
        string typeName = TypeResolver.GetTypeName(reader, field.GetDeclaringType()) ?? "?";
        return $"{typeName}::{fieldName}";
    }

    /// <summary>
    /// Builds a GenericContext from a MemberReference's parent type for accurate parameter type
    /// rendering on generic type members.
    /// </summary>
    static GenericContext? BuildGenericContext(MetadataReader reader, MemberReference memberRef)
    {
        try
        {
            if (memberRef.Parent.Kind == HandleKind.TypeSpecification)
            {
                var typeSpec = reader.GetTypeSpecification((TypeSpecificationHandle)memberRef.Parent);
                var parentType = typeSpec.DecodeSignature(SignatureDecoder.Instance, genericContext: null);
                var typeArgs = ExtractGenericArguments(parentType);
                if (typeArgs.Count > 0)
                    return new GenericContext(typeArgs, []);
            }
        }
        // Graceful fallback when generic type signature cannot be decoded
        catch { }
        return null;
    }

    static IReadOnlyList<string> ExtractGenericArguments(string typeName)
    {
        int openAngle = typeName.IndexOf('<');
        if (openAngle < 0) return [];

        int depth = 0;
        List<string> args = [];
        int argStart = openAngle + 1;

        for (int i = openAngle; i < typeName.Length; i++)
        {
            switch (typeName[i])
            {
                case '<': depth++; break;
                case '>':
                    depth--;
                    if (depth == 0)
                    {
                        var arg = typeName[argStart..i].Trim();
                        if (arg.Length > 0) args.Add(arg);
                        return args;
                    }
                    break;
                case ',' when depth == 1:
                    args.Add(typeName[argStart..i].Trim());
                    argStart = i + 1;
                    break;
            }
        }
        return args;
    }
}
