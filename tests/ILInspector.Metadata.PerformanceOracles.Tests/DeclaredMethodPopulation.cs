using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NLinq;
using QuerySpace.Composition;

namespace DotnetInspector.PerformanceOracles;

public sealed record DeclaredMethodScorecardCheck(
    int Count,
    string AnswerHash,
    MetadataDeclaredMethodPopulationReceipt CountReceipt,
    MetadataDeclaredMethodPopulationReceipt RowsReceipt);

public sealed record DeclaredMethodScorecardCell(
    string Implementation,
    string Terminal,
    double Microseconds,
    long AllocatedBytes);

public sealed record DeclaredMethodScorecardResult(
    string TypeName,
    DeclaredMethodScorecardCheck Check,
    IReadOnlyList<DeclaredMethodScorecardCell> Cells);

public sealed record PreparedQuerySpaceScorecardCell(
    string Boundary,
    string Terminal,
    double Microseconds,
    long AllocatedBytes);

public sealed record PreparedQuerySpaceScorecardResult(
    string TypeName,
    int Count,
    IReadOnlyList<PreparedQuerySpaceScorecardCell> Cells);

public static class DeclaredMethodPopulation
{
    private const int OperationsPerSample = 256;
    private static readonly MetadataDeclaredMethodPopulationReceipt
        s_countReceipt = new(
            TypeDefinitionRowsRead: 1,
            MethodDefinitionHandlesVisited: 0,
            MethodDefinitionRowsRead: 0,
            MethodNamesDecoded: 0,
            MethodSignaturesDecoded: 0,
            MethodAttributesDecoded: 0,
            ProjectedRows: 0);

    public static DeclaredMethodScorecardCheck Check(
        string path,
        MetadataTypeDefinitionName type)
    {
        using var asset = Asset.Open(path, type);
        return Check(asset);
    }

    public static DeclaredMethodScorecardResult Measure(
        string path,
        MetadataTypeDefinitionName type)
    {
        using var asset = Asset.Open(path, type);
        DeclaredMethodScorecardCheck check = Check(asset);
        var cells = new List<DeclaredMethodScorecardCell>();
        DeclaredMethodScorecardCase[] cases =
            Enum.GetValues<DeclaredMethodScorecardCase>();
        foreach (DeclaredMethodScorecardCase measurementCase
            in cases)
        {
            for (int warmup = 0; warmup < 10; warmup++)
                Warmup(asset, measurementCase);
        }

        var times = cases.ToDictionary(
            static measurementCase => measurementCase,
            static _ => new List<double>());
        var allocations = cases.ToDictionary(
            static measurementCase => measurementCase,
            static _ => new List<long>());
        for (int round = 0; round < 8; round++)
        {
            for (int offset = 0; offset < cases.Length; offset++)
            {
                DeclaredMethodScorecardCase measurementCase =
                    cases[(round + offset) % cases.Length];
                Measurement measurement =
                    Measure(asset, measurementCase);
                times[measurementCase].Add(measurement.Microseconds);
                allocations[measurementCase].Add(
                    measurement.AllocatedBytes);
            }
        }
        foreach (DeclaredMethodScorecardCase measurementCase
            in cases)
        {
            cells.Add(
                new(
                    Implementation(measurementCase),
                    Terminal(measurementCase),
                    Median(times[measurementCase]),
                    Median(allocations[measurementCase])));
        }
        return new(DisplayName(type), check, cells);
    }

    public static PreparedQuerySpaceScorecardResult
        MeasurePreparedQuerySpace(
            string path,
            MetadataTypeDefinitionName type)
    {
        using var asset = Asset.Open(path, type);
        TypeDeclaredMethodPopulationOutcome.Counted count =
            AssertSectionsCounted(
                asset.ExecutePreparedQuerySpaceRequest(
                    QuerySpaceTerminalRequirement.Count).Content);
        TypeDeclaredMethodPopulationOutcome.Read rows =
            AssertSectionsRead(
                asset.ExecutePreparedQuerySpaceRequest(
                    QuerySpaceTerminalRequirement.Rows).Content);
        ValidatePreparedQuerySpace(asset, count, rows);

        PreparedQuerySpaceScorecardCase[] cases =
            Enum.GetValues<PreparedQuerySpaceScorecardCase>();
        foreach (PreparedQuerySpaceScorecardCase measurementCase
            in cases)
        {
            for (int warmup = 0; warmup < 10; warmup++)
                WarmupPreparedQuerySpace(asset, measurementCase);
        }

        var times = cases.ToDictionary(
            static measurementCase => measurementCase,
            static _ => new List<double>());
        var allocations = cases.ToDictionary(
            static measurementCase => measurementCase,
            static _ => new List<long>());
        for (int round = 0; round < 8; round++)
        {
            for (int offset = 0; offset < cases.Length; offset++)
            {
                PreparedQuerySpaceScorecardCase measurementCase =
                    cases[(round + offset) % cases.Length];
                Measurement measurement =
                    MeasurePreparedQuerySpace(
                        asset,
                        measurementCase);
                times[measurementCase].Add(measurement.Microseconds);
                allocations[measurementCase].Add(
                    measurement.AllocatedBytes);
            }
        }

        return new(
            DisplayName(type),
            count.Count,
            [.. cases.Select(measurementCase =>
                new PreparedQuerySpaceScorecardCell(
                    PreparedQuerySpaceBoundary(measurementCase),
                    PreparedQuerySpaceTerminal(measurementCase),
                    Median(times[measurementCase]),
                    Median(allocations[measurementCase])))]);
    }

