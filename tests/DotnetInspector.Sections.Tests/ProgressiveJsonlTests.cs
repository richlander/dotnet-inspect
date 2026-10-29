using System.Text.Json;
using DotnetInspector.InspectionContracts;

namespace DotnetInspector.Sections.Tests;

public sealed class ProgressiveJsonlTests
{
    private const string DescriptorIdentity =
        "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void Open_RejectsMismatchedFormatDescriptorAndTargetBeforeEncoding()
    {
        ProgressiveJsonlProfile profile = Profile();

        ProgressiveJsonlFailure format = Rejected(
            profile,
            new(2, DescriptorIdentity, 16));
        Assert.Equal(
            ProgressiveJsonlFailureKind.UnsupportedFormatVersion,
            format.Kind);
        Assert.Equal(2, format.Actual);
        Assert.Equal(1, format.Limit);

        ProgressiveJsonlFailure descriptor = Rejected(
            profile,
            new(1, $"sha256:{new string('1', 64)}", 16));
        Assert.Equal(
            ProgressiveJsonlFailureKind.DescriptorMismatch,
            descriptor.Kind);
        Assert.Equal(DescriptorIdentity, descriptor.ExpectedIdentity);

        ProgressiveJsonlFailure target = Rejected(
            profile,
            new(1, DescriptorIdentity, 65));
        Assert.Equal(
            ProgressiveJsonlFailureKind.InvalidTargetBatchJsonCodeUnits,
            target.Kind);
        Assert.Equal(65, target.Actual);
        Assert.Equal(64, target.Limit);
    }

    [Fact]
    public void EmptyStreamPublishesNoBatchAndZeroAccounting()
    {
        var batches = new List<ProgressiveJsonlBatch>();
        var batcher = Batcher(batches.Add);

        batcher.Flush();

        Assert.Empty(batches);
        Assert.False(batcher.HasBufferedRecords);
        Assert.Equal(
            new ProgressiveJsonlAccounting(
                1,
                DescriptorIdentity,
                0,
                0,
                0),
            batcher.Accounting);
    }

    [Fact]
    public void FirstRecordPublishesImmediatelyWithOneFinalLf()
    {
        var batches = new List<ProgressiveJsonlBatch>();
        var batcher = Batcher(batches.Add);

        ProgressiveJsonlAppendOutcome outcome =
            batcher.Append("""{"id":1}""");

        Assert.IsType<ProgressiveJsonlAppendOutcome.Appended>(outcome);
        ProgressiveJsonlBatch batch = Assert.Single(batches);
        Assert.Equal(1, batch.RecordCount);
        Assert.Equal("{\"id\":1}\n", batch.Content);
        Assert.DoesNotContain("\r", batch.Content, StringComparison.Ordinal);
        Assert.Equal(batch.Content.Length, batcher.Accounting.PublishedJsonCodeUnits);
    }

    [Fact]
    public void SoftTargetUsesCompleteRecordsAtExactAndOverBoundaries()
    {
        var batches = new List<ProgressiveJsonlBatch>();
        var batcher = Batcher(batches.Add, target: 8);
        batcher.Append(@"""0""");
        batches.Clear();

        batcher.Append(@"""a""");
        Assert.Empty(batches);
        batcher.Append(@"""b""");
        ProgressiveJsonlBatch exact = Assert.Single(batches);
        Assert.Equal("\"a\"\n\"b\"\n", exact.Content);

        batches.Clear();
        batcher.Append(@"""c""");
        batcher.Append(@"""dd""");
        ProgressiveJsonlBatch under = Assert.Single(batches);
        Assert.Equal("\"c\"\n", under.Content);
        Assert.True(batcher.HasBufferedRecords);

        batcher.Flush();
        Assert.Equal("\"dd\"\n", batches[1].Content);
    }

    [Fact]
    public void OneRecordMayExceedSoftTargetButNotHardMaximum()
    {
        var batches = new List<ProgressiveJsonlBatch>();
        var batcher = Batcher(
            batches.Add,
            target: 4,
            maximumBatchJsonCodeUnits: 16);

        ProgressiveJsonlAppendOutcome outcome =
            batcher.Append(@"""abcdef""");

        Assert.IsType<ProgressiveJsonlAppendOutcome.Appended>(outcome);
        ProgressiveJsonlBatch batch = Assert.Single(batches);
        Assert.Equal("\"abcdef\"\n", batch.Content);
        Assert.Equal(9, batch.Content.Length);
    }

