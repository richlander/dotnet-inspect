// Minimal stand-ins for the gRPC infrastructure that real generated code calls, plus
// protobuf/gRPC consumer fixtures. The protobuf runtime stand-ins (FileDescriptor,
// GeneratedClrTypeInfo, MessageParser<T>) live in the separate ILInspector.Analysis.ProtobufFixtures
// project, which compiles to an assembly named Google.Protobuf; the consumers below bind to
// those external types so the generated-bootstrap predicates see real Google.Protobuf assembly
// identity (#1580). The gRPC stand-in is matched by namespace + generated __* members, not by
// assembly, so it stays local.

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

    // A generated protobuf message: its static initializer bootstraps the per-message parser.
    public sealed class FakeProtobufMessage
    {
        static FakeProtobufMessage()
        {
            Parser = new Google.Protobuf.MessageParser<FakeProtobufMessage>(() => new FakeProtobufMessage());
        }

        public static Google.Protobuf.MessageParser<FakeProtobufMessage> Parser { get; }
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

    // Hand-written gRPC registration: legitimately calls ServerServiceDefinition.CreateBuilder
    // but declares no generated __* members, so it must NOT be flagged as generated.
    public static class HandWrittenGrpcRegistration
    {
        public static Grpc.Core.ServerServiceDefinition.Builder Register()
            => Grpc.Core.ServerServiceDefinition.CreateBuilder();
    }

    // A user type that declares a generated-looking __Helper_* member but makes no gRPC call.
    // A member name alone must NOT classify it as generated (#1574), otherwise its real
    // optimization opportunities would be suppressed from Performance Triage.
    public static class GeneratedLookalike
    {
        public static int __Helper_NotGenerated() => 1;

        public static int MakesLocalArrayUnsuppressed()
        {
            var a = new int[4];
            a[0] = 1;
            return a[0];
        }
    }
}
