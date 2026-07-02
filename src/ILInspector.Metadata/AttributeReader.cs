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
    private const string EditorBrowsableAttributeName = "System.ComponentModel.EditorBrowsableAttribute";
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
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName == KnownAttributeNames.ExtensionAttribute)
                return true;
        }
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
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);

            if (attrTypeName == EditorBrowsableAttributeName)
            {
                if (IsEditorBrowsableNever(reader, attr))
                    return true;
            }
            else if (attrTypeName == ObsoleteAttributeName)
            {
                if (!IsCompilerCompatibilityObsolete(reader, attributes, attr))
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
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName == EditorBrowsableAttributeName && IsEditorBrowsableNever(reader, attr))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if the member has the [Obsolete] attribute, returning the optional message.
    /// </summary>
    public static bool TryGetObsoleteAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes, out string? message)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName == ObsoleteAttributeName)
            {
                message = TryGetAttributeDisplayValue(reader, attr);
                if (IsCompilerCompatibilityObsolete(reader, attributes, attr))
                {
                    message = null;
                    return false;
                }

                return true;
            }
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
        CustomAttribute obsoleteAttribute)
    {
        var message = TryGetAttributeDisplayValue(reader, obsoleteAttribute);

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
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName != KnownAttributeNames.CompilerFeatureRequiredAttribute)
                continue;

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
            var attrName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrName == attributeTypeName)
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
        bool qualifyNames = false)
    {
        var result = new List<string>();
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var typeName = GetAttributeTypeName(reader, attr.Constructor);
            if (typeName is null || IsReEmittedAttribute(typeName))
                continue;
            if (skipAttribute?.Invoke(typeName) == true)
                continue;
            if (TryRenderAttribute(reader, attr, qualifyNames) is not { } rendered)
                continue;
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

    public static List<string> RenderParameterAttributes(MetadataReader reader, ParameterHandle parameter, SortedSet<string>? namespaces = null)
        => RenderAttributes(
            reader,
            reader.GetParameter(parameter).GetCustomAttributes(),
            namespaces,
            IsParameterSyntaxAttribute,
            qualifyNames: true);

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

    static string? TryRenderAttribute(MetadataReader reader, CustomAttribute attr, bool qualifyName)
    {
        if (AttributeDecoder.TryDecode(reader, attr) is not { } value)
            return null;
        var typeName = GetAttributeTypeName(reader, attr.Constructor)!;
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
        return trimmed.Length == 0 || trimmed.EndsWith('.', StringComparison.Ordinal)
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
            => $"({type}){value}",
        _ => null,
    };

    static string? RenderTypeArgument(string typeName)
    {
        if (typeName.IndexOfAny(['`', '[', ',']) >= 0)
            return null;
        return $"typeof({typeName.Replace('+', '.')})";
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
        _ when char.IsControl(value) => $"\\u{(int)value:x4}",
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
                    if (char.IsControl(c))
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
        KnownAttributeNames.CompilerGeneratedAttribute => true,
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
        KnownAttributeNames.AsyncStateMachineAttribute => true,
        KnownAttributeNames.IteratorStateMachineAttribute => true,
        "System.Reflection.DefaultMemberAttribute" => true,
        _ => false,
    };

    static bool IsParameterSyntaxAttribute(string name) => name switch
    {
        "System.ParamArrayAttribute" => true,
        KnownAttributeNames.DecimalConstantAttribute => true,
        KnownAttributeNames.DateTimeConstantAttribute => true,
        _ => false,
    };

    private static string? TryGetAttributeDisplayValue(MetadataReader reader, CustomAttribute attr)
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
