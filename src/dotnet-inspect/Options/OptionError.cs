namespace DotnetInspector.Options;

/// <summary>
/// A validation failure carrying its own structure: a single-line message and
/// zero or more detail lines.
/// </summary>
/// <remarks>
/// Option validators used to return one <c>string</c>, and some of them built a
/// multi-line value out of it -- an unknown <c>--where</c> field appended a
/// blank line, a <c>Did you mean:</c> header, and an indented suggestion. That
/// worked only while the writer honored newlines inside a message, which it can
/// no longer do: a message quotes untrusted text, and a writer that turns an
/// embedded newline into a real line cannot tell this structure from one an
/// attacker injected (issue #3319).
///
/// Collapsing the suggestion onto the message line is the other obvious answer,
/// and it is what happened first. It loses nothing semantically and reads worse
/// the more suggestions there are. This type is the reason it does not have to
/// be a trade: <see cref="Details"/> travels beside the message and reaches
/// <c>CommandError.WriteDetail</c>, which indents each line itself, so the
/// structure is composed by the writer rather than smuggled through a string
/// the writer has to parse.
///
/// The implicit conversion keeps the dozen validators that genuinely have no
/// detail spelling <c>error = "...";</c> unchanged.
/// </remarks>
public readonly record struct OptionError(string Message, string[] Details)
{
    public OptionError(string message)
        : this(message, [])
    {
    }

    public static implicit operator OptionError(string message) => new(message);

    public override string ToString() => Message;
}
