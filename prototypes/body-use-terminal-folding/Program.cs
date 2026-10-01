using System.Diagnostics;
using System.Runtime.CompilerServices;

enum OperandKind
{
    Rejected,
    Admitted,
    Failed,
}

enum Completion
{
    Exhausted,
    Settled,
    Failed,
}

readonly record struct Operand(OperandKind Kind, int OccurrenceCount)
{
    public static Operand Rejected() => new(OperandKind.Rejected, 0);

    public static Operand Admitted(int occurrenceCount) =>
        new(OperandKind.Admitted, occurrenceCount);

    public static Operand Failed() => new(OperandKind.Failed, 0);
}

readonly record struct TerminalResult(
    bool Succeeded,
    int Value,
    int Checksum,
    Completion Completion,
    int OperandsVisited,
    int RowsConstructed)
{
    public static TerminalResult Success(
        int value,
        int checksum,
        Completion completion,
        int operandsVisited,
        int rowsConstructed) =>
        new(
            true,
            value,
            checksum,
            completion,
            operandsVisited,
            rowsConstructed);

    public static TerminalResult Failure(
        int operandsVisited,
        int rowsConstructed) =>
        new(
            false,
            0,
            0,
            Completion.Failed,
            operandsVisited,
            rowsConstructed);
}

readonly record struct CombinedResult(
    TerminalResult Exists,
    TerminalResult Count,
    TerminalResult Rows,
    int PhysicalOperandsVisited);

readonly record struct FoldStep(bool Settled, int RowsConstructed);

interface ITerminalFold<TState>
{
    TState Seed();

    FoldStep Admit(ref TState state, int operandIndex, int occurrenceCount);

    int Value(ref TState state);

    int Checksum(ref TState state);
}

