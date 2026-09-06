using CSharpText;
using System.Buffers;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;

namespace ILInspector.Metadata;

public static partial class AttributeReader
{
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
        Action<int>? beforeMaterialize = null)
    {
        var result = new List<string>();
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var typeName = GetAttributeTypeName(
                reader,
                attr.Constructor,
                beforeMaterialize);
            if (typeName is null || IsReEmittedAttribute(typeName))
                continue;
            if (skipAttribute?.Invoke(typeName) == true)
                continue;
            beforeMaterialize?.Invoke(
                reader.GetBlobReader(attr.Value).Length);
            if (TryRenderAttribute(
                    reader,
                    attr,
                    typeName,
                    qualifyNames,
                    beforeMaterialize) is not { } rendered)
                continue;
            int lastDot = typeName.LastIndexOf('.');
            if (lastDot > 0)
                namespaces?.Add(typeName[..lastDot]);
            beforeRetain?.Invoke(rendered);
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
        Action<int>? beforeMaterialize = null)
    {
        var result = RenderAttributes(
            reader,
            reader.GetParameter(parameter).GetCustomAttributes(),
            namespaces,
            IsParameterSyntaxAttribute,
            qualifyNames: true,
            beforeRetain: beforeRetain,
            beforeMaterialize: beforeMaterialize);
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
        Action<int>? beforeMaterialize)
    {
        if (AttributeDecoder.TryDecode(reader, attr, beforeMaterialize) is not { } value)
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
        if (typeName.AsSpan().ContainsAny(s_typeArgumentDelimiters))
            return null;
        string escapedType = MetadataDeclarationQuery.EscapeCompatibilityTypeKeywords(
            typeName.Replace('+', '.'));
        return $"typeof({escapedType})";
    }

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
        KnownAttributeNames.RequiresUnsafeAttribute => true,
        KnownAttributeNames.RequiresUnsafeAttributeCompilerServices => true,
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
}
