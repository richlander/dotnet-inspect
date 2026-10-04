using System.Collections.Generic;

// Declared in namespace System.Linq inside an assembly named System.Linq, but
// unsigned -> no framework public-key-token. Strong identity (#1708 Row A) must not
// classify a call to this Enumerable.ToArray lookalike as a framework copy.
namespace System.Linq
{
    public static class Enumerable
    {
        public static int[] ToArray(IEnumerable<int> values) => new int[0];

        public static bool Any(
            IEnumerable<int> values,
            System.Func<int, bool> predicate)
            => false;
    }

    public static class Spoofer
    {
        public static int[] CallsFakeEnumerableToArray(IEnumerable<int> values)
            => Enumerable.ToArray(values);

        public static int CallsFakeEnumerableAnyInLoop(
            IEnumerable<int> values,
            int[] keys)
        {
            int matches = 0;
            foreach (int key in keys)
            {
                if (Enumerable.Any(values, value => value == key))
                    matches++;
            }

            return matches;
        }
    }
}
