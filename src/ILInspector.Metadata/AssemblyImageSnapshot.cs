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
        Guid moduleVersionId,
        AssemblyAcquisitionRegistration registration,
        DateTime? lastWriteTimeUtc)
    {
        Content = content;
        Identity = identity;
        ModuleVersionId = moduleVersionId;
        Registration = registration;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    public ImmutableArray<byte> Content { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public Guid ModuleVersionId { get; }
    public AssemblyAcquisitionRegistration Registration { get; }
    /// <summary>
    /// Last write time captured from the source that supplied
    /// <see cref="Content"/>, when available.
    /// </summary>
    public DateTime? LastWriteTimeUtc { get; }
    public long Length => Content.Length;

    /// <summary>
    /// Returns the same acquisition descriptor backed by this immutable snapshot instead of its
    /// original content source.
    /// </summary>
    public ResolvedAssemblyReference RetainAssemblyReference(
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!ReferenceEquals(assembly.Registration, Registration)
            || !IdentityMatches(
                assembly.Identity,
                Identity))
        {
            throw new ArgumentException(
                "The assembly descriptor does not own this snapshot.",
                nameof(assembly));
        }

        byte[] bytes = ImmutableCollectionsMarshal.AsArray(Content)!;
        return assembly.WithOpenRead(
            () => new MemoryStream(bytes, writable: false),
            LastWriteTimeUtc);
    }

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
            Stream stream = OpenSource(assembly);
            Exception? primaryFailure = null;
            bool rejectionEstablished = false;
            AssemblyImageSnapshotResult RejectOpenedImage(
                CandidateOpenFailureKind kind,
                string detail,
                MetadataRootMalformedReason? metadataRootReason = null)
            {
                rejectionEstablished = true;
                return Reject(kind, detail, metadataRootReason);
            }

            try
            {
                DateTime? lastWriteTimeUtc = stream is FileStream fileStream
                    ? File.GetLastWriteTimeUtc(fileStream.SafeFileHandle)
                    : assembly.LastWriteTimeUtc;
                long length = ReadRemainingLength(stream);

                if (length > int.MaxValue
                    || !tryReserveBytes(length))
                {
                    return RejectOpenedImage(
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
                if (!MetadataFormatAdmission.AdmitImage(peReader))
                {
                    return RejectOpenedImage(
                        CandidateOpenFailureKind.InvalidImage,
                        "The selected image has no managed metadata.");
                }

                MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);
                assembly.ValidateArtifactContent(peReader);
                AssemblyReferenceIdentity identity =
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        reader);
                if (!IdentityMatches(assembly.Identity, identity))
                {
                    return RejectOpenedImage(
                        CandidateOpenFailureKind.InvalidImage,
                        "The opened image identity does not match its descriptor.");
                }

                snapshot = new AssemblyImageSnapshot(
                    content,
                    identity,
                    reader.GetGuid(
                        reader.GetModuleDefinition().Mvid),
                    assembly.Registration,
                    lastWriteTimeUtc);
            }
            catch (Exception ex)
            {
                primaryFailure = ex;
                throw;
            }
            finally
            {
                if (primaryFailure is not null)
                {
                    OwnedResourceCleanup.DisposeAfterFailure(
                        stream,
                        primaryFailure);
                }
                else if (rejectionEstablished)
                {
                    OwnedResourceCleanup.DisposeWithoutReplacingOutcome(
                        stream);
                }
                else
                {
                    stream.Dispose();
                }
            }

            var result = new AssemblyImageSnapshotResult.Ready(
                snapshot);
            reservedBytes = 0;
            return result;
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Reject(
                CandidateOpenFailureKind.UnsupportedMetadataFormat,
                "The selected image uses an unsupported metadata format.");
        }
        catch (MalformedMetadataRootException ex)
        {
            return Reject(
                CandidateOpenFailureKind.InvalidImage,
                $"The selected image has a malformed metadata root ({ex.Reason}).",
                ex.Reason);
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

    /// <summary>
    /// Validates and retains caller-owned immutable assembly content without
    /// copying it.
    /// </summary>
    public static AssemblyImageSnapshotResult FromRetainedContent(
        ResolvedAssemblyReference assembly,
        ImmutableArray<byte> content,
        DateTime? lastWriteTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (content.IsDefaultOrEmpty)
            return Reject(
                CandidateOpenFailureKind.InvalidImage,
                "The selected image is empty.");

        try
        {
            using var peReader = new PEReader(content);
            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                return Reject(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected image has no managed metadata.");
            }

            MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);
            assembly.ValidateArtifactContent(peReader);
            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            if (!IdentityMatches(assembly.Identity, identity))
            {
                return Reject(
                    CandidateOpenFailureKind.InvalidImage,
                    "The retained image identity does not match its descriptor.");
            }

            return new AssemblyImageSnapshotResult.Ready(
                new AssemblyImageSnapshot(
                    content,
                    identity,
                    reader.GetGuid(reader.GetModuleDefinition().Mvid),
                    assembly.Registration,
                    lastWriteTimeUtc ?? assembly.LastWriteTimeUtc));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Reject(
                CandidateOpenFailureKind.UnsupportedMetadataFormat,
                "The retained image uses an unsupported metadata format.");
        }
        catch (MalformedMetadataRootException ex)
        {
            return Reject(
                CandidateOpenFailureKind.InvalidImage,
                $"The retained image has a malformed metadata root ({ex.Reason}).",
                ex.Reason);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Reject(
                CandidateOpenFailureKind.InvalidImage,
                "The retained image contains invalid metadata.");
        }
    }

    internal static Stream OpenSource(
        ResolvedAssemblyReference assembly)
    {
        Stream? stream = null;
        try
        {
            stream = assembly.OpenRead();
            if (stream is null || !stream.CanRead)
            {
                throw new IOException(
                    "The assembly opener did not return a readable stream.");
            }

            return stream;
        }
        catch (Exception ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(stream, ex);
            throw;
        }
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
        expected.IsEquivalentTo(actual);

    static AssemblyImageSnapshotResult.Rejected Reject(
        CandidateOpenFailureKind kind,
        string detail,
        MetadataRootMalformedReason? metadataRootReason = null) =>
        new(new CandidateOpenFailure(kind, detail)
        {
            MetadataRootReason = metadataRootReason,
        });
}
