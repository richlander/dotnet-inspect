// Minimal stand-ins for the protobuf/gRPC infrastructure that real generated code calls.
// LibraryBodyIndex.GeneratedFrameworkTypeNames matches structurally by namespace/type/method
// name, so these reproduce the bootstrap signals without a dependency on Google.Protobuf or
// Grpc.Core. Block namespaces are required because the stubs live in foreign namespaces.

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

namespace Grpc.Core
{
    public sealed class ServerServiceDefinition
    {
        public static Builder CreateBuilder() => new();

        public sealed class Builder
        {
        }
    }
}

namespace ILInspector.Analysis.Tests
{
    // A protobuf *Reflection holder: its static initializer bootstraps the descriptor.
    public static class FakeProtobufReflection
    {
        static FakeProtobufReflection()
        {
            var info = new Google.Protobuf.Reflection.GeneratedClrTypeInfo();
            Descriptor = Google.Protobuf.Reflection.FileDescriptor.FromGeneratedCode([], [], info);
        }

        public static Google.Protobuf.Reflection.FileDescriptor Descriptor { get; }
    }

    // A gRPC service stub: declares a __Helper_ member and binds via ServerServiceDefinition.
    public static class FakeGrpcServiceStub
    {
        public static byte[] __Helper_SerializeMessage(object message) => [];

        public static Grpc.Core.ServerServiceDefinition.Builder BindService()
            => Grpc.Core.ServerServiceDefinition.CreateBuilder();
    }

    // Uses a protobuf type but does not bootstrap generated infrastructure — must NOT be flagged.
    public static class NormalProtobufConsumer
    {
        public static Google.Protobuf.Reflection.FileDescriptor Passthrough(Google.Protobuf.Reflection.FileDescriptor descriptor) => descriptor;
    }
}
