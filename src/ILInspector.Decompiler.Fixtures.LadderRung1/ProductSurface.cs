using System;
using System.Collections.Generic;
using System.Linq;

namespace LadderRung1;

// Rung 1 product-path surface for the decompiler product quality ladder (#1599 /
// #1695). These C# 2-7 forms only recover correctly through the *product* path —
// the one users hit via `dotnet-inspect type ... -S "Decompiled Source"` — which
// wires the cross-method import seam and a platform assembly locator. The bare
// per-method seam the other rung gates ride does NOT wire those, so it
// under-resolves these constructs (lambdas leak <>c names, cross-assembly
// out-args render `ref`). LadderRung1ProductPathGateTests composes this type
// through TypeSourceComposer with a runtime platform locator and pins the real
// product rendering, closing the seam-vs-product blind spot. Other rungs can
// follow this same model for seam-sensitive constructs.
public class ProductSurface
{
    // C# 3 lambda — raised through the cross-method import seam.
    public Func<int, int> Lambda()
    {
        return x => x * 2;
    }

    // C# 3 lambda capturing a local — display-class environment folded back.
    public Func<int, int> CapturingLambda(int factor)
    {
        return x => x * factor;
    }

    // C# 3 LINQ — query operators with raised lambda arguments.
    public IEnumerable<int> LinqQuery(int[] values)
    {
        return from value in values where value > 0 select value * 2;
    }

    // C# 7 out variable to a cross-assembly method — the platform locator
    // resolves int.TryParse's parameter rows so the call renders `out`, not `ref`.
    public bool OutVariable(string text)
    {
        return int.TryParse(text, out int result) && result > 0;
    }
}