    public static string Report(DeclaredMethodScorecardResult result)
    {
        using var writer = new StringWriter(
            System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# Metadata and NLinq kernels — {result.TypeName}");
        writer.WriteLine(
            $"# answer: {result.Check.Count:N0} declared MethodDefs, "
                + result.Check.AnswerHash);
        writer.WriteLine(
            "| Implementation | Terminal | Median | Allocated | "
                + "Time vs implementation Count | "
                + "Allocation vs implementation Count | "
                + "Time vs Metadata | Allocation vs Metadata |");
        writer.WriteLine(
            "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (DeclaredMethodScorecardCell cell in result.Cells)
        {
            DeclaredMethodScorecardCell implementationCount =
                result.Cells.Single(candidate =>
                    candidate.Implementation == cell.Implementation
                    && candidate.Terminal == "Count");
            DeclaredMethodScorecardCell metadata =
                result.Cells.Single(candidate =>
                    candidate.Implementation == "Metadata"
                    && candidate.Terminal == cell.Terminal);
            writer.WriteLine(
                $"| {cell.Implementation} | {cell.Terminal} | "
                    + $"{cell.Microseconds:F3} us | "
                    + $"{cell.AllocatedBytes:N0} B | "
                    + $"{cell.Microseconds / implementationCount.Microseconds:F2}x | "
                    + $"{FormatRatio(cell.AllocatedBytes, implementationCount.AllocatedBytes)} | "
                    + $"{cell.Microseconds / metadata.Microseconds:F2}x | "
                    + $"{FormatRatio(cell.AllocatedBytes, metadata.AllocatedBytes)} |");
        }
        writer.WriteLine();
        writer.WriteLine(
            "| Terminal | Method handles | Method rows | Names | "
                + "Signatures | Attributes | Projected rows |");
        writer.WriteLine(
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        WriteReceipt("Count", result.Check.CountReceipt);
        WriteReceipt("Rows", result.Check.RowsReceipt);
        return writer.ToString();

        void WriteReceipt(
            string terminal,
            MetadataDeclaredMethodPopulationReceipt receipt)
        {
            writer.WriteLine(
                $"| {terminal} | "
                    + $"{receipt.MethodDefinitionHandlesVisited} | "
                    + $"{receipt.MethodDefinitionRowsRead} | "
                    + $"{receipt.MethodNamesDecoded} | "
                    + $"{receipt.MethodSignaturesDecoded} | "
                    + $"{receipt.MethodAttributesDecoded} | "
                    + $"{receipt.ProjectedRows} |");
        }
    }

    public static string ReportPreparedQuerySpace(
        PreparedQuerySpaceScorecardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var writer = new StringWriter(
            System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# Prepared-borrow QuerySpace + NLinq — {result.TypeName}");
        writer.WriteLine(
            $"# answer: {result.Count:N0} declared MethodDefs");
        writer.WriteLine(
            "| Boundary | Terminal | Median | Allocated |");
        writer.WriteLine("| --- | --- | ---: | ---: |");
        foreach (PreparedQuerySpaceScorecardCell cell in result.Cells)
        {
            writer.WriteLine(
                $"| {cell.Boundary} | {cell.Terminal} | "
                    + $"{cell.Microseconds:F3} us | "
                    + $"{cell.AllocatedBytes:N0} B |");
        }
        writer.WriteLine();
        return writer.ToString();
    }

    private static DeclaredMethodScorecardCheck Check(Asset asset)
    {
        var rows = AssertRead(
            asset.ExecuteMetadata(
                MetadataDeclaredMethodPopulationTerminal.Rows));
        var count = AssertCounted(
            asset.ExecuteMetadata(
                MetadataDeclaredMethodPopulationTerminal.Count));
        int nlinqCount = asset.ExecuteNLinqCount();
        ImmutableArray<int> nlinqRows =
            asset.ExecuteNLinqRows();
        var nlinqOutcomeCount = AssertCounted(
            asset.ExecuteNLinqOutcomeCount());
        var nlinqOutcomeRows = AssertRead(
            asset.ExecuteNLinqOutcomeRows());
        int borrowedNLinqCount =
            asset.ExecuteBorrowedNLinqCount();
        ImmutableArray<int> borrowedNLinqRows =
            asset.ExecuteBorrowedNLinqRows();
        var borrowedNLinqOutcomeCount = AssertCounted(
            asset.ExecuteBorrowedNLinqOutcomeCount());
        var borrowedNLinqOutcomeRows = AssertRead(
            asset.ExecuteBorrowedNLinqOutcomeRows());
        int preparedBorrowNLinqCount =
            asset.ExecutePreparedBorrowNLinqCount();
        ImmutableArray<int> preparedBorrowNLinqRows =
            asset.ExecutePreparedBorrowNLinqRows();
        var preparedBorrowNLinqOutcomeCount = AssertCounted(
            asset.ExecutePreparedBorrowNLinqOutcomeCount());
        var preparedBorrowNLinqOutcomeRows = AssertRead(
            asset.ExecutePreparedBorrowNLinqOutcomeRows());
        if (count.Count != rows.Count
            || count.Count != rows.Rows.Length)
        {
            throw new InvalidOperationException(
                "Declared MethodDef Count and Rows disagree.");
        }
        if (count.Receipt.MethodDefinitionHandlesVisited != 0
            || count.Receipt.MethodDefinitionRowsRead != 0
            || count.Receipt.MethodNamesDecoded != 0
            || count.Receipt.MethodSignaturesDecoded != 0
            || count.Receipt.MethodAttributesDecoded != 0
            || count.Receipt.ProjectedRows != 0)
        {
            throw new InvalidOperationException(
                "Declared MethodDef Count performed per-method work.");
        }
        if (nlinqCount != count.Count
            || !nlinqRows.AsSpan().SequenceEqual(rows.Rows.AsSpan()))
        {
            throw new InvalidOperationException(
                "NLinq declared MethodDef Count and Rows disagree.");
        }
        if (nlinqOutcomeCount.Count != count.Count
            || nlinqOutcomeRows.Count != rows.Count
            || !nlinqOutcomeRows.Rows.AsSpan()
                .SequenceEqual(rows.Rows.AsSpan())
            || nlinqOutcomeCount.Receipt != count.Receipt
            || nlinqOutcomeRows.Receipt != rows.Receipt)
        {
            throw new InvalidOperationException(
                "Output-shaped NLinq declared MethodDef results disagree.");
        }
        if (borrowedNLinqCount != count.Count
            || !borrowedNLinqRows.AsSpan()
                .SequenceEqual(rows.Rows.AsSpan())
            || borrowedNLinqOutcomeCount.Count != count.Count
            || borrowedNLinqOutcomeRows.Count != rows.Count
            || !borrowedNLinqOutcomeRows.Rows.AsSpan()
                .SequenceEqual(rows.Rows.AsSpan())
            || borrowedNLinqOutcomeCount.Receipt != count.Receipt
            || borrowedNLinqOutcomeRows.Receipt != rows.Receipt)
        {
            throw new InvalidOperationException(
                "Ready-snapshot borrowed NLinq declared MethodDef "
                    + "results disagree.");
        }
        if (preparedBorrowNLinqCount != count.Count
            || !preparedBorrowNLinqRows.AsSpan()
                .SequenceEqual(rows.Rows.AsSpan())
            || preparedBorrowNLinqOutcomeCount.Count != count.Count
            || preparedBorrowNLinqOutcomeRows.Count != rows.Count
            || !preparedBorrowNLinqOutcomeRows.Rows.AsSpan()
                .SequenceEqual(rows.Rows.AsSpan())
            || preparedBorrowNLinqOutcomeCount.Receipt != count.Receipt
            || preparedBorrowNLinqOutcomeRows.Receipt != rows.Receipt)
        {
            throw new InvalidOperationException(
                "Prepared-borrow NLinq declared MethodDef "
                    + "results disagree.");
        }

        return new(
            count.Count,
            Hash(rows.Rows),
            count.Receipt,
            rows.Receipt);
    }

    private static MetadataDeclaredMethodPopulationOutcome.Counted
        AssertCounted(MetadataDeclaredMethodPopulationOutcome outcome) =>
            outcome
                as MetadataDeclaredMethodPopulationOutcome.Counted
            ?? throw new InvalidOperationException(
                $"Expected Counted, got {outcome}.");

    private static MetadataDeclaredMethodPopulationOutcome.Read
        AssertRead(MetadataDeclaredMethodPopulationOutcome outcome) =>
            outcome
                as MetadataDeclaredMethodPopulationOutcome.Read
            ?? throw new InvalidOperationException(
                $"Expected Read, got {outcome}.");

    private static TypeDeclaredMethodPopulationOutcome.Counted
        AssertSectionsCounted(
            TypeDeclaredMethodPopulationOutcome outcome) =>
            outcome
                as TypeDeclaredMethodPopulationOutcome.Counted
            ?? throw new InvalidOperationException(
                $"Expected Sections Counted, got {outcome}.");

    private static TypeDeclaredMethodPopulationOutcome.Read
        AssertSectionsRead(
            TypeDeclaredMethodPopulationOutcome outcome) =>
            outcome
                as TypeDeclaredMethodPopulationOutcome.Read
            ?? throw new InvalidOperationException(
                $"Expected Sections Read, got {outcome}.");

    private static void ValidatePreparedQuerySpace(
        Asset asset,
        TypeDeclaredMethodPopulationOutcome.Counted count,
        TypeDeclaredMethodPopulationOutcome.Read rows)
    {
        if (count.Count != rows.Count
            || count.Count != rows.Rows.Length)
        {
            throw new InvalidOperationException(
                "Prepared QuerySpace Count and Rows disagree.");
        }

        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
            currentCount =
                asset.ExecuteCurrentQuerySpace(
                    QuerySpaceTerminalRequirement.Count);
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
            currentRows =
                asset.ExecuteCurrentQuerySpace(
                    QuerySpaceTerminalRequirement.Rows);
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
            preparedPlanCount =
                asset.ExecutePreparedQuerySpacePlan(
                    QuerySpaceTerminalRequirement.Count);
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
            preparedPlanRows =
                asset.ExecutePreparedQuerySpacePlan(
                    QuerySpaceTerminalRequirement.Rows);
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
            discoveryRequestCount =
                asset.ExecuteDiscoveryQuerySpaceRequest(
                    QuerySpaceTerminalRequirement.Count);
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
            discoveryRequestRows =
                asset.ExecuteDiscoveryQuerySpaceRequest(
                    QuerySpaceTerminalRequirement.Rows);
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
            discoveryPlanCount =
                asset.ExecuteDiscoveryQuerySpacePlan(
                    QuerySpaceTerminalRequirement.Count);
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
            discoveryPlanRows =
                asset.ExecuteDiscoveryQuerySpacePlan(
                    QuerySpaceTerminalRequirement.Rows);
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
            preparedRequestCount =
                asset.ExecutePreparedQuerySpaceRequest(
                    QuerySpaceTerminalRequirement.Count);
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
            preparedRequestRows =
                asset.ExecutePreparedQuerySpaceRequest(
                    QuerySpaceTerminalRequirement.Rows);
        if (!Equivalent(currentCount, preparedRequestCount)
            || !Equivalent(currentRows, preparedRequestRows)
            || !Equivalent(currentCount, preparedPlanCount)
            || !Equivalent(currentRows, preparedPlanRows)
            || !Equivalent(currentCount, discoveryRequestCount)
            || !Equivalent(currentRows, discoveryRequestRows)
            || !Equivalent(currentCount, discoveryPlanCount)
            || !Equivalent(currentRows, discoveryPlanRows))
        {
            throw new InvalidOperationException(
                "Prepared QuerySpace envelopes disagree with the "
                    + "current product operation.");
        }
    }

    private static bool Equivalent(
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome> left,
        InspectionEnvelope<TypeDeclaredMethodPopulationOutcome> right)
    {
        if (left.Diagnostics.Length != right.Diagnostics.Length
            || left.Share is not InspectionShare.NonProjectable leftShare
            || right.Share is not InspectionShare.NonProjectable rightShare
            || leftShare.Path != rightShare.Path
            || leftShare.Reason.ToString()
                != rightShare.Reason.ToString())
        {
            return false;
        }

        return (left.Content, right.Content) switch
        {
            (
                TypeDeclaredMethodPopulationOutcome.Counted first,
                TypeDeclaredMethodPopulationOutcome.Counted second) =>
                    first.Subject == second.Subject
                    && ReferenceEquals(first.Type, second.Type)
                    && first.Binding == second.Binding
                    && first.Count == second.Count
                    && first.Receipt == second.Receipt,
            (
                TypeDeclaredMethodPopulationOutcome.Read first,
                TypeDeclaredMethodPopulationOutcome.Read second) =>
                    first.Subject == second.Subject
                    && ReferenceEquals(first.Type, second.Type)
                    && first.Binding == second.Binding
                    && first.Count == second.Count
                    && first.Rows.AsSpan()
                        .SequenceEqual(second.Rows.AsSpan())
                    && first.Receipt == second.Receipt,
            _ => false,
        };
    }

    private static void Warmup(
        Asset asset,
        DeclaredMethodScorecardCase measurementCase)
    {
        switch (measurementCase)
        {
            case DeclaredMethodScorecardCase.MetadataCount:
                GC.KeepAlive(
                    asset.ExecuteMetadata(
                        MetadataDeclaredMethodPopulationTerminal.Count));
                break;
            case DeclaredMethodScorecardCase.MetadataRows:
                GC.KeepAlive(
                    asset.ExecuteMetadata(
                        MetadataDeclaredMethodPopulationTerminal.Rows));
                break;
            case DeclaredMethodScorecardCase.NLinqCount:
                GC.KeepAlive(asset.ExecuteNLinqCount());
                break;
            case DeclaredMethodScorecardCase.NLinqRows:
                GC.KeepAlive(asset.ExecuteNLinqRows());
                break;
            case DeclaredMethodScorecardCase.NLinqOutcomeCount:
                GC.KeepAlive(asset.ExecuteNLinqOutcomeCount());
                break;
            case DeclaredMethodScorecardCase.NLinqOutcomeRows:
                GC.KeepAlive(asset.ExecuteNLinqOutcomeRows());
                break;
            case DeclaredMethodScorecardCase.BorrowedNLinqCount:
                GC.KeepAlive(asset.ExecuteBorrowedNLinqCount());
                break;
            case DeclaredMethodScorecardCase.BorrowedNLinqRows:
                GC.KeepAlive(asset.ExecuteBorrowedNLinqRows());
                break;
            case DeclaredMethodScorecardCase.BorrowedNLinqOutcomeCount:
                GC.KeepAlive(
                    asset.ExecuteBorrowedNLinqOutcomeCount());
                break;
            case DeclaredMethodScorecardCase.BorrowedNLinqOutcomeRows:
                GC.KeepAlive(
                    asset.ExecuteBorrowedNLinqOutcomeRows());
                break;
            case DeclaredMethodScorecardCase.PreparedBorrowNLinqCount:
                GC.KeepAlive(
                    asset.ExecutePreparedBorrowNLinqCount());
                break;
            case DeclaredMethodScorecardCase.PreparedBorrowNLinqRows:
                GC.KeepAlive(
                    asset.ExecutePreparedBorrowNLinqRows());
                break;
            case DeclaredMethodScorecardCase
                .PreparedBorrowNLinqOutcomeCount:
                GC.KeepAlive(
                    asset.ExecutePreparedBorrowNLinqOutcomeCount());
                break;
            case DeclaredMethodScorecardCase
                .PreparedBorrowNLinqOutcomeRows:
                GC.KeepAlive(
                    asset.ExecutePreparedBorrowNLinqOutcomeRows());
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(measurementCase));
        }
    }

    private static Measurement Measure(
        Asset asset,
        DeclaredMethodScorecardCase measurementCase) =>
        measurementCase switch
        {
            DeclaredMethodScorecardCase.MetadataCount =>
                Measure(
                    () => asset.ExecuteMetadata(
                        MetadataDeclaredMethodPopulationTerminal.Count)),
            DeclaredMethodScorecardCase.MetadataRows =>
                Measure(
                    () => asset.ExecuteMetadata(
                        MetadataDeclaredMethodPopulationTerminal.Rows)),
            DeclaredMethodScorecardCase.NLinqCount =>
                Measure(asset.ExecuteNLinqCount),
            DeclaredMethodScorecardCase.NLinqRows =>
                Measure(asset.ExecuteNLinqRows),
            DeclaredMethodScorecardCase.NLinqOutcomeCount =>
                Measure(asset.ExecuteNLinqOutcomeCount),
            DeclaredMethodScorecardCase.NLinqOutcomeRows =>
                Measure(asset.ExecuteNLinqOutcomeRows),
            DeclaredMethodScorecardCase.BorrowedNLinqCount =>
                Measure(asset.ExecuteBorrowedNLinqCount),
            DeclaredMethodScorecardCase.BorrowedNLinqRows =>
                Measure(asset.ExecuteBorrowedNLinqRows),
            DeclaredMethodScorecardCase.BorrowedNLinqOutcomeCount =>
                Measure(asset.ExecuteBorrowedNLinqOutcomeCount),
            DeclaredMethodScorecardCase.BorrowedNLinqOutcomeRows =>
                Measure(asset.ExecuteBorrowedNLinqOutcomeRows),
            DeclaredMethodScorecardCase.PreparedBorrowNLinqCount =>
                Measure(asset.ExecutePreparedBorrowNLinqCount),
            DeclaredMethodScorecardCase.PreparedBorrowNLinqRows =>
                Measure(asset.ExecutePreparedBorrowNLinqRows),
            DeclaredMethodScorecardCase
                .PreparedBorrowNLinqOutcomeCount =>
                Measure(
                    asset.ExecutePreparedBorrowNLinqOutcomeCount),
            DeclaredMethodScorecardCase
                .PreparedBorrowNLinqOutcomeRows =>
                Measure(
                    asset.ExecutePreparedBorrowNLinqOutcomeRows),
            _ => throw new ArgumentOutOfRangeException(
                nameof(measurementCase)),
        };

    private static void WarmupPreparedQuerySpace(
        Asset asset,
        PreparedQuerySpaceScorecardCase measurementCase)
    {
        switch (measurementCase)
        {
            case PreparedQuerySpaceScorecardCase.RequestCount:
                GC.KeepAlive(
                    asset.ExecutePreparedQuerySpaceRequest(
                        QuerySpaceTerminalRequirement.Count));
                break;
            case PreparedQuerySpaceScorecardCase.RequestRows:
                GC.KeepAlive(
                    asset.ExecutePreparedQuerySpaceRequest(
                        QuerySpaceTerminalRequirement.Rows));
                break;
            case PreparedQuerySpaceScorecardCase.PlanCount:
                GC.KeepAlive(
                    asset.ExecutePreparedQuerySpacePlan(
                        QuerySpaceTerminalRequirement.Count));
                break;
            case PreparedQuerySpaceScorecardCase.PlanRows:
                GC.KeepAlive(
                    asset.ExecutePreparedQuerySpacePlan(
                        QuerySpaceTerminalRequirement.Rows));
                break;
            case PreparedQuerySpaceScorecardCase.DiscoveryRequestCount:
                GC.KeepAlive(
                    asset.ExecuteDiscoveryQuerySpaceRequest(
                        QuerySpaceTerminalRequirement.Count));
                break;
            case PreparedQuerySpaceScorecardCase.DiscoveryRequestRows:
                GC.KeepAlive(
                    asset.ExecuteDiscoveryQuerySpaceRequest(
                        QuerySpaceTerminalRequirement.Rows));
                break;
            case PreparedQuerySpaceScorecardCase.DiscoveryPlanCount:
                GC.KeepAlive(
                    asset.ExecuteDiscoveryQuerySpacePlan(
                        QuerySpaceTerminalRequirement.Count));
                break;
            case PreparedQuerySpaceScorecardCase.DiscoveryPlanRows:
                GC.KeepAlive(
                    asset.ExecuteDiscoveryQuerySpacePlan(
                        QuerySpaceTerminalRequirement.Rows));
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(measurementCase));
        }
    }

    private static Measurement MeasurePreparedQuerySpace(
        Asset asset,
        PreparedQuerySpaceScorecardCase measurementCase) =>
        measurementCase switch
        {
            PreparedQuerySpaceScorecardCase.RequestCount =>
                Measure(
                    () => asset.ExecutePreparedQuerySpaceRequest(
                        QuerySpaceTerminalRequirement.Count)),
            PreparedQuerySpaceScorecardCase.RequestRows =>
                Measure(
                    () => asset.ExecutePreparedQuerySpaceRequest(
                        QuerySpaceTerminalRequirement.Rows)),
            PreparedQuerySpaceScorecardCase.PlanCount =>
                Measure(
                    () => asset.ExecutePreparedQuerySpacePlan(
                        QuerySpaceTerminalRequirement.Count)),
            PreparedQuerySpaceScorecardCase.PlanRows =>
                Measure(
                    () => asset.ExecutePreparedQuerySpacePlan(
                        QuerySpaceTerminalRequirement.Rows)),
            PreparedQuerySpaceScorecardCase.DiscoveryRequestCount =>
                Measure(
                    () => asset.ExecuteDiscoveryQuerySpaceRequest(
                        QuerySpaceTerminalRequirement.Count)),
            PreparedQuerySpaceScorecardCase.DiscoveryRequestRows =>
                Measure(
                    () => asset.ExecuteDiscoveryQuerySpaceRequest(
                        QuerySpaceTerminalRequirement.Rows)),
            PreparedQuerySpaceScorecardCase.DiscoveryPlanCount =>
                Measure(
                    () => asset.ExecuteDiscoveryQuerySpacePlan(
                        QuerySpaceTerminalRequirement.Count)),
            PreparedQuerySpaceScorecardCase.DiscoveryPlanRows =>
                Measure(
                    () => asset.ExecuteDiscoveryQuerySpacePlan(
                        QuerySpaceTerminalRequirement.Rows)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(measurementCase)),
        };

    private static Measurement Measure<T>(Func<T> execute)
    {
        var times = new double[31];
        var allocations = new long[31];
        for (int sample = 0; sample < times.Length; sample++)
        {
            long before =
                GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            T? result = default;
            for (int operation = 0;
                operation < OperationsPerSample;
                operation++)
            {
                result = execute();
            }
            long elapsed = Stopwatch.GetTimestamp() - started;
            allocations[sample] =
                (GC.GetAllocatedBytesForCurrentThread() - before)
                    / OperationsPerSample;
            times[sample] =
                elapsed * 1_000_000.0
                    / Stopwatch.Frequency
                    / OperationsPerSample;
            GC.KeepAlive(result);
        }
        Array.Sort(times);
        Array.Sort(allocations);
        return new(
            times[times.Length / 2],
            allocations[allocations.Length / 2]);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static long Median(List<long> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static double Ratio(long value, long baseline) =>
        baseline == 0
            ? value == 0 ? 1 : double.PositiveInfinity
            : (double)value / baseline;

    private static string FormatRatio(
        long value,
        long baseline) =>
        baseline == 0 && value != 0
            ? "unbounded"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{Ratio(value, baseline):F2}x");

    private static string PreparedQuerySpaceBoundary(
        PreparedQuerySpaceScorecardCase measurementCase) =>
        measurementCase switch
        {
            PreparedQuerySpaceScorecardCase.RequestCount
                or PreparedQuerySpaceScorecardCase.RequestRows =>
                "Request + resolve + execute",
            PreparedQuerySpaceScorecardCase.PlanCount
                or PreparedQuerySpaceScorecardCase.PlanRows =>
                "Pre-resolved plan execute",
            PreparedQuerySpaceScorecardCase.DiscoveryRequestCount
                or PreparedQuerySpaceScorecardCase
                    .DiscoveryRequestRows =>
                "Request + resolve + discovery",
            PreparedQuerySpaceScorecardCase.DiscoveryPlanCount
                or PreparedQuerySpaceScorecardCase
                    .DiscoveryPlanRows =>
                "Prepared discovery + handoff",
            _ => throw new ArgumentOutOfRangeException(
                nameof(measurementCase)),
        };

    private static string PreparedQuerySpaceTerminal(
        PreparedQuerySpaceScorecardCase measurementCase) =>
        measurementCase switch
        {
            PreparedQuerySpaceScorecardCase.RequestCount
                or PreparedQuerySpaceScorecardCase.PlanCount
                or PreparedQuerySpaceScorecardCase
                    .DiscoveryRequestCount
                or PreparedQuerySpaceScorecardCase
                    .DiscoveryPlanCount =>
                "Count",
            PreparedQuerySpaceScorecardCase.RequestRows
                or PreparedQuerySpaceScorecardCase.PlanRows
                or PreparedQuerySpaceScorecardCase
                    .DiscoveryRequestRows
                or PreparedQuerySpaceScorecardCase
                    .DiscoveryPlanRows =>
                "Rows",
            _ => throw new ArgumentOutOfRangeException(
                nameof(measurementCase)),
        };

    private static string Hash(
        ImmutableArray<int> rows)
    {
        ulong hash = 14695981039346656037;
        foreach (int token in rows)
        {
            unchecked
            {
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)(token >> shift);
                    hash *= 1099511628211;
                }
            }
        }
        return hash.ToString("x16");
    }

    private static string DisplayName(
        MetadataTypeDefinitionName type) =>
        string.IsNullOrEmpty(type.Namespace)
            ? string.Join('+', type.Segments)
            : $"{type.Namespace}.{string.Join('+', type.Segments)}";

    private static string Implementation(
        DeclaredMethodScorecardCase measurementCase) =>
        measurementCase switch
        {
            DeclaredMethodScorecardCase.MetadataCount
                or DeclaredMethodScorecardCase.MetadataRows =>
                "Metadata",
            DeclaredMethodScorecardCase.NLinqCount
                or DeclaredMethodScorecardCase.NLinqRows =>
                "NLinq raw",
            DeclaredMethodScorecardCase.NLinqOutcomeCount
                or DeclaredMethodScorecardCase.NLinqOutcomeRows =>
                "NLinq outcome",
            DeclaredMethodScorecardCase.BorrowedNLinqCount
                or DeclaredMethodScorecardCase.BorrowedNLinqRows =>
                "NLinq ready-borrow raw",
            DeclaredMethodScorecardCase.BorrowedNLinqOutcomeCount
                or DeclaredMethodScorecardCase.BorrowedNLinqOutcomeRows =>
                "NLinq ready-borrow outcome",
            DeclaredMethodScorecardCase.PreparedBorrowNLinqCount
                or DeclaredMethodScorecardCase
                    .PreparedBorrowNLinqRows =>
                "NLinq prepared-borrow raw",
            DeclaredMethodScorecardCase
                    .PreparedBorrowNLinqOutcomeCount
                or DeclaredMethodScorecardCase
                    .PreparedBorrowNLinqOutcomeRows =>
                "NLinq prepared-borrow outcome",
            _ => throw new ArgumentOutOfRangeException(
                nameof(measurementCase)),
        };

    private static string Terminal(
        DeclaredMethodScorecardCase measurementCase) =>
        measurementCase switch
        {
            DeclaredMethodScorecardCase.MetadataCount
                or DeclaredMethodScorecardCase.NLinqCount
                or DeclaredMethodScorecardCase.NLinqOutcomeCount
                or DeclaredMethodScorecardCase.BorrowedNLinqCount
                or DeclaredMethodScorecardCase
                    .BorrowedNLinqOutcomeCount
                or DeclaredMethodScorecardCase
                    .PreparedBorrowNLinqCount
                or DeclaredMethodScorecardCase
                    .PreparedBorrowNLinqOutcomeCount =>
                "Count",
            DeclaredMethodScorecardCase.MetadataRows
                or DeclaredMethodScorecardCase.NLinqRows
                or DeclaredMethodScorecardCase.NLinqOutcomeRows
                or DeclaredMethodScorecardCase.BorrowedNLinqRows
                or DeclaredMethodScorecardCase
                    .BorrowedNLinqOutcomeRows
                or DeclaredMethodScorecardCase
                    .PreparedBorrowNLinqRows
                or DeclaredMethodScorecardCase
                    .PreparedBorrowNLinqOutcomeRows =>
                "Rows",
            _ => throw new ArgumentOutOfRangeException(
                nameof(measurementCase)),
        };

    private enum DeclaredMethodScorecardCase
    {
        MetadataCount,
        MetadataRows,
        NLinqCount,
        NLinqRows,
        NLinqOutcomeCount,
        NLinqOutcomeRows,
        BorrowedNLinqCount,
        BorrowedNLinqRows,
        BorrowedNLinqOutcomeCount,
        BorrowedNLinqOutcomeRows,
        PreparedBorrowNLinqCount,
        PreparedBorrowNLinqRows,
        PreparedBorrowNLinqOutcomeCount,
        PreparedBorrowNLinqOutcomeRows,
    }

    private enum PreparedQuerySpaceScorecardCase
    {
        RequestCount,
        RequestRows,
        PlanCount,
        PlanRows,
        DiscoveryRequestCount,
        DiscoveryRequestRows,
        DiscoveryPlanCount,
        DiscoveryPlanRows,
    }

    private readonly record struct Measurement(
        double Microseconds,
        long AllocatedBytes);

    private sealed class Asset : IDisposable
    {
        private readonly InspectionWorkspace _workspace;
        private readonly AssemblyContextGroup _group;
        private readonly ResolvedAssemblyReference _assembly;
        private readonly AssemblyContextParticipant _participant;
        private readonly AssemblyInspectionSession _session;
        private readonly MetadataTypeDefinitionName _typeName;
        private readonly MetadataTypeDefinitionBinding _type;
        private readonly MetadataReader _reader;
        private readonly MethodDefinitionHandleCollection _methods;
        private readonly TypeDeclaredMethodPopulationSubject _subject;
        private readonly TypeDeclaredMethodPopulationBinding
            _sectionsBinding;
        private readonly InspectionShare.NonProjectable _share;
        private readonly MetadataDeclaredMethodPopulationReceipt
            _rowsReceipt;
        private readonly TypeDeclaredMethodPopulationQueryResult.Accepted
            _countPlan;
        private readonly TypeDeclaredMethodPopulationQueryResult.Accepted
            _rowsPlan;
        private readonly AssemblyImageCallback<int>
            _borrowedNLinqCount;
        private readonly AssemblyImageCallback<ImmutableArray<int>>
            _borrowedNLinqRows;
        private readonly AssemblyImageCallback<
            MetadataDeclaredMethodPopulationOutcome>
            _borrowedNLinqOutcomeCount;
        private readonly AssemblyImageCallback<
            MetadataDeclaredMethodPopulationOutcome>
            _borrowedNLinqOutcomeRows;

        private Asset(
            InspectionWorkspace workspace,
            AssemblyContextGroup group,
            ResolvedAssemblyReference assembly,
            AssemblyContextParticipant participant,
            AssemblyInspectionSession session,
            MetadataTypeDefinitionName typeName,
            MetadataTypeDefinitionBinding type,
            MetadataReader reader)
        {
            _workspace = workspace;
            _group = group;
            _assembly = assembly;
            _participant = participant;
            _session = session;
            _typeName = typeName;
            _type = type;
            _reader = reader;
            _methods = PrepareMethods(reader, type);
            _subject = new(assembly.Identity, assembly.Provenance);
            _sectionsBinding = new(
                type.ModuleVersionId,
                type.Definition.Value);
            _share = new(
                "type-declared-method-population/share",
                "Raw declared MethodDefs do not yet have a "
                    + "portable Workspace scenario.");
            _rowsReceipt = new(
                TypeDefinitionRowsRead: 1,
                MethodDefinitionHandlesVisited: _methods.Count,
                MethodDefinitionRowsRead: 0,
                MethodNamesDecoded: 0,
                MethodSignaturesDecoded: 0,
                MethodAttributesDecoded: 0,
                ProjectedRows: _methods.Count);
            _countPlan = Resolve(
                TypeDeclaredMethodPopulationQuery.CreateRequest(
                    QuerySpaceTerminalRequirement.Count));
            _rowsPlan = Resolve(
                TypeDeclaredMethodPopulationQuery.CreateRequest(
                    QuerySpaceTerminalRequirement.Rows));
            _borrowedNLinqCount = BorrowedNLinqCount;
            _borrowedNLinqRows = BorrowedNLinqRows;
            _borrowedNLinqOutcomeCount =
                BorrowedNLinqOutcomeCount;
            _borrowedNLinqOutcomeRows =
                BorrowedNLinqOutcomeRows;
        }

        internal static Asset Open(
            string path,
            MetadataTypeDefinitionName type)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(type);
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local(
                        "ready-snapshot NLinq scorecard"));
            var participant = new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance);
            var workspace = new InspectionWorkspace();
            AssemblyContextGroup? group = null;
            AssemblyInspectionSession? session = null;
            try
            {
                group =
                    workspace.CreateAssemblyContextGroup([participant]);
                ResolvedAssemblyReference retained =
                    Available(
                        group.RetainAssemblyReference(assembly));
                session =
                    AssemblyInspectionSession.Open(retained);
                TypeDeclarationResult.Defined defined =
                    session.ProbeDeclaration(type)
                        as TypeDeclarationResult.Defined
                    ?? throw new InvalidOperationException(
                        $"{DisplayName(type)} was not defined.");
                return new(
                    workspace,
                    group,
                    assembly,
                    participant,
                    session,
                    type,
                    new(
                        session.ModuleVersionId(),
                        defined.Definition),
                    session.GetAdmittedMetadataReader());
            }
            catch
            {
                session?.Dispose();
                group?.Dispose();
                workspace.DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                throw;
            }
        }

