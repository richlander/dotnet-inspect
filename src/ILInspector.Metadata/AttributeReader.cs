using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
 
namespace ILInspector.Metadata;

/// <summary>
/// Authentic framework-signed <c>[JSExport]</c> metadata retained without
/// collapsing malformed or duplicate rows into absence.
/// </summary>
public readonly record struct RuntimeJsExportAttributeEvidence(
    int Count,
    int ValidRowCount,
    bool HasMalformedRow)
{
    public bool HasValidRow => ValidRowCount > 0;
}

/// <summary>
/// Reads and checks custom attributes on types and members.
/// </summary>
public static partial class AttributeReader
{
    private const string EditorBrowsableAttributeName = "System.ComponentModel.EditorBrowsableAttribute";
    private const string ExtensionMarkerAttributeName = "System.Runtime.CompilerServices.ExtensionMarkerAttribute";
    private const string ExtensionMarkerNameAttributeName = "System.Runtime.CompilerServices.ExtensionMarkerNameAttribute";
    private const string ObsoleteAttributeName = "System.ObsoleteAttribute";
    private const string JsonConverterAttributeName = "System.Text.Json.Serialization.JsonConverterAttribute";
    private const string SystemTextJsonAssemblyName = "System.Text.Json";
    private const string JsonStringEnumConverterTypeName = "System.Text.Json.Serialization.JsonStringEnumConverter";
    private const string RuntimeJsExportAttributeName =
        "System.Runtime.InteropServices.JavaScript.JSExportAttribute";
    private const string RuntimeJavaScriptAssemblyName =
        "System.Runtime.InteropServices.JavaScript";
    private const string GeneratedCodeAttributeName =
        "System.CodeDom.Compiler.GeneratedCodeAttribute";
    private const string DynamicDependencyAttributeName =
        "System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute";
    private const string SystemRuntimeAssemblyName = "System.Runtime";
    private const string SystemTextJsonSourceGeneratorName =
        "System.Text.Json.SourceGeneration";
    private const string JsonStringEnumMemberNameAttributeName =
        "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute";
    private const string JsonSerializableAttributeName =
        "System.Text.Json.Serialization.JsonSerializableAttribute";
    private const string FlagsAttributeName = "System.FlagsAttribute";
    private const string JsonIncludeAttributeName = "System.Text.Json.Serialization.JsonIncludeAttribute";
    private const string JsonIgnoreAttributeName = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    private const string JsonIgnoreConditionTypeName =
        "System.Text.Json.Serialization.JsonIgnoreCondition";
    private const string JsonPropertyNameAttributeName = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
    private const string JsonSourceGenerationOptionsAttributeName = "System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute";
    private const string JsonNumberHandlingAttributeName =
        "System.Text.Json.Serialization.JsonNumberHandlingAttribute";
    private const string JsonNumberHandlingTypeName =
        "System.Text.Json.Serialization.JsonNumberHandling";
    private const string JsonObjectCreationHandlingAttributeName =
        "System.Text.Json.Serialization.JsonObjectCreationHandlingAttribute";
    private const string JsonObjectCreationHandlingTypeName =
        "System.Text.Json.Serialization.JsonObjectCreationHandling";
    private const string JsonPolymorphicAttributeName =
        "System.Text.Json.Serialization.JsonPolymorphicAttribute";
    private const string JsonDerivedTypeAttributeName =
        "System.Text.Json.Serialization.JsonDerivedTypeAttribute";
    private const string JsonExtensionDataAttributeName =
        "System.Text.Json.Serialization.JsonExtensionDataAttribute";
    private const string JsonKnownNamingPolicyTypeName =
        "System.Text.Json.Serialization.JsonKnownNamingPolicy";
    private static readonly IReadOnlyDictionary<string, PrimitiveTypeCode>
        JsonSourceGenerationExternalEnumUnderlyingTypes =
            new Dictionary<string, PrimitiveTypeCode>(StringComparer.Ordinal)
            {
                ["System.Text.Json.JsonCommentHandling"] =
                    PrimitiveTypeCode.Byte,
                ["System.Text.Json.JsonSerializerDefaults"] =
                    PrimitiveTypeCode.Int32,
                [JsonNumberHandlingTypeName] =
                    PrimitiveTypeCode.Int32,
                [JsonObjectCreationHandlingTypeName] =
                    PrimitiveTypeCode.Int32,
            };
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

    internal static bool HasUnionAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (TryGetTopLevelAttributeType(
                    reader,
                    attribute.Constructor,
                    KnownAttributeNames.UnionAttribute,
                    beforeMaterialize,
                    out _))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Checks if the enum has the <c>[Flags]</c> attribute.</summary>
    /// <remarks>
    /// Reports only well-formed authentic rows. Callers that project a wire
    /// contract must read <see cref="ReadFlagsAttributes"/> instead, because a
    /// malformed or duplicated authentic row is unsupported evidence rather
    /// than absence.
    /// </remarks>
    public static bool HasFlagsAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
        => ReadFlagsAttributes(
            reader,
            attributes,
            beforeMaterialize).Count > 0;

    /// <summary>
    /// Reads the authentic <c>[Flags]</c> rows on a type, separating
    /// well-formed rows from authentic rows whose constructor or value blob
    /// cannot be honored.
    /// </summary>
    /// <remarks>
    /// A malformed authentic row is deliberately not folded into absence, and
    /// duplicate rows are kept countable: the type really did claim the
    /// framework's flags meaning, and a string-converted enum projected as a
    /// member-name union from metadata that could not be read would be an
    /// incomplete wire contract shaped like a complete one. Untrusted
    /// same-named attributes are still skipped outright.
    /// <c>JsonPropertyNameAttributeTests.MalformedAuthenticFlagsIsUnsupportedEvidence</c>,
    /// <c>DuplicateAuthenticFlagsRowsAreCounted</c>, and
    /// <c>UntrustedFlagsAttributeIsIgnoredRatherThanMalformed</c> are the gates.
    /// </remarks>
    public static FlagsAttributeEvidence ReadFlagsAttributes(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        (int count, bool malformed) = ReadAuthenticMarkerAttributeRows(
            reader,
            attributes,
            FlagsAttributeName,
            assemblyName: null,
            beforeMaterialize);
        return new FlagsAttributeEvidence(count, malformed);
    }

    /// <summary>Checks if the member has the <c>[JsonInclude]</c> attribute.</summary>
    public static bool HasJsonIncludeAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
        => ReadJsonIncludeAttributes(
            reader,
            attributes,
            beforeMaterialize) is { Count: > 0 };

    /// <summary>
    /// Reads the authentic <c>[JsonInclude]</c> rows on a member, separating
    /// well-formed rows from authentic rows whose constructor or value blob
    /// cannot be honored.
    /// </summary>
    /// <remarks>
    /// A malformed authentic row is deliberately not folded into absence: the
    /// member really did opt in, so a consumer that silently dropped the row
    /// would emit a success-shaped contract from metadata it could not read.
    /// Untrusted same-named attributes are still skipped outright, because they
    /// never claimed the framework's meaning.
    /// <c>JsonPropertyNameAttributeTests.MalformedAuthenticJsonIncludeIsUnsupportedEvidence</c>
    /// is the gate.
    /// </remarks>
    public static JsonIncludeAttributeEvidence ReadJsonIncludeAttributes(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        (int count, bool malformed) = ReadAuthenticMarkerAttributeRows(
            reader,
            attributes,
            JsonIncludeAttributeName,
            SystemTextJsonAssemblyName,
            beforeMaterialize);
        return new JsonIncludeAttributeEvidence(count, malformed);
    }

    public static int CountJsonConverterAttributes(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        int count = 0;
        foreach (CustomAttributeHandle attrHandle in attributes)
        {
            CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
            if (IsFrameworkAttributeType(
                    reader,
                    attr.Constructor,
                    JsonConverterAttributeName,
                    SystemTextJsonAssemblyName,
                    beforeMaterialize))
            {
                count++;
            }
        }
        return count;
    }

    public static bool HasUnsupportedJsonTypeWireAttributes(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null) =>
        HasUnsupportedJsonNumberHandlingAttribute(
            reader,
            attributes,
            beforeMaterialize)
        || HasUnsupportedJsonObjectCreationHandlingAttribute(
            reader,
            attributes,
            beforeMaterialize)
        || HasFrameworkAttribute(
            reader,
            attributes,
            JsonPolymorphicAttributeName,
            beforeMaterialize)
        || HasFrameworkAttribute(
            reader,
            attributes,
            JsonDerivedTypeAttributeName,
            beforeMaterialize);

