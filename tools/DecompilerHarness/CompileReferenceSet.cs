using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// An exact ECMA assembly identity, optionally pinned to an inventory entry.
/// Null culture/token mean neutral/unsigned, not wildcard requests.
/// </summary>
public sealed class CompileReferenceRequest
{
    public CompileReferenceRequest(
        AssemblyReferenceIdentity identity,
        ArtifactIdentity? pin = null,
        ImmutableArray<string> aliases = default,
        bool embedInteropTypes = false)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
        ArgumentNullException.ThrowIfNull(identity.Version);
        Identity = identity;
        Pin = pin;
        // Alias order is not binding policy. Default means the global alias.
        ImmutableArray<string> canonicalAliases = aliases.IsDefaultOrEmpty
            ? ["global"]
            : [.. aliases.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Properties = new MetadataReferenceProperties(
            MetadataImageKind.Assembly, canonicalAliases, embedInteropTypes);
    }

    public AssemblyReferenceIdentity Identity { get; }
    public ArtifactIdentity? Pin { get; }
    public MetadataReferenceProperties Properties { get; }
}

public sealed class CompileReferenceDescriptor
{
    internal CompileReferenceDescriptor(
        CompileReferenceImage image, int selectedOrdinal, MetadataReferenceProperties properties,
        bool isPlatformAuthorized = false)
    {
        Image = image;
        SelectedOrdinal = selectedOrdinal;
        Properties = properties;
        IsPlatformAuthorized = isPlatformAuthorized;
    }

    public CompileReferenceImage Image { get; }
    public ArtifactIdentity InventoryId => Image.InventoryId;
    public int SelectedOrdinal { get; }
    public MetadataReferenceProperties Properties { get; }
    public bool IsPlatformAuthorized { get; }
}

/// <summary>
/// A generation-scoped set key. HexValue alone is not a cross-generation identity:
/// the encoding uses owner ordinals only as deterministic generation-local order.
/// Platform keys also retain the typed Services policy-version association.
/// </summary>
public sealed class CompileReferenceSetDigest : IEquatable<CompileReferenceSetDigest>
{
    internal CompileReferenceSetDigest(
        ArtifactGenerationIdentity generation, string hexValue,
        AssemblyBindingPolicyVersion? ownerPolicyVersion = null)
    {
        Generation = generation;
        HexValue = hexValue;
        OwnerPolicyVersion = ownerPolicyVersion;
    }

    public ArtifactGenerationIdentity Generation { get; }
    public string Algorithm => "SHA-256";
    public string HexValue { get; }
    public AssemblyBindingPolicyVersion? OwnerPolicyVersion { get; }
    public bool Equals(CompileReferenceSetDigest? other) =>
        other is not null && ReferenceEquals(Generation, other.Generation)
        && ReferenceEquals(OwnerPolicyVersion, other.OwnerPolicyVersion) && HexValue == other.HexValue;
    public override bool Equals(object? obj) => obj is CompileReferenceSetDigest other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Generation, HexValue, OwnerPolicyVersion);
}

/// <summary>
/// Frozen source association and compiler references. The caller keeps the
/// discovery lease and session alive through Select and every Use operation.
/// </summary>
public sealed class CompileReferenceSet
{
    readonly CompileReferenceInventory _inventory;
    internal FrozenPlatformBindings? PlatformBindings { get; }
    internal AssemblyBindingPolicyVersion BindingVersion { get; } = new();

    CompileReferenceSet(
        CompileReferenceInventory inventory,
        ImmutableArray<CompileReferenceDescriptor> references,
        FrozenPlatformBindings? platformBindings)
    {
        _inventory = inventory;
        Source = inventory.Source;
        References = references;
        PlatformBindings = platformBindings;
        Digest = ComputeDigest();
    }

    public CompileReferenceImage Source { get; }
    public ImmutableArray<CompileReferenceDescriptor> References { get; }
    public CompileReferenceSetDigest Digest { get; }

    internal static CompileReferenceResult<CompileReferenceSet> Select(
        CompileReferenceInventory inventory,
        IEnumerable<CompileReferenceRequest> requests,
        CancellationToken cancellationToken,
        FrozenPlatformBindings? platformBindings = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (Validate(inventory, inventory.Source, cancellationToken) is { } unavailable)
            return new CompileReferenceResult<CompileReferenceSet>.Rejected(unavailable);

        var selected = new Dictionary<ArtifactIdentity, (CompileReferenceImage Image, MetadataReferenceProperties Properties)>();
        IEnumerable<CompileReferenceRequest> allRequests = platformBindings is null ? requests
            : requests.Concat(platformBindings.PlatformImages.Select(image =>
                new CompileReferenceRequest(image.Identity, image.InventoryId)));
        foreach (CompileReferenceRequest request in allRequests)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(request.Pin, inventory.Source.InventoryId))
                return Reject(CompileReferenceFailureKind.SourceReferenceExcluded, request);

            CompileReferenceImage[] matches = [.. inventory.Candidates.Where(candidate =>
                !ReferenceEquals(candidate.InventoryId, inventory.Source.InventoryId)
                && request.Identity.IsEquivalentTo(candidate.Identity)
                && (request.Pin is null || ReferenceEquals(request.Pin, candidate.InventoryId)))];
            if (matches.Length == 0)
                return Reject(CompileReferenceFailureKind.ReferenceNotFound, request);
            if (matches.Length > 1)
                return Reject(CompileReferenceFailureKind.ReferenceSelectionAmbiguous, request, matches);

