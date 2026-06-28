using System;
using Microsoft.AspNetCore.Components.Rendering;

namespace ILInspector.Analysis.RenderFixtures;

public static class RealRenderFixtures
{
    // Takes the REAL framework RenderTreeBuilder and allocates a capturing delegate inside a
    // loop. Because RenderTreeBuilder carries a trusted framework public-key-token, this is
    // recognized as intrinsic Blazor render plumbing and its allocations are suppressed.
    public static int RenderWithDelegateLoop(RenderTreeBuilder builder, int[] values, int seed)
    {
        var total = 0;
        foreach (var v in values)
        {
            Func<int> handler = () => v + seed;
            total += handler();
        }

        return total;
    }
}
