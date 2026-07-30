// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace ILInspector.Decompiler;

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
}