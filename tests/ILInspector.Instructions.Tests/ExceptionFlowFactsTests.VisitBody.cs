using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.Instructions.Tests;

// Gates InstructionDecoder.VisitBody against the materializing decode it
// stands in for (docs/design/instruction-substrate.md#layer-0--layer-1).
public partial class ExceptionFlowFactsTests
{
    enum DecodeVerdict
    {
        Complete,
        Incomplete,
        Throws,
    }

    static readonly byte[] s_mutationBytes =
        [0x00, 0x2B, 0x38, 0x45, 0xDD, 0xFE, 0xFF];

    [Fact]
    public void VisitBody_AgreesWithMaterializationOverRealBodies()
    {
        foreach (string path in new[] { typeof(object).Assembly.Location, SelfPath })
        {
            using var session = AssemblyInspectionSession.Open(path);
            using var pe = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(path));
            MetadataReader reader = pe.GetMetadataReader();
            int bodies = 0;
            var mismatches = new List<string>();
            foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
            {
                if (reader.GetMethodDefinition(handle).RelativeVirtualAddress == 0)
                    continue;
                int token = MetadataTokens.GetToken(handle);
                if (session.MethodBodies.Read(token)
                    is not MethodBodyReadResult.Available available)
                {
                    continue;
                }

                bodies++;
                DecodeVerdict expected = Materialized(available.Body, out int instructions);
                DecodeVerdict actual = Visited(available.Body, out int visited);
                if (expected != actual
                    || (expected != DecodeVerdict.Throws && visited != instructions))
                {
                    mismatches.Add(
                        $"0x{token:X8}: materialized {expected} ({instructions}), visited {actual} ({visited})");
                }
            }

            Assert.True(bodies > 0, path);
            Assert.True(
                mismatches.Count == 0,
                $"{Path.GetFileName(path)}: {string.Join("; ", mismatches.Take(10))}");
        }
    }

    [Fact]
    public void VisitBody_AgreesWithMaterializationOverMutatedBodies()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        var mismatches = new List<string>();
        int checkedBodies = 0;
        foreach (MethodInfo sample in typeof(ExceptionFlowFactsSamples).GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            int token = sample.MetadataToken;
            MethodBodyData original = ReadBody(image, token)
                ?? throw new InvalidOperationException(sample.Name);
            int code = MethodCodeOffset(image, token);
            for (int position = 0; position < original.IL.Length; position++)
            {
                byte saved = image[code + position];
                foreach (byte value in s_mutationBytes)
                {
                    if (value == saved)
                        continue;
                    image[code + position] = value;
                    Compare(image, token, $"{sample.Name} IL+{position}=0x{value:X2}");
                }
                image[code + position] = saved;
            }

            int clauses = original.ExceptionRegionCatalog.Clauses.Length;
            for (int ordinal = 0; ordinal < clauses; ordinal++)
            {
                MethodExceptionClause clause = original.ExceptionRegionCatalog.Clauses[ordinal];
                foreach ((int start, int length) in Shifted(clause.ProtectedExtent))
                {
                    MutateAndCompare(
                        image,
                        token,
                        bytes => WriteTryExtent(bytes, token, ordinal, start, length),
                        $"{sample.Name} try[{ordinal}]={start}+{length}");
                }
                foreach ((int start, int length) in Shifted(clause.HandlerExtent))
                {
                    MutateAndCompare(
                        image,
                        token,
                        bytes => WriteHandlerExtent(bytes, token, ordinal, start, length),
                        $"{sample.Name} handler[{ordinal}]={start}+{length}");
                }
                for (int other = ordinal + 1; other < clauses; other++)
                {
                    int right = other;
                    MutateAndCompare(
                        image,
                        token,
                        bytes => SwapClauseRows(bytes, token, ordinal, right),
                        $"{sample.Name} swap[{ordinal},{right}]");
                }
            }
        }

        Assert.True(checkedBodies > 100, $"Only {checkedBodies} mutated bodies were readable.");
        Assert.True(
            mismatches.Count == 0,
            string.Join("; ", mismatches.Take(10)));

        void MutateAndCompare(byte[] source, int token, Action<byte[]> mutate, string label)
        {
            byte[] mutated = (byte[])source.Clone();
            try
            {
                mutate(mutated);
            }
            catch (OverflowException)
            {
                return;
            }
            Compare(mutated, token, label);
        }

        void Compare(byte[] bytes, int token, string label)
        {
            if (ReadBody(bytes, token) is not { } body)
                return;
            checkedBodies++;
            DecodeVerdict expected = Materialized(body, out int instructions);
            DecodeVerdict actual = Visited(body, out int visited);
            if (expected != actual
                || (expected != DecodeVerdict.Throws && visited != instructions))
            {
                mismatches.Add(
                    $"{label}: materialized {expected} ({instructions}), visited {actual} ({visited})");
            }
        }
    }

    [Fact]
    public void VisitBody_StopsWhenTheVisitorStops()
    {
        (MethodBodyData body, _) =
            Decode(nameof(ExceptionFlowFactsSamples.SharedCatchExtent));
        int visited = 0;
        MethodBodyVisitOutcome outcome = InstructionDecoder.VisitBody(
            body,
            ref visited,
            static (ref int count, ILOpCode _, int _, int _) => ++count < 2);

        Assert.Equal(MethodBodyVisitOutcome.Stopped, outcome);
        Assert.Equal(2, visited);
    }

    static IEnumerable<(int Start, int Length)> Shifted(MethodBodyExtent extent)
    {
        foreach (int start in new[] { extent.Start - 1, extent.Start, extent.Start + 1 })
        {
            foreach (int end in new[] { extent.End - 1, extent.End, extent.End + 1 })
            {
                if (start >= 0
                    && end > start
                    && (start, end) != (extent.Start, extent.End))
                {
                    yield return (start, end - start);
                }
            }
        }
    }

    static MethodBodyData? ReadBody(byte[] image, int token)
    {
        using var session = AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(image, writable: false));
        return session.MethodBodies.Read(token)
            is MethodBodyReadResult.Available available
                ? available.Body
                : null;
    }

    static DecodeVerdict Materialized(MethodBodyData body, out int instructions)
    {
        instructions = 0;
        try
        {
            MethodInstructions method =
                InstructionSequence.CreateResolved(body.IL).Materialize(body);
            instructions = method.Instructions.Length;
            return method.IsComplete ? DecodeVerdict.Complete : DecodeVerdict.Incomplete;
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            return DecodeVerdict.Throws;
        }
    }

    static DecodeVerdict Visited(MethodBodyData body, out int visited)
    {
        visited = 0;
        try
        {
            MethodBodyVisitOutcome outcome = InstructionDecoder.VisitBody(
                body,
                ref visited,
                static (ref int count, ILOpCode _, int _, int _) =>
                {
                    count++;
                    return true;
                });
            Assert.NotEqual(MethodBodyVisitOutcome.Stopped, outcome);
            return outcome == MethodBodyVisitOutcome.Complete
                ? DecodeVerdict.Complete
                : DecodeVerdict.Incomplete;
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            return DecodeVerdict.Throws;
        }
    }

    static bool IsDecodeFailure(Exception ex) =>
        ex is BadImageFormatException
            or ArgumentOutOfRangeException
            or InvalidOperationException;
}
