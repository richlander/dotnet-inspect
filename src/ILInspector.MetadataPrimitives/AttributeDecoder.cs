using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.ExceptionServices;

namespace ILInspector.Metadata;

/// <summary>
/// The SRM-only decode half of custom-attribute reading: resolves an
/// attribute's type name and decodes its constructor blob to typed argument
/// values. Rendering those values to C# (or any other surface) is the caller's
/// concern — this layer is shared by anything that needs attribute data.
/// </summary>
public static class AttributeDecoder
{
    internal sealed class MaterializationContext(Action<int> observe)
    {
        Dictionary<string, TypeDefinitionHandle>? _typeDefinitionsByName;
        ExceptionDispatchInfo? _typeDefinitionsByNameFailure;
        MetadataTypeNameFailure? _typeDefinitionsByNameFailureModel;
        bool _typeDefinitionsByNameObserverFailure;

        public void Observe(int characters) => observe(characters);

        public bool TryGetCachedIndexFailure(
            [NotNullWhen(true)] out MetadataTypeNameFailure? failure)
        {
            if (_typeDefinitionsByNameFailureModel is not null
                && !_typeDefinitionsByNameObserverFailure)
            {
                failure = _typeDefinitionsByNameFailureModel;
                return true;
            }

            failure = null;
            return false;
        }

        public Dictionary<string, TypeDefinitionHandle> GetOrCreateTypeDefinitionsByName(
            Func<Dictionary<string, TypeDefinitionHandle>> create)
        {
            if (_typeDefinitionsByName is not null)
                return _typeDefinitionsByName;
            if (_typeDefinitionsByNameFailure is not null)
            {
                if (_typeDefinitionsByNameObserverFailure)
                {
                    throw new MaterializationObserverException(
                        _typeDefinitionsByNameFailure);
                }
                _typeDefinitionsByNameFailure.Throw();
            }

            try
            {
                return _typeDefinitionsByName = create();
            }
            catch (MaterializationObserverException ex)
            {
                _typeDefinitionsByNameFailure = ex.Failure;
                _typeDefinitionsByNameObserverFailure = true;
                throw;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                _typeDefinitionsByNameFailure =
                    ExceptionDispatchInfo.Capture(ex);
                _typeDefinitionsByNameFailureModel =
                    ex is TypeDefinitionIndexException index
                        ? index.Failure
                        : MetadataTypeNameFailure.Malformed(default, ex.Message);
                throw;
            }
        }
    }

    /// <summary>
    /// The fully qualified type name of an attribute, from its constructor handle.
    /// </summary>
    public static string? GetAttributeTypeName(
        MetadataReader reader,
        EntityHandle constructorHandle)
        => GetAttributeTypeName(
            reader,
            constructorHandle,
            beforeMaterialize: null);