        internal MetadataDeclaredMethodPopulationOutcome ExecuteMetadata(
            MetadataDeclaredMethodPopulationTerminal terminal) =>
            _session.DeclaredMethods(
                new(_type, terminal));

        internal int ExecuteNLinqCount()
        {
            MethodHandles methods = Source();
            return methods.Count<
                MethodHandles,
                MethodDefinitionHandle>();
        }

        internal ImmutableArray<int> ExecuteNLinqRows()
        {
            MethodHandles methods = Source();
            var rows =
                ImmutableArray.CreateBuilder<int>(
                    methods.Count<
                        MethodHandles,
                        MethodDefinitionHandle>());
            return methods.Fold<
                    MethodHandles,
                    MethodDefinitionHandle,
                    ImmutableArray<int>.Builder,
                    AddToken>(
                    rows,
                    new AddToken())
                .MoveToImmutable();
        }

        internal MetadataDeclaredMethodPopulationOutcome
            ExecuteNLinqOutcomeCount()
        {
            MethodHandles methods = Source();
            int count = methods.Count<
                MethodHandles,
                MethodDefinitionHandle>();
            return new MetadataDeclaredMethodPopulationOutcome.Counted(
                count,
                s_countReceipt);
        }

        internal MetadataDeclaredMethodPopulationOutcome
            ExecuteNLinqOutcomeRows()
        {
            MethodHandles methods = Source();
            var rows =
                ImmutableArray.CreateBuilder<int>(
                    methods.Count<
                        MethodHandles,
                        MethodDefinitionHandle>());
            ImmutableArray<int> projectedRows =
                methods.Fold<
                        MethodHandles,
                        MethodDefinitionHandle,
                        ImmutableArray<int>.Builder,
                        AddToken>(
                        rows,
                        new AddToken())
                    .MoveToImmutable();
            return new MetadataDeclaredMethodPopulationOutcome.Read(
                projectedRows.Length,
                projectedRows,
                new(
                    TypeDefinitionRowsRead: 1,
                    MethodDefinitionHandlesVisited:
                        projectedRows.Length,
                    MethodDefinitionRowsRead: 0,
                    MethodNamesDecoded: 0,
                    MethodSignaturesDecoded: 0,
                    MethodAttributesDecoded: 0,
                    ProjectedRows: projectedRows.Length));
        }

