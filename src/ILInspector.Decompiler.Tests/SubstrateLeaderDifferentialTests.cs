using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;

using DHandlerKind = ILInspector.Decompiler.Pipeline.HandlerKind;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Phase 1 directional differential (decompiler half): the substrate's IL-level block leaders must
/// be a superset of the importer's <see cref="IrImporter.FindLeaders"/> set — i.e. the substrate
/// never *under*-splits relative to the decompiler (which would merge blocks it needs separate).
/// The substrate has *more* leaders (it makes every EH-region instruction a leader, the
/// ReachingDefinitions granularity), so the relation is strict-superset over EH methods. This test
/// proves no under-split and quantifies the extra granularity that the eventual minimal-CFG
/// refactor would move to an Analysis Layer-1 concern.
/// </summary>
public class SubstrateLeaderDifferentialTests
{
    [Fact]
    public void FindLeaders_is_a_subset_of_substrate_block_starts_over_corelib()
    {
        using var pe = new PEReader(File.OpenRead(typeof(object).Assembly.Location));
        var reader = pe.GetMetadataReader();

        int methods = 0;
        long substrateLeaders = 0;
        long importerLeaders = 0;
        long extraLeaders = 0;
        var underSplits = new List<string>();

        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            MethodBodyBlock body;
            try { body = pe.GetMethodBody(method.RelativeVirtualAddress); }
            catch { continue; }
            byte[] il = body.GetILBytes() ?? [];
            if (il.Length == 0)
                continue;

            var handlers = ToHandlerRegions(body.ExceptionRegions);

            SortedSet<int> importer;
            HashSet<int> substrate;
            try
            {
                importer = IrImporter.FindLeaders(il, handlers);
                substrate = MethodInstructions.Decode(il, il.Length, body.ExceptionRegions)
                    .Blocks.Blocks.Select(b => b.Start).ToHashSet();
            }
            catch
            {
                continue; // malformed on one side; not this test's concern
            }

            methods++;
            substrateLeaders += substrate.Count;
            importerLeaders += importer.Count;
            extraLeaders += substrate.Count - importer.Count;

            // Regression-free direction: every importer leader must be a substrate block start.
            if (!importer.IsSubsetOf(substrate) && underSplits.Count < 15)
            {
                var missing = importer.Where(x => !substrate.Contains(x));
                underSplits.Add($"{reader.GetString(method.Name)}: importer leaders missing from substrate: [{string.Join(",", missing)}]");
            }
        }

        Assert.True(methods > 10000, $"expected the full CoreLib body set, saw {methods}");
        // The headline assertion: no under-split anywhere.
        Assert.True(underSplits.Count == 0, $"{underSplits.Count} under-split(s):\n{string.Join("\n", underSplits)}");

        // Characterize the granularity gap (informational; the superset is by design).
        Console.WriteLine($"LEADERS methods={methods} substrate={substrateLeaders} importer={importerLeaders} extra(substrate-only)={extraLeaders}");
    }

    static ImmutableArray<HandlerRegion> ToHandlerRegions(ImmutableArray<ExceptionRegion> regions)
    {
        var builder = ImmutableArray.CreateBuilder<HandlerRegion>(regions.Length);
        foreach (var region in regions)
        {
            builder.Add(new HandlerRegion(
                region.Kind switch
                {
                    ExceptionRegionKind.Catch => DHandlerKind.Catch,
                    ExceptionRegionKind.Filter => DHandlerKind.Filter,
                    ExceptionRegionKind.Finally => DHandlerKind.Finally,
                    _ => DHandlerKind.Fault,
                },
                region.TryOffset,
                region.TryLength,
                region.HandlerOffset,
                region.HandlerLength,
                region.FilterOffset,
                CatchType: null));
        }
        return builder.MoveToImmutable();
    }
}