    [Fact]
    public void HardOversizeFlushesEarlierRowsAndRejectsWithoutPartialRecord()
    {
        var batches = new List<ProgressiveJsonlBatch>();
        var batcher = Batcher(
            batches.Add,
            target: 12,
            maximumBatchJsonCodeUnits: 12);
        batcher.Append(@"""0""");
        batches.Clear();
        batcher.Append(@"""a""");

        var rejected =
            Assert.IsType<ProgressiveJsonlAppendOutcome.Rejected>(
                batcher.Append(@"""12345678901"""));

        ProgressiveJsonlBatch earlier = Assert.Single(batches);
        Assert.Equal("\"a\"\n", earlier.Content);
        Assert.Equal(
            ProgressiveJsonlFailureKind.RecordTooLarge,
            rejected.Failure.Kind);
        Assert.Equal(14, rejected.Failure.Actual);
        Assert.Equal(12, rejected.Failure.Limit);
        Assert.DoesNotContain("12345678901", earlier.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordCountFlushesIndependentlyOfCodeUnitTarget()
    {
        var batches = new List<ProgressiveJsonlBatch>();
        var batcher = Batcher(
            batches.Add,
            target: 64,
            maximumRecordsPerBatch: 2);
        batcher.Append(@"""0""");
        batches.Clear();

        batcher.Append(@"""a""");
        batcher.Append(@"""b""");

        ProgressiveJsonlBatch batch = Assert.Single(batches);
        Assert.Equal(2, batch.RecordCount);
        Assert.Equal("\"a\"\n\"b\"\n", batch.Content);
    }

    [Fact]
    public void Utf16AccountingIncludesEscapesAndSurrogatePairs()
    {
        string record = JsonSerializer.Serialize(
            "BMP-\u00E9 astral-\U0001F600 line\nbreak");
        var batches = new List<ProgressiveJsonlBatch>();
        var batcher = Batcher(batches.Add);

        batcher.Append(record);

        ProgressiveJsonlBatch batch = Assert.Single(batches);
        Assert.Equal(record.Length + 1, batch.Content.Length);
        Assert.Equal(
            record.Length + 1,
            batcher.Accounting.PublishedJsonCodeUnits);
        Assert.Equal(1, batch.Content.Count(character => character == '\n'));
        Assert.EndsWith("\\nbreak\"\n", batch.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" null")]
    [InlineData("null ")]
    [InlineData("null\n")]
    [InlineData("null\r")]
    public void InvalidRecordContentIsRejectedWithoutPublication(string record)
    {
        var batches = new List<ProgressiveJsonlBatch>();
        var batcher = Batcher(batches.Add);

        var rejected =
            Assert.IsType<ProgressiveJsonlAppendOutcome.Rejected>(
                batcher.Append(record));

        Assert.Equal(
            ProgressiveJsonlFailureKind.InvalidRecordContent,
            rejected.Failure.Kind);
        Assert.Empty(batches);
        Assert.Equal(0, batcher.Accounting.PublishedRecordCount);
    }

    [Fact]
    public void WriterFailureIsReturnedWithoutPublication()
    {
        var batches = new List<ProgressiveJsonlBatch>();
        var batcher = new ProgressiveJsonlBatcher<string>(
            Session(),
            new FailedWriter(),
            batches.Add);

        var rejected =
            Assert.IsType<ProgressiveJsonlAppendOutcome.Rejected>(
                batcher.Append("ignored"));

        Assert.Equal(
            ProgressiveJsonlFailureKind.RecordSerializationFailed,
            rejected.Failure.Kind);
        Assert.Empty(batches);
    }

    [Fact]
    public void CallbackFailureLeavesRejectedBatchOutOfAccounting()
    {
        var batcher = Batcher(
            _ => throw new InvalidOperationException("callback failed"));

        Assert.Throws<InvalidOperationException>(
            () => batcher.Append(@"""first"""));

        Assert.True(batcher.HasBufferedRecords);
        Assert.Equal(0, batcher.Accounting.PublishedRecordCount);
        Assert.Equal(0, batcher.Accounting.PublishedBatchCount);
        Assert.Equal(0, batcher.Accounting.PublishedJsonCodeUnits);
    }

    private static ProgressiveJsonlBatcher<string> Batcher(
        Action<ProgressiveJsonlBatch> publish,
        int target = 32,
        int maximumBatchJsonCodeUnits = 64,
        int maximumRecordsPerBatch = 20) =>
        new(
            Session(
                target,
                maximumBatchJsonCodeUnits,
                maximumRecordsPerBatch),
            new PassThroughWriter(),
            publish);

    private static ProgressiveJsonlSession Session(
        int target = 32,
        int maximumBatchJsonCodeUnits = 64,
        int maximumRecordsPerBatch = 20) =>
        Assert.IsType<ProgressiveJsonlSessionOutcome.Accepted>(
            ProgressiveJsonlSession.Open(
                Profile(
                    maximumBatchJsonCodeUnits,
                    maximumRecordsPerBatch),
                new(1, DescriptorIdentity, target)))
            .Session;

    private static ProgressiveJsonlProfile Profile(
        int maximumBatchJsonCodeUnits = 64,
        int maximumRecordsPerBatch = 20) =>
        new(
            1,
            "test.row",
            ProgressiveJsonlDirection.Serialize,
            DescriptorIdentity,
            maximumBatchJsonCodeUnits,
            maximumRecordsPerBatch,
            ProgressiveJsonlLineTerminator.Lf);

    private static ProgressiveJsonlFailure Rejected(
        ProgressiveJsonlProfile profile,
        ProgressiveJsonlRequest request) =>
        Assert.IsType<ProgressiveJsonlSessionOutcome.Rejected>(
            ProgressiveJsonlSession.Open(profile, request))
        .Failure;

    private sealed class PassThroughWriter
        : IProgressiveJsonlRecordWriter<string>
    {
        public ProgressiveJsonlRecordEncodingOutcome Encode(string row) =>
            new ProgressiveJsonlRecordEncodingOutcome.Encoded(row);
    }

    private sealed class FailedWriter
        : IProgressiveJsonlRecordWriter<string>
    {
        public ProgressiveJsonlRecordEncodingOutcome Encode(string row) =>
            new ProgressiveJsonlRecordEncodingOutcome.Failed(
                new(
                    ProgressiveJsonlFailureKind.RecordSerializationFailed));
    }
}