        internal int ExecuteBorrowedNLinqCount() =>
            Available(
                _group.UseAssemblyImage(
                    _assembly,
                    _borrowedNLinqCount));

        internal ImmutableArray<int> ExecuteBorrowedNLinqRows() =>
            Available(
                _group.UseAssemblyImage(
                    _assembly,
                    _borrowedNLinqRows));

        internal MetadataDeclaredMethodPopulationOutcome
            ExecuteBorrowedNLinqOutcomeCount() =>
                Available(
                    _group.UseAssemblyImage(
                        _assembly,
                        _borrowedNLinqOutcomeCount));

        internal MetadataDeclaredMethodPopulationOutcome
            ExecuteBorrowedNLinqOutcomeRows() =>
                Available(
                    _group.UseAssemblyImage(
                        _assembly,
                        _borrowedNLinqOutcomeRows));

        internal InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            ExecuteCurrentQuerySpace(
                QuerySpaceTerminalRequirement terminal) =>
                TypeDeclaredMethodPopulationInspectionOperation.Execute(
                    Request(terminal));

        internal InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            ExecutePreparedQuerySpaceRequest(
                QuerySpaceTerminalRequirement terminal)
        {
            TypeDeclaredMethodPopulationInspectionRequest request =
                Request(terminal);
            return ExecutePreparedQuerySpace(
                Resolve(request.Query));
        }

