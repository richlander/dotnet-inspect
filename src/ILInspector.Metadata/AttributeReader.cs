using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// Reads and checks custom attributes on types and members.
/// </summary>
public static partial class AttributeReader
{
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
    public static bool HasExtensionAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes)
        => HasExtensionAttribute(
            reader,
            attributes,
            beforeMaterialize: null);

    public static bool HasExtensionAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(
                reader,
                attr.Constructor,
                beforeMaterialize);
            if (attrTypeName == KnownAttributeNames.ExtensionAttribute)
                return true;
        }
        return false;
    }

    public static bool TryGetExtensionMarkerName(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out string? markerName)
        => TryGetExtensionMarkerName(
            reader,
            attributes,
            out markerName,
            beforeMaterialize: null);

    public static bool TryGetExtensionMarkerName(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out string? markerName,
        Action<int>? beforeMaterialize)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(
                reader,
                attr.Constructor,
                beforeMaterialize);
            if (attrTypeName is not (ExtensionMarkerAttributeName or ExtensionMarkerNameAttributeName))
                continue;

            try
            {
                beforeMaterialize?.Invoke(reader.GetBlobReader(attr.Value).Length);
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
    public static bool HasHiddenAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(
                reader,
                attr.Constructor,
                beforeMaterialize);

            if (attrTypeName == EditorBrowsableAttributeName)
            {
                if (IsEditorBrowsableNever(reader, attr, beforeMaterialize))
                    return true;
            }
            else if (attrTypeName == ObsoleteAttributeName)
            {
                if (!IsCompilerCompatibilityObsolete(
                    reader,
                    attributes,
                    attr,
                    beforeMaterialize))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if the member has the [EditorBrowsable(Never)] attribute.
    /// </summary>
    public static bool HasEditorBrowsableNeverAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(
                reader,
                attr.Constructor,
                beforeMaterialize);
            if (attrTypeName == EditorBrowsableAttributeName
                && IsEditorBrowsableNever(reader, attr, beforeMaterialize))
                return true;
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
        Action<int>? beforeMaterialize = null)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(
                reader,
                attr.Constructor,
                beforeMaterialize);
            if (attrTypeName == ObsoleteAttributeName)
            {
                message = TryGetAttributeDisplayValue(
                    reader,
                    attr,
                    beforeMaterialize);
                if (IsCompilerCompatibilityObsolete(
                    reader,
                    attributes,
                    attr,
                    beforeMaterialize))
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

    public static bool HasRequiredMemberAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes)
        => HasRequiredMemberAttribute(
            reader,
            attributes,
            beforeMaterialize: null);

    public static bool HasRequiredMemberAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize)
        => HasAttribute(
            reader,
            attributes,
            KnownAttributeNames.RequiredMemberAttribute,
            beforeMaterialize);

    /// <summary>
    /// Checks whether the member carries <c>RequiresUnsafeAttribute</c> — the
    /// metadata form of the <c>unsafe</c>/<c>extern</c> modifier stamped under the
    /// updated memory-safety rules. Tolerates the two namespace spellings.
    /// </summary>
    public static bool HasRequiresUnsafeAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes)
        => HasRequiresUnsafeAttribute(
            reader,
            attributes,
            beforeMaterialize: null);

    public static bool HasRequiresUnsafeAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize)
        => HasAttribute(
            reader,
            attributes,
            KnownAttributeNames.RequiresUnsafeAttribute,
            beforeMaterialize)
        || HasAttribute(
            reader,
            attributes,
            KnownAttributeNames.RequiresUnsafeAttributeCompilerServices,
            beforeMaterialize);

    private static bool IsCompilerCompatibilityObsolete(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        CustomAttribute obsoleteAttribute,
        Action<int>? beforeMaterialize)
    {
        // Roslyn stamps a synthetic [Obsolete] on certain types/members purely to block older
        // compilers, pairing it with [CompilerFeatureRequired(<feature>)]. These are not real
        // deprecations, so they must not hide the API. Covers required members and ref structs
        // (Span<T>, ReadOnlySpan<T>, and other byref-like types).
        return (AttributeValueEquals(
                    reader,
                    obsoleteAttribute,
                    RequiredMembersConstructorObsoleteMessage,
                    beforeMaterialize)
                && HasCompilerFeatureRequiredAttribute(
                    reader,
                    attributes,
                    RequiredMembersFeatureName,
                    beforeMaterialize))
            || (AttributeValueEquals(
                    reader,
                    obsoleteAttribute,
                    RefStructsObsoleteMessage,
                    beforeMaterialize)
                && HasCompilerFeatureRequiredAttribute(
                    reader,
                    attributes,
                    RefStructsFeatureName,
                    beforeMaterialize));
    }

    static bool AttributeValueEquals(
        MetadataReader reader,
        CustomAttribute attribute,
        string expected,
        Action<int>? beforeMaterialize)
    {
        int blobLength = reader.GetBlobReader(attribute.Value).Length;
        int maximumComparableLength = Encoding.UTF8.GetByteCount(expected) + 16;
        beforeMaterialize?.Invoke(Math.Min(blobLength, maximumComparableLength));
        if (blobLength > maximumComparableLength)
            return false;
        return string.Equals(
            TryGetAttributeDisplayValue(reader, attribute),
            expected,
            StringComparison.Ordinal);
    }

    private static bool HasCompilerFeatureRequiredAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string featureName,
        Action<int>? beforeMaterialize)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(
                reader,
                attr.Constructor,
                beforeMaterialize);
            if (attrTypeName != KnownAttributeNames.CompilerFeatureRequiredAttribute)
                continue;

            var value = TryGetAttributeDisplayValue(
                reader,
                attr,
                beforeMaterialize);
            if (string.Equals(value, featureName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsEditorBrowsableNever(
        MetadataReader reader,
        CustomAttribute attr,
        Action<int>? beforeMaterialize)
    {
        beforeMaterialize?.Invoke(
            Math.Min(reader.GetBlobReader(attr.Value).Length, 6));
        // Check if the value is EditorBrowsableState.Never (value = 1)
        var value = reader.GetBlobReader(attr.Value);
        // Attribute blob format: 2-byte prolog (0x0001), then the enum value as int32
        if (value.Length >= 6)
        {
            value.ReadUInt16();
            int enumValue = value.ReadInt32();
            return enumValue == 1; // EditorBrowsableState.Never
        }
        return false;
    }

    /// <summary>
    /// Checks if the member has a specific attribute by full type name.
    /// </summary>
    public static bool HasAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string attributeTypeName)
        => HasAttribute(
            reader,
            attributes,
            attributeTypeName,
            beforeMaterialize: null);

    public static bool HasAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string attributeTypeName,
        Action<int>? beforeMaterialize)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrName = GetAttributeTypeName(
                reader,
                attr.Constructor,
                beforeMaterialize);
            if (attrName == attributeTypeName)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the fully qualified type name of an attribute from its constructor handle.
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
        => AttributeDecoder.GetAttributeTypeName(
            reader,
            constructorHandle,
            beforeMaterialize);

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
    internal static string? TryGetAttributeDisplayValue(
        MetadataReader reader,
        CustomAttribute attr,
        Action<int>? beforeMaterialize = null)
    {
        beforeMaterialize?.Invoke(reader.GetBlobReader(attr.Value).Length);
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
