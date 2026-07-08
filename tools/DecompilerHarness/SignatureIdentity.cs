using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Ordinal-independent method signature identity used to correlate RTS metadata
/// targets with fixture source members. The same normalization is applied to
/// metadata (via a custom <see cref="ISignatureTypeProvider{TType, TGenericContext}"/>)
/// and to C# source syntax so that a target's metadata-derived signature matches its
/// source declaration without depending on hidden/non-public same-name member ordering.
///
/// Format: <c>`{genericArity}(p0,p1,...)</c>, with <c>:{returnType}</c> appended for
/// conversion operators (which overload on return type). Named types are reduced to
/// their simple (arity-stripped) name, generic parameters to their declared name, and
/// primitives to C# keywords, so both sides agree for the common overload-set cases.
/// Cases the normalization cannot reconcile fall back to ordinal matching at the call
/// site, so a lossy token never mis-selects a member.
/// </summary>
static class SignatureIdentity
{
    static readonly Provider s_provider = new();

    public static string? ForMetadataMethod(MetadataReader reader, TypeDefinition typeDef, MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        string name = reader.GetString(method.Name);
        try
        {
            var signature = method.DecodeSignature(s_provider, GenericContext.ForMethod(reader, typeDef, method));
            return Format(
                signature.GenericParameterCount,
                signature.ParameterTypes,
                IsConversionOperator(name) ? signature.ReturnType : null);
        }
        catch (BadImageFormatException)
        {
            // A signature we cannot decode simply yields no signature identity, so the
            // caller falls back to ordinal resolution rather than failing the probe.
            return null;
        }
    }

    public static string ForSourceMethod(MethodDeclarationSyntax method)
        => Format(method.TypeParameterList?.Parameters.Count ?? 0, ParameterTokens(method.ParameterList.Parameters), returnType: null);

    public static string ForSourceConstructor(ParameterListSyntax? parameters)
        => Format(0, ParameterTokens(parameters?.Parameters ?? default), returnType: null);

    public static string ForSourceOperator(OperatorDeclarationSyntax op)
        => Format(0, ParameterTokens(op.ParameterList.Parameters), returnType: null);

    public static string ForSourceConversion(ConversionOperatorDeclarationSyntax conversion)
        => Format(0, ParameterTokens(conversion.ParameterList.Parameters), SourceTypeToken(conversion.Type));

    public static string ForSourcePropertyGetter()
        => Format(0, [], returnType: null);

    public static string ForSourcePropertySetter(TypeSyntax propertyType)
        => Format(0, [SourceTypeToken(propertyType)], returnType: null);

    public static string ForSourceIndexerGetter(BracketedParameterListSyntax parameters)
        => Format(0, ParameterTokens(parameters.Parameters), returnType: null);

    public static string ForSourceIndexerSetter(BracketedParameterListSyntax parameters, TypeSyntax indexerType)
        => Format(0, [.. ParameterTokens(parameters.Parameters), SourceTypeToken(indexerType)], returnType: null);

    static string Format(int genericArity, IReadOnlyList<string> parameterTokens, string? returnType)
    {
        string signature = $"`{genericArity}({string.Join(",", parameterTokens)})";
        return returnType is null ? signature : $"{signature}:{returnType}";
    }

    static bool IsConversionOperator(string name)
        => name is "op_Implicit" or "op_Explicit" or "op_CheckedImplicit" or "op_CheckedExplicit";

    static IReadOnlyList<string> ParameterTokens(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var tokens = new List<string>(parameters.Count);
        foreach (var parameter in parameters)
        {
            if (parameter.Type is not { } type)
                continue;
            bool byRef = parameter.Modifiers.Any(modifier =>
                modifier.IsKind(SyntaxKind.RefKeyword)
                || modifier.IsKind(SyntaxKind.OutKeyword)
                || modifier.IsKind(SyntaxKind.InKeyword));
            tokens.Add(byRef ? $"ref {SourceTypeToken(type)}" : SourceTypeToken(type));
        }

        return tokens;
    }

    static string SourceTypeToken(TypeSyntax type) => type switch
    {
        PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic =>
            $"{generic.Identifier.ValueText}<{string.Join(",", generic.TypeArgumentList.Arguments.Select(SourceTypeToken))}>",
        QualifiedNameSyntax qualified => SourceTypeToken(qualified.Right),
        AliasQualifiedNameSyntax alias => SourceTypeToken(alias.Name),
        ArrayTypeSyntax array =>
            SourceTypeToken(array.ElementType)
                + string.Concat(array.RankSpecifiers.Select(rank => $"[{new string(',', rank.Rank - 1)}]")),
        PointerTypeSyntax pointer => $"{SourceTypeToken(pointer.ElementType)}*",
        NullableTypeSyntax nullable => $"{SourceTypeToken(nullable.ElementType)}?",
        RefTypeSyntax reference => $"ref {SourceTypeToken(reference.Type)}",
        TupleTypeSyntax tuple =>
            $"ValueTuple<{string.Join(",", tuple.Elements.Select(element => SourceTypeToken(element.Type)))}>",
        _ => type.ToString(),
    };

    sealed class Provider : ISignatureTypeProvider<string, GenericContext?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.UIntPtr => "nuint",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            _ => typeCode.ToString(),
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => SimpleName(reader.GetString(reader.GetTypeDefinition(handle).Name));

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => SimpleName(reader.GetString(reader.GetTypeReference(handle).Name));

        public string GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, context);

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetArrayType(string elementType, ArrayShape shape)
            => $"{elementType}[{new string(',', shape.Rank - 1)}]";

        public string GetByReferenceType(string elementType) => $"ref {elementType}";

        public string GetPointerType(string elementType) => $"{elementType}*";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => genericType == "Nullable" && typeArguments.Length == 1
                ? $"{typeArguments[0]}?"
                : $"{genericType}<{string.Join(",", typeArguments)}>";

        public string GetGenericMethodParameter(GenericContext? context, int index)
            => context is not null && index < context.MethodParameters.Count
                ? context.MethodParameters[index]
                : $"!!{index}";

        public string GetGenericTypeParameter(GenericContext? context, int index)
            => context is not null && index < context.TypeParameters.Count
                ? context.TypeParameters[index]
                : $"!{index}";

        public string GetFunctionPointerType(MethodSignature<string> signature)
            => $"delegate*<{string.Join(",", signature.ParameterTypes.Append(signature.ReturnType))}>";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;

        static string SimpleName(string name)
        {
            int backtick = name.IndexOf('`');
            return backtick < 0 ? name : name[..backtick];
        }
    }
}