        internal InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            ExecutePreparedQuerySpacePlan(
                QuerySpaceTerminalRequirement terminal) =>
                ExecutePreparedQuerySpace(
                    terminal switch
                    {
                        QuerySpaceTerminalRequirement.Count =>
                            _countPlan,
                        QuerySpaceTerminalRequirement.Rows =>
                            _rowsPlan,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(terminal)),
                    });

        internal InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            ExecuteDiscoveryQuerySpaceRequest(
                QuerySpaceTerminalRequirement terminal)
        {
            TypeDeclaredMethodPopulationInspectionRequest request =
                Request(terminal);
            return ExecuteDiscoveryQuerySpace(
                Resolve(request.Query));
        }

        internal InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            ExecuteDiscoveryQuerySpacePlan(
                QuerySpaceTerminalRequirement terminal) =>
                ExecuteDiscoveryQuerySpace(
                    terminal switch
                    {
                        QuerySpaceTerminalRequirement.Count =>
                            _countPlan,
                        QuerySpaceTerminalRequirement.Rows =>
                            _rowsPlan,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(terminal)),
                    });

        private InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            ExecutePreparedQuerySpace(
                TypeDeclaredMethodPopulationQueryResult.Accepted
                    accepted) =>
                accepted.Terminal switch
                {
                    QuerySpaceTerminalRequirement.Count =>
                        _session.SnapshotOperation(
                            this,
                            static access =>
                                access.Operation
                                    .ProjectPreparedQuerySpaceCount()),
                    QuerySpaceTerminalRequirement.Rows =>
                        _session.SnapshotOperation(
                            this,
                            static access =>
                                access.Operation
                                    .ProjectPreparedQuerySpaceRows()),
                    _ => throw new InvalidOperationException(
                        "Unknown prepared QuerySpace terminal."),
                };

        private InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            ExecuteDiscoveryQuerySpace(
                TypeDeclaredMethodPopulationQueryResult.Accepted
                    accepted) =>
                accepted.Terminal switch
                {
                    QuerySpaceTerminalRequirement.Count =>
                        HandoffDiscoveryCount(
                            _session.SnapshotOperation(
                                this,
                                static access =>
                                    access.Operation
                                        .DiscoveryCount())),
                    QuerySpaceTerminalRequirement.Rows =>
                        HandoffDiscoveryRows(
                            _session.SnapshotOperation(
                                this,
                                static access =>
                                    access.Operation
                                        .DiscoveryRows())),
                    _ => throw new InvalidOperationException(
                        "Unknown discovery QuerySpace terminal."),
                };

        private InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            ProjectPreparedQuerySpaceCount()
        {
            var counted = AssertCounted(
                ExecuteNLinqOutcomeCount());
            return Envelope(
                new TypeDeclaredMethodPopulationOutcome.Counted(
                    Subject(),
                    _typeName,
                    Binding(),
                    counted.Count,
                    counted.Receipt));
        }

        private InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            ProjectPreparedQuerySpaceRows()
        {
            var read = AssertRead(
                ExecuteNLinqOutcomeRows());
            return Envelope(
                new TypeDeclaredMethodPopulationOutcome.Read(
                    Subject(),
                    _typeName,
                    Binding(),
                    read.Count,
                    read.Rows,
                    read.Receipt));
        }

        private int DiscoveryCount()
        {
            var methods = new MethodHandles(_methods);
            return methods.Count<
                MethodHandles,
                MethodDefinitionHandle>();
        }

        private InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            HandoffDiscoveryCount(int count) =>
            new(
                new TypeDeclaredMethodPopulationOutcome.Counted(
                    _subject,
                    _typeName,
                    _sectionsBinding,
                    count,
                    s_countReceipt),
                _share,
                ImmutableArray<InspectionDiagnostic>.Empty);

        private InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome>
            HandoffDiscoveryRows(ImmutableArray<int> rows) =>
            new(
                new TypeDeclaredMethodPopulationOutcome.Read(
                    _subject,
                    _typeName,
                    _sectionsBinding,
                    rows.Length,
                    rows,
                    _rowsReceipt),
                _share,
                ImmutableArray<InspectionDiagnostic>.Empty);

        private ImmutableArray<int> DiscoveryRows()
        {
            int[] tokens =
                GC.AllocateUninitializedArray<int>(_methods.Count);
            var methods = new MethodHandles(_methods);
            TokenBuffer buffer =
                methods.Fold<
                    MethodHandles,
                    MethodDefinitionHandle,
                    TokenBuffer,
                    WriteToken>(
                    new(tokens.AsSpan()),
                    new WriteToken());
            if (buffer.Count != tokens.Length)
            {
                throw new InvalidOperationException(
                    "Prepared MethodDef Rows did not fill the exact "
                        + "output buffer.");
            }
            return ImmutableCollectionsMarshal.AsImmutableArray(tokens);
        }

        private TypeDeclaredMethodPopulationInspectionRequest Request(
            QuerySpaceTerminalRequirement terminal) =>
            new(
                _group,
                _participant,
                _typeName,
                _type,
                TypeDeclaredMethodPopulationQuery.CreateRequest(
                    terminal));

        private static TypeDeclaredMethodPopulationQueryResult.Accepted
            Resolve(QuerySpaceRequest request) =>
                TypeDeclaredMethodPopulationQuery.ResolveRequest(request)
                    as TypeDeclaredMethodPopulationQueryResult.Accepted
                ?? throw new InvalidOperationException(
                    "Expected the declared-method QuerySpace request "
                        + "to resolve.");

        private TypeDeclaredMethodPopulationSubject Subject() =>
            new(_subject.Identity, _subject.Provenance);

        private TypeDeclaredMethodPopulationBinding Binding() =>
            new(
                _sectionsBinding.ModuleVersionId,
                _sectionsBinding.TypeDefinitionToken);

        private static InspectionEnvelope<
            TypeDeclaredMethodPopulationOutcome> Envelope(
                TypeDeclaredMethodPopulationOutcome outcome) =>
            new(
                outcome,
                new InspectionShare.NonProjectable(
                    "type-declared-method-population/share",
                    "Raw declared MethodDefs do not yet have a "
                        + "portable Workspace scenario."));

        internal int ExecutePreparedBorrowNLinqCount() =>
            _session.SnapshotOperation(
                this,
                static access =>
                    access.Operation.ExecuteNLinqCount());

        internal ImmutableArray<int>
            ExecutePreparedBorrowNLinqRows() =>
                _session.SnapshotOperation(
                    this,
                    static access =>
                        access.Operation.ExecuteNLinqRows());

        internal MetadataDeclaredMethodPopulationOutcome
            ExecutePreparedBorrowNLinqOutcomeCount() =>
                _session.SnapshotOperation(
                    this,
                    static access =>
                        access.Operation.ExecuteNLinqOutcomeCount());

        internal MetadataDeclaredMethodPopulationOutcome
            ExecutePreparedBorrowNLinqOutcomeRows() =>
                _session.SnapshotOperation(
                    this,
                    static access =>
                        access.Operation.ExecuteNLinqOutcomeRows());

        private int BorrowedNLinqCount(
            scoped AssemblyImageView image)
        {
            ValidateBorrow(image);
            return ExecuteNLinqCount();
        }

        private ImmutableArray<int> BorrowedNLinqRows(
            scoped AssemblyImageView image)
        {
            ValidateBorrow(image);
            return ExecuteNLinqRows();
        }

        private MetadataDeclaredMethodPopulationOutcome
            BorrowedNLinqOutcomeCount(
                scoped AssemblyImageView image)
        {
            ValidateBorrow(image);
            return ExecuteNLinqOutcomeCount();
        }

        private MetadataDeclaredMethodPopulationOutcome
            BorrowedNLinqOutcomeRows(
                scoped AssemblyImageView image)
        {
            ValidateBorrow(image);
            return ExecuteNLinqOutcomeRows();
        }

        private void ValidateBorrow(
            scoped AssemblyImageView image)
        {
            if (!ReferenceEquals(image.Assembly, _assembly))
            {
                throw new InvalidOperationException(
                    "The ready NLinq source belongs to another "
                        + "assembly-context participant.");
            }
        }

        private static T Available<T>(
            AssemblyImageAccessResult<T> result) =>
            result switch
            {
                AssemblyImageAccessResult<T>.Available available =>
                    available.Value,
                AssemblyImageAccessResult<T>.Rejected rejected =>
                    throw new IOException(
                        $"Assembly image unavailable "
                            + $"({rejected.Failure.Kind}): "
                            + rejected.Failure.Detail),
                _ => throw new InvalidOperationException(
                    "Unknown assembly image access result."),
            };

        private MethodHandles Source()
            => new(PrepareMethods(_reader, _type));

        private static MethodDefinitionHandleCollection PrepareMethods(
            MetadataReader reader,
            MetadataTypeDefinitionBinding type)
        {
            Guid moduleVersionId =
                reader.GetGuid(
                    reader.GetModuleDefinition().Mvid);
            if (moduleVersionId != type.ModuleVersionId)
            {
                throw new InvalidOperationException(
                    "The NLinq source binding belongs to another module.");
            }

            EntityHandle entity =
                MetadataTokens.EntityHandle(
                    type.Definition.Value);
            int rowNumber =
                MetadataTokens.GetRowNumber(entity);
            if (entity.Kind != HandleKind.TypeDefinition
                || rowNumber <= 0
                || rowNumber
                    > reader.GetTableRowCount(
                        TableIndex.TypeDef))
            {
                throw new InvalidOperationException(
                    "The NLinq source binding is outside the TypeDef table.");
            }

            return reader.GetTypeDefinition(
                    (TypeDefinitionHandle)entity)
                .GetMethods();
        }

        public void Dispose()
        {
            try
            {
                _session.Dispose();
            }
            finally
            {
                try
                {
                    _group.Dispose();
                }
                finally
                {
                    _workspace.DisposeAsync()
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
            }
        }
    }

    private struct MethodHandles
        : NLinq.IEnumerator<
            MethodHandles,
            MethodDefinitionHandle>
    {
        private MethodDefinitionHandleCollection.Enumerator
            _enumerator;
        private int _remaining;

        internal MethodHandles(
            MethodDefinitionHandleCollection methods)
        {
            _enumerator = methods.GetEnumerator();
            _remaining = methods.Count;
        }

        public static int Count(
            scoped ref MethodHandles source) =>
            source._remaining;

        public MethodDefinitionHandle TryGetNext(
            out bool hasMore)
        {
            hasMore = _enumerator.MoveNext();
            if (!hasMore)
                return default;
            _remaining--;
            return _enumerator.Current;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static TAcc NLinq.IEnumerator<
            MethodHandles,
            MethodDefinitionHandle>.Fold<TAcc, TFunc>(
                scoped ref MethodHandles source,
                TAcc accumulator,
                TFunc func)
        {
            while (source._enumerator.MoveNext())
            {
                source._remaining--;
                accumulator =
                    func.Invoke(
                        accumulator,
                        source._enumerator.Current);
            }
            return accumulator;
        }
    }

    private readonly struct AddToken
        : IFunc<
            ImmutableArray<int>.Builder,
            MethodDefinitionHandle,
            ImmutableArray<int>.Builder>
    {
        public ImmutableArray<int>.Builder Invoke(
            ImmutableArray<int>.Builder rows,
            MethodDefinitionHandle method)
        {
            rows.Add(MetadataTokens.GetToken(method));
            return rows;
        }
    }

    private ref struct TokenBuffer
    {
        internal TokenBuffer(Span<int> tokens)
        {
            Tokens = tokens;
            Count = 0;
        }

        internal Span<int> Tokens { get; }
        internal int Count { get; set; }
    }

    private readonly struct WriteToken
        : IFunc<
            TokenBuffer,
            MethodDefinitionHandle,
            TokenBuffer>
    {
        public TokenBuffer Invoke(
            TokenBuffer buffer,
            MethodDefinitionHandle method)
        {
            buffer.Tokens[buffer.Count++] =
                MetadataTokens.GetToken(method);
            return buffer;
        }
    }
}
