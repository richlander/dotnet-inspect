// Alpha -> Beta -> Gamma -> Alpha is a three-namespace cycle; Delta depends on
// the cycle and Epsilon is the leaf everything reaches: Epsilon is level 0,
// the cycle is level 1, and Delta is level 2.
namespace Alpha
{
    public static class A
    {
        public static int Run() => Beta.B.Run() + Epsilon.E.Value();
    }
}

namespace Beta
{
    public static class B
    {
        public static int Run() => Gamma.G.Run();
    }

    public static class Outer
    {
        // Nested types belong to their outermost type's namespace.
        public static class Inner
        {
            public static int Run() => Epsilon.E.Value();
        }
    }
}

namespace Gamma
{
    public static class G
    {
        static int _depth;

        public static int Run() => ++_depth > 1 ? 0 : Alpha.A.Run();
    }
}

namespace Delta
{
    public static class D
    {
        public static int Run() => Alpha.A.Run();
    }
}

namespace Epsilon
{
    public static class E
    {
        public static int Value() => 1;
    }

    public sealed class Item
    {
    }
}