    public static bool HasUnsupportedJsonMemberWireAttributes(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null) =>
        HasUnsupportedJsonNumberHandlingAttribute(
            reader,
            attributes,
            beforeMaterialize)
        || HasUnsupportedJsonObjectCreationHandlingAttribute(
            reader,
            attributes,
            beforeMaterialize)
        || HasFrameworkAttribute(
            reader,
            attributes,
            JsonExtensionDataAttributeName,
            beforeMaterialize);

    public static bool HasRuntimeJsExportAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
        => ReadRuntimeJsExportAttributes(
            reader,
            attributes,
            beforeMaterialize).HasValidRow;

    /// <summary>
    /// Reads every authentic framework-signed <c>[JSExport]</c> row. A row
    /// whose constructor or value blob is not the expected marker shape remains
    /// visible as malformed evidence; an untrusted same-named attribute remains
    /// ignored because it never claimed the framework contract.
    /// </summary>
    public static RuntimeJsExportAttributeEvidence
        ReadRuntimeJsExportAttributes(
            MetadataReader reader,
            CustomAttributeHandleCollection attributes,
            Action<int>? beforeMaterialize = null)
    {
        int count = 0;
        int validRowCount = 0;
        bool hasMalformedRow = false;
        foreach (CustomAttributeHandle attrHandle in attributes)
        {
            CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
            if (!IsFrameworkAttributeType(
                    reader,
                    attr.Constructor,
                    RuntimeJsExportAttributeName,
                    RuntimeJavaScriptAssemblyName,
                    beforeMaterialize))
            {
                continue;
            }

            count++;
            if (!HasExpectedConstructor(
                    reader,
                    attr.Constructor,
                    FrameworkConstructorKind.Marker,
                    beforeMaterialize)
                || !HasMarkerValueBlob(
                    reader,
                    attr))
            {
                hasMalformedRow = true;
            }
            else
            {
                validRowCount++;
            }
        }
        return new(count, validRowCount, hasMalformedRow);
    }

