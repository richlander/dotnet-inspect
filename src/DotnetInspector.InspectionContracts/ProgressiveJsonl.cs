using System.Text;
using System.Text.Json.Serialization;

namespace DotnetInspector.InspectionContracts;

/// <summary>The authenticated JSON direction encoded by one profile.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProgressiveJsonlDirection>))]
public enum ProgressiveJsonlDirection
{
    [JsonStringEnumMemberName("serialize")]
    Serialize,
}

/// <summary>The line terminator required by one Progressive JSONL profile.</summary>
[JsonConverter(
    typeof(JsonStringEnumConverter<ProgressiveJsonlLineTerminator>))]
public enum ProgressiveJsonlLineTerminator
{
    [JsonStringEnumMemberName("LF")]
    Lf,
}

/// <summary>One product-authored Progressive JSONL encoding profile.</summary>
public sealed record ProgressiveJsonlProfile
{
    public ProgressiveJsonlProfile(
        int formatVersion,
        string contractIdentity,
        ProgressiveJsonlDirection direction,
        string descriptorIdentity,
        int maximumBatchJsonCodeUnits,
        int maximumRecordsPerBatch,
        ProgressiveJsonlLineTerminator lineTerminator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(formatVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractIdentity);
        if (direction != ProgressiveJsonlDirection.Serialize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "Progressive JSONL requires the Serialize direction.");
        }
        ValidateDigest(descriptorIdentity, nameof(descriptorIdentity));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumBatchJsonCodeUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumRecordsPerBatch);
        if (lineTerminator != ProgressiveJsonlLineTerminator.Lf)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineTerminator),
                lineTerminator,
                "Progressive JSONL requires LF line termination.");
        }

        FormatVersion = formatVersion;
        ContractIdentity = contractIdentity;
        Direction = direction;
        DescriptorIdentity = descriptorIdentity;
        MaximumBatchJsonCodeUnits = maximumBatchJsonCodeUnits;
        MaximumRecordsPerBatch = maximumRecordsPerBatch;
        LineTerminator = lineTerminator;
    }

    public int FormatVersion { get; }

    public string ContractIdentity { get; }

    public ProgressiveJsonlDirection Direction { get; }

    public string DescriptorIdentity { get; }

    public int MaximumBatchJsonCodeUnits { get; }

    public int MaximumRecordsPerBatch { get; }

    public ProgressiveJsonlLineTerminator LineTerminator { get; }

    internal static void ValidateDigest(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 71
            || !value.StartsWith("sha256:", StringComparison.Ordinal)
            || value.AsSpan(7).IndexOfAnyExcept(
                "0123456789abcdef".AsSpan()) >= 0)
        {
            throw new ArgumentException(
                "An exact identity must be 'sha256:' followed by "
                    + "64 lowercase hexadecimal digits.",
                parameterName);
        }
    }
}

/// <summary>One consumer's exact Progressive JSONL encoding request.</summary>
public sealed record ProgressiveJsonlRequest(
    int ExpectedFormatVersion,
    string ExpectedDescriptorIdentity,
    int TargetBatchJsonCodeUnits);

/// <summary>One nonempty batch of complete LF-terminated JSON records.</summary>
public sealed record ProgressiveJsonlBatch
{
    public ProgressiveJsonlBatch(int recordCount, string content)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordCount);
        ArgumentException.ThrowIfNullOrEmpty(content);
        if (content[^1] != '\n')
        {
            throw new ArgumentException(
                "A Progressive JSONL batch must end in LF.",
                nameof(content));
        }

        RecordCount = recordCount;
        Content = content;
    }

    public int RecordCount { get; }

    public string Content { get; }
}

/// <summary>Final encoding accounting for one Progressive JSONL stream.</summary>
public sealed record ProgressiveJsonlAccounting(
    int FormatVersion,
    string DescriptorIdentity,
    long PublishedRecordCount,
    long PublishedBatchCount,
    long PublishedJsonCodeUnits);

/// <summary>Why Progressive JSONL encoding could not continue.</summary>
public enum ProgressiveJsonlFailureKind
{
    UnsupportedFormatVersion,
    DescriptorMismatch,
    InvalidTargetBatchJsonCodeUnits,
    RecordSerializationFailed,
    InvalidRecordContent,
    RecordTooLarge,
}

