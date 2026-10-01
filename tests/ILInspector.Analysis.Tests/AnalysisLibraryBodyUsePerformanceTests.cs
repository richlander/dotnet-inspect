using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.AnalysisHarness;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class AnalysisLibraryBodyUsePerformanceTests(
    ITestOutputHelper output)
{
    [Fact]
    [Trait("Speed", "Slow")]
    public void DirectAndProductRoutesRemainEquivalentWithMeasuredCost()
    {
        string artifacts = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts");
        string systemTextJson = Path.Combine(
            artifacts,
            "packages",
            "System.Text.Json.10.0.0.dll");
        string coreLibrary = Path.Combine(
            artifacts,
            "System.Private.CoreLib.dll");
        BodyUseScorecardCheck scorecard = BodyUseScorecard.Check(
            BodyUseScorecard.LoadAssets(
                [systemTextJson, coreLibrary]),
            cancellationToken:
                TestContext.Current.CancellationToken);
        Assert.True(
            scorecard.Agrees,
            string.Join(
                Environment.NewLine,
                scorecard.Mismatches.Select(static mismatch =>
                    $"{mismatch.Asset} {mismatch.Column}: "
                        + $"{mismatch.Answer}; oracle "
                        + mismatch.OracleAnswer)));

        Measurement[] measurements =
        [
            Measure(
                "System.Text.Json",
                systemTextJson),
            Measure(
                "System.Private.CoreLib",
                coreLibrary),
        ];

        string[] lines =
        [
            "asset\ttypes\tbodies\toperands\toccurrences"
                + "\tbefore_ms\tbefore_bytes\tafter_ms\tafter_bytes"
                + "\ttime_ratio\tallocation_ratio\tunits_visited"
                + "\tbody_acquisitions\tlookup_uses",
            .. measurements.Select(Format),
        ];
        foreach (string line in lines)
            output.WriteLine(line);

        string? reportPath =
            Environment.GetEnvironmentVariable(
                "DOTNET_INSPECT_BODY_USE_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
            File.WriteAllLines(reportPath, lines);
    }

    Measurement Measure(string asset, string path)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        ReferenceResult before =
            Reference(path, cancellationToken);
        AnalysisLibraryBodyUseResult after =
            Product(path, cancellationToken);
        Assert.Equal(before.Types, after.Types.Length);
        Assert.Equal(
            before.Bodies,
            after.Coverage.BodiesConsidered);
        Assert.Equal(
            before.Operands,
            after.Coverage.OperandsConsidered);
        Assert.Equal(
            before.Occurrences,
            [.. after.Occurrences.Select(
                static occurrence =>
                    new CanonicalOccurrence(
                        occurrence.Source.Definition.Value,
                        occurrence.Target.Definition.Value,
                        occurrence.PhysicalMethodToken,
                        occurrence.OperandKind,
                        occurrence.OperandToken,
                        occurrence.IlOffset,
                        occurrence.OccurrenceOrdinal))]);
        if (asset == "System.Text.Json")
        {
            Assert.Equal(
                "System.Text.Json",
                after.Receipt.Assembly.Name);
            Assert.Equal(
                new Version(10, 0, 0, 0),
                after.Receipt.Assembly.Version);
        }

        for (int index = 0; index < 2; index++)
        {
            _ = Reference(path, cancellationToken);
            _ = Product(path, cancellationToken);
        }

        const int repetitions = 7;
        var beforeTimes = new double[repetitions];
        var beforeAllocations = new long[repetitions];
        var afterTimes = new double[repetitions];
        var afterAllocations = new long[repetitions];
        for (int index = 0; index < repetitions; index++)
        {
            if ((index & 1) == 0)
            {
                MeasureReference(
                    path,
                    cancellationToken,
                    index,
                    beforeTimes,
                    beforeAllocations);
                MeasureProduct(
                    path,
                    cancellationToken,
                    index,
                    afterTimes,
                    afterAllocations);
            }
            else
            {
                MeasureProduct(
                    path,
                    cancellationToken,
                    index,
                    afterTimes,
                    afterAllocations);
                MeasureReference(
                    path,
                    cancellationToken,
                    index,
                    beforeTimes,
                    beforeAllocations);
            }
        }

        ProducerParticipation participation =
            Assert.Single(after.Receipt.Work.Producers);
        return new(
            asset,
            after.Types.Length,
            after.Coverage.BodiesConsidered,
            after.Coverage.OperandsConsidered,
            after.Occurrences.Length,
            Median(beforeTimes),
            Median(beforeAllocations),
            Median(afterTimes),
            Median(afterAllocations),
            after.Receipt.Work.UnitsVisited,
            Layer(participation, "Body"),
            Layer(participation, "ModuleLookup"));
    }

    static void MeasureReference(
        string path,
        CancellationToken cancellationToken,
        int index,
        double[] times,
        long[] allocations)
    {
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long timestamp = Stopwatch.GetTimestamp();
        ReferenceResult result = Reference(path, cancellationToken);
        times[index] =
            Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
        allocations[index] =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        GC.KeepAlive(result);
    }

    static void MeasureProduct(
        string path,
        CancellationToken cancellationToken,
        int index,
        double[] times,
        long[] allocations)
    {
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long timestamp = Stopwatch.GetTimestamp();
        AnalysisLibraryBodyUseResult result =
            Product(path, cancellationToken);
        times[index] =
            Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
        allocations[index] =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        GC.KeepAlive(result);
    }

    static ReferenceResult Reference(
        string path,
        CancellationToken cancellationToken)
    {
        using FileStream stream = File.OpenRead(path);
        using var image = new PEReader(
            stream,
            PEStreamOptions.PrefetchEntireImage);
        MetadataReader reader = image.GetMetadataReader();
        using var builder =
            new LibraryBodyAnalysisBuilder(path, reader, image);
        var runner = new LibraryMethodAnalysisRunner(builder);
        var occurrences =
            ImmutableArray.CreateBuilder<CanonicalOccurrence>();
        int types = 0;
        int bodies = 0;
        int operands = 0;
        foreach (TypeDefinitionHandle typeHandle
            in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeDefinition type =
                reader.GetTypeDefinition(typeHandle);
            if (!AnalysisLibraryBodyUseProducer
                    .IsTypeInPopulation(reader, type))
                continue;
            TypeRef decodedType =
                TypeRefDecoder.Instance.GetTypeFromDefinition(
                    reader,
                    typeHandle,
                    0);
            if (decodedType.Resolution?.Type is { } name
                && !(name.Namespace.Length == 0
                    && name.Segments is ["<Module>"]))
            {
                types++;
            }

            foreach (MethodDefinitionHandle methodHandle
                in type.GetMethods())
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0
                    || !LibraryMethodAnalysisRunner
                        .HasManagedIlBody(method.ImplAttributes))
                {
                    continue;
                }

                bodies++;
                BodyTypeUseMethodFact fact =
                    runner.AnalyzeBodyTypeUses(
                        typeHandle,
                        type,
                        methodHandle,
                        method,
                        image.GetMethodBody(
                            method.RelativeVirtualAddress),
                        ProducerTerminal.Rows,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        cancellationToken);
                operands += fact.OperandsConsidered;
                if (fact.Fidelity
                    != AnalysisLibraryBodyUseFidelity.LogicalOwner)
                {
                    continue;
                }
                foreach (BodyTypeUseOccurrence occurrence
                    in fact.Occurrences)
                {
                    occurrences.Add(
                        new(
                            MetadataTokens.GetToken(
                                occurrence.Source),
                            MetadataTokens.GetToken(
                                occurrence.Target),
                            occurrence.PhysicalMethodToken,
                            occurrence.OperandKind,
                            occurrence.OperandToken,
                            occurrence.IlOffset,
                            occurrence.OccurrenceOrdinal));
                }
            }
        }

        return new(
            types,
            bodies,
            operands,
            occurrences.ToImmutable());
    }

    static AnalysisLibraryBodyUseResult Product(
        string path,
        CancellationToken cancellationToken) =>
        AnalysisLibraryBodyUseService.ExecutePath(
            path,
            new(),
            cancellationToken) switch
        {
            AnalysisLibraryBodyUseOutcome.Available available =>
                available.Result,
            AnalysisLibraryBodyUseOutcome.Rejected rejected =>
                throw new Xunit.Sdk.XunitException(
                    $"{rejected.Kind}: {rejected.Detail}"),
            _ => throw new Xunit.Sdk.XunitException(
                "The product route returned an unknown outcome."),
        };

    static int Layer(
        ProducerParticipation participation,
        string name) =>
        participation.Layers.Single(
            layer => layer.Layer == name).Acquired;

    static double Median(double[] values)
    {
        double[] sorted = [.. values.Order()];
        return sorted[sorted.Length / 2];
    }

    static long Median(long[] values)
    {
        long[] sorted = [.. values.Order()];
        return sorted[sorted.Length / 2];
    }

    static string Format(Measurement measurement) =>
        string.Join(
            '\t',
            measurement.Asset,
            measurement.Types.ToString(CultureInfo.InvariantCulture),
            measurement.Bodies.ToString(CultureInfo.InvariantCulture),
            measurement.Operands.ToString(CultureInfo.InvariantCulture),
            measurement.Occurrences.ToString(
                CultureInfo.InvariantCulture),
            measurement.BeforeMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.BeforeBytes.ToString(
                CultureInfo.InvariantCulture),
            measurement.AfterMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.AfterBytes.ToString(
                CultureInfo.InvariantCulture),
            (measurement.AfterMilliseconds
                / measurement.BeforeMilliseconds).ToString(
                    "F4",
                    CultureInfo.InvariantCulture),
            ((double)measurement.AfterBytes
                / measurement.BeforeBytes).ToString(
                    "F4",
                    CultureInfo.InvariantCulture),
            measurement.UnitsVisited.ToString(
                CultureInfo.InvariantCulture),
            measurement.BodyAcquisitions.ToString(
                CultureInfo.InvariantCulture),
            measurement.LookupUses.ToString(
                CultureInfo.InvariantCulture));

    sealed record ReferenceResult(
        int Types,
        int Bodies,
        int Operands,
        ImmutableArray<CanonicalOccurrence> Occurrences);

    sealed record CanonicalOccurrence(
        int SourceToken,
        int TargetToken,
        int PhysicalMethodToken,
        AnalysisLibraryBodyUseOperandKind OperandKind,
        int OperandToken,
        int IlOffset,
        int OccurrenceOrdinal);

    sealed record Measurement(
        string Asset,
        int Types,
        int Bodies,
        int Operands,
        int Occurrences,
        double BeforeMilliseconds,
        long BeforeBytes,
        double AfterMilliseconds,
        long AfterBytes,
        int UnitsVisited,
        int BodyAcquisitions,
        int LookupUses);

}
