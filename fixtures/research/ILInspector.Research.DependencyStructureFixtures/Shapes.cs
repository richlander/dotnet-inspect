using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// A type in the global namespace.
public static class GlobalType
{
    public static int Run() => Epsilon.E.Value();
}

namespace Lifted
{
    public static class Owner
    {
        public static Func<int> Lambda() => static () => Epsilon.E.Value();

        public static int Local()
        {
            return Inner();

            static int Inner() => Epsilon.E.Value();
        }

        public static async Task<int> Async()
        {
            await Task.Yield();
            return Epsilon.E.Value();
        }

        public static IEnumerable<int> Iterator()
        {
            yield return Epsilon.E.Value();
        }

        // Two lifted bodies whose calls sit at the same IL offset.
        public static int TwoLambdas()
        {
            Func<int> first = static () => Epsilon.E.Value();
            Func<int> second = static () => Epsilon.E.Value();
            return first() + second();
        }
    }
}

namespace Boxes
{
    public class Box<T>
    {
        T _value = default!;

        public T Get() => _value;

        public T Twice()
        {
            Get();
            return Get();
        }
    }
}

namespace BoxUser
{
    // Calls through generic instantiations are MemberRefs on TypeSpecs.
    public static class User
    {
        public static int Closed() => new Boxes.Box<int>().Get();

        public static T Open<T>() => new Boxes.Box<T>().Twice();
    }
}

namespace Referenced
{
    // Task lives in System.Runtime and List<T> in System.Collections, as
    // referenced; neither is folded into the core library.
    public static class Refs
    {
        public static System.Threading.Tasks.Task Yield() => System.Threading.Tasks.Task.CompletedTask;

        public static int Count(List<int> items) => items.Count;
    }
}

namespace Pointers
{
    // Zeta reaches Epsilon only through a method-group ldftn.
    public static class Zeta
    {
        public static Func<int> Capture() => Epsilon.E.Value;
    }
}

namespace Generic
{
    // A call through List<Epsilon.Item> depends on System.Collections.Generic,
    // not on Epsilon.
    public static class Omega
    {
        public static void Add(List<Epsilon.Item> items, Epsilon.Item item) => items.Add(item);
    }
}

namespace Unresolvable
{
    public static unsafe class Theta
    {
        public static int Indirect(delegate*<int> target) => target();

        public static int Array(int[,] grid) => grid[0, 0];
    }
}

namespace Explained
{
    // Seven types call Epsilon with distinct call-site counts, so the
    // Explained -> Epsilon edge keeps five explanations and a remainder of two.
    public static class T1 { public static int Run() => Epsilon.E.Value(); }
    public static class T2 { public static int Run() => Epsilon.E.Value() + Epsilon.E.Value(); }
    public static class T3 { public static int Run() => Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value(); }
    public static class T4 { public static int Run() => Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value(); }
    public static class T5 { public static int Run() => Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value(); }
    public static class T6 { public static int Run() => Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value(); }
    public static class T7 { public static int Run() => Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value() + Epsilon.E.Value(); }
}
