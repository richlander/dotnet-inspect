using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Typed result of acquiring an immutable assembly image snapshot.
/// </summary>
public abstract class AssemblyImageSnapshotResult
{
    private protected AssemblyImageSnapshotResult()
    {
    }

    public sealed class Ready : AssemblyImageSnapshotResult
    {
        internal Ready(AssemblyImageSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public AssemblyImageSnapshot Snapshot { get; }
    }

    public sealed class Rejected : AssemblyImageSnapshotResult
    {
        internal Rejected(CandidateOpenFailure failure)
        {
            Failure = failure;
        }

        public CandidateOpenFailure Failure { get; }
    }
}

/// <summary>
/// Immutable, non-pooled content and identity for one managed assembly image.
/// </summary>
public sealed class AssemblyImageSnapshot
{
    public const long DefaultMaxRetainedImageBytes =
        512L * 1024 * 1024;

    AssemblyImageSnapshot(
        ImmutableArray<byte> content,
        AssemblyReferenceIdentity identity,
        Guid moduleVersionId)
    {
        Content = content;
        Identity = identity;
        ModuleVersionId = moduleVersionId;
    }

    public ImmutableArray<byte> Content { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public Guid ModuleVersionId { get; }
    public long Length => Content.Length;

    /// <summary>
    /// Opens, bounds, copies, and validates an assembly image.
    /// </summary>
    /// <remarks>
    /// The image length is reserved before allocation. A failed acquisition
    /// releases any successful reservation. A successful acquisition leaves
    /// the reservation with the caller until it stops retaining the snapshot.
    /// </remarks>
    public static AssemblyImageSnapshotResult Open(
        ResolvedAssemblyReference assembly,
        Func<long, bool> tryReserveBytes,
        Action<long> releaseBytes)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(tryReserveBytes);
        ArgumentNullException.ThrowIfNull(releaseBytes);

        long reservedBytes = 0;
        try
        {
            AssemblyImageSnapshot snapshot;
            using (Stream stream = OpenSource(assembly))
            {
                long length = ReadRemainingLength(stream);

                if (length > int.MaxValue
                    || !tryReserveBytes(length))
                {
                    return Reject(
                        CandidateOpenFailureKind.ResourceBudget,
                        "The retained-image budget was exhausted.");
                }

                reservedBytes = length;
                var bytes =
                    GC.AllocateUninitializedArray<byte>((int)length);
                stream.ReadExactly(bytes);
                ImmutableArray<byte> content =
                    ImmutableCollectionsMarshal.AsImmutableArray(bytes);

                using var peReader = new PEReader(content);
                if (!peReader.HasMetadata)
                {
                    return Reject(
                        CandidateOpenFailureKind.InvalidImage,
                        "The selected image has no managed metadata.");
                }

                MetadataReader reader = peReader.GetMetadataReader();
                AssemblyReferenceIdentity identity =
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        reader);
                if (!IdentityMatches(assembly.Identity, identity))
                {
                    return Reject(
                        CandidateOpenFailureKind.InvalidImage,
                        "The opened image identity does not match its descriptor.");
                }

                snapshot = new AssemblyImageSnapshot(
                    content,
                    identity,
                    reader.GetGuid(
                        reader.GetModuleDefinition().Mvid));
            }

            var result = new AssemblyImageSnapshotResult.Ready(
                snapshot);
            reservedBytes = 0;
            return result;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ObjectDisposedException)
        {
            return Reject(
                CandidateOpenFailureKind.Unreadable,
                "The selected image could not be read.");
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Reject(
                CandidateOpenFailureKind.InvalidImage,
                "The selected image contains invalid metadata.");
        }
        finally
        {
            if (reservedBytes != 0)
                releaseBytes(reservedBytes);
        }
    }

    internal static Stream OpenSource(
        ResolvedAssemblyReference assembly)
    {
        Stream? stream = assembly.OpenRead();
        if (stream is null || !stream.CanRead)
        {
            stream?.Dispose();
            throw new IOException(
                "The assembly opener did not return a readable stream.");
        }

        return stream;
    }

    internal static long ReadRemainingLength(Stream stream)
    {
        if (!stream.CanSeek)
        {
            throw new NotSupportedException(
                "Assembly streams must support seeking for bounded inspection.");
        }

        long length = checked(stream.Length - stream.Position);
        if (length <= 0)
            throw new BadImageFormatException("The selected image is empty.");
        return length;
    }

    internal static bool IdentityMatches(
        AssemblyReferenceIdentity expected,
        AssemblyReferenceIdentity actual) =>
        StringComparer.OrdinalIgnoreCase.Equals(expected.Name, actual.Name)
        && expected.Version == actual.Version
        && CultureMatches(expected.Culture, actual.Culture)
        && StringComparer.OrdinalIgnoreCase.Equals(
            expected.PublicKeyToken ?? "",
            actual.PublicKeyToken ?? "");

    static bool CultureMatches(string? left, string? right)
    {
        static string Normalize(string? value) =>
            string.IsNullOrEmpty(value)
                || value.Equals(
                    "neutral",
                    StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : value;

        return StringComparer.OrdinalIgnoreCase.Equals(
            Normalize(left),
            Normalize(right));
    }

    static AssemblyImageSnapshotResult.Rejected Reject(
        CandidateOpenFailureKind kind,
        string detail) =>
        new(new CandidateOpenFailure(kind, detail));
}
