using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;
using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

public class DecodeBenchmark
{
    [Fact]
    public void Benchmark_substrate_vs_reaching_definitions()
    {
        using var pe = new PEReader(File.OpenRead(typeof(object).Assembly.Location));
        var reader = pe.GetMetadataReader();

        var bodies = new List<(MethodDefinitionHandle Handle, MethodBodyBlock Body, byte[] Il, ImmutableArray<ExceptionRegion> Regions)>();
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0) continue;
            MethodBodyBlock body;
            try { body = pe.GetMethodBody(method.RelativeVirtualAddress); } catch { continue; }
            byte[] il = body.GetILBytes() ?? [];
            if (il.Length == 0) continue;
            bodies.Add((handle, body, il, body.ExceptionRegions));
        }

        // Warm up both paths (JIT, caches).
        foreach (var b in bodies)
        {
            _ = MethodInstructions.Decode(b.Il, b.Il.Length, b.Regions);
            _ = ReachingDefinitions.BlockStartsForParity(b.Il, b.Regions);
            _ = InstructionDecoder.Decode(b.Il);
        }

        const int iters = 5;
        double sub = Time(iters, bodies, b => MethodInstructions.Decode(b.Il, b.Il.Length, b.Regions));
        double rd = Time(iters, bodies, b => ReachingDefinitions.BlockStartsForParity(b.Il, b.Regions));
        double dec = Time(iters, bodies, b => InstructionDecoder.Decode(b.Il));
        double typed = Time(iters, bodies, b => MethodInstructions.Decode(b.Body).InterpretStack(reader, b.Handle, b.Body));

        Console.WriteLine($"BENCH bodies={bodies.Count} iters={iters}");
        Console.WriteLine($"BENCH decode-only (substrate)     : {dec:F0} ms  ({dec / iters:F0} ms/pass)");
        Console.WriteLine($"BENCH decode+blocks (substrate)   : {sub:F0} ms  ({sub / iters:F0} ms/pass)");
        Console.WriteLine($"BENCH decode+blocks (ReachingDefs): {rd:F0} ms  ({rd / iters:F0} ms/pass)");
        Console.WriteLine($"BENCH ratio substrate/RD          : {sub / rd:F2}x");
        Console.WriteLine($"BENCH decode+blocks+typedstack    : {typed:F0} ms  ({typed / iters:F0} ms/pass)");

        Assert.True(bodies.Count > 10000);
    }

    static double Time<T>(int iters, List<T> items, Func<T, object> work)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iters; i++)
            foreach (var item in items)
                _ = work(item);
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }
}
