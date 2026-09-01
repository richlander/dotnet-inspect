using System;
using System.Runtime.CompilerServices;

// A real compiler-produced type-forwarder row, so a Research target request can
// distinguish "the declaring type is absent from this input" from "the
// declaring type is forwarded away from this input". A forwarder is the only
// evidence that makes the second case unprovable as absence.
[assembly: TypeForwardedTo(typeof(Uri))]

namespace ILInspector.Research.TargetFixtures
{
    /// <summary>
    /// Compiler-produced member shapes for Research target requests: a plain
    /// method, an ambiguous overload pair, a read/write property, a read-only
    /// property, an event, and a field with no physical method relationship.
    /// </summary>
    public class TargetSample
    {
        int _value;

        public int Field;

        public int Value
        {
            get => _value;
            set => _value = value;
        }

        public int ReadOnlyValue => _value;

        public event EventHandler Changed
        {
            add => GC.KeepAlive(value);
            remove => GC.KeepAlive(value);
        }

        public int Method() => _value + 1;

        public int Overloaded(int value) => value + 1;

        public int Overloaded(string value) => value.Length;

        // Seventeen or more overloads of one name guarantee, by pigeonhole over
        // the sixteen hex digits, that at least two stable fingerprints share
        // their first character. That makes an ambiguous one-character digest
        // prefix reachable without hard-coding a hash.
        public int Many() => 0;
        public int Many(int a) => a;
        public int Many(long a) => (int)a;
        public int Many(short a) => a;
        public int Many(byte a) => a;
        public int Many(sbyte a) => a;
        public int Many(uint a) => (int)a;
        public int Many(ulong a) => (int)a;
        public int Many(ushort a) => a;
        public int Many(char a) => a;
        public int Many(bool a) => a ? 1 : 0;
        public int Many(float a) => (int)a;
        public int Many(double a) => (int)a;
        public int Many(decimal a) => (int)a;
        public int Many(string a) => a.Length;
        public int Many(object a) => a.GetHashCode();
        public int Many(int[] a) => a.Length;
        public int Many(long[] a) => a.Length;
        public int Many(string[] a) => a.Length;
        public int Many(object[] a) => a.Length;
        public int Many(int a, int b) => a + b;
        public int Many(int a, long b) => a + (int)b;
        public int Many(int a, string b) => a + b.Length;
        public int Many(string a, string b) => a.Length + b.Length;
    }

    /// <summary>
    /// A nested declaring type, so declaring-type intent is exercised against a
    /// nested metadata full name rather than a top-level one alone.
    /// </summary>
    public class TargetOuter
    {
        public class TargetInner
        {
            public int Method() => 1;
        }
    }
}
