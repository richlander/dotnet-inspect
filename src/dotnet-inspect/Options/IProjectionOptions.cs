namespace DotnetInspector.Options;

/// <summary>
/// The payload-shaping request carried by a command's options.
/// </summary>
/// <remarks>
/// <para>
/// The five projection flags are declared independently on each command's options record, so a
/// shared render path cannot ask what the caller requested without being handed the concrete
/// options type. This interface gives those flags a single typed identity that alternate render
/// paths can accept.
/// </para>
/// <para>
/// Only <see cref="Count"/> is universal. The search commands (<c>find</c>, <c>implements</c>,
/// <c>depends</c>, <c>extensions</c>) expose no <c>--print</c>, <c>--value</c>, <c>--urls</c>, or
/// <c>--paths</c> option at all, so those members default to <see langword="false"/> rather than
/// being declared on records whose commands do not accept them. A record that does declare the
/// property implements the member implicitly and the default does not apply.
/// </para>
/// </remarks>
public interface IProjectionOptions
{
    /// <summary>Whether <c>--count</c> was requested.</summary>
    bool Count { get; }

    /// <summary>Whether <c>--print</c> was requested. False for commands with no such option.</summary>
    bool Print => false;

    /// <summary>Whether <c>--value</c> was requested. False for commands with no such option.</summary>
    bool Value => false;

    /// <summary>Whether <c>--urls</c> was requested. False for commands with no such option.</summary>
    bool Urls => false;

    /// <summary>Whether <c>--paths</c> was requested. False for commands with no such option.</summary>
    bool Paths => false;
}
