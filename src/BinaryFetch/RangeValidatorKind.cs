namespace BinaryFetch;

/// <summary>Which validator, if any, the first response supplied for <c>If-Range</c>.</summary>
public enum RangeValidatorKind
{
    /// <summary>No strong validator; identity across requests is unverified.</summary>
    None,

    /// <summary>A strong entity tag.</summary>
    EntityTag,

    /// <summary>A last-modified time.</summary>
    LastModified,
}
