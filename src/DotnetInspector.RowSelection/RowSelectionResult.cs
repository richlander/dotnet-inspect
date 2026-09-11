namespace DotnetInspector.RowSelection;

public sealed class RowWindowFailure
{
    internal RowWindowFailure(
        int stageNumber,
        int requiredPosition,
        int availableCount)
    {
        StageNumber = stageNumber;
        RequiredPosition = requiredPosition;
        AvailableCount = availableCount;
    }

    public int StageNumber { get; }

    public int RequiredPosition { get; }

    public int AvailableCount { get; }
}

public sealed class NamedRowWindowFailure
{
    internal NamedRowWindowFailure(
        RowSequenceKey key,
        RowWindowFailure failure)
    {
        Key = key;
        Failure = failure;
    }

    public RowSequenceKey Key { get; }

    public RowWindowFailure Failure { get; }
}

public sealed class RowSelectionResult<T>
{
    private RowSelectionResult(
        bool isSuccess,
        IReadOnlyList<T> values,
        RowWindowFailure? failure)
    {
        IsSuccess = isSuccess;
        Values = values;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public IReadOnlyList<T> Values { get; }

    public RowWindowFailure? Failure { get; }

    internal static RowSelectionResult<T> Success(
        IReadOnlyList<T> values) =>
        new(
            true,
            RowSelectionSnapshot.Copy(values),
            null);

    internal static RowSelectionResult<T> Failed(
        RowWindowFailure failure) =>
        new(
            false,
            RowSelectionSnapshot.Empty<T>(),
            failure);
}

public sealed class NamedRowSelectionResult<T>
{
    private NamedRowSelectionResult(
        bool isSuccess,
        IReadOnlyList<NamedRowSequence<T>> sequences,
        NamedRowWindowFailure? failure)
    {
        IsSuccess = isSuccess;
        Sequences = sequences;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public IReadOnlyList<NamedRowSequence<T>> Sequences { get; }

    public NamedRowWindowFailure? Failure { get; }

    internal static NamedRowSelectionResult<T> Success(
        IReadOnlyList<NamedRowSequence<T>> sequences) =>
        new(
            true,
            RowSelectionSnapshot.Copy(sequences),
            null);

    internal static NamedRowSelectionResult<T> Failed(
        NamedRowWindowFailure failure) =>
        new(
            false,
            RowSelectionSnapshot.Empty<NamedRowSequence<T>>(),
            failure);
}

internal static class RowSelectionSnapshot
{
    public static IReadOnlyList<T> Empty<T>() =>
        Array.AsReadOnly(Array.Empty<T>());

    public static IReadOnlyList<T> Copy<T>(
        IReadOnlyList<T> values)
    {
        var copy = new T[values.Count];
        for (int index = 0; index < values.Count; index++)
            copy[index] = values[index];
        return Own(copy);
    }

    public static IReadOnlyList<T> Own<T>(T[] values) =>
        Array.AsReadOnly(values);
}
