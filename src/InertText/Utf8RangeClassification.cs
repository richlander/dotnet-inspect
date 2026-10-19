namespace InertText.Encoding;

/// <summary>
/// The explicit capability by which a typed domain view classifies an exact range as UTF-8 text.
/// </summary>
/// <remarks>
/// An opaque range is not text. A domain whose grammar gives one exact range UTF-8 meaning calls
/// this from its typed text view, and only from there; binary keys and blobs never do. The
/// classification establishes nothing about the bytes: the reader answers closed queries, and
/// presentation still passes through <see cref="InertUtf8String"/> construction.
///
/// It lives in this conspicuous namespace beside the encoders so that every classification site
/// is a short search. Like the encoders, that is an audit boundary, not a capability barrier.
/// </remarks>
public static class Utf8RangeClassification
{
    /// <summary>Issues a scoped UTF-8 reader over a range the caller's domain classifies as text.</summary>
    public static Utf8RangeReader Read(OpaqueByteRange text) => new(text);
}
