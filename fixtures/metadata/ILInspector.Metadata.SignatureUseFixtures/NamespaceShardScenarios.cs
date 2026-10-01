namespace ILInspector.Metadata.SignatureUseFixtures.ShardA
{
    public sealed class NamespaceSource
    {
        public NamespacePeer? Peer;
        public ShardB.NamespaceExternal? External;
    }

    public sealed class NamespacePeer
    {
        public NamespaceSource? Source;
    }
}

namespace ILInspector.Metadata.SignatureUseFixtures.ShardB
{
    public sealed class NamespaceExternal
    {
        public ShardA.NamespaceSource? Source;
    }
}
