using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace DotnetInspector.Platforms;

/// <summary>One exact canonical SemVer 2 platform version.</summary>
public sealed class PlatformVersion : IEquatable<PlatformVersion>
{
    private readonly string[] _prereleaseIdentifiers;

    private PlatformVersion(
        string value,
        BigInteger major,
        BigInteger minor,
        BigInteger patch,
        string? prerelease,
        string? buildMetadata,
        string[] prereleaseIdentifiers)
    {
        Value = value;
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        BuildMetadata = buildMetadata;
        _prereleaseIdentifiers = prereleaseIdentifiers;
    }

    /// <summary>Compares platform versions by SemVer precedence.</summary>
    public static IComparer<PlatformVersion> SemanticPrecedenceComparer { get; } =
        new PrecedenceComparer();

    /// <summary>The exact canonical identity.</summary>
    public string Value { get; }

    /// <summary>The major version.</summary>
    public BigInteger Major { get; }

    /// <summary>The minor version.</summary>
    public BigInteger Minor { get; }

    /// <summary>The patch version.</summary>
    public BigInteger Patch { get; }

    /// <summary>The prerelease identity without the leading hyphen.</summary>
    public string? Prerelease { get; }

    /// <summary>The build metadata without the leading plus sign.</summary>
    public string? BuildMetadata { get; }

    /// <summary>Whether this is a prerelease version.</summary>
    public bool IsPrerelease => Prerelease is not null;

    /// <summary>Parses one exact canonical SemVer 2 platform version.</summary>
    public static PlatformVersion Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out PlatformVersion? version)
            ? version
            : throw new FormatException(
                "The value is not a canonical exact SemVer 2 platform version.");
    }

    /// <summary>Attempts to parse one exact canonical SemVer 2 platform version.</summary>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out PlatformVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(value)
            || value.AsSpan().Trim().Length != value.Length)
        {
            return false;
        }

        int plus = value.IndexOf('+');
        if (plus >= 0 && value.IndexOf('+', plus + 1) >= 0)
            return false;

        string versionAndPrerelease = plus >= 0 ? value[..plus] : value;
        string? buildMetadata = plus >= 0 ? value[(plus + 1)..] : null;
        if (buildMetadata is not null
            && !ValidateIdentifiers(buildMetadata, allowLeadingZeroes: true))
        {
            return false;
        }

        int hyphen = versionAndPrerelease.IndexOf('-');
        string core = hyphen >= 0
            ? versionAndPrerelease[..hyphen]
            : versionAndPrerelease;
        string? prerelease = hyphen >= 0
            ? versionAndPrerelease[(hyphen + 1)..]
            : null;
        if (prerelease is not null
            && !ValidateIdentifiers(prerelease, allowLeadingZeroes: false))
        {
            return false;
        }

        string[] coreParts = core.Split('.');
        if (coreParts.Length != 3
            || !TryParseCoreNumber(coreParts[0], out BigInteger major)
            || !TryParseCoreNumber(coreParts[1], out BigInteger minor)
            || !TryParseCoreNumber(coreParts[2], out BigInteger patch))
        {
            return false;
        }

        version = new PlatformVersion(
            value,
            major,
            minor,
            patch,
            prerelease,
            buildMetadata,
            prerelease?.Split('.') ?? []);
        return true;
    }

    /// <summary>
    /// Compares SemVer precedence. Build metadata does not participate.
    /// </summary>
    public int ComparePrecedenceTo(PlatformVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);

        int result = Major.CompareTo(other.Major);
        if (result != 0)
            return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
            return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
            return result;

        if (_prereleaseIdentifiers.Length == 0)
            return other._prereleaseIdentifiers.Length == 0 ? 0 : 1;
        if (other._prereleaseIdentifiers.Length == 0)
            return -1;

        int count = Math.Min(
            _prereleaseIdentifiers.Length,
            other._prereleaseIdentifiers.Length);
        for (int i = 0; i < count; i++)
        {
            result = ComparePrereleaseIdentifier(
                _prereleaseIdentifiers[i],
                other._prereleaseIdentifiers[i]);
            if (result != 0)
                return result;
        }

        return _prereleaseIdentifiers.Length.CompareTo(
            other._prereleaseIdentifiers.Length);
    }

    /// <inheritdoc />
    public bool Equals(PlatformVersion? other) =>
        other is not null
        && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is PlatformVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Tests two version identities for equality.</summary>
    public static bool operator ==(
        PlatformVersion? left,
        PlatformVersion? right) =>
        Equals(left, right);

    /// <summary>Tests two version identities for inequality.</summary>
    public static bool operator !=(
        PlatformVersion? left,
        PlatformVersion? right) =>
        !Equals(left, right);

    private static bool TryParseCoreNumber(
        string value,
        out BigInteger number)
    {
        number = default;
        return value.Length > 0
            && (value.Length == 1 || value[0] != '0')
            && IsAsciiDigits(value)
            && BigInteger.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out number);
    }

    private static bool ValidateIdentifiers(
        string value,
        bool allowLeadingZeroes)
    {
        if (value.Length == 0)
            return false;

        ReadOnlySpan<char> valueSpan = value;
        foreach (Range range in valueSpan.Split('.'))
        {
            ReadOnlySpan<char> identifier = valueSpan[range];
            if (identifier.IsEmpty)
                return false;

            bool numeric = true;
            foreach (char character in identifier)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                    return false;
                numeric &= char.IsAsciiDigit(character);
            }

            if (!allowLeadingZeroes
                && numeric
                && identifier.Length > 1
                && identifier[0] == '0')
            {
                return false;
            }
        }

        return true;
    }

    private static int ComparePrereleaseIdentifier(
        string left,
        string right)
    {
        bool leftNumeric = IsAsciiDigits(left);
        bool rightNumeric = IsAsciiDigits(right);
        if (leftNumeric && rightNumeric)
        {
            int length = left.Length.CompareTo(right.Length);
            return length != 0
                ? length
                : string.CompareOrdinal(left, right);
        }

        if (leftNumeric)
            return -1;
        if (rightNumeric)
            return 1;
        return string.CompareOrdinal(left, right);
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
            if (!char.IsAsciiDigit(character))
                return false;
        return true;
    }

    private sealed class PrecedenceComparer : IComparer<PlatformVersion>
    {
        public int Compare(PlatformVersion? x, PlatformVersion? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            return x.ComparePrecedenceTo(y);
        }
    }
}