/// <summary>Typed Progressive JSONL failure evidence.</summary>
public sealed record ProgressiveJsonlFailure(
    ProgressiveJsonlFailureKind Kind,
    long? Actual = null,
    long? Limit = null,
    string? ExpectedIdentity = null,
    string? ActualIdentity = null);

/// <summary>The result of validating one request against one exact profile.</summary>
public abstract record ProgressiveJsonlSessionOutcome
{
    private ProgressiveJsonlSessionOutcome()
    {
    }

    public sealed record Accepted(ProgressiveJsonlSession Session)
        : ProgressiveJsonlSessionOutcome;

    public sealed record Rejected(ProgressiveJsonlFailure Failure)
        : ProgressiveJsonlSessionOutcome;
}

/// <summary>
/// One validated association between an exact Progressive JSONL profile and a
/// consumer's soft batch target.
/// </summary>
public sealed record ProgressiveJsonlSession
{
    private ProgressiveJsonlSession(
        ProgressiveJsonlProfile profile,
        int targetBatchJsonCodeUnits)
    {
        Profile = profile;
        TargetBatchJsonCodeUnits = targetBatchJsonCodeUnits;
    }

    public ProgressiveJsonlProfile Profile { get; }

    public int TargetBatchJsonCodeUnits { get; }

    public static ProgressiveJsonlSessionOutcome Open(
        ProgressiveJsonlProfile profile,
        ProgressiveJsonlRequest request)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExpectedFormatVersion != profile.FormatVersion)
        {
            return new ProgressiveJsonlSessionOutcome.Rejected(
                new(
                    ProgressiveJsonlFailureKind.UnsupportedFormatVersion,
                    Actual: request.ExpectedFormatVersion,
                    Limit: profile.FormatVersion));
        }

        if (!string.Equals(
                request.ExpectedDescriptorIdentity,
                profile.DescriptorIdentity,
                StringComparison.Ordinal))
        {
            return new ProgressiveJsonlSessionOutcome.Rejected(
                new(
                    ProgressiveJsonlFailureKind.DescriptorMismatch,
                    ExpectedIdentity: profile.DescriptorIdentity,
                    ActualIdentity: request.ExpectedDescriptorIdentity));
        }

        if (request.TargetBatchJsonCodeUnits <= 0
            || request.TargetBatchJsonCodeUnits
                > profile.MaximumBatchJsonCodeUnits)
        {
            return new ProgressiveJsonlSessionOutcome.Rejected(
                new(
                    ProgressiveJsonlFailureKind
                        .InvalidTargetBatchJsonCodeUnits,
                    Actual: request.TargetBatchJsonCodeUnits,
                    Limit: profile.MaximumBatchJsonCodeUnits));
        }

        return new ProgressiveJsonlSessionOutcome.Accepted(
            new(profile, request.TargetBatchJsonCodeUnits));
    }
}

/// <summary>The atomic result of encoding one complete JSON record.</summary>
public abstract record ProgressiveJsonlRecordEncodingOutcome
{
    private ProgressiveJsonlRecordEncodingOutcome()
    {
    }

    public sealed record Encoded(string Content)
        : ProgressiveJsonlRecordEncodingOutcome;

    public sealed record Failed(ProgressiveJsonlFailure Failure)
        : ProgressiveJsonlRecordEncodingOutcome;
}

/// <summary>Writes one complete JSON value without line framing.</summary>
public interface IProgressiveJsonlRecordWriter<in TRow>
{
    ProgressiveJsonlRecordEncodingOutcome Encode(TRow row);
}

/// <summary>The result of admitting one row to a Progressive JSONL stream.</summary>
public abstract record ProgressiveJsonlAppendOutcome
{
    private ProgressiveJsonlAppendOutcome()
    {
    }

    public sealed record Appended : ProgressiveJsonlAppendOutcome;

    public sealed record Rejected(ProgressiveJsonlFailure Failure)
        : ProgressiveJsonlAppendOutcome;
}

