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

namespace ILInspector.Metadata.SignatureUseFixtures.IsolationBusy
{
    public sealed class BusySource
    {
        public ShardA.NamespaceSource? First;
        public ShardA.NamespaceSource? Second;
        public ShardA.NamespaceSource? Third;
    }

    public class LimitedExceptionBase : Exception;
}

namespace ILInspector.Metadata.SignatureUseFixtures.IsolationHealthy
{
    public sealed class HealthyException :
        IsolationBusy.LimitedExceptionBase;
}