readonly struct ExistsFold : ITerminalFold<int>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Seed() => 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FoldStep Admit(
        ref int state,
        int operandIndex,
        int occurrenceCount)
    {
        if (occurrenceCount <= 0)
            return default;
        state = 1;
        return new(true, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Value(ref int state) => state;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Checksum(ref int state) => state;
}

readonly struct CountFold : ITerminalFold<int>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Seed() => 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FoldStep Admit(
        ref int state,
        int operandIndex,
        int occurrenceCount)
    {
        state += occurrenceCount;
        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Value(ref int state) => state;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Checksum(ref int state) => state;
}

readonly struct RowsFold : ITerminalFold<List<int>>
{
    public List<int> Seed() => [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FoldStep Admit(
        ref List<int> state,
        int operandIndex,
        int occurrenceCount)
    {
        for (int occurrence = 0; occurrence < occurrenceCount; occurrence++)
            state.Add(RowValue(operandIndex, occurrence));
        return new(false, occurrenceCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Value(ref List<int> state) => state.Count;

    public int Checksum(ref List<int> state)
    {
        int checksum = 0;
        foreach (int row in state)
            checksum = unchecked((checksum * 31) + row);
        return checksum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int RowValue(int operandIndex, int occurrence) =>
        unchecked((operandIndex * 397) + occurrence);
}

enum Terminal
{
    Exists,
    Count,
    Rows,
}

static class Folding
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TerminalResult DirectExists(ReadOnlySpan<Operand> source)
    {
        for (int index = 0; index < source.Length; index++)
        {
            Operand operand = source[index];
            if (operand.Kind == OperandKind.Failed)
                return TerminalResult.Failure(index + 1, 0);
            if (operand.Kind == OperandKind.Admitted
                && operand.OccurrenceCount > 0)
            {
                return TerminalResult.Success(
                    1,
                    1,
                    Completion.Settled,
                    index + 1,
                    0);
            }
        }
        return TerminalResult.Success(
            0,
            0,
            Completion.Exhausted,
            source.Length,
            0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TerminalResult DirectCount(ReadOnlySpan<Operand> source)
    {
        int count = 0;
        for (int index = 0; index < source.Length; index++)
        {
            Operand operand = source[index];
            if (operand.Kind == OperandKind.Failed)
                return TerminalResult.Failure(index + 1, 0);
            if (operand.Kind == OperandKind.Admitted)
                count += operand.OccurrenceCount;
        }
        return TerminalResult.Success(
            count,
            count,
            Completion.Exhausted,
            source.Length,
            0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TerminalResult DirectRows(ReadOnlySpan<Operand> source)
    {
        List<int> rows = [];
        for (int index = 0; index < source.Length; index++)
        {
            Operand operand = source[index];
            if (operand.Kind == OperandKind.Failed)
                return TerminalResult.Failure(index + 1, rows.Count);
            if (operand.Kind != OperandKind.Admitted)
                continue;
            for (int occurrence = 0;
                occurrence < operand.OccurrenceCount;
                occurrence++)
            {
                rows.Add(unchecked((index * 397) + occurrence));
            }
        }

        int checksum = 0;
        foreach (int row in rows)
            checksum = unchecked((checksum * 31) + row);
        return TerminalResult.Success(
            rows.Count,
            checksum,
            Completion.Exhausted,
            source.Length,
            rows.Count);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TerminalResult StaticExists(ReadOnlySpan<Operand> source) =>
        Fold<ExistsFold, int>(source);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TerminalResult StaticCount(ReadOnlySpan<Operand> source) =>
        Fold<CountFold, int>(source);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TerminalResult StaticRows(ReadOnlySpan<Operand> source) =>
        Fold<RowsFold, List<int>>(source);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TerminalResult Planned(
        Terminal terminal,
        ReadOnlySpan<Operand> source) =>
        terminal switch
        {
            Terminal.Exists => Fold<ExistsFold, int>(source),
            Terminal.Count => Fold<CountFold, int>(source),
            Terminal.Rows => Fold<RowsFold, List<int>>(source),
            _ => throw new ArgumentOutOfRangeException(nameof(terminal)),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static TerminalResult Fold<TFold, TState>(
        ReadOnlySpan<Operand> source)
        where TFold : struct, ITerminalFold<TState>
    {
        TFold fold = default;
        TState state = fold.Seed();
        int rowsConstructed = 0;
        for (int index = 0; index < source.Length; index++)
        {
            Operand operand = source[index];
            if (operand.Kind == OperandKind.Failed)
            {
                return TerminalResult.Failure(
                    index + 1,
                    rowsConstructed);
            }
            if (operand.Kind != OperandKind.Admitted)
                continue;
            FoldStep step = fold.Admit(
                ref state,
                index,
                operand.OccurrenceCount);
            rowsConstructed += step.RowsConstructed;
            if (step.Settled)
            {
                return TerminalResult.Success(
                    fold.Value(ref state),
                    fold.Checksum(ref state),
                    Completion.Settled,
                    index + 1,
                    rowsConstructed);
            }
        }
        return TerminalResult.Success(
            fold.Value(ref state),
            fold.Checksum(ref state),
            Completion.Exhausted,
            source.Length,
            rowsConstructed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CombinedResult DirectIndependent(
        ReadOnlySpan<Operand> source)
    {
        TerminalResult exists = DirectExists(source);
        TerminalResult count = DirectCount(source);
        TerminalResult rows = DirectRows(source);
        return new(
            exists,
            count,
            rows,
            exists.OperandsVisited
                + count.OperandsVisited
                + rows.OperandsVisited);
    }

    public static CombinedResult StaticIndependent(
        ReadOnlySpan<Operand> source)
    {
        TerminalResult exists = StaticExists(source);
        TerminalResult count = StaticCount(source);
        TerminalResult rows = StaticRows(source);
        return new(
            exists,
            count,
            rows,
            exists.OperandsVisited
                + count.OperandsVisited
                + rows.OperandsVisited);
    }

    public static CombinedResult PlannedIndependent(
        ReadOnlySpan<Operand> source)
    {
        TerminalResult exists = Planned(Terminal.Exists, source);
        TerminalResult count = Planned(Terminal.Count, source);
        TerminalResult rows = Planned(Terminal.Rows, source);
        return new(
            exists,
            count,
            rows,
            exists.OperandsVisited
                + count.OperandsVisited
                + rows.OperandsVisited);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CombinedResult Fused(ReadOnlySpan<Operand> source)
    {
        bool existsSettled = false;
        int existsVisited = 0;
        int count = 0;
        List<int> rows = [];

        for (int index = 0; index < source.Length; index++)
        {
            Operand operand = source[index];
            if (!existsSettled)
                existsVisited++;
            if (operand.Kind == OperandKind.Failed)
            {
                TerminalResult existsFailure = existsSettled
                    ? TerminalResult.Success(
                        1,
                        1,
                        Completion.Settled,
                        existsVisited,
                        0)
                    : TerminalResult.Failure(existsVisited, 0);
                return new(
                    existsFailure,
                    TerminalResult.Failure(index + 1, 0),
                    TerminalResult.Failure(index + 1, rows.Count),
                    index + 1);
            }
            if (operand.Kind != OperandKind.Admitted)
                continue;

            if (!existsSettled && operand.OccurrenceCount > 0)
                existsSettled = true;
            count += operand.OccurrenceCount;
            for (int occurrence = 0;
                occurrence < operand.OccurrenceCount;
                occurrence++)
            {
                rows.Add(unchecked((index * 397) + occurrence));
            }
        }

        int checksum = 0;
        foreach (int row in rows)
            checksum = unchecked((checksum * 31) + row);
        TerminalResult exists = TerminalResult.Success(
            existsSettled ? 1 : 0,
            existsSettled ? 1 : 0,
            existsSettled
                ? Completion.Settled
                : Completion.Exhausted,
            existsVisited,
            0);
        return new(
            exists,
            TerminalResult.Success(
                count,
                count,
                Completion.Exhausted,
                source.Length,
                0),
            TerminalResult.Success(
                rows.Count,
                checksum,
                Completion.Exhausted,
                source.Length,
                rows.Count),
            source.Length);
    }
}

static class Program
{
    static int s_sink;

    public static int Main(string[] args)
    {
        string command = args.Length == 0 ? "check" : args[0];
        return command switch
        {
            "check" => Check(),
            "time" => Time(),
            _ => Usage(),
        };
    }

    static int Check()
    {
        Operand[] pathological =
        [
            Operand.Rejected(),
            Operand.Admitted(2),
            Operand.Failed(),
            Operand.Admitted(1),
        ];
        Operand[] complete = CreateSource(512);

        CheckSource(pathological);
        CheckSource(complete);

        CombinedResult fused = Folding.Fused(pathological);
        Console.WriteLine(
            $"pathological physical={fused.PhysicalOperandsVisited} "
            + $"exists={Format(fused.Exists)} "
            + $"count={Format(fused.Count)} "
            + $"rows={Format(fused.Rows)}");
        return 0;
    }

    static int Time()
    {
        Operand[] source = CreateSource(512);
        Operand[] lateExists = CreateLateExistsSource(512, 256);
        const int ScalarIterations = 200_000;
        const int RowsIterations = 10_000;

        Console.WriteLine(
            "variant\tterminal\tns/op\tB/op\tvisited\trows");
        Measure(
            "direct",
            "exists-early",
            () => Folding.DirectExists(source),
            ScalarIterations);
        Measure(
            "static-fold",
            "exists-early",
            () => Folding.StaticExists(source),
            ScalarIterations);
        Measure(
            "planned-fold",
            "exists-early",
            () => Folding.Planned(Terminal.Exists, source),
            ScalarIterations);
        Measure(
            "direct",
            "exists-late",
            () => Folding.DirectExists(lateExists),
            ScalarIterations);
        Measure(
            "static-fold",
            "exists-late",
            () => Folding.StaticExists(lateExists),
            ScalarIterations);
        Measure(
            "planned-fold",
            "exists-late",
            () => Folding.Planned(Terminal.Exists, lateExists),
            ScalarIterations);
        Measure(
            "direct",
            "count",
            () => Folding.DirectCount(source),
            ScalarIterations);
        Measure(
            "static-fold",
            "count",
            () => Folding.StaticCount(source),
            ScalarIterations);
        Measure(
            "planned-fold",
            "count",
            () => Folding.Planned(Terminal.Count, source),
            ScalarIterations);
        Measure(
            "direct",
            "rows",
            () => Folding.DirectRows(source),
            RowsIterations);
        Measure(
            "static-fold",
            "rows",
            () => Folding.StaticRows(source),
            RowsIterations);
        Measure(
            "planned-fold",
            "rows",
            () => Folding.Planned(Terminal.Rows, source),
            RowsIterations);
        MeasureCombined(
            "direct-independent",
            () => Folding.DirectIndependent(source),
            RowsIterations);
        MeasureCombined(
            "static-independent",
            () => Folding.StaticIndependent(source),
            RowsIterations);
        MeasureCombined(
            "planned-independent",
            () => Folding.PlannedIndependent(source),
            RowsIterations);
        MeasureCombined(
            "fused",
            () => Folding.Fused(source),
            RowsIterations);
        return 0;
    }

    static void CheckSource(Operand[] source)
    {
        AssertEqual(
            Folding.DirectExists(source),
            Folding.StaticExists(source),
            "static Exists");
        AssertEqual(
            Folding.DirectExists(source),
            Folding.Planned(Terminal.Exists, source),
            "planned Exists");
        AssertEqual(
            Folding.DirectCount(source),
            Folding.StaticCount(source),
            "static Count");
        AssertEqual(
            Folding.DirectCount(source),
            Folding.Planned(Terminal.Count, source),
            "planned Count");
        AssertEqual(
            Folding.DirectRows(source),
            Folding.StaticRows(source),
            "static Rows");
        AssertEqual(
            Folding.DirectRows(source),
            Folding.Planned(Terminal.Rows, source),
            "planned Rows");

        CombinedResult fused = Folding.Fused(source);
        AssertEqual(
            Folding.DirectExists(source),
            fused.Exists,
            "fused Exists");
        AssertEqual(
            Folding.DirectCount(source),
            fused.Count,
            "fused Count");
        AssertEqual(
            Folding.DirectRows(source),
            fused.Rows,
            "fused Rows");
    }

    static Operand[] CreateSource(int length)
    {
        var source = new Operand[length];
        for (int index = 0; index < source.Length; index++)
        {
            source[index] = index % 11 == 0
                ? Operand.Admitted((index % 3) + 1)
                : Operand.Rejected();
        }
        return source;
    }

    static Operand[] CreateLateExistsSource(
        int length,
        int firstMatch)
    {
        var source = new Operand[length];
        for (int index = 0; index < source.Length; index++)
        {
            source[index] =
                index == firstMatch
                || (index > firstMatch && index % 11 == 0)
                    ? Operand.Admitted((index % 3) + 1)
                    : Operand.Rejected();
        }
        return source;
    }

    static void Measure(
        string variant,
        string terminal,
        Func<TerminalResult> action,
        int iterations)
    {
        for (int warmup = 0; warmup < 100; warmup++)
            Consume(action());

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        TerminalResult last = default;
        for (int iteration = 0; iteration < iterations; iteration++)
            last = action();
        long elapsed = Stopwatch.GetTimestamp() - started;
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Consume(last);

        double nanoseconds =
            elapsed * 1_000_000_000d
            / Stopwatch.Frequency
            / iterations;
        Console.WriteLine(
            $"{variant}\t{terminal}\t{nanoseconds:F2}\t"
            + $"{allocated / (double)iterations:F2}\t"
            + $"{last.OperandsVisited}\t{last.RowsConstructed}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Consume(TerminalResult result) =>
        s_sink = unchecked(
            s_sink
            + result.Value
            + result.Checksum
            + result.OperandsVisited
            + result.RowsConstructed);

    static void MeasureCombined(
        string variant,
        Func<CombinedResult> action,
        int iterations)
    {
        for (int warmup = 0; warmup < 100; warmup++)
            Consume(action());

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        CombinedResult last = default;
        for (int iteration = 0; iteration < iterations; iteration++)
            last = action();
        long elapsed = Stopwatch.GetTimestamp() - started;
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Consume(last);

        double nanoseconds =
            elapsed * 1_000_000_000d
            / Stopwatch.Frequency
            / iterations;
        Console.WriteLine(
            $"{variant}\tcombined\t{nanoseconds:F2}\t"
            + $"{allocated / (double)iterations:F2}\t"
            + $"{last.PhysicalOperandsVisited}\t"
            + $"{last.Rows.RowsConstructed}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Consume(CombinedResult result) =>
        s_sink = unchecked(
            s_sink
            + result.Exists.Value
            + result.Count.Value
            + result.Rows.Value
            + result.Rows.Checksum
            + result.PhysicalOperandsVisited);

    static void AssertEqual(
        TerminalResult expected,
        TerminalResult actual,
        string name)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException(
                $"{name}: expected {expected}; actual {actual}");
        }
    }

    static string Format(TerminalResult result) =>
        result.Succeeded
            ? $"{result.Value}/{result.Completion}/"
                + $"{result.OperandsVisited}/{result.RowsConstructed}"
            : $"failure/{result.OperandsVisited}/"
                + $"{result.RowsConstructed}";

    static int Usage()
    {
        Console.Error.WriteLine(
            "usage: body-use-terminal-folding-probe [check|time]");
        return 2;
    }
}
