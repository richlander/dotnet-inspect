using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DotnetInspector.Platforms;

/// <summary>Identifies one canonical base target framework for platform assets.</summary>
public sealed class PlatformTargetFramework :
    IEquatable<PlatformTargetFramework>
{
    private PlatformTargetFramework(
        PlatformTargetFrameworkForm form,
        int major,
        int minor)
    {
        Form = form;
        Major = major;
        Minor = minor;
    }

    /// <summary>The canonical framework form.</summary>
    public PlatformTargetFrameworkForm Form { get; }

    /// <summary>The framework major version.</summary>
    public int Major { get; }

    /// <summary>The framework minor version.</summary>
    public int Minor { get; }

    /// <summary>Parses one canonical platform target framework.</summary>
    public static PlatformTargetFramework Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out PlatformTargetFramework? framework)
            ? framework
            : throw new FormatException(
                "The value is not a canonical supported platform target framework.");
    }

    /// <summary>Attempts to parse one canonical platform target framework.</summary>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out PlatformTargetFramework? framework)
    {
        framework = null;
        if (string.IsNullOrEmpty(value)
            || value.AsSpan().Trim().Length != value.Length)
        {
            return false;
        }

        PlatformTargetFrameworkForm form;
        ReadOnlySpan<char> version;
        if (value.StartsWith("netcoreapp", StringComparison.Ordinal))
        {
            form = PlatformTargetFrameworkForm.NetCoreApp;
            version = value.AsSpan("netcoreapp".Length);
        }
        else if (value.StartsWith("net", StringComparison.Ordinal))
        {
            form = PlatformTargetFrameworkForm.Net;
            version = value.AsSpan("net".Length);
        }
        else
        {
            return false;
        }

        int separator = version.IndexOf('.');
        if (separator <= 0
            || separator == version.Length - 1
            || version[(separator + 1)..].Contains('.')
            || !TryParseCanonicalNumber(version[..separator], out int major)
            || !TryParseCanonicalNumber(version[(separator + 1)..], out int minor))
        {
            return false;
        }

        bool supported = form switch
        {
            PlatformTargetFrameworkForm.NetCoreApp =>
                (major, minor) is
                    (1, 0) or (1, 1)
                    or (2, 0) or (2, 1) or (2, 2)
                    or (3, 0) or (3, 1),
            PlatformTargetFrameworkForm.Net => major >= 5,
            _ => false,
        };
        if (!supported)
            return false;

        framework = new PlatformTargetFramework(form, major, minor);
        return true;
    }

    /// <inheritdoc />
    public bool Equals(PlatformTargetFramework? other) =>
        other is not null
        && Form == other.Form
        && Major == other.Major
        && Minor == other.Minor;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is PlatformTargetFramework other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Form, Major, Minor);

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(Form == PlatformTargetFrameworkForm.NetCoreApp ? "netcoreapp" : "net")}{Major}.{Minor}");

    /// <summary>Tests two framework values for equality.</summary>
    public static bool operator ==(
        PlatformTargetFramework? left,
        PlatformTargetFramework? right) =>
        Equals(left, right);

    /// <summary>Tests two framework values for inequality.</summary>
    public static bool operator !=(
        PlatformTargetFramework? left,
        PlatformTargetFramework? right) =>
        !Equals(left, right);

    private static bool TryParseCanonicalNumber(
        ReadOnlySpan<char> value,
        out int number)
    {
        number = 0;
        if (value.IsEmpty
            || (value.Length > 1 && value[0] == '0')
            || !IsAsciiDigits(value))
        {
            return false;
        }

        return int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
            if (!char.IsAsciiDigit(character))
                return false;
        return true;
    }
}

/// <summary>Distinguishes legacy .NET Core and modern .NET base TFMs.</summary>
public enum PlatformTargetFrameworkForm
{
    /// <summary>The <c>netcoreapp</c> form used by .NET Core 1.0 through 3.1.</summary>
    NetCoreApp = 0,

    /// <summary>The <c>net</c> form used by .NET 5 and later.</summary>
    Net = 1,
}
