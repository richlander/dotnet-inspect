// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Text;

namespace ILInspector.CSharp;

/// <summary>
/// Appends a line terminator that does not vary by platform.
/// </summary>
/// <remarks>
/// <see cref="StringBuilder.AppendLine()"/> writes <see cref="System.Environment.NewLine"/>, so the
/// same input rendered as CRLF on Windows and LF everywhere else. Rendered C# and IL text is
/// compared, diffed, and embedded in Markdown, so it has to be byte-identical on every platform;
/// a decompilation is not a platform-specific artifact. Printers therefore spell the terminator
/// explicitly instead of inheriting the ambient one. See #3526.
/// </remarks>
internal static class StringBuilderLineExtensions
{
    /// <summary>Appends a single LF.</summary>
    public static StringBuilder AppendLf(this StringBuilder builder) => builder.Append('\n');

    /// <summary>Appends <paramref name="value"/> followed by a single LF.</summary>
    public static StringBuilder AppendLf(this StringBuilder builder, string? value) =>
        builder.Append(value).Append('\n');

    /// <summary>
    /// Appends an interpolated string followed by a single LF.
    /// </summary>
    /// <remarks>
    /// This mirrors <see cref="StringBuilder.AppendLine(ref StringBuilder.AppendInterpolatedStringHandler)"/>:
    /// the handler formats each hole straight into <paramref name="builder"/> as it is constructed, so
    /// the interpolated string is never materialized. Without this overload the printers' interpolated
    /// call sites would silently fall back to the <see cref="string"/> overload and allocate one string
    /// per printed line, which matters because these run once per instruction and per method across a
    /// whole corpus.
    /// </remarks>
    public static StringBuilder AppendLf(
        this StringBuilder builder,
        [InterpolatedStringHandlerArgument(nameof(builder))]
        ref StringBuilder.AppendInterpolatedStringHandler handler) => builder.Append('\n');
}