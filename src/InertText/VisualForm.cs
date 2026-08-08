namespace InertText;

/// <summary>
/// The spellings <see cref="InertString"/> can emit.
/// </summary>
/// <remarks>
/// Reported so a caller can print a legend derived from what was actually emitted rather than
/// from a second copy of the table. A legend written independently drifts; one projected from
/// the encoder cannot.
/// </remarks>
[Flags]
public enum VisualForm
{
    /// <summary>Nothing was encoded.</summary>
    None = 0,

    /// <summary>Caret notation for a C0 control, as in <c>\^[</c> for <c>ESC</c>.</summary>
    Caret = 1 << 0,

    /// <summary>Caret notation for <c>DEL</c>, spelled <c>\^?</c>.</summary>
    CaretDelete = 1 << 1,

    /// <summary>A scalar in the BMP, spelled <c>\uXXXX</c>.</summary>
    BmpHex = 1 << 2,

    /// <summary>A scalar above the BMP, spelled <c>\UXXXXXXXX</c>.</summary>
    AstralHex = 1 << 3,

    /// <summary>A literal backslash, doubled so the transform stays invertible.</summary>
    Backslash = 1 << 4,
}
