using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Builds a structural, cross-module identity string for a metadata method
/// definition. Unlike the display-oriented canonical member signature, this key
/// preserves the facts that distinguish CLR-observable method identities:
/// return type, calling convention and the instance bit, generic arity
/// (positionally, never by parameter name), custom modifiers
/// (<c>modreq</c>/<c>modopt</c>), the nested-versus-namespace boundary of every
/// referenced type, the defining assembly/module identity of every referenced
/// type (so two same-named types from different assemblies never collide), and
/// the calling convention of every function-pointer type. Two methods share a
/// key only when their signatures are interchangeable across modules, so a
/// recompiled donor member corresponds to its original exactly when the keys are
/// equal.
/// </summary>
public static class MethodStructuralSignature
{
    public static string Build(MetadataReader reader, MethodDefinition method)
    {
        string declaringType = StructuralTypeName.OfDefinition(reader, method.GetDeclaringType());
        string name = reader.GetString(method.Name);
        var signature = GuardedProviderDecode.Method(
            reader,
            method,
            StructuralSignatureTypeProvider.Instance,
            context: null,
            fallbackReturn: "?");
        string convention = signature.Header.IsInstance ? "instance" : "static";
        string callKind = signature.Header.CallingConvention.ToString();
        string parameters = string.Join(",", signature.ParameterTypes);
        return $"{declaringType}::{name}`{signature.GenericParameterCount} "
            + $"[{convention} {callKind}]({parameters}):{signature.ReturnType}";
    }

    /// <summary>
    /// Nesting-preserving, bounded, fail-closed full names. Nested types are
    /// joined with <c>+</c> so <c>A.B+C</c> never collapses onto <c>A.B.C</c>;
    /// generic arity is carried by the raw metadata name (for example
    /// <c>List`1</c>).
    /// </summary>
    static class StructuralTypeName
    {
        public static string OfDefinition(MetadataReader reader, TypeDefinitionHandle handle)
        {
            try
            {
                List<string> names = [];
                string? ns = null;
                var current = handle;
                for (int guard = 0; !current.IsNil && guard < MetadataSafetyPolicy.MaxRelationshipNodes; guard++)
                {
                    var definition = reader.GetTypeDefinition(current);
                    names.Add(reader.GetString(definition.Name));
                    var declaring = definition.GetDeclaringType();
                    if (declaring.IsNil)
                    {
                        ns = reader.GetString(definition.Namespace);
                        break;
                    }
                    current = declaring;
                }
                return Compose(ns, names);
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return "?";
            }
        }

        public static string OfReference(MetadataReader reader, TypeReferenceHandle handle)
        {
            try
            {
                List<string> names = [];
                string? ns = null;
                string scope = "";
                var current = handle;
                for (int guard = 0; guard < MetadataSafetyPolicy.MaxRelationshipNodes; guard++)
                {
                    var reference = reader.GetTypeReference(current);
                    names.Add(reader.GetString(reference.Name));
                    if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
                    {
                        current = (TypeReferenceHandle)reference.ResolutionScope;
                        continue;
                    }
                    ns = reader.GetString(reference.Namespace);
                    scope = DescribeScope(reader, reference.ResolutionScope);
                    break;
                }
                return Compose(ns, names, scope);
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return "?";
            }
        }

        /// <summary>
        /// The defining assembly or module of a referenced type. Including it in
        /// the key prevents two same-named types from different assemblies (for
        /// example <c>LibA::Shared.Token</c> and <c>LibB::Shared.Token</c>) from
        /// producing the same structural signature.
        /// </summary>
        static string DescribeScope(MetadataReader reader, EntityHandle scope)
        {
            try
            {
                switch (scope.Kind)
                {
                    case HandleKind.AssemblyReference:
                        var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
                        return FormatAssemblyIdentity(
                            reader.GetString(assembly.Name),
                            assembly.Version,
                            assembly.Culture.IsNil ? null : reader.GetString(assembly.Culture),
                            assembly.PublicKeyOrToken.IsNil ? null : reader.GetBlobBytes(assembly.PublicKeyOrToken));
                    case HandleKind.ModuleReference:
                        return $"module:{reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)scope).Name)}";
                    case HandleKind.ModuleDefinition:
                        return $"module:{reader.GetString(reader.GetModuleDefinition().Name)}";
                    default:
                        // Nil (ExportedType / current-module lookup) or an
                        // unexpected scope kind carries no additional identity.
                        return "";
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return "?";
            }
        }

        static string FormatAssemblyIdentity(string name, Version version, string? culture, byte[]? publicKeyOrToken)
        {
            string cultureText = string.IsNullOrEmpty(culture) ? "neutral" : culture;
            string keyText = publicKeyOrToken is { Length: > 0 } ? Convert.ToHexString(publicKeyOrToken) : "null";
            return $"{name}, {version}, {cultureText}, {keyText}";
        }

        static string Compose(string? ns, List<string> leafToRoot, string scope = "")
        {
            leafToRoot.Reverse();
            string nested = string.Join("+", leafToRoot);
            string full = string.IsNullOrEmpty(ns) ? nested : $"{ns}.{nested}";
            return string.IsNullOrEmpty(scope) ? full : $"{full}[{scope}]";
        }
    }

    /// <summary>
    /// Structural signature provider: preserves custom modifiers, resolves types
    /// with nesting intact, and emits generic parameters positionally so that
    /// parameter renaming across modules cannot change the key.
    /// </summary>
    sealed class StructuralSignatureTypeProvider : ISignatureTypeProvider<string, object?>
    {
        public static readonly StructuralSignatureTypeProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
            => typeCode switch
            {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.Void => "System.Void",
                PrimitiveTypeCode.TypedReference => "System.TypedReference",
                _ => typeCode.ToString(),
            };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => StructuralTypeName.OfDefinition(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => StructuralTypeName.OfReference(reader, handle);

        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return "System.Object";
            using (scope)
            {
                return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
            }
        }

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetArrayType(string elementType, ArrayShape shape)
            => $"{elementType}[{(shape.Rank <= 1 ? "*" : new string(',', shape.Rank - 1))}]";

        public string GetByReferenceType(string elementType) => $"{elementType}&";

        public string GetPointerType(string elementType) => $"{elementType}*";

        public string GetPinnedType(string elementType) => $"pinned {elementType}";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => $"{genericType}<{string.Join(",", typeArguments)}>";

        public string GetGenericTypeParameter(object? context, int index) => $"!{index}";

        public string GetGenericMethodParameter(object? context, int index) => $"!!{index}";

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            // Preserve the calling convention (managed vs unmanaged and the
            // specific unmanaged convention, which also surfaces as a return-type
            // modopt) and the vararg boundary; otherwise two function-pointer
            // types that differ only by convention would share a key.
            string parameters = string.Join(",", signature.ParameterTypes);
            string body = signature.RequiredParameterCount == signature.ParameterTypes.Length
                ? parameters
                : $"{parameters}|req{signature.RequiredParameterCount}";
            return $"delegate* [{signature.Header.CallingConvention}]({body}):{signature.ReturnType}";
        }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
            => isRequired
                ? $"modreq({modifier}){unmodifiedType}"
                : $"modopt({modifier}){unmodifiedType}";
    }
}
