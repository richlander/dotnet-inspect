using System.Security.Cryptography;
using System.Collections.ObjectModel;

namespace CiChangeDetection.Planning;

/// <summary>
/// The change statuses the planner accepts. Rename detection is disabled at
/// acquisition, so a rename arrives as a deletion plus an addition.
/// </summary>
internal enum ChangeStatus
{
    Added,
    Modified,
    Deleted,
    TypeChanged,
}

/// <summary>
/// One changed path and its status. The path is bytes, not display text: it is
/// copied on construction and only ever exposed as a read-only span.
/// </summary>
internal sealed class ChangeRecord
{
    private readonly byte[] path;

    internal ChangeRecord(ChangeStatus status, ReadOnlySpan<byte> path)
    {
        Status = status;
        this.path = path.ToArray();
    }

    internal ChangeStatus Status { get; }

    internal ReadOnlySpan<byte> Path => path;

    /// <summary>
    /// Gets the canonical status byte used in the record stream.
    /// </summary>
    /// <param name="status">The change status.</param>
    /// <returns>The single ASCII status byte.</returns>
    internal static byte StatusByte(ChangeStatus status) => status switch
    {
        ChangeStatus.Added => (byte)'A',
        ChangeStatus.Modified => (byte)'M',
        ChangeStatus.Deleted => (byte)'D',
        ChangeStatus.TypeChanged => (byte)'T',
        _ => throw new PlanRefusalException(
            PlanRefusalCategory.EvidenceStatus,
            "unsupported change status"),
    };

    /// <summary>
    /// Maps a Git status byte onto an accepted status.
    /// </summary>
    /// <param name="value">The status byte read from the diff stream.</param>
    /// <returns>The accepted status.</returns>
    internal static ChangeStatus ParseStatusByte(byte value) => value switch
    {
        (byte)'A' => ChangeStatus.Added,
        (byte)'M' => ChangeStatus.Modified,
        (byte)'D' => ChangeStatus.Deleted,
        (byte)'T' => ChangeStatus.TypeChanged,
        _ => throw new PlanRefusalException(
            PlanRefusalCategory.EvidenceStatus,
            "changed-path stream contains an unsupported status byte"),
    };
}

/// <summary>
/// The acquired change set. The canonical record stream is exactly
/// <c>status-byte NUL path-bytes NUL</c> per record in acquisition order, and
/// the input digest is taken over those bytes.
/// </summary>
internal sealed class ChangeEvidence
{
    private readonly ReadOnlyCollection<ChangeRecord> records;
    private readonly byte[] canonicalBytes;

    private ChangeEvidence(ChangeRecord[] records, byte[] canonicalBytes)
    {
        this.records = Array.AsReadOnly(records);
        this.canonicalBytes = canonicalBytes;
        Sha256 = Digest.LowercaseSha256(canonicalBytes);
    }

    internal IReadOnlyList<ChangeRecord> Records => records;

    internal int RecordCount => records.Count;

    internal string Sha256 { get; }

    internal ReadOnlySpan<byte> CanonicalBytes => canonicalBytes;

    /// <summary>
    /// Creates evidence from an ordered record sequence, rejecting duplicate
    /// paths and re-deriving the canonical stream from the accepted records.
    /// </summary>
    /// <param name="source">The ordered records.</param>
    /// <returns>The immutable evidence.</returns>
    internal static ChangeEvidence Create(IEnumerable<ChangeRecord> source)
    {
        ChangeRecord[] ordered = [.. source];
        int length = 0;
        for (int index = 0; index < ordered.Length; index++)
        {
            ChangeRecord record = ordered[index];
            ChangePathRules.Validate(record.Path);
            for (int previous = 0; previous < index; previous++)
            {
                if (ordered[previous].Path.SequenceEqual(record.Path))
                {
                    throw new PlanRefusalException(
                        PlanRefusalCategory.EvidenceDuplicate,
                        "changed-path stream contains a duplicate path");
                }
            }

            length = checked(length + record.Path.Length + 3);
        }

        byte[] canonical = new byte[length];
        int offset = 0;
        foreach (ChangeRecord record in ordered)
        {
            canonical[offset++] = ChangeRecord.StatusByte(record.Status);
            canonical[offset++] = 0;
            record.Path.CopyTo(canonical.AsSpan(offset));
            offset += record.Path.Length;
            canonical[offset++] = 0;
        }

        return new ChangeEvidence(ordered, canonical);
    }
}

/// <summary>
/// Canonical relative-path rules, ported from the shell classifier's
/// <c>case</c> rejections so that path admission does not change with the
/// implementation language.
/// </summary>
internal static class ChangePathRules
{
    /// <summary>
    /// Refuses empty, absolute, trailing-separator, doubled-separator, and
    /// dot-component paths without decoding the path bytes.
    /// </summary>
    /// <param name="path">The raw path bytes.</param>
    internal static void Validate(ReadOnlySpan<byte> path)
    {
        const byte Separator = (byte)'/';
        const byte Dot = (byte)'.';
        if (path.Length == 0)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.EvidencePath,
                "changed-path stream contains an empty path");
        }

        if (path.IndexOf((byte)0) >= 0)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.EvidencePath,
                "changed-path stream contains an embedded NUL in a path");
        }

        if (path[0] == Separator || path[^1] == Separator)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.EvidencePath,
                "changed-path stream contains a non-relative path");
        }

        int start = 0;
        for (int index = 0; index <= path.Length; index++)
        {
            if (index != path.Length && path[index] != Separator)
            {
                continue;
            }

            ReadOnlySpan<byte> component = path[start..index];
            if (component.Length == 0
                || (component.Length == 1 && component[0] == Dot)
                || (component.Length == 2
                    && component[0] == Dot
                    && component[1] == Dot))
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.EvidencePath,
                    "changed-path stream contains a non-canonical path");
            }

            start = index + 1;
        }
    }
}

/// <summary>
/// Digest helpers producing the lowercase hexadecimal spelling the plan uses.
/// </summary>
internal static class Digest
{
    /// <summary>
    /// Computes the lowercase hexadecimal SHA-256 of exact bytes.
    /// </summary>
    /// <param name="value">The bytes to digest.</param>
    /// <returns>The 64-character lowercase digest.</returns>
    internal static string LowercaseSha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));
}