/// <summary>
/// Forms bounded batches of complete JSON records without owning stream
/// ordering, cancellation, credit, or terminal semantics.
/// </summary>
public sealed class ProgressiveJsonlBatcher<TRow>
{
    private readonly ProgressiveJsonlSession _session;
    private readonly IProgressiveJsonlRecordWriter<TRow> _writer;
    private readonly Action<ProgressiveJsonlBatch> _publish;
    private readonly StringBuilder _buffer = new();
    private int _bufferedRecordCount;
    private long _publishedRecordCount;
    private long _publishedBatchCount;
    private long _publishedJsonCodeUnits;

    public ProgressiveJsonlBatcher(
        ProgressiveJsonlSession session,
        IProgressiveJsonlRecordWriter<TRow> writer,
        Action<ProgressiveJsonlBatch> publish)
    {
        _session = session
            ?? throw new ArgumentNullException(nameof(session));
        _writer = writer
            ?? throw new ArgumentNullException(nameof(writer));
        _publish = publish
            ?? throw new ArgumentNullException(nameof(publish));
    }

    public bool HasBufferedRecords => _bufferedRecordCount != 0;

    public ProgressiveJsonlAccounting Accounting =>
        new(
            _session.Profile.FormatVersion,
            _session.Profile.DescriptorIdentity,
            _publishedRecordCount,
            _publishedBatchCount,
            _publishedJsonCodeUnits);

    public ProgressiveJsonlAppendOutcome Append(TRow row)
    {
        ProgressiveJsonlRecordEncodingOutcome encoded =
            _writer.Encode(row);
        if (encoded is ProgressiveJsonlRecordEncodingOutcome.Failed failed)
            return RejectAfterFlush(failed.Failure);

        string record =
            ((ProgressiveJsonlRecordEncodingOutcome.Encoded)encoded).Content;
        if (!IsValidRecord(record))
        {
            return RejectAfterFlush(
                new(ProgressiveJsonlFailureKind.InvalidRecordContent));
        }

        long framedLength = checked((long)record.Length + 1);
        ProgressiveJsonlProfile profile = _session.Profile;
        if (framedLength > profile.MaximumBatchJsonCodeUnits)
        {
            return RejectAfterFlush(
                new(
                    ProgressiveJsonlFailureKind.RecordTooLarge,
                    framedLength,
                    profile.MaximumBatchJsonCodeUnits));
        }

        long combinedLength = checked(_buffer.Length + framedLength);
        if (_bufferedRecordCount > 0
            && (combinedLength > _session.TargetBatchJsonCodeUnits
                || combinedLength > profile.MaximumBatchJsonCodeUnits
                || _bufferedRecordCount
                    >= profile.MaximumRecordsPerBatch))
        {
            Flush();
        }

        _buffer.Append(record);
        _buffer.Append('\n');
        _bufferedRecordCount++;

        if (_publishedRecordCount == 0
            || _buffer.Length >= _session.TargetBatchJsonCodeUnits
            || _bufferedRecordCount >= profile.MaximumRecordsPerBatch)
        {
            Flush();
        }

        return new ProgressiveJsonlAppendOutcome.Appended();
    }

    private ProgressiveJsonlAppendOutcome.Rejected RejectAfterFlush(
        ProgressiveJsonlFailure failure)
    {
        Flush();
        return new(failure);
    }

    public void Flush()
    {
        if (_bufferedRecordCount == 0)
            return;

        var batch = new ProgressiveJsonlBatch(
            _bufferedRecordCount,
            _buffer.ToString());
        _publish(batch);
        _publishedRecordCount = checked(
            _publishedRecordCount + batch.RecordCount);
        _publishedBatchCount = checked(_publishedBatchCount + 1);
        _publishedJsonCodeUnits = checked(
            _publishedJsonCodeUnits + batch.Content.Length);
        _buffer.Clear();
        _bufferedRecordCount = 0;
    }

    private static bool IsValidRecord(string? record) =>
        !string.IsNullOrEmpty(record)
        && record[0] != '\uFEFF'
        && !char.IsWhiteSpace(record[0])
        && !char.IsWhiteSpace(record[^1])
        && record.AsSpan().IndexOfAny('\r', '\n') < 0;
}
