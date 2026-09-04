// libB
namespace Shared
{
    public sealed class Token { }
}

namespace StructuralBoundary.Outer
{
    public sealed class Inner { }
}

namespace StructuralBoundaryProbe
{
    public static class Api
    {
        public static void Accept(StructuralBoundary.Outer.Inner value) { }
    }
}