    /// <summary>
    /// Recognizes the authentic type marker emitted by the System.Text.Json
    /// source generator. The framework attribute identity, constructor shape,
    /// and generator name are all required.
    /// </summary>
    public static bool HasSystemTextJsonSourceGenerationMarker(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        foreach (CustomAttributeHandle attrHandle in attributes)
        {
            CustomAttribute attribute = reader.GetCustomAttribute(attrHandle);
            if (!IsFrameworkAttributeType(
                    reader,
                    attribute.Constructor,
                    GeneratedCodeAttributeName,
                    SystemRuntimeAssemblyName,
                    beforeMaterialize)
                || !HasExpectedConstructor(
                    reader,
                    attribute.Constructor,
                    FrameworkConstructorKind.StringString,
                    beforeMaterialize)
                || AttributeDecoder.TryDecode(
                    reader,
                    attribute,
                    beforeMaterialize) is not
                    {
                        FixedArguments.Length: 2,
                        NamedArguments.Length: 0,
                    } decoded
                || decoded.FixedArguments[0].Value is not string generatorName
                || decoded.FixedArguments[1].Value is not string)
            {
                continue;
            }

            if (generatorName == SystemTextJsonSourceGeneratorName)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Decodes exact runtime-wrapper MethodDef names and textual targets from
    /// framework <c>[DynamicDependency(string, string, string)]</c> rows. The
    /// extractor separately authenticates the SDK registration container and
    /// matches each textual target to its owning type definition.
    /// </summary>
    /// <remarks>
    /// <c>JsExportSurfaceBuilderTests.Build_RejectsHandwrittenRuntimeWrapperCandidate</c>
    /// and <c>Build_DoesNotCreditPrefixSiblingWrapper</c> gate the
    /// registration and exact-name boundaries against compiler-produced
    /// fixtures.
    /// </remarks>
    public static IReadOnlyList<RuntimeJsExportWrapperRegistration>
        ReadRuntimeJsExportWrapperRegistrations(
            MetadataReader reader,
            CustomAttributeHandleCollection attributes,
            Action<int>? beforeMaterialize = null)
    {
        List<RuntimeJsExportWrapperRegistration> registrations = [];
        foreach (CustomAttributeHandle handle in attributes)
        {
            CustomAttribute attribute =
                reader.GetCustomAttribute(handle);
            if (!IsFrameworkAttributeType(
                    reader,
                    attribute.Constructor,
                    DynamicDependencyAttributeName,
                    SystemRuntimeAssemblyName,
                    beforeMaterialize)
                || !HasExpectedConstructor(
                    reader,
                    attribute.Constructor,
                    FrameworkConstructorKind
                        .StringStringString,
                    beforeMaterialize)
                || AttributeDecoder.TryDecode(
                    reader,
                    attribute,
                    beforeMaterialize) is not
                    {
                        FixedArguments.Length: 3,
                        NamedArguments.Length: 0,
                    } decoded
                || decoded.FixedArguments[0].Value
                    is not string memberName
                || decoded.FixedArguments[1].Value
                    is not string targetTypeName
                || decoded.FixedArguments[2].Value
                    is not string targetAssemblyName
                || !memberName.StartsWith(
                    "__Wrapper_",
                    StringComparison.Ordinal)
                || string.IsNullOrEmpty(targetTypeName)
                || string.IsNullOrEmpty(targetAssemblyName))
            {
                continue;
            }

            registrations.Add(
                new(
                    memberName,
                    targetTypeName,
                    targetAssemblyName));
        }

        return registrations;
    }

    /// <summary>
    /// Reads one entry per authentic <c>[JsonIgnore]</c> row on a member, in
    /// metadata order. A <see langword="null"/> entry marks an authentic row
    /// whose constructor, fixed arguments, or <c>Condition</c> named argument
    /// could not be honored; every other entry is the decoded condition, with a
    /// bare <c>[JsonIgnore]</c> reported as
    /// <see cref="JsonWireIgnoreCondition.Always"/> to match the attribute's own
    /// property default.
    /// </summary>
    /// <remarks>
    /// The condition is preserved rather than collapsed to "present" because
    /// <c>WhenWriting</c> and <c>WhenReading</c> are directional: the member
    /// still appears in the other direction's contract. Malformed rows use the
    /// same <see langword="null"/> marker convention as
    /// <see cref="ReadJsonPropertyNames"/>, so a consumer cannot mistake
    /// unreadable metadata for an absent attribute. Untrusted same-named
    /// attributes are skipped outright. Gated by
    /// <c>JsonPropertyNameAttributeTests.MalformedAuthenticJsonIgnoreIsUnsupportedEvidence</c>
    /// and
    /// <c>JsonPropertyNameAttributeTests.DirectionalJsonIgnoreConditionsAreDecodedFromCompiledMetadata</c>.
    /// </remarks>
    public static List<JsonWireIgnoreCondition?> ReadJsonIgnoreConditions(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        var conditions = new List<JsonWireIgnoreCondition?>();
        foreach (CustomAttributeHandle attrHandle in attributes)
        {
            CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
            if (!IsFrameworkAttributeType(
                    reader,
                    attr.Constructor,
                    JsonIgnoreAttributeName,
                    SystemTextJsonAssemblyName,
                    beforeMaterialize))
            {
                continue;
            }

            conditions.Add(
                IsValidJsonIgnoreAttribute(
                    reader,
                    attr,
                    beforeMaterialize,
                    out int? condition)
                    ? (JsonWireIgnoreCondition)(
                        condition ?? (int)JsonWireIgnoreCondition.Always)
                    : null);
        }

        return conditions;
    }

    /// <summary>
    /// Checks if the member has the <c>[JsonIgnore]</c> attribute in any form,
    /// including a malformed authentic row. Callers that need the directional
    /// meaning of a conditional form must read
    /// <see cref="ReadJsonIgnoreConditions"/> instead.
    /// </summary>
    public static bool HasJsonIgnoreAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
        => ReadJsonIgnoreConditions(
            reader,
            attributes,
            beforeMaterialize).Count > 0;

    /// <summary>
    /// Checks whether the member carries exactly one authentic
    /// <c>[JsonIgnore]</c> row and that row explicitly keeps the member in the
    /// wire shape with <c>Condition = Never</c>.
    /// </summary>
    public static bool HasJsonIgnoreNeverAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
        => ReadJsonIgnoreConditions(
                reader,
                attributes,
                beforeMaterialize)
            is [JsonWireIgnoreCondition.Never];

    public static List<string?> ReadJsonPropertyNames(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        var propertyNames = new List<string?>();
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            if (!IsFrameworkAttributeType(
                    reader,
                    attr.Constructor,
                    JsonPropertyNameAttributeName,
                    SystemTextJsonAssemblyName,
                    beforeMaterialize))
            {
                continue;
            }

            if (!HasExpectedConstructor(
                    reader,
                    attr.Constructor,
                    FrameworkConstructorKind.String,
                    beforeMaterialize))
            {
                propertyNames.Add(null);
                continue;
            }

            propertyNames.Add(
                TryGetSingleStringFixedArgument(
                    reader,
                    attr,
                    out string? propertyName,
                    beforeMaterialize)
                    ? propertyName
                    : null);
        }

        return propertyNames;
    }

    public static List<string?> ReadJsonStringEnumMemberNames(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
    {
        var names = new List<string?>();
        foreach (CustomAttributeHandle attrHandle in attributes)
        {
            CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
            if (!IsFrameworkAttributeType(
                    reader,
                    attr.Constructor,
                    JsonStringEnumMemberNameAttributeName,
                    SystemTextJsonAssemblyName,
                    beforeMaterialize))
            {
                continue;
            }

            if (!HasExpectedConstructor(
                    reader,
                    attr.Constructor,
                    FrameworkConstructorKind.String,
                    beforeMaterialize))
            {
                names.Add(null);
                continue;
            }

            names.Add(
                TryGetSingleStringFixedArgument(
                    reader,
                    attr,
                    out string? name,
                    beforeMaterialize)
                    ? name
                    : null);
        }
        return names;
    }

    /// <summary>
    /// Reads the property-naming policy declared by the authentic
    /// <c>[JsonSourceGenerationOptions]</c> rows on a serializer context.
    /// Returns <see langword="false"/> only when the context carries no
    /// authentic row at all.
    /// </summary>
    /// <remarks>
    /// Once the structured attribute identity and its platform-signed assembly
    /// authenticate, the row counts — an unexpected constructor or an
    /// undecodable value blob makes the policy
    /// <see cref="JsonWireNamingPolicy.Unsupported"/> rather than absent.
    /// Folding such a row into absence would both default the naming policy and
    /// let a malformed row pair with a well-formed one without tripping the
    /// duplicate-row rejection, so a context declaring two policies could still
    /// resolve one. Untrusted same-named attributes are still skipped outright,
    /// because they never claimed the framework's meaning.
    /// <c>JsonSourceGenerationOptionsAttributeTests.UnexpectedConstructorIsUnsupported</c>,
    /// <c>MalformedRowPairedWithValidRowIsUnsupportedRegardlessOfOrder</c>, and
    /// <c>SameNameOptionsAttributeFromUntrustedAssemblyIsIgnored</c> are the
    /// gates.
    /// </remarks>
    public static bool TryGetJsonSourceGenerationPropertyNamingPolicy(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out JsonWireNamingPolicy? namingPolicy,
        Action<int>? beforeMaterialize = null)
        => TryGetJsonSourceGenerationOptions(
            reader,
            attributes,
            out namingPolicy,
            out _,
            beforeMaterialize);

    /// <summary>
    /// Reads the wire-relevant source-generation options for a serializer
    /// context. The mode is retained independently of naming because a
    /// serialization-only context must not authenticate deserialization.
    /// </summary>
    public static bool TryGetJsonSourceGenerationOptions(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out JsonWireNamingPolicy? namingPolicy,
        out JsonSourceGenerationMode generationMode,
        Action<int>? beforeMaterialize = null)
    {
        bool found = false;
        namingPolicy = null;
        generationMode = JsonSourceGenerationMode.Default;
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            if (!IsFrameworkAttributeType(
                    reader,
                    attr.Constructor,
                    JsonSourceGenerationOptionsAttributeName,
                    SystemTextJsonAssemblyName,
                    beforeMaterialize))
            {
                continue;
            }

            bool hasExpectedConstructor =
                HasExpectedConstructor(
                    reader,
                    attr.Constructor,
                    FrameworkConstructorKind.Marker,
                    beforeMaterialize)
                || HasExpectedConstructor(
                    reader,
                    attr.Constructor,
                    FrameworkConstructorKind.JsonSerializerDefaults,
                    beforeMaterialize);
            JsonSourceGenerationOptionsEvidence current =
                hasExpectedConstructor
                    ? ReadJsonSourceGenerationOptions(
                        reader,
                        attr,
                        beforeMaterialize)
                    : new(
                        JsonWireNamingPolicy.Unsupported,
                        JsonSourceGenerationMode.Default);
            namingPolicy = found
                ? JsonWireNamingPolicy.Unsupported
                : current.NamingPolicy;
            generationMode = found
                ? JsonSourceGenerationMode.Default
                : current.GenerationMode;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Checks whether the enum carries <c>[JsonConverter(typeof(JsonStringEnumConverter&lt;...&gt;))]</c> (or
    /// the non-generic <c>JsonStringEnumConverter</c>) — the marker that makes STJ serialize declared values
    /// by name. Its default configuration can still serialize undefined values by their numeric underlying
    /// value. The converter's <c>typeof()</c> argument is a generic type reference, which
    /// <see cref="AttributeReader.Rendering"/>'s C#-spelling renderer cannot render (it returns null for any
    /// argument whose serialized name contains a backtick, which drops the whole attribute from the rendered
    /// <c>Attributes</c> list) — so this reads the argument's raw serialized type name directly via
    /// <see cref="AttributeDecoder"/> instead of relying on the rendered attribute text.
    /// </summary>
    public static bool HasJsonStringEnumConverterAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        MetadataTypeDefinitionName enumDefinitionName,
        ApiAssemblyIdentity? enumAssemblyIdentity,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            bool isJsonConverter = IsFrameworkAttributeType(
                reader,
                attr.Constructor,
                JsonConverterAttributeName,
                SystemTextJsonAssemblyName,
                beforeMaterialize);
            if (!isJsonConverter)
                continue;
            if (!HasExpectedConstructor(
                    reader,
                    attr.Constructor,
                    FrameworkConstructorKind.SystemType,
                    beforeMaterialize)
                || !HasSingleTypeArgumentValueBlob(
                    reader,
                    attr)
                || AttributeDecoder
                    .TryDecodePreservingSerializedTypeNames(
                        reader,
                        attr,
                        beforeMaterialize)
                    is not
                    {
                        FixedArguments.Length: 1,
                        NamedArguments.Length: 0,
                    } decoded
                || decoded.FixedArguments[0].Value is not string converterTypeName)
                continue;
            if (IsSupportedJsonStringEnumConverter(
                    converterTypeName,
                    enumDefinitionName,
                    enumAssemblyIdentity))
                return true;
        }
        return false;
    }

    public static List<ApiJsonSerializableRoot>
        ReadJsonSerializableRoots(
            MetadataReader reader,
            CustomAttributeHandleCollection attributes,
            ApiAssemblyIdentity? currentAssemblyIdentity,
            out int attributeCount,
            Action<int>? beforeMaterialize = null)
    {
        attributeCount = 0;
        var roots = new List<ApiJsonSerializableRoot>();
        foreach (CustomAttributeHandle attrHandle in attributes)
        {
            CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
            if (!IsFrameworkAttributeType(
                    reader,
                    attr.Constructor,
                    JsonSerializableAttributeName,
                    SystemTextJsonAssemblyName,
                    beforeMaterialize))
            {
                continue;
            }

            attributeCount++;
            if (!HasExpectedConstructor(
                    reader,
                    attr.Constructor,
                    FrameworkConstructorKind.SystemType,
                    beforeMaterialize)
                || AttributeDecoder
                    .TryDecodePreservingSerializedTypeNames(
                        reader,
                        attr,
                        beforeMaterialize) is not
                    {
                        FixedArguments.Length: 1,
                    } decoded
                || decoded.FixedArguments[0].Value
                    is not string serializedTypeName
                || !TryGetJsonSerializableTypeInfoPropertyName(
                    decoded.NamedArguments,
                    out string? typeInfoPropertyName,
                    out JsonSourceGenerationMode generationMode))
            {
                roots.Add(new(
                    ElementType: null,
                    IsArray: false)
                {
                    UnsupportedReason =
                        "JsonSerializable metadata is malformed or unsupported",
                });
                continue;
            }

            ApiTypeShape? rootShape =
                currentAssemblyIdentity is null
                    ? null
                    : ParseJsonSerializableRootShape(
                        serializedTypeName,
                        currentAssemblyIdentity);
            roots.Add(new(
                ElementType: GetLegacyRootElementType(rootShape),
                IsArray: rootShape?.Kind is ApiTypeShapeKind.SzArray
                    or ApiTypeShapeKind.Array,
                TypeInfoPropertyName: typeInfoPropertyName)
            {
                Type = rootShape,
                UnsupportedReason = rootShape is null
                    ? "serializer root type shape is unsupported"
                    : ContainsMultidimensionalArray(rootShape)
                        ? "multidimensional serializer roots are not supported"
                        : null,
                GenerationMode = generationMode,
            });
        }
        return roots;
    }

    static bool TryGetJsonSerializableTypeInfoPropertyName(
        ImmutableArray<CustomAttributeNamedArgument<string>> arguments,
        out string? typeInfoPropertyName,
        out JsonSourceGenerationMode generationMode)
    {
        typeInfoPropertyName = null;
        generationMode = JsonSourceGenerationMode.Default;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (CustomAttributeNamedArgument<string> argument in arguments)
        {
            if (argument.Kind != CustomAttributeNamedArgumentKind.Property
                || !names.Add(argument.Name!))
            {
                return false;
            }

            switch (argument.Name)
            {
                case "GenerationMode":
                    if (!IsExpectedSerializedOptionType(
                            argument.Type,
                            "System.Text.Json.Serialization.JsonSourceGenerationMode")
                        || !TryReadInt32(argument.Value, out int mode)
                        || !TryGetJsonSourceGenerationMode(
                            mode,
                            out generationMode))
                    {
                        return false;
                    }
                    break;
                case "TypeInfoPropertyName":
                    if (argument.Type != "string"
                        || argument.Value is not string)
                    {
                        return false;
                    }
                    typeInfoPropertyName = (string)argument.Value;
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    static ApiTypeReferenceIdentity? GetLegacyRootElementType(
        ApiTypeShape? root)
    {
        while (root?.Kind is ApiTypeShapeKind.SzArray
            or ApiTypeShapeKind.Array)
        {
            root = root.ElementType;
        }

        return root?.Definition;
    }

    static ApiTypeShape? ParseJsonSerializableRootShape(
        string serializedTypeName,
        ApiAssemblyIdentity currentAssemblyIdentity)
    {
        if (serializedTypeName.Length
            > MetadataSafetyPolicy.MaxTypeNameCharacters)
        {
            return null;
        }

        return ParseJsonSerializableTypeShape(
            serializedTypeName,
            currentAssemblyIdentity,
            depth: 0);
    }

    static ApiTypeShape? ParseJsonSerializableTypeShape(
        string serializedTypeName,
        ApiAssemblyIdentity currentAssemblyIdentity,
        int depth)
    {
        if (depth >= MetadataSafetyPolicy.MaxRelationshipNodes)
            return null;

        string text;
        ApiAssemblyIdentity assembly;
        if (TryReadSerializedTypeIdentity(
                serializedTypeName,
                out string? parsedTypeName,
                out ApiAssemblyIdentity? parsedAssemblyIdentity)
            && parsedTypeName is { } parsedType
            && parsedAssemblyIdentity is { } parsedAssembly)
        {
            text = parsedType;
            assembly = parsedAssembly;
        }
        else
        {
            text = serializedTypeName.Trim();
            assembly = currentAssemblyIdentity;
        }

        if (text.Length == 0
            || text.IndexOfAny(['*', '&']) >= 0)
        {
            return null;
        }

        var arrayRanks = new List<int>();
        while (TryStripArraySuffix(text, out string? elementText, out int rank))
        {
            arrayRanks.Add(rank);
            text = elementText!;
        }

        ApiTypeShape? shape = ParseJsonSerializableNonArrayType(
            text,
            assembly,
            currentAssemblyIdentity,
            depth);
        if (shape is null)
            return null;

        for (int index = arrayRanks.Count - 1; index >= 0; index--)
        {
            shape = arrayRanks[index] == 1
                ? ApiTypeShape.SzArray(shape)
                : ApiTypeShape.Array(shape, arrayRanks[index]);
        }

        return shape;
    }

    static ApiTypeShape? ParseJsonSerializableNonArrayType(
        string text,
        ApiAssemblyIdentity assembly,
        ApiAssemblyIdentity currentAssemblyIdentity,
        int depth)
    {
        int genericStart = text.IndexOf('[');
        if (genericStart < 0)
            return ParseJsonSerializableNamedType(text, assembly);

        if (genericStart == 0
            || !text[..genericStart].Contains(
                '`',
                StringComparison.Ordinal)
            || FindMatchingBracket(text, genericStart) != text.Length - 1)
        {
            return null;
        }

        ImmutableArray<string>? serializedArguments =
            ParseSerializedGenericArguments(text, genericStart);
        if (serializedArguments is null)
            return null;

        ApiTypeShape? definition =
            ParseJsonSerializableNamedType(
                text[..genericStart],
                assembly);
        if (definition?.Definition is not
            {
                DefinitionName: { } definitionName,
            } definitionIdentity
            || !HasMatchingGenericArity(
                definitionName,
                serializedArguments.Value.Length))
        {
            return null;
        }

        var arguments = ImmutableArray.CreateBuilder<ApiTypeShape>(
            serializedArguments.Value.Length);
        foreach (string argument in serializedArguments.Value)
        {
            ApiTypeShape? argumentShape =
                ParseJsonSerializableTypeShape(
                    argument,
                    currentAssemblyIdentity,
                    depth + 1);
            if (argumentShape is null)
                return null;
            arguments.Add(argumentShape);
        }

        return ApiTypeShape.GenericInstance(
            definitionIdentity,
            arguments.MoveToImmutable());
    }

    static bool HasMatchingGenericArity(
        MetadataTypeDefinitionName definitionName,
        int argumentCount)
    {
        int declaredArity = 0;
        foreach (string segment in definitionName.Segments)
        {
            int segmentArity = MetadataNameArity.OfSegment(segment);
            if (segmentArity > argumentCount - declaredArity)
                return false;
            declaredArity += segmentArity;
        }

        return declaredArity == argumentCount;
    }

    static ApiTypeShape? ParseJsonSerializableNamedType(
        string text,
        ApiAssemblyIdentity assembly)
    {
        if (TryGetSerializedPrimitive(
                text,
                assembly,
                out ApiPrimitiveType primitive))
        {
            return ApiTypeShape.PrimitiveType(primitive);
        }

        MetadataTypeDefinitionName? definitionName =
            MetadataTypeDefinitionName.ParseSerialized(text)
                is MetadataTypeDefinitionNameResult.Valid valid
                    ? valid.Name
                    : null;
        if (definitionName is null)
            return null;

        return ApiTypeShape.Named(new(
            assembly,
            text.Replace('+', '.'),
            definitionName));
    }

    static bool TryGetSerializedPrimitive(
        string text,
        ApiAssemblyIdentity assembly,
        out ApiPrimitiveType primitive)
    {
        primitive = text switch
        {
            "System.Void" => ApiPrimitiveType.Void,
            "System.Boolean" => ApiPrimitiveType.Boolean,
            "System.Char" => ApiPrimitiveType.Char,
            "System.SByte" => ApiPrimitiveType.SByte,
            "System.Byte" => ApiPrimitiveType.Byte,
            "System.Int16" => ApiPrimitiveType.Int16,
            "System.UInt16" => ApiPrimitiveType.UInt16,
            "System.Int32" => ApiPrimitiveType.Int32,
            "System.UInt32" => ApiPrimitiveType.UInt32,
            "System.Int64" => ApiPrimitiveType.Int64,
            "System.UInt64" => ApiPrimitiveType.UInt64,
            "System.Single" => ApiPrimitiveType.Single,
            "System.Double" => ApiPrimitiveType.Double,
            "System.String" => ApiPrimitiveType.String,
            "System.Object" => ApiPrimitiveType.Object,
            _ => (ApiPrimitiveType)(-1),
        };
        return primitive != (ApiPrimitiveType)(-1)
            && assembly.Name is
                "System.Private.CoreLib"
                or "System.Runtime"
                or "mscorlib"
                or "netstandard"
            && PlatformKeys.IsPlatform(assembly.PublicKeyToken);
    }

    static bool TryStripArraySuffix(
        string text,
        [NotNullWhen(true)] out string? elementText,
        out int rank)
    {
        elementText = null;
        rank = 0;
        if (text.Length < 3 || text[^1] != ']')
            return false;

        int openBracket = text.LastIndexOf('[');
        if (openBracket <= 0)
            return false;

        ReadOnlySpan<char> rankText =
            text.AsSpan(openBracket + 1, text.Length - openBracket - 2);
        foreach (char character in rankText)
        {
            if (character != ',')
                return false;
        }

        elementText = text[..openBracket];
        rank = rankText.Length + 1;
        return true;
    }

    static bool ContainsMultidimensionalArray(ApiTypeShape shape)
    {
        var pending = new Stack<ApiTypeShape>();
        pending.Push(shape);
        while (pending.Count > 0)
        {
            ApiTypeShape current = pending.Pop();
            if (current.Kind == ApiTypeShapeKind.Array
                && current.ArrayRank > 1)
            {
                return true;
            }

            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            for (int index = 0; index < current.TypeArguments.Length; index++)
                pending.Push(current.TypeArguments[index]);
        }

        return false;
    }

    static ImmutableArray<string>? ParseSerializedGenericArguments(
        string text,
        int genericStart)
    {
        ReadOnlySpan<char> arguments = text.AsSpan(
            genericStart + 1,
            text.Length - genericStart - 2);
        if (arguments.IsEmpty)
            return ImmutableArray<string>.Empty;

        var result = ImmutableArray.CreateBuilder<string>();
        int position = 0;
        while (position < arguments.Length)
        {
            string argument;
            if (arguments[position] == '[')
            {
                int depth = 1;
                int start = ++position;
                while (position < arguments.Length && depth > 0)
                {
                    switch (arguments[position++])
                    {
                        case '[':
                            depth++;
                            break;
                        case ']':
                            depth--;
                            break;
                    }
                }
                if (depth != 0)
                    return null;

                argument = arguments[(start)..(position - 1)].ToString();
            }
            else
            {
                int start = position;
                while (position < arguments.Length
                    && arguments[position] != ',')
                {
                    position++;
                }
                argument = arguments[start..position].ToString().Trim();
            }
            if (argument.Length == 0)
                return null;
            result.Add(argument);

            if (position == arguments.Length)
                break;
            if (arguments[position++] != ','
                || position == arguments.Length)
                return null;
        }

        return result.ToImmutable();
    }

    static bool TryGetJsonSourceGenerationMode(
        int value,
        out JsonSourceGenerationMode mode)
    {
        mode = (JsonSourceGenerationMode)value;
        return value is >= (int)JsonSourceGenerationMode.Default
            and <= (int)JsonSourceGenerationMode.MetadataAndSerialization;
    }

    static bool IsValidJsonIgnoreAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize,
        out int? condition)
    {
        condition = null;
        if (!IsFrameworkAttributeType(
                reader,
                attribute.Constructor,
                JsonIgnoreAttributeName,
                SystemTextJsonAssemblyName,
                beforeMaterialize)
            || !HasExpectedConstructor(
                reader,
                attribute.Constructor,
                FrameworkConstructorKind.Marker,
                beforeMaterialize)
            || AttributeDecoder.TryDecodePreservingSerializedTypeNames(
                reader,
                attribute,
                beforeMaterialize) is not
                {
                    FixedArguments.Length: 0,
                } decoded)
        {
            return false;
        }

        if (decoded.NamedArguments.Length == 0)
            return true;
        if (decoded.NamedArguments is not [var argument]
            || argument.Kind
                != CustomAttributeNamedArgumentKind.Property
            || argument.Name != "Condition"
            || !IsExpectedSerializedOptionType(
                argument.Type,
                JsonIgnoreConditionTypeName)
            || !TryReadInt32(argument.Value, out int rawValue)
            || rawValue is < 0 or > 5)
        {
            return false;
        }

        condition = rawValue;
        return true;
    }

    enum FrameworkConstructorKind
    {
        Marker,
        Int32,
        SystemType,
        SystemTypeInt32,
        String,
        StringString,
        StringStringString,
        JsonSerializerDefaults,
        JsonNumberHandling,
        JsonObjectCreationHandling,
    }

    internal static bool HasExpectedMarkerConstructor(
        MetadataReader reader,
        EntityHandle constructor,
        Action<int>? beforeMaterialize = null)
        => HasExpectedConstructor(
            reader,
            constructor,
            FrameworkConstructorKind.Marker,
            beforeMaterialize);

    internal static bool HasExpectedInt32Constructor(
        MetadataReader reader,
        EntityHandle constructor,
        Action<int>? beforeMaterialize = null)
        => HasExpectedConstructor(
            reader,
            constructor,
            FrameworkConstructorKind.Int32,
            beforeMaterialize);

    internal static bool HasExpectedSystemTypeInt32Constructor(
        MetadataReader reader,
        EntityHandle constructor,
        Action<int>? beforeMaterialize = null)
        => HasExpectedConstructor(
            reader,
            constructor,
            FrameworkConstructorKind.SystemTypeInt32,
            beforeMaterialize);

    static bool HasExpectedConstructor(
        MetadataReader reader,
        EntityHandle constructor,
        FrameworkConstructorKind expected,
        Action<int>? beforeMaterialize)
    {
        try
        {
            MethodSignature<TypeNode> signature;
            switch (constructor.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    MethodDefinition method = reader.GetMethodDefinition(
                        (MethodDefinitionHandle)constructor);
                    if (!reader.StringComparer.Equals(
                            method.Name,
                            ".ctor"))
                    {
                        return false;
                    }
                    if (!SignatureBlobGuard
                        .IsSafeAndCompleteToDecode(
                            reader,
                            method.Signature,
                            SignatureBlobGuard.Kind.Method))
                    {
                        return false;
                    }
                    signature = method.DecodeSignature(
                        new TypeNodeProvider(
                            beforeMaterialize:
                                beforeMaterialize),
                        genericContext: null);
                    break;
                }
                case HandleKind.MemberReference:
                {
                    MemberReference member = reader.GetMemberReference(
                        (MemberReferenceHandle)constructor);
                    if (!reader.StringComparer.Equals(
                            member.Name,
                            ".ctor"))
                    {
                        return false;
                    }
                    if (!SignatureBlobGuard
                        .IsSafeAndCompleteToDecode(
                            reader,
                            member.Signature,
                            SignatureBlobGuard.Kind.Method))
                    {
                        return false;
                    }
                    signature = member.DecodeMethodSignature(
                        new TypeNodeProvider(
                            beforeMaterialize:
                                beforeMaterialize),
                        genericContext: null);
                    break;
                }
                default:
                    return false;
            }

            if (!signature.Header.IsInstance
                || signature.Header.CallingConvention
                    != SignatureCallingConvention.Default
                || signature.Header.HasExplicitThis
                || signature.GenericParameterCount != 0
                || signature.ReturnType
                    is not PrimitiveTypeNode { Name: "void" })
            {
                return false;
            }

            return expected switch
            {
                FrameworkConstructorKind.Marker =>
                    signature.ParameterTypes.Length == 0,
                FrameworkConstructorKind.Int32 =>
                    signature.ParameterTypes is
                    [
                        PrimitiveTypeNode { Name: "int" },
                    ],
                FrameworkConstructorKind.SystemType =>
                    signature.ParameterTypes is
                    [
                        NamedTypeNode type,
                    ]
                    && IsExpectedTopLevelSignatureType(
                        type,
                        "System",
                        "Type",
                        IsCoreContractAssembly),
                FrameworkConstructorKind.SystemTypeInt32 =>
                    signature.ParameterTypes is
                    [
                        NamedTypeNode type,
                        PrimitiveTypeNode { Name: "int" },
                    ]
                    && IsExpectedTopLevelSignatureType(
                        type,
                        "System",
                        "Type",
                        IsCoreContractAssembly),
                FrameworkConstructorKind.String =>
                    signature.ParameterTypes is
                    [
                        PrimitiveTypeNode { Name: "string" },
                    ],
                FrameworkConstructorKind.StringString =>
                    signature.ParameterTypes is
                    [
                        PrimitiveTypeNode { Name: "string" },
                        PrimitiveTypeNode { Name: "string" },
                    ],
                FrameworkConstructorKind.StringStringString =>
                    signature.ParameterTypes is
                    [
                        PrimitiveTypeNode { Name: "string" },
                        PrimitiveTypeNode { Name: "string" },
                        PrimitiveTypeNode { Name: "string" },
                    ],
                FrameworkConstructorKind.JsonSerializerDefaults =>
                    signature.ParameterTypes is
                    [
                        NamedTypeNode type,
                    ]
                    && IsExpectedTopLevelSignatureType(
                        type,
                        "System.Text.Json",
                        "JsonSerializerDefaults",
                        IsSystemTextJsonAssembly),
                FrameworkConstructorKind.JsonNumberHandling =>
                    signature.ParameterTypes is
                    [
                        NamedTypeNode type,
                    ]
                    && IsExpectedTopLevelSignatureType(
                        type,
                        "System.Text.Json.Serialization",
                        "JsonNumberHandling",
                        IsSystemTextJsonAssembly),
                FrameworkConstructorKind.JsonObjectCreationHandling =>
                    signature.ParameterTypes is
                    [
                        NamedTypeNode type,
                    ]
                    && IsExpectedTopLevelSignatureType(
                        type,
                        "System.Text.Json.Serialization",
                        "JsonObjectCreationHandling",
                        IsSystemTextJsonAssembly),
                _ => false,
            };
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static bool HasMarkerValueBlob(
        MetadataReader reader,
        CustomAttribute attribute)
    {
        try
        {
            BlobReader blob = reader.GetBlobReader(attribute.Value);
            return blob.Length == 4
                && blob.ReadUInt16() == 1
                && blob.ReadUInt16() == 0;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static bool HasSingleTypeArgumentValueBlob(
        MetadataReader reader,
        CustomAttribute attribute)
    {
        try
        {
            BlobReader blob = reader.GetBlobReader(attribute.Value);
            if (blob.RemainingBytes < 5
                || blob.ReadUInt16() != 1
                || blob.ReadByte() == 0xff)
            {
                return false;
            }

            blob.Offset--;
            int length = blob.ReadCompressedInteger();
            if (length < 0
                || blob.RemainingBytes != length + 2)
            {
                return false;
            }
            blob.Offset += length;
            return blob.ReadUInt16() == 0;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Structured expected identities for the framework attributes this reader
    /// authenticates, keyed by the repository-authored constant that names
    /// them. The key is our own source text, not artifact-authored display
    /// text: the artifact side of every comparison is read structurally.
    /// </summary>
    static readonly ConcurrentDictionary<string, MetadataTypeDefinitionName?>
        ExpectedAttributeNames = new(StringComparer.Ordinal);

    static MetadataTypeDefinitionName? ExpectedTopLevelName(
        string fullTypeName)
        => ExpectedAttributeNames.GetOrAdd(
            fullTypeName,
            static name =>
            {
                int separator = name.LastIndexOf('.');
                string @namespace = separator < 0
                    ? ""
                    : name[..separator];
                string simpleName = separator < 0
                    ? name
                    : name[(separator + 1)..];
                return MetadataTypeDefinitionName.Create(
                        @namespace,
                        ImmutableArray.Create(simpleName))
                    is MetadataTypeDefinitionNameResult.Valid valid
                        ? valid.Name
                        : null;
            });

    /// <summary>
    /// Resolves an attribute constructor's declaring type only when it is
    /// exactly the top-level <paramref name="fullTypeName"/> definition, judged
    /// by structured <see cref="MetadataTypeDefinitionName"/> identity rather
    /// than by a flattened display spelling.
    /// </summary>
    static bool TryGetTopLevelAttributeType(
        MetadataReader reader,
        EntityHandle constructor,
        string fullTypeName,
        Action<int>? beforeMaterialize,
        out EntityHandle declaringType)
    {
        declaringType = default;
        if (ExpectedTopLevelName(fullTypeName) is not { } expected)
            return false;

        declaringType = constructor.Kind switch
        {
            HandleKind.MemberReference =>
                reader.GetMemberReference(
                    (MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition =>
                reader.GetMethodDefinition(
                    (MethodDefinitionHandle)constructor)
                    .GetDeclaringType(),
            _ => default,
        };
        if (declaringType.IsNil)
            return false;

        // A locally defined attribute authenticates through either constructor
        // spelling. ECMA-335 lets a MemberRef name a member of a TypeDef in the
        // same module, so requiring a MethodDef token would read a well-formed
        // marker as absent. Identity still comes from the declaring type's
        // structured name, so a nested carrier stays rejected.
        if (declaringType.Kind == HandleKind.TypeDefinition)
        {
            return MetadataTypeDefinitionNameReader.Read(
                    reader,
                    (TypeDefinitionHandle)declaringType,
                    beforeMaterialize)
                is MetadataTypeDefinitionNameReadResult.Read defined
                && defined.Name.Equals(expected);
        }

        return declaringType.Kind == HandleKind.TypeReference
            && MetadataTypeDefinitionNameReader.Read(
                    reader,
                    (TypeReferenceHandle)declaringType,
                    beforeMaterialize)
                is MetadataTypeDefinitionNameReadResult.Read referenced
            && referenced.Name.Equals(expected);
    }

    /// <summary>
    /// Resolves the defining assembly of an attribute whose type is exactly the
    /// top-level <paramref name="fullTypeName"/> definition.
    /// </summary>
    /// <remarks>
    /// The flattened spelling of a nested <c>TypeRef</c> chain joins its
    /// segments with <c>.</c>, so a <c>System.Text.Json/Serialization</c> outer
    /// reference with a nested <c>JsonIgnoreAttribute</c> leaf renders exactly
    /// like the genuine top-level attribute — and its resolution scope still
    /// terminates at the authentic framework <c>AssemblyRef</c>. Comparing
    /// namespace and root-to-leaf segments separates the two.
    /// <c>JsonPropertyNameAttributeTests.NestedAttributeIdentityCannotAliasTopLevelFrameworkAttribute</c>
    /// is the gate.
    /// </remarks>
    static bool TryGetAuthenticAttributeAssembly(
        MetadataReader reader,
        EntityHandle constructor,
        string fullTypeName,
        Action<int>? beforeMaterialize,
        [NotNullWhen(true)] out ApiAssemblyIdentity? identity)
    {
        identity = null;
        if (!TryGetTopLevelAttributeType(
                reader,
                constructor,
                fullTypeName,
                beforeMaterialize,
                out EntityHandle declaringType))
        {
            return false;
        }

        if (declaringType.Kind == HandleKind.TypeDefinition)
        {
            if (!reader.IsAssembly)
                return false;
            identity = ApiAssemblyIdentity.FromDefinition(
                reader,
                beforeMaterialize);
            return true;
        }

        var typeReference = (TypeReferenceHandle)declaringType;

        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    typeReference,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out _)
            || terminal.Kind != HandleKind.AssemblyReference)
        {
            return false;
        }

        identity = ApiAssemblyIdentity.FromReference(
            reader,
            (AssemblyReferenceHandle)terminal,
            beforeMaterialize);
        return true;
    }

    internal static bool IsTopLevelAttributeType(
        MetadataReader reader,
        EntityHandle constructor,
        string fullTypeName,
        Action<int>? beforeMaterialize)
    {
        try
        {
            return TryGetTopLevelAttributeType(
                reader,
                constructor,
                fullTypeName,
                beforeMaterialize,
                out _);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static bool IsFrameworkAttributeType(
        MetadataReader reader,
        EntityHandle constructor,
        string fullTypeName,
        string assemblyName,
        Action<int>? beforeMaterialize)
    {
        try
        {
            return TryGetAuthenticAttributeAssembly(
                    reader,
                    constructor,
                    fullTypeName,
                    beforeMaterialize,
                    out ApiAssemblyIdentity? identity)
                && identity.Name == assemblyName
                && PlatformKeys.IsPlatform(
                    identity.PublicKeyToken);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Counts the authentic marker-attribute rows on one attribute collection,
    /// separating rows carrying the expected parameterless constructor and
    /// complete marker value blob from authentic rows that do not. A row from
    /// an untrusted same-named attribute is neither counted nor malformed: it
    /// never claimed the framework's meaning.
    /// </summary>
    static (int Count, bool HasMalformedRow) ReadAuthenticMarkerAttributeRows(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string fullTypeName,
        string? assemblyName,
        Action<int>? beforeMaterialize)
    {
        int count = 0;
        bool malformed = false;
        foreach (CustomAttributeHandle attrHandle in attributes)
        {
            CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
            bool authentic = assemblyName is null
                ? IsPlatformAttributeType(
                    reader,
                    attr.Constructor,
                    fullTypeName,
                    beforeMaterialize)
                : IsFrameworkAttributeType(
                    reader,
                    attr.Constructor,
                    fullTypeName,
                    assemblyName,
                    beforeMaterialize);
            if (!authentic)
                continue;

            if (HasExpectedConstructor(
                    reader,
                    attr.Constructor,
                    FrameworkConstructorKind.Marker,
                    beforeMaterialize)
                && HasMarkerValueBlob(reader, attr))
            {
                count++;
            }
            else
            {
                malformed = true;
            }
        }

        return (count, malformed);
    }

    static bool HasFrameworkAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string fullTypeName,
        Action<int>? beforeMaterialize)
    {
        foreach (CustomAttributeHandle attrHandle in attributes)
        {
            CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
            if (IsFrameworkAttributeType(
                    reader,
                    attr.Constructor,
                    fullTypeName,
                    SystemTextJsonAssemblyName,
                    beforeMaterialize))
            {
                return true;
            }
        }
        return false;
    }

    static bool HasUnsupportedJsonNumberHandlingAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize)
        => HasUnsupportedJsonEnumAttribute(
            reader,
            attributes,
            JsonNumberHandlingAttributeName,
            FrameworkConstructorKind.JsonNumberHandling,
            beforeMaterialize);

    static bool HasUnsupportedJsonObjectCreationHandlingAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize)
        => HasUnsupportedJsonEnumAttribute(
            reader,
            attributes,
            JsonObjectCreationHandlingAttributeName,
            FrameworkConstructorKind.JsonObjectCreationHandling,
            beforeMaterialize);

    static bool HasUnsupportedJsonEnumAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string attributeTypeName,
        FrameworkConstructorKind constructorKind,
        Action<int>? beforeMaterialize)
    {
        bool found = false;
        foreach (CustomAttributeHandle attrHandle in attributes)
        {
            CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
            if (!IsFrameworkAttributeType(
                    reader,
                    attr.Constructor,
                    attributeTypeName,
                    SystemTextJsonAssemblyName,
                    beforeMaterialize))
            {
                continue;
            }

            if (found
                || !HasExpectedConstructor(
                    reader,
                    attr.Constructor,
                    constructorKind,
                    beforeMaterialize)
                || AttributeDecoder.TryDecode(
                    reader,
                    attr,
                    beforeMaterialize,
                    JsonSourceGenerationExternalEnumUnderlyingTypes) is not
                    {
                        FixedArguments: [var handling],
                        NamedArguments.Length: 0,
                    }
                || !TryReadInt32(handling.Value, out int rawValue)
                || rawValue != 0)
            {
                return true;
            }

            found = true;
        }
        return false;
    }

    internal static bool IsPlatformAttributeType(
        MetadataReader reader,
        EntityHandle constructor,
        string fullTypeName,
        Action<int>? beforeMaterialize)
    {
        try
        {
            return TryGetAuthenticAttributeAssembly(
                    reader,
                    constructor,
                    fullTypeName,
                    beforeMaterialize,
                    out ApiAssemblyIdentity? identity)
                && PlatformKeys.IsPlatform(identity.PublicKeyToken);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the attribute's declaring assembly is one of the core
    /// contracts that can declare a compiler construct's marker attribute, and
    /// carries a platform key.
    /// </summary>
    /// <remarks>
    /// This is a fidelity filter rather than a trust anchor. A single-file
    /// inspection cannot verify either the name or the key, since both are
    /// artifact-authored; what it can do is require the shape the compiler
    /// actually emits, so a marker reached through an unrelated library is not
    /// read as the compiler construct it resembles.
    /// </remarks>
    internal static bool IsPlatformCoreContractAttributeType(
        MetadataReader reader,
        EntityHandle constructor,
        string fullTypeName,
        Action<int>? beforeMaterialize)
    {
        try
        {
            return TryGetAuthenticAttributeAssembly(
                    reader,
                    constructor,
                    fullTypeName,
                    beforeMaterialize,
                    out ApiAssemblyIdentity? identity)
                && IsCoreContractName(identity.Name)
                && PlatformKeys.IsPlatform(identity.PublicKeyToken);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// The core contracts through which a compiler emits a reference to a
    /// marker attribute declared in the runtime's core library.
    /// </summary>
    internal static bool IsCoreContractName(string? assemblyName)
        => assemblyName is
            "System.Private.CoreLib"
                or "System.Runtime"
                or "mscorlib"
                or "netstandard";

    static bool IsSupportedJsonStringEnumConverter(
        string serializedName,
        MetadataTypeDefinitionName enumDefinitionName,
        ApiAssemblyIdentity? enumAssemblyIdentity)
    {
        const string genericPrefix =
            JsonStringEnumConverterTypeName + "`1[";
        if (serializedName.StartsWith(
                genericPrefix,
                StringComparison.Ordinal))
        {
            int outerArgumentEnd = FindMatchingBracket(
                serializedName,
                genericPrefix.Length - 1);
            if (outerArgumentEnd < 0)
                return false;

            string serializedArgument = serializedName[
                genericPrefix.Length..outerArgumentEnd];
            if (serializedArgument.Length >= 2
                && serializedArgument[0] == '['
                && serializedArgument[^1] == ']')
            {
                serializedArgument = serializedArgument[1..^1];
            }

            bool hasArgumentAssembly = TryReadSerializedTypeIdentity(
                    serializedArgument,
                    out string? argumentType,
                    out ApiAssemblyIdentity? argumentAssembly);
            if (!hasArgumentAssembly)
            {
                argumentType = serializedArgument.Trim();
                argumentAssembly = enumAssemblyIdentity;
            }

            if (MetadataTypeDefinitionName.ParseSerialized(
                    argumentType!)
                    is not MetadataTypeDefinitionNameResult.Valid argumentName
                || !argumentName.Name.Equals(enumDefinitionName)
                || enumAssemblyIdentity is null
                || !enumAssemblyIdentity.Equals(argumentAssembly))
            {
                return false;
            }

            int outerAssemblyStart = outerArgumentEnd + 1;
            return outerAssemblyStart < serializedName.Length
                && serializedName[outerAssemblyStart] == ','
                && IsTrustedSerializedAssembly(
                    serializedName[(outerAssemblyStart + 1)..]);
        }

        int assemblySeparator = serializedName.IndexOf(',');
        return assemblySeparator > 0
            && serializedName[..assemblySeparator].Trim()
                == JsonStringEnumConverterTypeName
            && IsTrustedSerializedAssembly(
                serializedName[(assemblySeparator + 1)..]);
    }

    static bool IsExpectedTopLevelSignatureType(
        NamedTypeNode type,
        string expectedNamespace,
        string expectedName,
        Func<ApiAssemblyIdentity, bool> isExpectedAssembly)
        => type.MetadataName is
        {
            Namespace: var actualNamespace,
            Segments: var segments,
        }
        && actualNamespace == expectedNamespace
        && segments.Count == 1
        && segments[0] == expectedName
        && type.AssemblyIdentity is { } identity
        && isExpectedAssembly(identity);

    static bool IsCoreContractAssembly(ApiAssemblyIdentity identity) =>
        identity.Name is "System.Private.CoreLib"
            or "System.Runtime"
            or "mscorlib"
            or "netstandard"
        && PlatformKeys.IsPlatform(identity.PublicKeyToken);

    static bool IsSystemTextJsonAssembly(ApiAssemblyIdentity identity) =>
        identity.Name == SystemTextJsonAssemblyName
        && PlatformKeys.IsPlatform(identity.PublicKeyToken);

    static int FindMatchingBracket(
        string serializedName,
        int openBracket)
    {
        int depth = 1;
        for (int i = openBracket + 1; i < serializedName.Length; i++)
        {
            switch (serializedName[i])
            {
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    if (depth == 0)
                        return i;
                    break;
            }
        }
        return -1;
    }

    static bool TryReadSerializedTypeIdentity(
        string serializedName,
        out string? typeName,
        out ApiAssemblyIdentity? assemblyIdentity)
    {
        int depth = 0;
        for (int i = 0; i < serializedName.Length; i++)
        {
            switch (serializedName[i])
            {
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    if (depth < 0)
                    {
                        typeName = null;
                        assemblyIdentity = null;
                        return false;
                    }
                    break;
                case ',' when depth == 0:
                    typeName = serializedName[..i].Trim();
                    bool parsed = TryReadAssemblyIdentity(
                        serializedName[(i + 1)..],
                        out assemblyIdentity);
                    return typeName.Length > 0
                        && parsed;
            }
        }

        typeName = null;
        assemblyIdentity = null;
        return false;
    }

    static bool IsTrustedSerializedAssembly(string assemblyIdentity)
    {
        if (!TryReadAssemblyIdentity(
                assemblyIdentity,
                out ApiAssemblyIdentity? identity)
            || identity.Name != SystemTextJsonAssemblyName)
        {
            return false;
        }

        return PlatformKeys.IsPlatform(
            identity.PublicKeyToken);
    }

    static bool TryReadAssemblyIdentity(
        string serializedIdentity,
        [NotNullWhen(true)] out ApiAssemblyIdentity? identity)
    {
        string[] components = serializedIdentity.Split(',');
        string name = components[0].Trim();
        if (name.Length == 0)
        {
            identity = null;
            return false;
        }

        Version? version = null;
        string? culture = null;
        string? publicKeyToken = null;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < components.Length; i++)
        {
            string component = components[i].Trim();
            int separator = component.IndexOf('=');
            if (separator <= 0
                || separator == component.Length - 1)
            {
                identity = null;
                return false;
            }

            string key = component[..separator].Trim();
            string value = component[(separator + 1)..].Trim();
            if (!seen.Add(key))
            {
                identity = null;
                return false;
            }

            switch (key.ToUpperInvariant())
            {
                case "VERSION":
                    if (!Version.TryParse(value, out version))
                    {
                        identity = null;
                        return false;
                    }
                    break;
                case "CULTURE":
                    culture = value.Equals(
                        "neutral",
                        StringComparison.OrdinalIgnoreCase)
                            ? null
                            : value;
                    break;
                case "PUBLICKEYTOKEN":
                    publicKeyToken = value.Equals(
                        "null",
                        StringComparison.OrdinalIgnoreCase)
                            ? null
                            : value;
                    break;
                default:
                    identity = null;
                    return false;
            }
        }

        identity = new(
            name,
            version,
            culture,
            publicKeyToken);
        return true;
    }



    static JsonSourceGenerationOptionsEvidence
        ReadJsonSourceGenerationOptions(
        MetadataReader reader,
        CustomAttribute attr,
        Action<int>? beforeMaterialize)
    {
        if (AttributeDecoder.TryDecodePreservingSerializedTypeNames(
                reader,
                attr,
                beforeMaterialize,
                JsonSourceGenerationExternalEnumUnderlyingTypes) is not
            { } decoded
            || (decoded.FixedArguments.Length != 0
                && !(decoded.FixedArguments.Length == 1
                    && decoded.FixedArguments[0].Value
                        is int defaults
                    && defaults == 0)))
        {
            return new(
                JsonWireNamingPolicy.Unsupported,
                JsonSourceGenerationMode.Default);
        }

        CustomAttributeNamedArgument<string>? propertyNamingPolicy = null;
        JsonSourceGenerationMode generationMode =
            JsonSourceGenerationMode.Default;
        var optionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var named in decoded.NamedArguments)
        {
            string? expectedType =
                ExpectedJsonSourceGenerationOptionType(named.Name);
            if (named.Kind != CustomAttributeNamedArgumentKind.Property
                || expectedType is null
                || !IsExpectedSerializedOptionType(
                    named.Type,
                    expectedType)
                || !optionNames.Add(named.Name!)
                || HasUnsupportedWireEffect(named))
            {
                return new(
                    JsonWireNamingPolicy.Unsupported,
                    JsonSourceGenerationMode.Default);
            }

            if (named.Name == "PropertyNamingPolicy")
            {
                propertyNamingPolicy = named;
            }
            else if (named.Name == "GenerationMode")
            {
                if (!TryReadInt32(named.Value, out int rawMode)
                    || !TryGetJsonSourceGenerationMode(
                        rawMode,
                        out generationMode))
                {
                    return new(
                        JsonWireNamingPolicy.Unsupported,
                        JsonSourceGenerationMode.Default);
                }
            }
        }

        if (propertyNamingPolicy is not { } policy)
        {
            return new(
                JsonWireNamingPolicy.None,
                generationMode);
        }

        if (!TryReadInt32(policy.Value, out int rawValue))
        {
            return new(
                JsonWireNamingPolicy.Unsupported,
                JsonSourceGenerationMode.Default);
        }

        JsonWireNamingPolicy namingPolicy = rawValue switch
        {
            0 => JsonWireNamingPolicy.None,
            1 => JsonWireNamingPolicy.CamelCase,
            2 => JsonWireNamingPolicy.SnakeCaseLower,
            3 => JsonWireNamingPolicy.SnakeCaseUpper,
            4 => JsonWireNamingPolicy.KebabCaseLower,
            5 => JsonWireNamingPolicy.KebabCaseUpper,
            _ => JsonWireNamingPolicy.Unsupported,
        };
        return new(namingPolicy, generationMode);
    }

    readonly record struct JsonSourceGenerationOptionsEvidence(
        JsonWireNamingPolicy NamingPolicy,
        JsonSourceGenerationMode GenerationMode);

    static bool HasUnsupportedWireEffect(
        CustomAttributeNamedArgument<string> option) =>
        option.Name switch
        {
            "Converters" or "TypeClassifiers" => true,
            "IgnoreReadOnlyFields"
                or "IgnoreReadOnlyProperties"
                or "IncludeFields"
                or "UseStringEnumConverter" =>
                option.Value is not false,
            "DefaultIgnoreCondition"
                or "DictionaryKeyPolicy"
                or "NumberHandling"
                or "PreferredObjectCreationHandling"
                or "ReferenceHandler" =>
                !TryReadInt32(option.Value, out int value) || value != 0,
            _ => false,
        };

    static bool TryGetSingleStringFixedArgument(
        MetadataReader reader,
        CustomAttribute attr,
        out string? value,
        Action<int>? beforeMaterialize)
    {
        if (AttributeDecoder.TryDecode(reader, attr, beforeMaterialize) is
            {
                FixedArguments.Length: 1,
                NamedArguments.Length: 0,
            } decoded
            && decoded.FixedArguments[0].Value is string text)
        {
            value = text;
            return true;
        }

        value = null;
        return false;
    }

    static bool TryReadInt32(object? value, out int result)
    {
        switch (value)
        {
            case byte b:
                result = b;
                return true;
            case sbyte sb:
                result = sb;
                return true;
            case short s:
                result = s;
                return true;
            case ushort us:
                result = us;
                return true;
            case int i:
                result = i;
                return true;
            case uint ui:
                result = unchecked((int)ui);
                return true;
            case long l:
                result = unchecked((int)l);
                return true;
            case ulong ul:
                result = unchecked((int)ul);
                return true;
            default:
                result = default;
                return false;
        }
    }

    static string? ExpectedJsonSourceGenerationOptionType(string? name) =>
        name switch
        {
            "AllowDuplicateProperties"
                or "AllowOutOfOrderMetadataProperties"
                or "AllowTrailingCommas"
                or "IgnoreReadOnlyFields"
                or "IgnoreReadOnlyProperties"
                or "IncludeFields"
                or "PropertyNameCaseInsensitive"
                or "RespectNullableAnnotations"
                or "RespectRequiredConstructorParameters"
                or "UseStringEnumConverter"
                or "WriteIndented" => "bool",
            "DefaultBufferSize"
                or "IndentSize"
                or "MaxDepth" => "int",
            "IndentCharacter" => "char",
            "NewLine" => "string",
            "Converters"
                or "TypeClassifiers" => "System.Type[]",
            "DefaultIgnoreCondition" =>
                "System.Text.Json.Serialization.JsonIgnoreCondition",
            "DictionaryKeyPolicy"
                or "PropertyNamingPolicy" => JsonKnownNamingPolicyTypeName,
            "GenerationMode" =>
                "System.Text.Json.Serialization.JsonSourceGenerationMode",
            "NumberHandling" =>
                "System.Text.Json.Serialization.JsonNumberHandling",
            "PreferredObjectCreationHandling" =>
                "System.Text.Json.Serialization.JsonObjectCreationHandling",
            "ReadCommentHandling" =>
                "System.Text.Json.JsonCommentHandling",
            "ReferenceHandler" =>
                "System.Text.Json.Serialization.JsonKnownReferenceHandler",
            "UnknownTypeHandling" =>
                "System.Text.Json.Serialization.JsonUnknownTypeHandling",
            "UnmappedMemberHandling" =>
                "System.Text.Json.Serialization.JsonUnmappedMemberHandling",
            _ => null,
        };

    static bool IsExpectedSerializedOptionType(
        string actual,
        string expected)
    {
        if (expected is "bool"
            or "int"
            or "char"
            or "string"
            or "System.Type[]")
        {
            return actual == expected;
        }

        return TryReadSerializedTypeIdentity(
                actual,
                out string? typeName,
                out ApiAssemblyIdentity? assembly)
            && typeName == expected
            && assembly is not null
            && assembly.Name == SystemTextJsonAssemblyName
            && PlatformKeys.IsPlatform(
                assembly.PublicKeyToken);
    }

    static bool TryGetNamedArgumentString(
        MetadataReader reader,
        CustomAttribute attr,
        string name,
        out string? value,
        Action<int>? beforeMaterialize)
    {
        if (AttributeDecoder.TryDecode(reader, attr, beforeMaterialize) is { } decoded)
        {
            foreach (var named in decoded.NamedArguments)
            {
                if (named.Name != name)
                    continue;

                if (named.Value is not null)
                {
                    value = named.Value switch
                    {
                        string text => text,
                        _ => named.Value.ToString(),
                    };

                    if (string.IsNullOrEmpty(value))
                        return false;

                    int lastDot = value.LastIndexOf('.');
                    if (lastDot >= 0)
                        value = value[(lastDot + 1)..];

                    return true;
                }
            }
        }

        value = null;
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