            CompileReferenceImage image = matches[0];
            if (selected.TryGetValue(image.InventoryId, out var previous))
            {
                if (!previous.Properties.Equals(request.Properties))
                    return Reject(CompileReferenceFailureKind.ReferenceRoleConflict, request);
                continue;
            }
            // Even explicitly pinned references must leave one exact Metadata resolution.
            CompileReferenceImage[] collisions = [.. selected.Values.Select(value => value.Image)
                .Where(value => value.Identity.IsEquivalentTo(image.Identity)).Append(image)];
            if (collisions.Length > 1)
                return Reject(CompileReferenceFailureKind.ReferenceSelectionAmbiguous, request, collisions);
            selected.Add(image.InventoryId, (image, request.Properties));
        }

        ImmutableArray<CompileReferenceDescriptor> descriptors = [.. selected.Values
            .OrderBy(value => value.Image.InventoryId.Ordinal)
            .Select((value, index) => new CompileReferenceDescriptor(value.Image, index, value.Properties,
                platformBindings?.PlatformImages.Contains(value.Image) == true))];
        foreach (CompileReferenceDescriptor descriptor in descriptors)
        {
            if (Validate(inventory, descriptor.Image, cancellationToken) is { } failure)
                return new CompileReferenceResult<CompileReferenceSet>.Rejected(failure);
        }
        return new CompileReferenceResult<CompileReferenceSet>.Ready(new(inventory, descriptors, platformBindings));
    }

    /// <summary>
    /// Opens all selected owner content before invoking the scoped consumer.
    /// Metadata openers retain the same lease guard; Roslyn reads only the
    /// retained snapshots. Consumer exceptions propagate unchanged.
    /// </summary>
    public CompileReferenceResult<T> Use<T>(
        Func<CompileReferenceContext, T> consume,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consume);
        var streams = new List<Stream>();
        try
        {
            foreach (CompileReferenceImage image in References.Select(reference => reference.Image).Prepend(Source))
            {
                if (Validate(_inventory, image, cancellationToken) is { } failure)
                    return new CompileReferenceResult<T>.Rejected(failure);
                try
                {
                    streams.Add(image.Assembly.OpenRead());
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or ObjectDisposedException)
                {
                    return new CompileReferenceResult<T>.Rejected(
                        new(CompileReferenceFailureKind.ReferenceAuthorityUnavailable, image.InventoryId));
                }
                catch (IOException)
                {
                    return new CompileReferenceResult<T>.Rejected(
                        new(CompileReferenceFailureKind.ReferenceContentUnavailable, image.InventoryId));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new CompileReferenceResult<T>.Ready(consume(new CompileReferenceContext(this)));
        }
        finally
        {
            foreach (Stream stream in streams)
                stream.Dispose();
        }
    }

    static CompileReferenceFailure? Validate(
        CompileReferenceInventory inventory, CompileReferenceImage image, CancellationToken cancellationToken) =>
        inventory.Owner.WithQueryContent(image.InventoryId, inventory.Lease, static (_, _) => true, cancellationToken)
            is ArtifactContentAccessOutcome<bool>.Accessed
                ? null
                : new(CompileReferenceFailureKind.ReferenceAuthorityUnavailable, image.InventoryId);

    static CompileReferenceResult<CompileReferenceSet> Reject(
        CompileReferenceFailureKind kind, CompileReferenceRequest request,
        IEnumerable<CompileReferenceImage>? matches = null) =>
        new CompileReferenceResult<CompileReferenceSet>.Rejected(new(kind, request.Pin, request.Identity,
            matches is null ? [] : [.. matches.OrderBy(image => image.InventoryId.Ordinal).Select(image => image.InventoryId)]));

    CompileReferenceSetDigest ComputeDigest()
    {
        using var encoding = new MemoryStream();
        using (var writer = new BinaryWriter(encoding, Encoding.UTF8, leaveOpen: true))
        {
            WriteText(writer, PlatformBindings is null
                ? "DecompilerHarness.CompileReferenceSet/v1/exact-identity"
                : "DecompilerHarness.CompileReferenceSet/v2/platform-compatibility");
            WriteImage(writer, Source);
            writer.Write(References.Length);
            foreach (CompileReferenceDescriptor descriptor in References)
            {
                writer.Write(descriptor.SelectedOrdinal);
                WriteImage(writer, descriptor.Image);
                writer.Write((int)descriptor.Properties.Kind);
                writer.Write(descriptor.Properties.Aliases.Length);
                foreach (string alias in descriptor.Properties.Aliases)
                    WriteText(writer, alias);
                writer.Write(descriptor.Properties.EmbedInteropTypes);
                writer.Write(descriptor.IsPlatformAuthorized);
            }
            PlatformBindings?.WriteEncoding(writer);
        }
        return new(Source.InventoryId.Generation,
            Convert.ToHexStringLower(SHA256.HashData(encoding.GetBuffer().AsSpan(0, checked((int)encoding.Length)))),
            PlatformBindings?.OwnerVersion);
    }

    internal static void WriteImage(BinaryWriter writer, CompileReferenceImage image)
    {
        writer.Write(image.InventoryId.Ordinal);
        WriteText(writer, image.ContentDigest.Algorithm);
        WriteText(writer, image.ContentDigest.HexValue);
        writer.Write(image.ModuleVersionId.ToByteArray());
        WriteIdentity(writer, image.Identity);
    }

    internal static void WriteIdentity(BinaryWriter writer, AssemblyReferenceIdentity identity)
    {
        WriteText(writer, identity.Name);
        WriteText(writer, identity.Version!.ToString());
        WriteText(writer, identity.Culture ?? "");
        WriteText(writer, identity.PublicKeyToken ?? "");
    }

    internal static void WriteText(BinaryWriter writer, string value)
    {
        // Length-prefixed UTF-16 code units also distinguish unpaired surrogates.
        writer.Write(value.Length);
        foreach (char character in value)
            writer.Write((ushort)character);
    }
}
