public class T
{
}

namespace CorrespondenceIdentity
{
    public class GenericCollisions
    {
        public int NamedType<U>(
            U value,
            global::T other) => 2;

        public int PrimitiveName<T>(T value) => 2;

        public int Tuple((int x, int y) value) => 2;
    }
}

namespace CorrespondenceIdentity.Outer
{
    public class Inner
    {
        public int M() => 2;
    }
}
