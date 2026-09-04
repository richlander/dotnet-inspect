using System;

namespace LadderRung1;

// Rung 1 owned residuals for the decompiler product quality ladder (#1599 /
// #1693). Some mainstream C# 2-7 expression forms decompile to valid, fully
// raised C# at a lower source altitude than the original spelling. They are not
// gaps (the output is Full and binds), but they are not the highest-altitude
// recovery either, so they are pinned here as explicitly owned residuals: the
// guard locks the current valid form so it cannot silently regress to invalid
// output, and documents the altitude that a future raise could still improve.
public class OwnedResiduals
{
    // C# 7 throw expression. `s ?? throw new ArgumentNullException(nameof(s))`
    // recovers as a valid `if (temp is null) throw ...; return temp;` null-check
    // statement rather than the `?? throw` expression. Recovering the expression
    // form is a separate, risk-bearing raise (a hand-written null-check-then-throw
    // is a legitimate alternative source for the same IL), so it is deferred; the
    // current rendering is valid and owned. See #1693.
    public string ThrowExpression(string s)
    {
        return s ?? throw new ArgumentNullException(nameof(s));
    }
}
