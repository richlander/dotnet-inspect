using System;
using System.Reflection.Metadata;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// Resolves type names from metadata handles.
/// </summary>
public static class TypeResolver
{
    // GetTypeNameFromReference climbs a nested TypeReference's resolution scope and
    // GetFullName(TypeDefinition) climbs a nested type's declaring-type chain. Malformed metadata
    // can make either chain cycle (a TypeRef whose resolution scope is itself, a TypeDef nested in
    // itself), which a naive recursive climb rides until an *uncatchable* StackOverflowException
    // tears down the process, or amplifies into a huge string. These climbs are reached while
    // decoding inspected (untrusted) signatures (SignatureDecoder delegates here) with a shallow
    // blob, so the SignatureBlobGuard prescan cannot see the cross-row cycle. Both climbs now walk
    // NestedTypeName's bounded, iterative chains and stop concatenating once the accumulated name
    // passes NestedTypeName.MaxLength, degrading to the truncated qualification.

    // FormatDisplayName renders a generic type name's `N arity marker as N placeholder parameters
    // (<T1, ..., TN>). The arity is parsed from an untrusted #Strings-heap type name, so an absurd
    // arity (e.g. "Foo`2000000000") would drive a multi-billion-iteration StringBuilder append and
    // OOM the process from a 15-byte string. Real generic arities are tiny (<= a couple dozen), so a
    // name whose arity exceeds this ceiling is malformed and is rendered raw instead of expanded.
    // A cumulative output cap (NestedTypeName.MaxLength) additionally bounds a name carrying many
    // bounded markers, whose expansions would otherwise still multiply into an OOM-scale string.
    const int MaxGenericArity = 1024;

