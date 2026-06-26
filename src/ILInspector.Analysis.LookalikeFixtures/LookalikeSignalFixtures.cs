namespace ILInspector.Analysis.LookalikeFixtures
{
    public static class LookalikeSignalFixtures
    {
        public static int CallsFakeReflection() => System.Reflection.UserReflector.GetMethods();

        public static int CallsFakeUnsafe() => System.Runtime.CompilerServices.Unsafe.As(41);

        public static byte CallsFakeBitConverter() => System.BitConverter.GetBytes(1)[0];
    }

    // Calls user-defined Google.Protobuf.* lookalike types declared in THIS assembly
    // (named ILInspector.Analysis.LookalikeFixtures, not Google.Protobuf). The protobuf
    // generated-bootstrap predicates require real Google.Protobuf assembly identity, so
    // this product type must NOT be classified as generated and its optimization
    // opportunities must stay actionable (#1580).
    public sealed class ProtobufBootstrapLookalike
    {
        public static object CallsLookalikeDescriptor()
            => Google.Protobuf.Reflection.FileDescriptor.FromGeneratedCode([], [], new Google.Protobuf.Reflection.GeneratedClrTypeInfo());

        public static object CallsLookalikeMessageParser()
            => new Google.Protobuf.MessageParser<ProtobufBootstrapLookalike>(() => new ProtobufBootstrapLookalike());

        public static int[] ShouldStillBeActionable() => new int[4];
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

// User-defined Google.Protobuf.* lookalikes declared in a user assembly (not the real
// Google.Protobuf runtime). Structurally identical to the protobuf bootstrap shapes, so
// name/namespace-only matching would misclassify their callers as generated (#1580).
namespace Google.Protobuf.Reflection
{
    public sealed class FileDescriptor
    {
        public static FileDescriptor FromGeneratedCode(byte[] descriptorData, FileDescriptor[] dependencies, GeneratedClrTypeInfo info) => new();
    }

    public sealed class GeneratedClrTypeInfo
    {
    }
}

namespace Google.Protobuf
{
    public sealed class MessageParser<T>
    {
        public MessageParser(Func<T> factory)
        {
        }
    }
}
