using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Serialization;

namespace ILInspector.Metadata;

/// <summary>
/// The closed vocabulary of Library enablements. Serialized names are the
/// stable identifiers owned by <c>docs/design/library-enablements.md</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LibraryEnablementKind>))]
public enum LibraryEnablementKind
{
    [JsonStringEnumMemberName("aot-compatible")]
    AotCompatible,

    [JsonStringEnumMemberName("runtime-async")]
    RuntimeAsync,

    [JsonStringEnumMemberName("memory-safety-v2")]
    MemorySafetyV2,
}

[JsonConverter(typeof(JsonStringEnumConverter<LibraryEnablementState>))]
public enum LibraryEnablementState
{
    [JsonStringEnumMemberName("enabled")]
    Enabled,

    [JsonStringEnumMemberName("not-enabled")]
    NotEnabled,

    [JsonStringEnumMemberName("unavailable")]
    Unavailable,
}

/// <summary>Why an enablement cannot be judged from the inspected image.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LibraryEnablementUnavailableReason>))]
public enum LibraryEnablementUnavailableReason
{
    [JsonStringEnumMemberName("reference-assembly")]
    ReferenceAssembly,

    [JsonStringEnumMemberName("undecodable-metadata")]
    UndecodableMetadata,

    [JsonStringEnumMemberName("unrecognized-value")]
    UnrecognizedValue,

    [JsonStringEnumMemberName("conflicting-values")]
    ConflictingValues,

    [JsonStringEnumMemberName("unsupported-memory-safety-rules")]
    UnsupportedMemorySafetyRules,

    [JsonStringEnumMemberName("malformed-memory-safety-rules")]
    MalformedMemorySafetyRules,

    [JsonStringEnumMemberName("conflicting-memory-safety-rules")]
    ConflictingMemorySafetyRules,

    [JsonStringEnumMemberName("memory-safety-metadata-unavailable")]
    MemorySafetyMetadataUnavailable,
}

/// <summary>One enablement judgment. <see cref="Reason"/> is set only when
/// <see cref="State"/> is <see cref="LibraryEnablementState.Unavailable"/>.</summary>
public sealed record LibraryEnablement
{
    public LibraryEnablement(
        LibraryEnablementKind kind,
        LibraryEnablementState state,
        LibraryEnablementUnavailableReason? reason = null)
    {
        if ((state == LibraryEnablementState.Unavailable) != reason.HasValue)
        {
            throw new ArgumentException(
                "An unavailable enablement requires a reason; other states carry none.",
                nameof(reason));
        }

        Kind = kind;
        State = state;
        Reason = reason;
    }

