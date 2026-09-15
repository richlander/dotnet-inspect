using System;
using System.Linq;

namespace LadderRung1;

// Rung 1 combined-code frontier for the decompiler product quality ladder (#1599
// / #1710). The other rung 1 fixtures exercise each construct in ISOLATION in a
// tiny method, and all raise to Full. But realistic code combines them, and the
// structuring pass is fragile to those combinations: a method that mixes a
// foreach loop, a switch, and a LINQ/lambda can tip the whole method into a
// goto-ladder fallback, which also defeats lambda raising (the delegate target
// leaks a `<>c` name). The output is honestly classified Partial (never a lying
// Full), but it is not recognizable C# — so these realistic shapes are an
// explicitly OWNED frontier, not a silent gap.
//
// This fixture deliberately has NO field initializer: a field initializer is
// dropped by the whole-type composer (a separate defect, #1713), which would
// make the constructor a misleading "clean Full" control. The default
// constructor here is genuinely empty, so it is honestly Full.
//
// LadderRung1CombinedFrontierGateTests pins these as honest-degrade Partial
// frontiers: it proves the degradation is honest (no malformed Full) and locks
// the frontier so that (a) it can never regress into an invalid Full claim and
// (b) when the structuring pass is improved to raise them, the "is Partial"
// assertion flips and forces an intentional promotion to a positive guard.
public class CombinedFrontier
{
    // A trivial member that genuinely raises to Full — the control that proves
    // the gate's "simple members stay Full" assertion is not vacuous.
    public int Echo(int value)
    {
        return value;
    }

    // Realistic combination: foreach + switch + LINQ + interpolation + tuple in
    // one method. Currently degrades to a whole-method goto ladder with a leaked
    // `<>c` lambda target.
    public (int Count, decimal Total) Summarize(decimal[] prices, string label)
    {
        decimal total = 0m;
        foreach (decimal price in prices)
        {
            total += price;
        }

        int bucket;
        switch (prices.Length)
        {
            case 0: bucket = 0; break;
            case 1: bucket = 1; break;
            default: bucket = 2; break;
        }

        decimal[] positives = prices.Where(price => price > 0m).ToArray();
        string tag = label ?? "untitled";
        Console.WriteLine($"{tag}: {positives.Length} positive, bucket {bucket}");
        return (positives.Length, total);
    }

    // Minimal trigger isolated from the realistic method: an assign-and-break
    // switch followed by a LINQ/lambda in the same method is enough to collapse
    // the switch into a goto ladder.
    public int SwitchThenLinq(int[] values)
    {
        int bucket;
        switch (values.Length)
        {
            case 0: bucket = 10; break;
            case 1: bucket = 20; break;
            default: bucket = 30; break;
        }

        int[] positives = values.Where(value => value > 0).ToArray();
        return bucket + positives.Length;
    }
}
