public class T
{
}

namespace CorrespondenceIdentity
{
    public class GenericCollisions
    {
        public int NamedType<T>(
            T value,
            global::T other) => 1;

        public int PrimitiveName<@int>(@int value) => 1;

        public int Tuple((int left, int right) value) => 1;
    }

    public class Outer
    {
        public class Inner
        {
            public int M() => 1;
        }
    }
}
