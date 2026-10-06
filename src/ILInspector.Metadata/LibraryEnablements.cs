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
[JsonConverter(typeof(JsonStringEnumConverter<LibraryEnablementId>))]
public enum LibraryEnablementId
{
    [JsonStringEnumMemberName("aot-compatible")]
    AotCompatible,

    [JsonStringEnumMemberName("runtime-async")]
    RuntimeAsync,

    [JsonStringEnumMemberName("memory-safety-v2")]
    MemorySafetyV2,
}

/// <summary>Why an enablement cannot be decided from the inspected image.</summary>
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

/// <summary>
/// One enablement fact: the closed Enabled, Not enabled, or Unavailable
/// state of one <see cref="LibraryEnablementId"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Enabled), "enabled")]
[JsonDerivedType(typeof(NotEnabled), "not-enabled")]
[JsonDerivedType(typeof(Unavailable), "unavailable")]
public abstract record LibraryEnablement
{
    private LibraryEnablement(LibraryEnablementId id) => Id = id;

    // Serialize the identifier before case-specific fields such as reason.
    [JsonPropertyOrder(-1)]
    public LibraryEnablementId Id { get; }

    /// <summary>The image carries the evidence that the capability was built in.</summary>
    public sealed record Enabled(LibraryEnablementId Id) : LibraryEnablement(Id);

    /// <summary>The image was read completely and does not carry that evidence.</summary>
    public sealed record NotEnabled(LibraryEnablementId Id) : LibraryEnablement(Id);

    /// <summary>The evidence cannot support a judgment; the reason says why.</summary>
    public sealed record Unavailable(
        LibraryEnablementId Id,
        LibraryEnablementUnavailableReason Reason) : LibraryEnablement(Id);
}

/// <summary>
/// Every enablement fact for one image, in vocabulary order. See
/// <c>docs/design/library-enablements.md</c>.
/// </summary>
public sealed record LibraryEnablementFacts(ImmutableArray<LibraryEnablement> Items)
{
    public bool Equals(LibraryEnablementFacts? other)
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
    public IEnumerable<LibraryEnablementId> Enabled()
        => Items
            .OfType<LibraryEnablement.Enabled>()
            .Select(static item => item.Id);

    /// <summary>The short host-neutral label for an enablement.</summary>
    public static string Label(LibraryEnablementId id) => id switch
    {
        LibraryEnablementId.AotCompatible => "AOT",
        LibraryEnablementId.RuntimeAsync => "Runtime Async",
        LibraryEnablementId.MemorySafetyV2 => "Memory Safety v2",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    internal static LibraryEnablementFacts Read(PEReader peReader)
    {
        MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);
        ReferenceAssemblyState reference = ReadReferenceAssemblyState(reader);
        if (reference != ReferenceAssemblyState.Implementation)
        {
            // An unnameable assembly attribute might be ReferenceAssemblyAttribute,
            // so the reference rule cannot be decided.
            LibraryEnablementUnavailableReason reason =
                reference == ReferenceAssemblyState.Reference
                    ? LibraryEnablementUnavailableReason.ReferenceAssembly
                    : LibraryEnablementUnavailableReason.UndecodableMetadata;
            return new(
            [
                Unavailable(LibraryEnablementId.AotCompatible, reason),
                Unavailable(LibraryEnablementId.RuntimeAsync, reason),
                Unavailable(LibraryEnablementId.MemorySafetyV2, reason),
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

    private enum ReferenceAssemblyState
    {
        Implementation,
        Reference,
        Undecidable,
    }

    private static ReferenceAssemblyState ReadReferenceAssemblyState(MetadataReader reader)
    {
        if (!reader.IsAssembly)
            return ReferenceAssemblyState.Implementation;

        bool undecidable = false;
        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            try
            {
                if (AttributeReader.GetAttributeTypeName(reader, attribute.Constructor) == ReferenceAssemblyAttribute)
                    return ReferenceAssemblyState.Reference;
            }
            catch (BadImageFormatException)
            {
                undecidable = true;
            }
        }

        return undecidable ? ReferenceAssemblyState.Undecidable : ReferenceAssemblyState.Implementation;
    }

    private static LibraryEnablement ReadAotCompatible(MetadataReader reader)
    {
        const LibraryEnablementId id = LibraryEnablementId.AotCompatible;
        if (!reader.IsAssembly)
            return new LibraryEnablement.NotEnabled(id);

        bool undecodable = false;
        bool unrecognized = false;
        bool sawTrue = false;
        bool sawFalse = false;
        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            // Every assembly attribute is nameable here: an unnameable one
            // already made the reference rule undecidable.
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
            return Unavailable(id, LibraryEnablementUnavailableReason.UndecodableMetadata);
        if (unrecognized)
            return Unavailable(id, LibraryEnablementUnavailableReason.UnrecognizedValue);
        if (sawTrue && sawFalse)
            return Unavailable(id, LibraryEnablementUnavailableReason.ConflictingValues);
        return sawTrue ? new LibraryEnablement.Enabled(id) : new LibraryEnablement.NotEnabled(id);
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
                return new LibraryEnablement.Enabled(LibraryEnablementId.RuntimeAsync);
        }

        return new LibraryEnablement.NotEnabled(LibraryEnablementId.RuntimeAsync);
    }

    private static LibraryEnablement ReadMemorySafetyV2(MemorySafetyRulesResult rules)
    {
        const LibraryEnablementId id = LibraryEnablementId.MemorySafetyV2;
        return rules switch
        {
            MemorySafetyRulesResult.Available { State: MemorySafetyRulesState.Updated } =>
                new LibraryEnablement.Enabled(id),
            MemorySafetyRulesResult.Available { State: MemorySafetyRulesState.Legacy } =>
                new LibraryEnablement.NotEnabled(id),
            MemorySafetyRulesResult.Available { State: MemorySafetyRulesState.Unsupported } =>
                Unavailable(id, LibraryEnablementUnavailableReason.UnsupportedMemorySafetyRules),
            MemorySafetyRulesResult.Available { State: MemorySafetyRulesState.Malformed } =>
                Unavailable(id, LibraryEnablementUnavailableReason.MalformedMemorySafetyRules),
            MemorySafetyRulesResult.Available { State: MemorySafetyRulesState.Conflicting } =>
                Unavailable(id, LibraryEnablementUnavailableReason.ConflictingMemorySafetyRules),
            MemorySafetyRulesResult.Unavailable =>
                Unavailable(id, LibraryEnablementUnavailableReason.MemorySafetyMetadataUnavailable),
            _ => throw new InvalidOperationException("Unknown memory-safety rules result."),
        };
    }

    private static LibraryEnablement Unavailable(
        LibraryEnablementId id,
        LibraryEnablementUnavailableReason reason)
        => new LibraryEnablement.Unavailable(id, reason);
}
