namespace InertText;

/// <summary>
/// The kinds of artifact-text concern that required visual containment.
/// </summary>
/// <remarks>
/// These flags retain why an <see cref="InertString"/> was changed without retaining or
/// recovering the untreated text. A literal backslash is deliberately absent: it may need to be
/// doubled to keep the encoding invertible, but it is not itself a concern.
/// <c>Concerns_ClassifyWhyContainmentOccurred</c> and
/// <c>Concerns_ClassifyAnUnpairedSurrogate</c> gate the classification.
/// </remarks>
[Flags]
public enum TextConcern
{
    /// <summary>No concerning scalar required containment.</summary>
    None = 0,

    /// <summary>A terminal or line-control scalar in Unicode category <c>Cc</c>.</summary>
    Control = 1 << 0,

    /// <summary>A formatting scalar in Unicode category <c>Cf</c>, including bidi controls.</summary>
    Format = 1 << 1,

    /// <summary>An unpaired UTF-16 surrogate in Unicode category <c>Cs</c>.</summary>
    Surrogate = 1 << 2,

    /// <summary>A Unicode line separator in category <c>Zl</c>.</summary>
    LineSeparator = 1 << 3,

    /// <summary>A Unicode paragraph separator in category <c>Zp</c>.</summary>
    ParagraphSeparator = 1 << 4,
}
