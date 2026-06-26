namespace ILInspector.Analysis.LookalikeFixtures
{
    public static class LookalikeSignalFixtures
    {
        public static int CallsFakeReflection() => System.Reflection.UserReflector.GetMethods();

        public static int CallsFakeUnsafe() => System.Runtime.CompilerServices.Unsafe.As(41);

        public static byte CallsFakeBitConverter() => System.BitConverter.GetBytes(1)[0];
    }
}

namespace System.Reflection
{
    public static class UserReflector
    {
        public static int GetMethods() => 42;
    }
}

namespace System.Runtime.CompilerServices
{
    public static class Unsafe
    {
        public static int As(int value) => value + 1;
    }
}

namespace System
{
    public static class BitConverter
    {
        public static byte[] GetBytes(int value) => [(byte)value];
    }
}
