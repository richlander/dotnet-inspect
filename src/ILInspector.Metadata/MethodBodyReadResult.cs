using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>Typed reason a complete whole-body observation was unavailable.</summary>
public abstract record MethodBodyUnavailableReason
{
    private protected MethodBodyUnavailableReason()
    {
    }

    public sealed record NotMethodDefinitionToken : MethodBodyUnavailableReason;
    public sealed record RowOutOfRange : MethodBodyUnavailableReason;
    public sealed record UnsupportedImplementation : MethodBodyUnavailableReason;
    public sealed record MalformedBody : MethodBodyUnavailableReason;

    public sealed record ILByteLimitExceeded : MethodBodyUnavailableReason
    {
        internal ILByteLimitExceeded(int ilByteCount, int maxILBytes)
        {
            ILByteCount = ilByteCount;
            MaxILBytes = maxILBytes;
        }

        public int ILByteCount { get; }
        public int MaxILBytes { get; }
    }
}

/// <summary>
/// Closed result of one whole-body and exception-region read.
/// </summary>
public abstract record MethodBodyReadResult
{
    private protected MethodBodyReadResult()
    {
    }

    /// <summary>
    /// The copied IL and complete exception catalog were materialized from one
    /// admitted body observation. The catalog may be empty.
    /// </summary>
    public sealed record Available : MethodBodyReadResult
    {
        internal Available(MethodBodyData body)
        {
            ArgumentNullException.ThrowIfNull(body);
            Body = body;
        }

        public MethodBodyData Body { get; }
    }

    /// <summary>The MethodDef definitively declares no managed IL body.</summary>
    public sealed record NoBody : MethodBodyReadResult
    {
        internal NoBody(MetadataMethodAddress method) => Method = method;

        public MetadataMethodAddress Method { get; }
    }

    /// <summary>The whole-body observation could not be completed.</summary>
    public sealed record Unavailable : MethodBodyReadResult
    {
        internal Unavailable(
            MetadataMethodAddress? method,
            MethodBodyUnavailableReason reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            Method = method;
            Reason = reason;
        }

        public MetadataMethodAddress? Method { get; }
        public MethodBodyUnavailableReason Reason { get; }
    }
}