    /// <summary>
    /// Gets the fully qualified type name from an entity handle.
    /// Handles TypeReference, TypeDefinition, and TypeSpecification.
    /// </summary>
    public static string? GetTypeName(MetadataReader reader, EntityHandle handle, GenericContext? context = null)
    {
        if (handle.IsNil)
            return null;

        return handle.Kind switch
        {
            HandleKind.TypeReference => GetTypeNameFromReference(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeDefinition => GetTypeNameFromDefinition(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeSpecification => GetTypeNameFromSpecification(reader, (TypeSpecificationHandle)handle, context),
            _ => null
        };
    }

    /// <summary>
    /// Gets the type name from a TypeReference handle, qualifying a nested type
    /// through its declaring type (<c>Outer.Inner</c>) — a nested
    /// <see cref="TypeReference"/> carries an empty namespace and a leaf name with
    /// its enclosing type as the resolution scope, so a raw namespace+name would
    /// drop the qualifier (rendering <c>ImmutableArray`1+Builder</c> as a bare
    /// <c>Builder</c>). Mirrors <see cref="GetFullName(MetadataReader, TypeDefinition)"/>.
    /// </summary>
    public static string GetTypeNameFromReference(MetadataReader reader, TypeReferenceHandle handle)
    {
        var typeRef = reader.GetTypeReference(handle);
        // Fast, allocation-free path for the common non-nested reference.
        if (typeRef.ResolutionScope.Kind != HandleKind.TypeReference)
            return GetFullName(reader.GetString(typeRef.Namespace), reader.GetString(typeRef.Name));

        // Nested: walk the resolution-scope chain iteratively (a self-scoped TypeReference cannot
        // recurse forever) and qualify outermost-first, stopping if the accumulated name amplifies
        // past the length ceiling.
        var chain = NestedTypeName.ResolutionScopeChain(reader, handle);
        var builder = new StringBuilder();
        for (int i = 0; i < chain.Count; i++)
        {
            var current = reader.GetTypeReference(chain[i]);
            if (i == 0)
                builder.Append(GetFullName(reader.GetString(current.Namespace), reader.GetString(current.Name)));
            else
                builder.Append('.').Append(reader.GetString(current.Name));
            if (builder.Length > NestedTypeName.MaxLength)
                break;
        }
        return builder.ToString();
    }

    /// <summary>
    /// Gets the type name from a TypeDefinition handle.
    /// </summary>
    public static string GetTypeNameFromDefinition(MetadataReader reader, TypeDefinitionHandle handle)
        => GetFullName(reader, reader.GetTypeDefinition(handle));

    /// <summary>
    /// Gets the type name from a TypeSpecification handle (generic instantiations).
    /// A TypeSpec blob is inspected (untrusted) metadata, so a malformed deeply-nested or
    /// huge-count signature would overflow SRM's native-recursive decoder (an uncatchable
    /// <see cref="System.StackOverflowException"/>) or trigger a huge pre-allocation. This is a
    /// *top-level* decode (SignatureDecoder's own re-entry guard only sees nested TypeSpecs
    /// reached by handle), so prescan with <see cref="SignatureBlobGuard"/> and fail closed to a
    /// parseable placeholder type instead of crashing.
    /// </summary>
    public static string GetTypeNameFromSpecification(MetadataReader reader, TypeSpecificationHandle handle, GenericContext? context = null)
    {
        var typeSpec = reader.GetTypeSpecification(handle);
        if (!SignatureBlobGuard.IsSafeToDecode(reader, typeSpec.Signature, SignatureBlobGuard.Kind.TypeSpecification))
            return "object";
        return typeSpec.DecodeSignature(SignatureDecoder.Instance, context);
    }

    /// <summary>
    /// Gets the full name of a type definition (Namespace.Name), qualifying a
    /// nested type through its declaring type (Outer.Inner).
    /// </summary>
    public static string GetFullName(MetadataReader reader, TypeDefinition typeDef)
    {
        // Fast, allocation-free path for the common non-nested definition.
        if (typeDef.GetDeclaringType().IsNil)
            return GetFullName(reader.GetString(typeDef.Namespace), reader.GetString(typeDef.Name));

        // Nested: walk the declaring-type chain iteratively (a self-nested TypeDefinition cannot
        // recurse forever) and qualify outermost-first, stopping if the accumulated name amplifies
        // past the length ceiling.
        return QualifyDeclaringChain(reader, typeDef, '.');
    }

    static string QualifyDeclaringChain(MetadataReader reader, TypeDefinition typeDef, char separator)
    {
        // Re-derive the leaf handle so the shared chain walker owns the bound; TypeDefinition does
        // not expose its own handle, so climb one level and prepend the leaf.
        var builder = new StringBuilder();
        var chain = NestedTypeName.DeclaringChain(reader, typeDef.GetDeclaringType());
        for (int i = 0; i < chain.Count; i++)
        {
            var current = reader.GetTypeDefinition(chain[i]);
            if (i == 0)
                builder.Append(GetFullName(reader.GetString(current.Namespace), reader.GetString(current.Name)));
            else
                builder.Append(separator).Append(reader.GetString(current.Name));
            if (builder.Length > NestedTypeName.MaxLength)
                return builder.ToString();
        }
        builder.Append(separator).Append(reader.GetString(typeDef.Name));
        return builder.ToString();
    }

    /// <summary>
    /// Gets the full name of a type (Namespace.Name) from an ApiType-like structure.
    /// </summary>
    public static string GetFullName(string? ns, string name)
    {
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    /// <summary>
    /// Renders a generic instantiation by substituting the supplied type arguments
    /// at each <c>`N</c> arity marker in the open type name, preserving the
    /// surrounding text — crucially any trailing nested-type segment such as the
    /// <c>.Enumerator</c> in <c>Dictionary`2.Enumerator</c>. Arguments are consumed
    /// in order across arity markers (so <c>Outer`1.Inner`1</c> with two arguments
    /// becomes <c>Outer&lt;A&gt;.Inner&lt;B&gt;</c>). When the name carries no arity
    /// marker the arguments are appended once, matching the simple
    /// <c>Name&lt;args&gt;</c> form.
    /// </summary>
    public static string ApplyGenericArguments(string genericTypeName, IReadOnlyList<string> typeArguments)
    {
        if (!genericTypeName.Contains('`'))
        {
            return typeArguments.Count == 0
                ? genericTypeName
                : $"{genericTypeName}<{string.Join(", ", typeArguments)}>";
        }

        var result = new System.Text.StringBuilder(genericTypeName.Length + 16);
        var argIndex = 0;
        for (var i = 0; i < genericTypeName.Length; i++)
        {
            if (genericTypeName[i] != '`')
            {
                result.Append(genericTypeName[i]);
                continue;
            }

            var digitStart = i + 1;
            var digitEnd = digitStart;
            while (digitEnd < genericTypeName.Length && char.IsDigit(genericTypeName[digitEnd]))
                digitEnd++;

            if (digitEnd == digitStart
                || !int.TryParse(genericTypeName.AsSpan(digitStart, digitEnd - digitStart), out var arity)
                || arity <= 0)
            {
                result.Append('`');
                continue;
            }

            var take = Math.Min(arity, typeArguments.Count - argIndex);
            result.Append('<');
            for (var k = 0; k < take; k++)
            {
                if (k > 0)
                    result.Append(", ");
                result.Append(typeArguments[argIndex + k]);
            }
            result.Append('>');
            argIndex += take;
            i = digitEnd - 1;
        }

        return result.ToString();
    }

    /// <summary>
    /// Formats raw metadata type names for display by replacing CLR generic arity suffixes
    /// with readable type parameter placeholders.
    /// </summary>
    public static string FormatDisplayName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName) || !typeName.Contains('`'))
            return typeName;

        var result = new System.Text.StringBuilder(typeName.Length + 8);
        for (var i = 0; i < typeName.Length; i++)
        {
            // A single arity marker is capped (MaxGenericArity), but a name carrying many bounded
            // markers still multiplies its untrusted length into an OOM-scale string. Once the
            // accumulated output passes the length ceiling the name is malformed / adversarial, so
            // append the untouched remainder raw and stop expanding.
            if (result.Length > NestedTypeName.MaxLength)
            {
                result.Append(typeName, i, typeName.Length - i);
                break;
            }

            if (typeName[i] != '`')
            {
                result.Append(typeName[i]);
                continue;
            }

            var digitStart = i + 1;
            var digitEnd = digitStart;
            while (digitEnd < typeName.Length && char.IsDigit(typeName[digitEnd]))
                digitEnd++;

            if (digitEnd == digitStart || !int.TryParse(typeName.AsSpan(digitStart, digitEnd - digitStart), out var arity) || arity <= 0 || arity > MaxGenericArity)
            {
                result.Append(typeName[i]);
                continue;
            }

            result.Append('<');
            for (var parameterIndex = 1; parameterIndex <= arity; parameterIndex++)
            {
                if (parameterIndex > 1)
                    result.Append(", ");
                result.Append(arity == 1 ? "T" : $"T{parameterIndex}");
            }
            result.Append('>');
            i = digitEnd - 1;
        }

        return result.ToString();
    }
}