    public static string? GetAttributeTypeName(
        MetadataReader reader,
        EntityHandle constructorHandle,
        Action<int>? beforeMaterialize)
    {
        if (constructorHandle.Kind == HandleKind.MemberReference)
        {
            var memberRef = reader.GetMemberReference((MemberReferenceHandle)constructorHandle);
            return TypeResolver.GetTypeName(
                reader,
                memberRef.Parent,
                context: null,
                beforeMaterialize);
        }
        if (constructorHandle.Kind == HandleKind.MethodDefinition)
        {
            var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)constructorHandle);
            TypeDefinitionHandle declaringType = methodDef.GetDeclaringType();
            return TypeResolver.GetTypeNameFromDefinition(
                reader,
                declaringType,
                beforeMaterialize);
        }
        return null;
    }

    /// <summary>
    /// Decodes an attribute's fixed and named arguments to typed values, or null
    /// when the blob cannot be decoded. Argument <c>Type</c> strings are C#
    /// keywords for primitives, <c>System.Type</c> for typeof targets, and the
    /// full type name otherwise (enums, etc.).
    /// </summary>
    public static CustomAttributeValue<string>? TryDecode(
        MetadataReader reader,
        CustomAttribute attribute)
        => TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: false,
            beforeMaterialize: null);

    public static CustomAttributeValue<string>? TryDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize)
        => TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: false,
            beforeMaterialize);

    /// <summary>
    /// Decodes an attribute while preserving the complete serialized names of
    /// <see cref="Type"/> fixed arguments, including nesting and assembly syntax.
    /// </summary>
    public static CustomAttributeValue<string>? TryDecodePreservingSerializedTypeNames(
        MetadataReader reader,
        CustomAttribute attribute)
        => TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: true,
            beforeMaterialize: null);

    static CustomAttributeValue<string>? TryDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        bool preserveSerializedTypeNames,
        Action<int>? beforeMaterialize)
    {
        var provider = new ArgTypeProvider(
            reader,
            preserveSerializedTypeNames,
            beforeMaterialize);
        try
        {
            if (!CustomAttributeValueGuard.IsSafeToDecode(
                    reader,
                    attribute,
                    beforeMaterialize,
                    provider.GetUnderlyingEnumType))
                return null;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return null;
        }

        try
        {
            return attribute.DecodeValue(provider);
        }
        catch (MaterializationObserverException ex)
        {
            ex.Rethrow();
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Type provider for attribute-blob decoding: primitives as C# keywords, everything else as its full name (enums and typeof targets).</summary>
    sealed class ArgTypeProvider(
        MetadataReader reader,
        bool preserveSerializedTypeNames,
        Action<int>? beforeMaterialize) : ICustomAttributeTypeProvider<string>
    {
        Dictionary<string, TypeDefinitionHandle>? _typeDefinitionsByName;
        readonly MaterializationContext? _materializationContext =
            beforeMaterialize?.Target as MaterializationContext;

        public string GetPrimitiveType(PrimitiveTypeCode code) => code switch
        {
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
            _ => "object",
        };

        public string GetSystemType() => "System.Type";
        public bool IsSystemType(string type) => type == "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle handle, byte rawTypeKind)
            => TypeResolver.GetTypeNameFromDefinition(r, handle, ObserveBeforeMaterialize);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle handle, byte rawTypeKind)
            => TypeResolver.GetTypeName(
                r,
                handle,
                context: null,
                beforeMaterialize: ObserveBeforeMaterialize) ?? "object";
        public string GetTypeFromSerializedName(string name)
        {
            if (preserveSerializedTypeNames)
                return name;
            int comma = name.IndexOf(',');
            return comma >= 0 ? name[..comma] : name;
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
            => TypeDefinitionsByName.TryGetValue(type, out var handle)
                ? EnumUnderlyingPrimitive.FromDefinition(reader, handle)
                : PrimitiveTypeCode.Int32;

        Dictionary<string, TypeDefinitionHandle> TypeDefinitionsByName =>
            _materializationContext?.GetOrCreateTypeDefinitionsByName(
                BuildTypeDefinitionIndex)
            ?? (_typeDefinitionsByName ??= BuildTypeDefinitionIndex());

        Dictionary<string, TypeDefinitionHandle> BuildTypeDefinitionIndex()
        {
            ObserveBeforeMaterialize(reader.TypeDefinitions.Count);
            var result = new Dictionary<string, TypeDefinitionHandle>(
                reader.TypeDefinitions.Count,
                StringComparer.Ordinal);
            foreach (var handle in reader.TypeDefinitions)
            {
                string name;
                try
                {
                    name = TypeResolver.GetTypeNameFromDefinition(
                        reader,
                        handle,
                        ObserveBeforeMaterialize);
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException or ArgumentOutOfRangeException)
                {
                    throw new TypeDefinitionIndexException(
                        MetadataTypeNameFailure.Malformed(handle, ex.Message));
                }

                result.TryAdd(name, handle);
            }
            return result;
        }

        void ObserveBeforeMaterialize(int characters)
        {
            try
            {
                beforeMaterialize?.Invoke(characters);
            }
            catch (Exception ex)
            {
                throw new MaterializationObserverException(
                    ExceptionDispatchInfo.Capture(ex));
            }
        }
    }

    sealed class TypeDefinitionIndexException(MetadataTypeNameFailure failure)
        : BadImageFormatException(failure.Detail)
    {
        public MetadataTypeNameFailure Failure { get; } = failure;
    }

    sealed class MaterializationObserverException(ExceptionDispatchInfo failure)
        : Exception(null, failure.SourceException)
    {
        public ExceptionDispatchInfo Failure { get; } = failure;

        public void Rethrow() =>
            Failure.Throw();
    }

}
