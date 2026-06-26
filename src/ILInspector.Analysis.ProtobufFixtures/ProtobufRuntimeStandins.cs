// Minimal stand-ins for the protobuf runtime infrastructure that real generated code
// calls. These are compiled into an assembly named Google.Protobuf (see the .csproj
// AssemblyName) so that the protobuf generated-bootstrap predicates in
// LibraryBodyIndex.GeneratedFrameworkTypeNames can require real protobuf assembly
// identity rather than namespace/name shape alone (#1580). Signatures mirror the
// shapes that generated code actually calls.

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