    public LibraryEnablementKind Kind { get; }
    public LibraryEnablementState State { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LibraryEnablementUnavailableReason? Reason { get; }
}

/// <summary>
/// Every enablement judged for one image, in vocabulary order. See
/// <c>docs/design/library-enablements.md</c>.
/// </summary>
public sealed record LibraryEnablements(ImmutableArray<LibraryEnablement> Items)
{
    public bool Equals(LibraryEnablements? other)
        => other is not null
            && Items.AsSpan().SequenceEqual(other.Items.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (LibraryEnablement item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    /// <summary>The enablements a badge or chip presents: Enabled only.</summary>
    public IEnumerable<LibraryEnablementKind> Enabled()
        => Items
            .Where(static item => item.State == LibraryEnablementState.Enabled)
            .Select(static item => item.Kind);

    /// <summary>The short host-neutral label for an enablement.</summary>
    public static string Label(LibraryEnablementKind kind) => kind switch
    {
        LibraryEnablementKind.AotCompatible => "AOT",
        LibraryEnablementKind.RuntimeAsync => "Runtime Async",
        LibraryEnablementKind.MemorySafetyV2 => "Memory Safety v2",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static LibraryEnablements Read(PEReader peReader)
    {
        MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);
        if (IsReferenceAssembly(reader))
        {
            return new(
            [
                Unavailable(LibraryEnablementKind.AotCompatible, LibraryEnablementUnavailableReason.ReferenceAssembly),
                Unavailable(LibraryEnablementKind.RuntimeAsync, LibraryEnablementUnavailableReason.ReferenceAssembly),
                Unavailable(LibraryEnablementKind.MemorySafetyV2, LibraryEnablementUnavailableReason.ReferenceAssembly),
            ]);
        }

        return new(
        [
            ReadAotCompatible(reader),
            ReadRuntimeAsync(reader),
            ReadMemorySafetyV2(MemorySafetyMetadataIndex.Create(reader).Rules),
        ]);
    }

    private const string AssemblyMetadataAttribute = "System.Reflection.AssemblyMetadataAttribute";
    private const string ReferenceAssemblyAttribute = "System.Runtime.CompilerServices.ReferenceAssemblyAttribute";
    private const string AotCompatibleKey = "IsAotCompatible";
    private const MethodImplAttributes AsyncImplFlag = (MethodImplAttributes)0x2000;

    private static bool IsReferenceAssembly(MetadataReader reader)
    {
        if (!reader.IsAssembly)
            return false;

        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            if (AttributeReader.GetAttributeTypeName(reader, attribute.Constructor) == ReferenceAssemblyAttribute)
                return true;
        }

        return false;
    }

    private static LibraryEnablement ReadAotCompatible(MetadataReader reader)
    {
        const LibraryEnablementKind kind = LibraryEnablementKind.AotCompatible;
        if (!reader.IsAssembly)
            return new(kind, LibraryEnablementState.NotEnabled);

        bool undecodable = false;
        bool unrecognized = false;
        bool sawTrue = false;
        bool sawFalse = false;
        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            if (AttributeReader.GetAttributeTypeName(reader, attribute.Constructor) != AssemblyMetadataAttribute)
                continue;

            if (!TryReadKeyValue(reader, attribute, out string? key, out string? value))
            {
                // The undecoded row might carry the key.
                undecodable = true;
                continue;
            }

            if (key != AotCompatibleKey)
                continue;

            if (value is not null && bool.TryParse(value, out bool parsed))
            {
                if (parsed)
                    sawTrue = true;
                else
                    sawFalse = true;
            }
            else
            {
                unrecognized = true;
            }
        }

        if (undecodable)
            return Unavailable(kind, LibraryEnablementUnavailableReason.UndecodableMetadata);
        if (unrecognized)
            return Unavailable(kind, LibraryEnablementUnavailableReason.UnrecognizedValue);
        if (sawTrue && sawFalse)
            return Unavailable(kind, LibraryEnablementUnavailableReason.ConflictingValues);
        return new(kind, sawTrue ? LibraryEnablementState.Enabled : LibraryEnablementState.NotEnabled);
    }

    private static bool TryReadKeyValue(
        MetadataReader reader,
        CustomAttribute attribute,
        out string? key,
        out string? value)
    {
        key = null;
        value = null;
        try
        {
            BlobReader blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 0x0001)
                return false;
            key = blob.ReadSerializedString();
            value = blob.ReadSerializedString();
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static LibraryEnablement ReadRuntimeAsync(MetadataReader reader)
    {
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            if ((reader.GetMethodDefinition(handle).ImplAttributes & AsyncImplFlag) != 0)
                return new(LibraryEnablementKind.RuntimeAsync, LibraryEnablementState.Enabled);
        }

        return new(LibraryEnablementKind.RuntimeAsync, LibraryEnablementState.NotEnabled);
    }

    private static LibraryEnablement ReadMemorySafetyV2(MemorySafetyRulesResult rules)
    {
        const LibraryEnablementKind kind = LibraryEnablementKind.MemorySafetyV2;
        return rules switch
        {
            MemorySafetyRulesResult.Available { State: MemorySafetyRulesState.Updated } =>
                new(kind, LibraryEnablementState.Enabled),
            MemorySafetyRulesResult.Available { State: MemorySafetyRulesState.Legacy } =>
                new(kind, LibraryEnablementState.NotEnabled),
            MemorySafetyRulesResult.Available { State: MemorySafetyRulesState.Unsupported } =>
                Unavailable(kind, LibraryEnablementUnavailableReason.UnsupportedMemorySafetyRules),
            MemorySafetyRulesResult.Available { State: MemorySafetyRulesState.Malformed } =>
                Unavailable(kind, LibraryEnablementUnavailableReason.MalformedMemorySafetyRules),
            MemorySafetyRulesResult.Available { State: MemorySafetyRulesState.Conflicting } =>
                Unavailable(kind, LibraryEnablementUnavailableReason.ConflictingMemorySafetyRules),
            MemorySafetyRulesResult.Unavailable =>
                Unavailable(kind, LibraryEnablementUnavailableReason.MemorySafetyMetadataUnavailable),
            _ => throw new InvalidOperationException("Unknown memory-safety rules result."),
        };
    }

    private static LibraryEnablement Unavailable(
        LibraryEnablementKind kind,
        LibraryEnablementUnavailableReason reason)
        => new(kind, LibraryEnablementState.Unavailable, reason);
}
