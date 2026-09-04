using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace ILInspector.Analysis.FacadeFixtures
{
    // Real framework positives whose declaring assembly is a legacy facade
    // (netstandard here; System.Core on net48), not the modern split assembly.
    // The signal predicates must still recognize them (#1708 Row B).
    public static class FacadeSignalFixtures
    {
        // System.Linq.Enumerable.ToArray -> copy signal.
        public static int[] EnumerableToArrayCopy(IEnumerable<int> values)
            => values.ToArray();

        // System.Linq.Enumerable.ToList -> copy signal.
        public static List<int> EnumerableToListCopy(IEnumerable<int> values)
            => values.ToList();

        // System.Linq.Expressions.* -> reflection signal.
        public static Expression BuildsExpression()
            => Expression.Constant(42);
    }
}
