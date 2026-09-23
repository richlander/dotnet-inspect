namespace ZipFetch;

/// <summary>The ways a ZIP read can fail for reasons the archive owns.</summary>
public enum ZipReadFailure
{
    /// <summary>The archive's structure is inconsistent with itself or with the bytes read.</summary>
    Malformed,

    /// <summary>The archive or an entry exceeds a bound the caller supplied.</summary>
    OverBound,

    /// <summary>The archive uses a feature the reader does not support (Zip64, encryption, a compression method other than stored or deflate).</summary>
    Unsupported,
}

/// <summary>
/// A failure of a ZIP read, classified by what the archive did. Failures of
/// the underlying source propagate as their own exception type.
/// </summary>
public sealed class ZipReadException : IOException
{
    public ZipReadException(ZipReadFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public ZipReadFailure Failure { get; }
}
