using CSharpText;
using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// Reads and checks custom attributes on types and members.
/// </summary>
public static class AttributeReader
{
    // Matches the Browser per-model retained-text ceiling. Preflight may materialize
    // an enum type name only when it fits one model; larger names fail closed.
    private const int MaxPreflightMaterializedStringCharacters = 1_000_000;

    private const string EditorBrowsableAttributeName = "System.ComponentModel.EditorBrowsableAttribute";
    private const string ExtensionMarkerAttributeName = "System.Runtime.CompilerServices.ExtensionMarkerAttribute";
    private const string ExtensionMarkerNameAttributeName = "System.Runtime.CompilerServices.ExtensionMarkerNameAttribute";
    private const string ObsoleteAttributeName = "System.ObsoleteAttribute";
    private const string RequiredMembersFeatureName = "RequiredMembers";
    private const string RequiredMembersConstructorObsoleteMessage =
        "Constructors of types with required members are not supported in this version of your compiler.";
    private const string RefStructsFeatureName = "RefStructs";
    private const string RefStructsObsoleteMessage =
        "Types with embedded references are not supported in this version of your compiler.";

    /// <summary>
    /// Checks if the member has the [Extension] attribute.
    /// </summary>
    public static bool HasExtensionAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            if (AttributeConstructorTypeNameEquals(
                    reader,
                    attr.Constructor,
                    KnownAttributeNames.ExtensionAttribute))
            {
                return true;
            }
        }
        return false;
    }

    public static bool TryGetExtensionMarkerName(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out string? markerName)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            if (!AttributeConstructorTypeNameEquals(reader, attr.Constructor, ExtensionMarkerAttributeName)
                && !AttributeConstructorTypeNameEquals(reader, attr.Constructor, ExtensionMarkerNameAttributeName))
            {
                continue;
            }

            try
            {
                var blob = reader.GetBlobReader(attr.Value);
                if (blob.ReadUInt16() != 1)
                    break;

                markerName = blob.ReadSerializedString();
                return !string.IsNullOrEmpty(markerName);
            }
            catch (BadImageFormatException)
            {
                break;
            }
        }

        markerName = null;
        return false;
    }

    /// <summary>
    /// Checks if the member has EditorBrowsable(Never) or [Obsolete] attribute.
    /// </summary>
    public static bool HasHiddenAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            if (AttributeConstructorTypeNameEquals(reader, attr.Constructor, EditorBrowsableAttributeName))
            {
                if (IsEditorBrowsableNever(reader, attr))
                    return true;
            }
            else if (AttributeConstructorTypeNameEquals(reader, attr.Constructor, ObsoleteAttributeName))
            {
                if (!IsCompilerCompatibilityObsolete(
                        reader,
                        attributes,
                        TryGetAttributeDisplayValue(reader, attr)))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if the member has the [EditorBrowsable(Never)] attribute.
    /// </summary>
    public static bool HasEditorBrowsableNeverAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            if (AttributeConstructorTypeNameEquals(reader, attr.Constructor, EditorBrowsableAttributeName)
                && IsEditorBrowsableNever(reader, attr))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if the member has the [Obsolete] attribute, returning the optional message.
    /// </summary>
    public static bool TryGetObsoleteAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out string? message,
        Action<long>? preflight = null)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            if (!AttributeConstructorTypeNameEquals(reader, attr.Constructor, ObsoleteAttributeName))
                continue;

            long lowerBound = 0;
            if (preflight is not null
                && !TryGetFirstSerializedStringLength(reader, attr, out lowerBound))
            {
                lowerBound = 0;
            }
            preflight?.Invoke(lowerBound);
            message = TryGetAttributeDisplayValue(reader, attr);
            if (IsCompilerCompatibilityObsolete(reader, attributes, message))
            {
                message = null;
                return false;
            }

            return true;
        }
        message = null;
        return false;
    }

    public static bool HasRequiredMemberAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => HasAttribute(reader, attributes, KnownAttributeNames.RequiredMemberAttribute);

    /// <summary>
    /// Checks whether the member carries <c>RequiresUnsafeAttribute</c> — the
    /// metadata form of the <c>unsafe</c>/<c>extern</c> modifier stamped under the
    /// updated memory-safety rules. Tolerates the two namespace spellings.
    /// </summary>
    public static bool HasRequiresUnsafeAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => HasAttribute(reader, attributes, KnownAttributeNames.RequiresUnsafeAttribute)
        || HasAttribute(reader, attributes, KnownAttributeNames.RequiresUnsafeAttributeCompilerServices);

    private static bool IsCompilerCompatibilityObsolete(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string? message)
    {
        // Roslyn stamps a synthetic [Obsolete] on certain types/members purely to block older
        // compilers, pairing it with [CompilerFeatureRequired(<feature>)]. These are not real
        // deprecations, so they must not hide the API. Covers required members and ref structs
        // (Span<T>, ReadOnlySpan<T>, and other byref-like types).
        return (string.Equals(message, RequiredMembersConstructorObsoleteMessage, StringComparison.Ordinal)
                && HasCompilerFeatureRequiredAttribute(reader, attributes, RequiredMembersFeatureName))
            || (string.Equals(message, RefStructsObsoleteMessage, StringComparison.Ordinal)
                && HasCompilerFeatureRequiredAttribute(reader, attributes, RefStructsFeatureName));
    }

    private static bool HasCompilerFeatureRequiredAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string featureName)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            if (!AttributeConstructorTypeNameEquals(
                    reader,
                    attr.Constructor,
                    KnownAttributeNames.CompilerFeatureRequiredAttribute))
            {
                continue;
            }

            var value = TryGetAttributeDisplayValue(reader, attr);
            if (string.Equals(value, featureName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsEditorBrowsableNever(MetadataReader reader, CustomAttribute attr)
    {
        // Check if the value is EditorBrowsableState.Never (value = 1)
        var value = reader.GetBlobBytes(attr.Value);
        // Attribute blob format: 2-byte prolog (0x0001), then the enum value as int32
        if (value.Length >= 6)
        {
            int enumValue = value[2] | (value[3] << 8) | (value[4] << 16) | (value[5] << 24);
            return enumValue == 1; // EditorBrowsableState.Never
        }
        return false;
    }

    /// <summary>
    /// Checks if the member has a specific attribute by full type name.
    /// </summary>
    public static bool HasAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes, string attributeTypeName)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            if (AttributeConstructorTypeNameEquals(reader, attr.Constructor, attributeTypeName))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the fully qualified type name of an attribute from its constructor handle.
    /// </summary>
    public static string? GetAttributeTypeName(MetadataReader reader, EntityHandle constructorHandle)
        => AttributeDecoder.GetAttributeTypeName(reader, constructorHandle);

    /// <summary>
    /// True when the constructor's declaring type name equals
    /// <paramref name="expectedFullName"/> without materializing names that cannot
    /// match (length mismatch). Known attribute filters must use this so a hostile
    /// multi-MB attribute type name cannot allocate during presence checks.
    /// </summary>
    static bool AttributeConstructorTypeNameEquals(
        MetadataReader reader,
        EntityHandle constructorHandle,
        string expectedFullName)
    {
        if (!MetadataSafetyPolicy.TryCountAttributeConstructorTypeNameCharacters(
                reader,
                constructorHandle,
                out long characters)
            || characters != expectedFullName.Length)
        {
            return false;
        }

        return GetAttributeTypeName(reader, constructorHandle) == expectedFullName;
    }

    /// <summary>
    /// Gets custom attributes for a specific method, identified by type name, method name, and overload index.
    /// Returns short attribute names with optional display values. Filters out compiler-generated noise.
    /// </summary>
    public static List<(string Name, string? Value)> GetMethodAttributes(
        PEReader peReader, string fullTypeName, string methodName, int overloadIndex, bool publicOnly = true)
    {
        if (!peReader.HasMetadata) return [];

        var reader = peReader.GetMetadataReader();
        return ReadMethodAttributes(reader, FindMethodHandle(reader, fullTypeName, methodName, overloadIndex, publicOnly));
    }

    /// <summary>
    /// Overload for callers that have already resolved the declaring type handle (e.g. the API output
    /// formatter, which resolves each method's type once instead of re-scanning per section).
    /// </summary>
    public static List<(string Name, string? Value)> GetMethodAttributes(
        MetadataReader reader, TypeDefinitionHandle typeHandle, string methodName, int overloadIndex, bool publicOnly = true)
        => ReadMethodAttributes(reader, FindMethodHandleInType(reader, typeHandle, methodName, overloadIndex, publicOnly));

    /// <summary>
    /// Overload for callers that already hold the method's own
    /// <see cref="MethodDefinitionHandle"/> — the canonical same-reader address
    /// (see docs/design/member-body-substrate.md), free of the overload-ordinal
    /// drift the name+index overloads are subject to.
    /// </summary>
    public static List<(string Name, string? Value)> GetMethodAttributes(
        MetadataReader reader, MethodDefinitionHandle methodHandle)
        => ReadMethodAttributes(reader, methodHandle);

    private static List<(string Name, string? Value)> ReadMethodAttributes(MetadataReader reader, MethodDefinitionHandle methodHandle)
    {
        List<(string Name, string? Value)> results = [];
        if (methodHandle.IsNil) return results;

        var method = reader.GetMethodDefinition(methodHandle);
        foreach (var attrHandle in method.GetCustomAttributes())
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName == null || IsMethodNoiseAttribute(attrTypeName))
                continue;

            var shortName = TypeMatcher.GetShortAttributeName(attrTypeName);
            var value = TryGetAttributeDisplayValue(reader, attr);
            results.Add((shortName, value));
        }

        return results;
    }

    private static MethodDefinitionHandle FindMethodHandle(
        MetadataReader reader, string fullTypeName, string methodName, int overloadIndex, bool publicOnly)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (TypeResolver.GetFullName(reader, typeDef) != fullTypeName) continue;

            return FindMethodHandleInType(reader, typeHandle, methodName, overloadIndex, publicOnly);
        }

        return default;
    }

    private static MethodDefinitionHandle FindMethodHandleInType(
        MetadataReader reader, TypeDefinitionHandle typeHandle, string methodName, int overloadIndex, bool publicOnly)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        int matchIndex = 0;
        foreach (var mHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(mHandle);
            if (publicOnly && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                continue;
            if (reader.GetString(method.Name) != methodName)
                continue;

            if (matchIndex == overloadIndex)
                return mHandle;
            matchIndex++;
        }

        return default;
    }

    /// <summary>
    /// Compiler-generated attributes that are noise for method-level display.
    /// </summary>
    private static bool IsMethodNoiseAttribute(string name) => name switch
    {
        KnownAttributeNames.ExtensionAttribute => true,
        KnownAttributeNames.NullableContextAttribute => true,
        KnownAttributeNames.NullableAttribute => true,
        KnownAttributeNames.CompilerGeneratedAttribute => true,
        KnownAttributeNames.AsyncStateMachineAttribute => true,
        KnownAttributeNames.IteratorStateMachineAttribute => true,
        "System.Diagnostics.DebuggerStepThroughAttribute" => true,
        "System.Diagnostics.DebuggerHiddenAttribute" => true,
        KnownAttributeNames.MethodImplAttribute => false, // interesting to see
        _ => false
    };

    /// <summary>
    /// Renders the source attributes on an entity as their bracket contents
    /// (e.g. <c>Flags</c>, <c>Obsolete("msg")</c>), short-named, adding each
    /// attribute's namespace to <paramref name="namespaces"/> so a using is
    /// emitted. Compiler-emitted attributes the C# compiler re-synthesizes are
    /// filtered; any attribute whose arguments cannot be faithfully and validly
    /// rendered is skipped rather than emitted wrong.
    /// </summary>
    public static List<string> RenderAttributes(
        MetadataReader reader, CustomAttributeHandleCollection attributes, SortedSet<string>? namespaces = null,
        Func<string, bool>? skipAttribute = null,
        bool qualifyNames = false,
        Action<string>? beforeRetain = null,
        Action<long>? preflight = null)
    {
        var result = new List<string>();
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            // Count before GetAttributeTypeName. A multi-MB constructor type name
            // must never materialize (Sol R7: ~80-240 MB on Exceeded). Names above
            // MaxPreflightMaterializedStringCharacters fail closed via preflight
            // when a budget is open, and are skipped when unbounded — without
            // charging ordinary value lower-bound preflight callbacks.
            if (!MetadataSafetyPolicy.TryCountAttributeConstructorTypeNameCharacters(
                    reader,
                    attr.Constructor,
                    out long typeNameCharacters))
            {
                continue;
            }

            if (typeNameCharacters > MaxPreflightMaterializedStringCharacters)
            {
                preflight?.Invoke(typeNameCharacters);
                continue;
            }

            var typeName = GetAttributeTypeName(reader, attr.Constructor);
            if (typeName is null || IsReEmittedAttribute(typeName))
                continue;
            if (skipAttribute?.Invoke(typeName) == true)
                continue;
            if (TryRenderAttribute(reader, attr, typeName, qualifyNames, preflight) is not { } rendered)
                continue;
            beforeRetain?.Invoke(rendered);
            int lastDot = typeName.LastIndexOf('.');
            if (lastDot > 0)
                namespaces?.Add(typeName[..lastDot]);
            result.Add(rendered);
        }
        return result;
    }

    // Scope-uniform entry points: every metadata entity exposes its own
    // GetCustomAttributes(), so these thin overloads render the attributes of an
    // assembly, module, type, or member without each caller repeating the
    // reader.GetX(handle).GetCustomAttributes() dance.
    public static List<string> RenderAssemblyAttributes(MetadataReader reader, SortedSet<string>? namespaces = null)
        => RenderAttributes(reader, reader.GetAssemblyDefinition().GetCustomAttributes(), namespaces);

    public static List<string> RenderModuleAttributes(MetadataReader reader, SortedSet<string>? namespaces = null)
        => RenderAttributes(reader, reader.GetModuleDefinition().GetCustomAttributes(), namespaces);

    public static List<string> RenderAttributes(MetadataReader reader, TypeDefinitionHandle type, SortedSet<string>? namespaces = null)
        => RenderAttributes(reader, reader.GetTypeDefinition(type).GetCustomAttributes(), namespaces);

    public static List<string> RenderAttributes(MetadataReader reader, MethodDefinitionHandle method, SortedSet<string>? namespaces = null)
        => RenderAttributes(reader, reader.GetMethodDefinition(method).GetCustomAttributes(), namespaces);

    public static List<string> RenderAttributes(MetadataReader reader, FieldDefinitionHandle field, SortedSet<string>? namespaces = null)
        => RenderAttributes(reader, reader.GetFieldDefinition(field).GetCustomAttributes(), namespaces);

    public static List<string> RenderAttributes(MetadataReader reader, PropertyDefinitionHandle property, SortedSet<string>? namespaces = null)
        => RenderAttributes(reader, reader.GetPropertyDefinition(property).GetCustomAttributes(), namespaces);

    public static List<string> RenderAttributes(MetadataReader reader, EventDefinitionHandle @event, SortedSet<string>? namespaces = null)
        => RenderAttributes(reader, reader.GetEventDefinition(@event).GetCustomAttributes(), namespaces);

    public static List<string> RenderAttributes(MetadataReader reader, ParameterHandle parameter, SortedSet<string>? namespaces = null)
        => RenderAttributes(reader, reader.GetParameter(parameter).GetCustomAttributes(), namespaces);

    public static List<string> RenderParameterAttributes(
        MetadataReader reader,
        ParameterHandle parameter,
        SortedSet<string>? namespaces = null,
        Action<string>? beforeRetain = null,
        Action<long>? preflight = null)
    {
        var result = RenderAttributes(
            reader,
            reader.GetParameter(parameter).GetCustomAttributes(),
            namespaces,
            IsParameterSyntaxAttribute,
            qualifyNames: true,
            beforeRetain: beforeRetain,
            preflight: preflight);
        try
        {
            if (TryRenderMarshalAsAttribute(reader, parameter) is { } marshalAs)
            {
                beforeRetain?.Invoke(marshalAs);
                result.Add(marshalAs);
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            // Match custom-attribute rendering: malformed or unsupported metadata is not emitted wrong.
        }

        return result;
    }

    static string? TryRenderMarshalAsAttribute(MetadataReader reader, ParameterHandle parameterHandle)
    {
        var parameter = reader.GetParameter(parameterHandle);
        if ((parameter.Attributes & ParameterAttributes.HasFieldMarshal) == 0)
            return null;

        var descriptor = parameter.GetMarshallingDescriptor();
        if (descriptor.IsNil)
            return null;

        var blob = reader.GetBlobReader(descriptor);
        if (blob.RemainingBytes == 0)
            return null;

        var nativeType = blob.ReadByte();
        if (nativeType == 0x2a)
            return TryRenderArrayMarshalAs(ref blob);

        if (blob.RemainingBytes != 0 || UnmanagedTypeName(nativeType) is not { } unmanagedType)
            return null;

        return $"System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.{unmanagedType})";
    }

    static string? TryRenderArrayMarshalAs(ref BlobReader blob)
    {
        var arguments = new List<string>
        {
            "System.Runtime.InteropServices.UnmanagedType.LPArray",
        };
        if (blob.RemainingBytes > 0)
        {
            var elementType = blob.ReadByte();
            if (elementType != 0x50)
            {
                if (UnmanagedTypeName(elementType) is not { } elementUnmanagedType)
                    return null;
                arguments.Add($"ArraySubType = System.Runtime.InteropServices.UnmanagedType.{elementUnmanagedType}");
            }
        }

        if (blob.RemainingBytes > 0)
        {
            var sizeParamIndex = blob.ReadCompressedInteger();
            if (blob.RemainingBytes == 0)
            {
                arguments.Add($"SizeParamIndex = {sizeParamIndex}");
            }
            else
            {
                var sizeConst = blob.ReadCompressedInteger();
                var sizeParamSpecified = blob.ReadCompressedInteger() != 0;
                if (blob.RemainingBytes != 0)
                    return null;
                if (sizeParamSpecified)
                    arguments.Add($"SizeParamIndex = {sizeParamIndex}");
                arguments.Add($"SizeConst = {sizeConst}");
            }
        }

        return $"System.Runtime.InteropServices.MarshalAs({string.Join(", ", arguments)})";
    }

    static string? UnmanagedTypeName(byte nativeType)
        => nativeType switch
        {
            0x02 => "Bool",
            0x03 => "I1",
            0x04 => "U1",
            0x05 => "I2",
            0x06 => "U2",
            0x07 => "I4",
            0x08 => "U4",
            0x09 => "I8",
            0x0a => "U8",
            0x0b => "R4",
            0x0c => "R8",
            0x0f => "Currency",
            0x13 => "BStr",
            0x14 => "LPStr",
            0x15 => "LPWStr",
            0x16 => "LPTStr",
            0x19 => "IUnknown",
            0x1a => "IDispatch",
            0x1b => "Struct",
            0x1c => "Interface",
            0x1d => "SafeArray",
            0x1f => "SysInt",
            0x20 => "SysUInt",
            0x23 => "AnsiBStr",
            0x24 => "TBStr",
            0x25 => "VariantBool",
            0x26 => "FunctionPtr",
            0x28 => "AsAny",
            0x2a => "LPArray",
            0x2b => "LPStruct",
            0x2d => "Error",
            0x2e => "IInspectable",
            0x2f => "HString",
            0x30 => "LPUTF8Str",
            _ => null,
        };

    /// <summary>
    /// Renders the attributes on a method, resolved by the same name + public-only
    /// overload counting the decompiler uses to select the body, so the attributes
    /// pair with the right overload.
    /// </summary>
    public static List<string> RenderMethodAttributes(
        MetadataReader reader, TypeDefinitionHandle typeHandle, string methodName, int overloadIndex, bool publicOnly, SortedSet<string>? namespaces = null)
    {
        var handle = FindMethodHandleInType(reader, typeHandle, methodName, overloadIndex, publicOnly);
        return handle.IsNil ? [] : RenderAttributes(reader, handle, namespaces);
    }

    /// <summary>
    /// Renders the attributes on a method addressed directly by its
    /// <see cref="MethodDefinitionHandle"/> — the canonical same-reader address
    /// (see docs/design/member-body-substrate.md), free of the overload-ordinal
    /// drift the name+index overload is subject to.
    /// </summary>
    public static List<string> RenderMethodAttributes(
        MetadataReader reader, MethodDefinitionHandle handle, SortedSet<string>? namespaces = null)
        => handle.IsNil ? [] : RenderAttributes(reader, handle, namespaces);

    /// <summary>Renders the attributes on a property, found by name within the type.</summary>
    public static List<string> RenderPropertyAttributes(
        MetadataReader reader, TypeDefinitionHandle typeHandle, string propertyName, SortedSet<string>? namespaces = null)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            if (reader.GetString(reader.GetPropertyDefinition(propertyHandle).Name) == propertyName)
                return RenderAttributes(reader, propertyHandle, namespaces);
        }
        return [];
    }

    /// <summary>Renders the attributes on an event, found by name within the type.</summary>
    public static List<string> RenderEventAttributes(
        MetadataReader reader, TypeDefinitionHandle typeHandle, string eventName, SortedSet<string>? namespaces = null)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        foreach (var eventHandle in typeDef.GetEvents())
        {
            if (reader.GetString(reader.GetEventDefinition(eventHandle).Name) == eventName)
                return RenderAttributes(reader, eventHandle, namespaces);
        }
        return [];
    }

    static string? TryRenderAttribute(
        MetadataReader reader,
        CustomAttribute attr,
        string typeName,
        bool qualifyName,
        Action<long>? preflight)
    {
        if (preflight is not null)
        {
            if (!TryGetAttributeStringLowerBound(reader, attr, out long lowerBound))
                return null;
            preflight(lowerBound);
        }
        if (AttributeDecoder.TryDecode(reader, attr) is not { } value)
            return null;
        string name = qualifyName ? GetQualifiedAttributeName(typeName) : TypeMatcher.GetShortAttributeName(typeName);
        var args = new List<string>();
        foreach (var arg in value.FixedArguments)
        {
            if (RenderArgument(arg.Type, arg.Value) is not { } text)
                return null;
            args.Add(text);
        }
        foreach (var named in value.NamedArguments)
        {
            if (RenderArgument(named.Type, named.Value) is not { } text)
                return null;
            args.Add($"{named.Name} = {text}");
        }
        return args.Count == 0 ? name : $"{name}({string.Join(", ", args)})";
    }

    static bool TryGetAttributeStringLowerBound(
        MetadataReader reader, CustomAttribute attribute, out long lowerBound)
    {
        lowerBound = 0;
        try
        {
            var provider = new AttributeValueTypeProvider();
            var parameters = attribute.Constructor.Kind switch
            {
                HandleKind.MethodDefinition => GuardedProviderDecode.Method(
                    reader,
                    reader.GetMethodDefinition(
                        (MethodDefinitionHandle)attribute.Constructor),
                    provider,
                    context: null,
                    AttributeValueType.Invalid).ParameterTypes,
                HandleKind.MemberReference => GuardedProviderDecode.MemberRefMethod(
                    reader,
                    reader.GetMemberReference(
                        (MemberReferenceHandle)attribute.Constructor),
                    provider,
                    context: null,
                    AttributeValueType.Invalid).ParameterTypes,
                _ => default,
            };
            if (parameters.IsDefault)
                return false;

            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1)
                return false;
            foreach (var parameter in parameters)
            {
                if (!TryScanAttributeValue(reader, ref blob, parameter, ref lowerBound))
                    return false;
            }
            int namedCount = blob.ReadUInt16();
            for (int i = 0; i < namedCount; i++)
            {
                byte kind = blob.ReadByte();
                // Named field/property names are retained as "Name = value", so
                // charge them here. Skipping them let DecodeValue allocate a
                // multi-MB name under a lower bound of 0.
                if (kind is not (0x53 or 0x54)
                    || !TryReadSerializedType(ref blob, out var type)
                    || !TryCountSerializedString(ref blob, ref lowerBound)
                    || !TryScanAttributeValue(reader, ref blob, type, ref lowerBound))
                {
                    return false;
                }
            }
            // A desynced scan that stops early would otherwise report success with a
            // lower bound of 0 and let DecodeValue allocate the unread remainder.
            return blob.RemainingBytes == 0;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or DecoderFallbackException
                or OverflowException)
        {
            return false;
        }
    }
    static bool TryScanAttributeValue(
        MetadataReader reader, ref BlobReader blob, AttributeValueType type, ref long lowerBound)
    {
        while (type.Code == SerializationTypeCode.TaggedObject)
        {
            if (!TryReadSerializedType(ref blob, out type))
                return false;
        }
        if (type.Code == SerializationTypeCode.Enum)
            return TryScanEnumValue(reader, ref blob, type.TypeName!, ref lowerBound);

        int size = FixedPrimitiveStorageSize(type.Code);
        if (size != 0)
            return TrySkipBytes(ref blob, size);
        return type.Code switch
        {
            SerializationTypeCode.String => TryCountSerializedString(ref blob, ref lowerBound),
            SerializationTypeCode.Type => TryScanTypeValue(ref blob, ref lowerBound),
            SerializationTypeCode.SZArray => blob.ReadUInt32() == uint.MaxValue,
            _ => false,
        };
    }

    static bool TryScanEnumValue(
        MetadataReader reader,
        ref BlobReader blob,
        string typeName,
        ref long lowerBound)
    {
        // Mirror AttributeDecoder.ArgTypeProvider.GetUnderlyingEnumType so the
        // scanner and SRM custom-attribute decoder agree on the value layout.
        if (!TryGetEnumUnderlyingPrimitive(reader, typeName, out PrimitiveTypeCode code))
            return false;

        if (code == PrimitiveTypeCode.String)
            return TryCountSerializedString(ref blob, ref lowerBound);

        int size = FixedPrimitiveStorageSize(code);
        return size != 0 && TrySkipBytes(ref blob, size);
    }
    static bool TryScanTypeValue(ref BlobReader blob, ref long lowerBound)
    {
        if (!TryReadSerializedStringLength(ref blob, out int byteCount))
            return false;
        if (byteCount < 0)
            return true;

        // DecodeValue materializes the full assembly-qualified string, while
        // retained typeof() text uses only the pre-comma simple name. Charge the
        // retained slice into lowerBound, but refuse names whose full decoded size
        // exceeds one model so a giant assembly suffix cannot allocate first.
        byte[] bytes = ArrayPool<byte>.Shared.Rent(Math.Min(byteCount, 4096));
        char[] chars = ArrayPool<char>.Shared.Rent(bytes.Length + 1);
        try
        {
            Decoder decoder = Encoding.UTF8.GetDecoder();
            int remaining = byteCount;
            long materializeCharacters = 0;
            long retainedCharacters = 0;
            bool beforeAssemblyName = true;
            while (remaining > 0)
            {
                int count = Math.Min(remaining, bytes.Length);
                blob.ReadBytes(count, bytes, 0);
                remaining -= count;
                int charCount = decoder.GetChars(
                    bytes.AsSpan(0, count),
                    chars,
                    flush: remaining == 0);
                for (int index = 0; index < charCount; index++)
                {
                    char character = chars[index];
                    materializeCharacters++;
                    if (materializeCharacters > MaxPreflightMaterializedStringCharacters)
                        return false;
                    if (beforeAssemblyName && character == ',')
                    {
                        beforeAssemblyName = false;
                    }
                    else if (beforeAssemblyName)
                    {
                        if (character is '`' or '[')
                            return false;
                        retainedCharacters++;
                    }
                }
            }
            lowerBound += retainedCharacters;
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<char>.Shared.Return(chars);
        }
    }
    static bool TryReadSerializedType(ref BlobReader blob, out AttributeValueType type)
    {
        var code = blob.ReadSerializationTypeCode();
        if (code == SerializationTypeCode.Enum)
        {
            // Count first so a multi-MB enum type name cannot allocate during
            // preflight. Only materialize names that fit one model budget.
            int start = blob.Offset;
            long nameCharacters = 0;
            if (!TryCountSerializedString(ref blob, ref nameCharacters)
                || nameCharacters > MaxPreflightMaterializedStringCharacters)
            {
                type = AttributeValueType.Invalid;
                return false;
            }
            blob.Offset = start;
            string? name = blob.ReadSerializedString();
            if (name is null)
            {
                type = AttributeValueType.Invalid;
                return false;
            }
            int comma = name.IndexOf(',');
            type = new(code, comma < 0 ? name : name[..comma]);
            return true;
        }
        if (code == SerializationTypeCode.SZArray)
        {
            var elementCode = blob.ReadSerializationTypeCode();
            bool validElement;
            if (elementCode == SerializationTypeCode.Enum)
            {
                int start = blob.Offset;
                long nameCharacters = 0;
                validElement = TryCountSerializedString(ref blob, ref nameCharacters)
                    && nameCharacters <= MaxPreflightMaterializedStringCharacters;
                if (validElement)
                {
                    blob.Offset = start;
                    validElement = blob.ReadSerializedString() is not null;
                }
            }
            else
            {
                validElement = AttributeValueType.For(elementCode).Code
                    != SerializationTypeCode.Invalid;
            }
            type = AttributeValueType.Array;
            return elementCode != SerializationTypeCode.SZArray && validElement;
        }
        type = AttributeValueType.For(code);
        return type.Code != SerializationTypeCode.Invalid;
    }
    static bool TryCountSerializedString(ref BlobReader blob, ref long lowerBound)
    {
        if (!TryReadSerializedStringLength(ref blob, out int byteCount))
            return false;
        if (byteCount < 0)
            return true;

        // Stream via Convert so multi-byte UTF-8 that straddles the 4 KiB window is
        // not double-counted by GetCharCount's replacement fallback (Opus R6).
        const int Window = 4096;
        byte[] byteBuffer = ArrayPool<byte>.Shared.Rent(Math.Min(byteCount, Window));
        char[] charBuffer = ArrayPool<char>.Shared.Rent(Window);
        try
        {
            Decoder decoder = Encoding.UTF8.GetDecoder();
            int remaining = byteCount;
            while (remaining > 0)
            {
                int count = Math.Min(remaining, byteBuffer.Length);
                blob.ReadBytes(count, byteBuffer, 0);
                remaining -= count;
                bool flush = remaining == 0;
                int byteOffset = 0;
                bool completed;
                do
                {
                    decoder.Convert(
                        byteBuffer,
                        byteOffset,
                        count - byteOffset,
                        charBuffer,
                        0,
                        charBuffer.Length,
                        flush,
                        out int bytesUsed,
                        out int charsUsed,
                        out completed);
                    lowerBound += charsUsed;
                    byteOffset += bytesUsed;
                }
                while (!completed);
            }
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuffer);
            ArrayPool<char>.Shared.Return(charBuffer);
        }
    }

    static bool TryGetFirstSerializedStringLength(
        MetadataReader reader,
        CustomAttribute attribute,
        out long length)
    {
        length = 0;
        try
        {
            var blob = reader.GetBlobReader(attribute.Value);
            return blob.ReadUInt16() == 1
                && TryCountSerializedString(ref blob, ref length);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or DecoderFallbackException
                or OverflowException)
        {
            return false;
        }
    }

    static bool TrySkipSerializedString(ref BlobReader blob)
    {
        if (!TryReadSerializedStringLength(ref blob, out int byteCount))
            return false;
        if (byteCount >= 0)
            blob.Offset += byteCount;
        return true;
    }
    static bool TryReadSerializedStringLength(ref BlobReader blob, out int byteCount)
    {
        byte marker = blob.ReadByte();
        if (marker == 0xff)
        {
            byteCount = -1;
            return true;
        }
        blob.Offset--;
        byteCount = blob.ReadCompressedInteger();
        return byteCount <= blob.RemainingBytes;
    }
    static bool TrySkipBytes(ref BlobReader blob, int count)
    {
        if (count > blob.RemainingBytes)
            return false;
        blob.Offset += count; return true;
    }

    static int FixedPrimitiveStorageSize(SerializationTypeCode code) => code switch
    {
        SerializationTypeCode.Boolean or SerializationTypeCode.Byte or SerializationTypeCode.SByte => 1,
        SerializationTypeCode.Char or SerializationTypeCode.Int16 or SerializationTypeCode.UInt16 => 2,
        SerializationTypeCode.Int32 or SerializationTypeCode.UInt32 or SerializationTypeCode.Single => 4,
        SerializationTypeCode.Int64 or SerializationTypeCode.UInt64 or SerializationTypeCode.Double => 8,
        _ => 0,
    };

    static int FixedPrimitiveStorageSize(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.Boolean or PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte => 1,
        PrimitiveTypeCode.Char or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16 => 2,
        PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 or PrimitiveTypeCode.Single => 4,
        PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64 or PrimitiveTypeCode.Double => 8,
        // String is variable-length and handled by the caller. IntPtr/UIntPtr/
        // Object and any non-primitive field type are unscannable here.
        _ => 0,
    };

    static bool TryGetEnumUnderlyingPrimitive(
        MetadataReader reader,
        string typeName,
        out PrimitiveTypeCode code)
    {
        // AttributeDecoder.ArgTypeProvider.GetUnderlyingEnumType defaults every
        // unresolved case to Int32, including unknown type names and enums with
        // no instance field. Match that so preflight and decode stay aligned.
        code = PrimitiveTypeCode.Int32;
        foreach (var handle in reader.TypeDefinitions)
        {
            if (TypeResolver.GetTypeNameFromDefinition(reader, handle) != typeName)
                continue;

            var definition = reader.GetTypeDefinition(handle);
            foreach (var fieldHandle in definition.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.Static) != 0)
                    continue;

                code = GuardedProviderDecode.Field(
                    reader,
                    field,
                    EnumUnderlyingPrimitiveProvider.Instance,
                    context: null,
                    fallback: PrimitiveTypeCode.Int32);
                return true;
            }

            return true;
        }

        return true;
    }

    sealed class EnumUnderlyingPrimitiveProvider
        : ISignatureTypeProvider<PrimitiveTypeCode, object?>
    {
        public static EnumUnderlyingPrimitiveProvider Instance { get; } = new();

        public PrimitiveTypeCode GetPrimitiveType(PrimitiveTypeCode code) => code;
        public PrimitiveTypeCode GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetSZArrayType(PrimitiveTypeCode elementType)
            => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetArrayType(
            PrimitiveTypeCode elementType,
            ArrayShape shape) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetByReferenceType(PrimitiveTypeCode elementType)
            => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetPointerType(PrimitiveTypeCode elementType)
            => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericInstantiation(
            PrimitiveTypeCode genericType,
            ImmutableArray<PrimitiveTypeCode> typeArguments)
            => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericMethodParameter(
            object? genericContext,
            int index) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericTypeParameter(
            object? genericContext,
            int index) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetModifiedType(
            PrimitiveTypeCode modifier,
            PrimitiveTypeCode unmodifiedType,
            bool isRequired) => unmodifiedType;
        public PrimitiveTypeCode GetPinnedType(PrimitiveTypeCode elementType)
            => elementType;
        public PrimitiveTypeCode GetFunctionPointerType(
            MethodSignature<PrimitiveTypeCode> signature)
            => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) => PrimitiveTypeCode.Int32;
    }

    readonly record struct AttributeValueType(SerializationTypeCode Code, string? TypeName = null)
    {
        public static readonly AttributeValueType Invalid = new(SerializationTypeCode.Invalid);
        public static readonly AttributeValueType Array = new(SerializationTypeCode.SZArray);
        public static AttributeValueType For(SerializationTypeCode code)
            => (code >= SerializationTypeCode.Boolean && code <= SerializationTypeCode.String)
                || code is SerializationTypeCode.Type or SerializationTypeCode.TaggedObject
                ? new(code)
                : Invalid;
    }
    sealed class AttributeValueTypeProvider : ISignatureTypeProvider<AttributeValueType, object?>
    {
        public AttributeValueType GetPrimitiveType(PrimitiveTypeCode code)
            => code == PrimitiveTypeCode.Object
                ? new(SerializationTypeCode.TaggedObject)
                : AttributeValueType.For((SerializationTypeCode)code);
        public AttributeValueType GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle handle, byte rawTypeKind)
            => ForNamedType(TypeResolver.GetTypeNameFromDefinition(r, handle));
        public AttributeValueType GetTypeFromReference(MetadataReader r, TypeReferenceHandle handle, byte rawTypeKind)
            => ForNamedType(TypeResolver.GetTypeName(r, handle));
        public AttributeValueType GetSZArrayType(AttributeValueType elementType)
            => elementType.Code is SerializationTypeCode.Invalid or SerializationTypeCode.SZArray
                ? AttributeValueType.Invalid
                : AttributeValueType.Array;
        AttributeValueType ForNamedType(string? name)
            => name == "System.Type" ? new(SerializationTypeCode.Type)
                : name is null ? AttributeValueType.Invalid
                : new(SerializationTypeCode.Enum, name);

        public AttributeValueType GetModifiedType(AttributeValueType modifier, AttributeValueType unmodifiedType, bool isRequired)
            => unmodifiedType;
        public AttributeValueType GetPinnedType(AttributeValueType elementType) => AttributeValueType.Invalid;
        public AttributeValueType GetPointerType(AttributeValueType elementType) => AttributeValueType.Invalid;
        public AttributeValueType GetByReferenceType(AttributeValueType elementType) => AttributeValueType.Invalid;
        public AttributeValueType GetArrayType(AttributeValueType elementType, ArrayShape shape) => AttributeValueType.Invalid;
        public AttributeValueType GetFunctionPointerType(MethodSignature<AttributeValueType> signature) => AttributeValueType.Invalid;
        public AttributeValueType GetGenericInstantiation(AttributeValueType genericType, System.Collections.Immutable.ImmutableArray<AttributeValueType> arguments) => AttributeValueType.Invalid;
        public AttributeValueType GetGenericMethodParameter(object? genericContext, int index) => AttributeValueType.Invalid;
        public AttributeValueType GetGenericTypeParameter(object? genericContext, int index) => AttributeValueType.Invalid;
        public AttributeValueType GetTypeFromSpecification(MetadataReader r, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => AttributeValueType.Invalid;
    }

    static string GetQualifiedAttributeName(string fullName)
    {
        if (!fullName.EndsWith("Attribute", StringComparison.Ordinal))
            return fullName;

        var trimmed = fullName[..^9];
        return trimmed.Length == 0 || trimmed.EndsWith(".", StringComparison.Ordinal)
            ? fullName
            : trimmed;
    }

    /// <summary>Renders one attribute-argument value, or null when its shape is not faithfully spellable (arrays, unknown).</summary>
    static string? RenderArgument(string type, object? value) => value switch
    {
        null => "null",
        // A Type argument decodes to its name string; spell only simple source
        // type names we can render faithfully.
        _ when type == "System.Type" && value is string typeName => RenderTypeArgument(typeName),
        string s => "\"" + EscapeStringLiteral(s) + "\"",
        bool b => b ? "true" : "false",
        char c => $"'{EscapeCharLiteral(c)}'",
        // A primitive keyword type came from the provider; render the literal.
        _ when type is "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "double" => value.ToString(),
        _ when type == "long" => value + "L",
        _ when type is "ulong" => value + "UL",
        _ when type == "float" => value + "f",
        // Anything else with an integral value is an enum constant; a cast is
        // always valid. Naming the member is a later refinement.
        _ when value is byte or sbyte or short or ushort or int or uint or long or ulong
            => $"({MetadataDeclarationQuery.EscapeCompatibilityTypeKeywords(type)}){value}",
        _ => null,
    };

    // A type name containing a backtick (arity), '[' (array/generic), or ','
    // (assembly-qualified) cannot be spelled as a plain typeof(); cached to
    // avoid allocating the delimiter array on every render.
    static readonly SearchValues<char> s_typeArgumentDelimiters = SearchValues.Create("`[,");

    static string? RenderTypeArgument(string typeName)
    {
        if (!CanRenderTypeArgument(typeName))
            return null;
        string escapedType = MetadataDeclarationQuery.EscapeCompatibilityTypeKeywords(
            typeName.Replace('+', '.'));
        return $"typeof({escapedType})";
    }

    static bool CanRenderTypeArgument(string typeName)
        => !typeName.AsSpan().ContainsAny(s_typeArgumentDelimiters);

    static string EscapeCharLiteral(char value) => value switch
    {
        '\\' => "\\\\",
        '\'' => "\\'",
        '\0' => "\\0",
        '\a' => "\\a",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\v' => "\\v",
        '\u0085' or '\u2028' or '\u2029' => $"\\u{(int)value:x4}",
        _ when CSharpIdentifierCore.IsRenderingHazard(value) => $"\\u{(int)value:x4}",
        _ => value.ToString()
    };

    static string EscapeStringLiteral(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\0':
                    builder.Append("\\0");
                    break;
                case '\a':
                    builder.Append("\\a");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\v':
                    builder.Append("\\v");
                    break;
                case '\u0085':
                case '\u2028':
                case '\u2029':
                    builder.Append($"\\u{(int)c:x4}");
                    break;
                default:
                    if (CSharpIdentifierCore.IsRenderingHazard(c))
                        builder.Append($"\\u{(int)c:x4}");
                    else
                        builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Attributes the C# compiler re-synthesizes from syntax — emitting them in source is redundant or a duplicate-attribute error.</summary>
    static bool IsReEmittedAttribute(string name) => name switch
    {
        KnownAttributeNames.ExtensionAttribute => true,
        KnownAttributeNames.CompilerFeatureRequiredAttribute => true,
        KnownAttributeNames.CompilerGeneratedAttribute => true,
        "System.Runtime.CompilerServices.InlineArrayAttribute" => true,
        KnownAttributeNames.NullableAttribute => true,
        KnownAttributeNames.NullableContextAttribute => true,
        KnownAttributeNames.IsReadOnlyAttribute => true,
        KnownAttributeNames.RequiresLocationAttribute => true,
        KnownAttributeNames.IsByRefLikeAttribute => true,
        KnownAttributeNames.IsUnmanagedAttribute => true,
        KnownAttributeNames.RefSafetyRulesAttribute => true,
        KnownAttributeNames.ScopedRefAttribute => true,
        KnownAttributeNames.NativeIntegerAttribute => true,
        KnownAttributeNames.DynamicAttribute => true,
        KnownAttributeNames.TupleElementNamesAttribute => true,
        KnownAttributeNames.RequiredMemberAttribute => true,
        KnownAttributeNames.DecimalConstantAttribute => true,
        KnownAttributeNames.DateTimeConstantAttribute => true,
        KnownAttributeNames.AsyncStateMachineAttribute => true,
        KnownAttributeNames.IteratorStateMachineAttribute => true,
        "System.Runtime.CompilerServices.FixedBufferAttribute" => true,
        "System.Runtime.CompilerServices.IntrinsicAttribute" => true,
        "System.Runtime.Versioning.NonVersionableAttribute" => true,
        "System.Reflection.DefaultMemberAttribute" => true,
        _ => false,
    };

    static bool IsParameterSyntaxAttribute(string name) => name switch
    {
        "System.ParamArrayAttribute" => true,
        "System.Runtime.InteropServices.MarshalAsAttribute" => true,
        KnownAttributeNames.ParamCollectionAttribute => true,
        KnownAttributeNames.DecimalConstantAttribute => true,
        KnownAttributeNames.DateTimeConstantAttribute => true,
        _ => false,
    };

    /// <summary>
    /// Reads a custom attribute's single leading string argument, or null when
    /// the blob does not plausibly hold one.
    /// </summary>
    /// <remarks>
    /// The character scan is a <em>decode plausibility</em> test, not a
    /// containment boundary, and the distinction is load-bearing in both
    /// directions.
    /// <para>
    /// It is not containment: an attribute value that survives this scan is
    /// still untrusted and is contained where it is rendered, by the C# literal
    /// escapers and the view-level containment helpers. Nothing here may be
    /// relied on for safety.
    /// </para>
    /// <para>
    /// It is deliberately narrower than
    /// <see cref="CSharpIdentifierCore.IsRenderingHazard"/>. That
    /// predicate answers "must this be escaped before rendering?", which is
    /// true of bidi controls; this one answers "did a blob that is not a string
    /// just get decoded as one?", which bidi controls are no evidence of, since
    /// they occur in genuine localized text. Widening this scan to the hazard
    /// set would silently discard real attribute values that render correctly
    /// today as escaped text, replacing visible evidence with a bare
    /// <c>[Obsolete]</c>.
    /// </para>
    /// <para>
    /// The scan is asymmetric in the other direction too: a value carrying a
    /// vertical tab is dropped whole while one carrying U+202E survives. That
    /// inconsistency is real but is a fidelity question about which blobs are
    /// worth showing, not an escaping question, and changing it moves output
    /// for ordinary assemblies. It is tracked separately rather than folded
    /// into a containment change.
    /// </para>
    /// </remarks>
    internal static string? TryGetAttributeDisplayValue(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            var blob = reader.GetBlobReader(attr.Value);
            if (blob.Length < 2) return null;
            blob.ReadUInt16(); // prolog

            var value = blob.ReadSerializedString();
            if (value == null) return null;

            foreach (char c in value)
            {
                if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r')
                    return null;
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }
}
