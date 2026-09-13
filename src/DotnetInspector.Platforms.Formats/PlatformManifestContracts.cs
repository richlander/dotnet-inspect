using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Platforms.Formats;

/// <summary>A portable shared-framework name carried by platform manifests.</summary>
public sealed class PlatformFrameworkName : IEquatable<PlatformFrameworkName>
{
    public const int MaximumLength = 236;

    private static readonly HashSet<string> ReservedFirstComponents =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
        };

    private PlatformFrameworkName(string value) => Value = value;

    public string Value { get; }

    public static PlatformFrameworkName Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out PlatformFrameworkName? name)
            ? name
            : throw new FormatException(
                "The value is not a portable platform framework name.");
    }

    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out PlatformFrameworkName? name)
    {
        name = null;
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumLength
            || !char.IsAsciiLetterOrDigit(value[0])
            || !char.IsAsciiLetterOrDigit(value[^1]))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '_' and not '-')
            {
                return false;
            }
        }

        int firstDot = value.IndexOf('.');
        string firstComponent =
            firstDot < 0 ? value : value[..firstDot];
        if (ReservedFirstComponents.Contains(firstComponent))
            return false;

        name = new PlatformFrameworkName(value);
        return true;
    }

    public bool Equals(PlatformFrameworkName? other) =>
        other is not null
        && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is PlatformFrameworkName other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

/// <summary>Framework roll-forward policy declared by runtime configuration.</summary>
public enum PlatformFrameworkRollForward
{
    Disable,
    LatestPatch,
    Minor,
    LatestMinor,
    Major,
    LatestMajor,
}

/// <summary>One effective direct shared-framework reference.</summary>
public sealed record PlatformFrameworkReference
{
    internal PlatformFrameworkReference(
        PlatformFrameworkName name,
        PlatformVersion version,
        PlatformFrameworkRollForward rollForward,
        bool applyPatches)
    {
        Name = name;
        Version = version;
        RollForward = rollForward;
        ApplyPatches = applyPatches;
    }

    public PlatformFrameworkName Name { get; }
    public PlatformVersion Version { get; }
    public PlatformFrameworkRollForward RollForward { get; }
    public bool ApplyPatches { get; }
}

/// <summary>Direct framework references interpreted from runtime configuration.</summary>
public sealed record PlatformRuntimeConfiguration
{
    public static PlatformRuntimeConfiguration DependencyFree { get; } =
        new(Array.Empty<PlatformFrameworkReference>());

    internal PlatformRuntimeConfiguration(
        IReadOnlyList<PlatformFrameworkReference> frameworks) =>
        Frameworks = frameworks;

    public IReadOnlyList<PlatformFrameworkReference> Frameworks { get; }
}

/// <summary>A validated logical asset coordinate from a dependency manifest.</summary>
public sealed class PlatformManifestAssetCoordinate :
    IEquatable<PlatformManifestAssetCoordinate>
{
    private PlatformManifestAssetCoordinate(
        string value,
        string fileName)
    {
        Value = value;
        FileName = fileName;
    }

    public string Value { get; }
    public string FileName { get; }

    internal static bool TryParse(
        string? value,
        out PlatformManifestAssetCoordinate? coordinate)
    {
        coordinate = null;
        if (string.IsNullOrEmpty(value)
            || value[0] == '/'
            || value.Length >= 2
                && char.IsAsciiLetter(value[0])
                && value[1] == ':'
            || value[^1] == '/'
            || value.Contains('\\'))
        {
            return false;
        }

        ReadOnlySpan<char> span = value;
        foreach (Range range in span.Split('/'))
        {
            ReadOnlySpan<char> segment = span[range];
            if (segment.IsEmpty
                || segment.SequenceEqual(".")
                || segment.SequenceEqual(".."))
            {
                return false;
            }

            foreach (char character in segment)
            {
                if (char.IsControl(character) || character == '\0')
                    return false;
            }
        }

        int separator = value.LastIndexOf('/');
        coordinate = new PlatformManifestAssetCoordinate(
            value,
            separator < 0 ? value : value[(separator + 1)..]);
        return true;
    }

    public bool Equals(PlatformManifestAssetCoordinate? other) =>
        other is not null
        && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is PlatformManifestAssetCoordinate other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

/// <summary>Managed implementation membership from one dependency manifest.</summary>
public sealed record PlatformDependencyManifest
{
    internal PlatformDependencyManifest(
        string runtimeTargetName,
        IReadOnlyList<PlatformManifestAssetCoordinate> managedAssets)
    {
        RuntimeTargetName = runtimeTargetName;
        ManagedAssets = managedAssets;
    }

    public string RuntimeTargetName { get; }
    public IReadOnlyList<PlatformManifestAssetCoordinate> ManagedAssets { get; }
}

/// <summary>Finite work allowed for one platform-manifest parse.</summary>
public sealed record PlatformManifestParseBudget
{
    public static PlatformManifestParseBudget Default { get; } =
        new(
            maxBytes: 4 * 1024 * 1024,
            maxFrameworkReferences: 32,
            maxLibraries: 4096,
            maxAssets: 4096);

    public PlatformManifestParseBudget(
        int maxBytes,
        int maxFrameworkReferences,
        int maxLibraries,
        int maxAssets)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxFrameworkReferences);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLibraries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAssets);
        MaxBytes = maxBytes;
        MaxFrameworkReferences = maxFrameworkReferences;
        MaxLibraries = maxLibraries;
        MaxAssets = maxAssets;
    }

    public int MaxBytes { get; }
    public int MaxFrameworkReferences { get; }
    public int MaxLibraries { get; }
    public int MaxAssets { get; }
}

public enum PlatformManifestDiagnosticKind
{
    MalformedJson,
    InvalidDocumentShape,
    InvalidFrameworkName,
    InvalidVersion,
    InvalidRollForward,
    DuplicateFrameworkReference,
    MissingRuntimeTarget,
    MissingRuntimeTargetAssets,
    InvalidAssetCoordinate,
    WorkLimitExceeded,
}

/// <summary>Typed format-owner diagnostic for a manifest parse.</summary>
public sealed record PlatformManifestDiagnostic(
    PlatformManifestDiagnosticKind Kind,
    string Summary);

/// <summary>Closed result of interpreting one bounded platform manifest.</summary>
public abstract record PlatformManifestParseOutcome<T>
    where T : notnull
{
    private protected PlatformManifestParseOutcome()
    {
    }

    public sealed record Succeeded : PlatformManifestParseOutcome<T>
    {
        public Succeeded(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public T Value { get; }
    }

    public sealed record Rejected : PlatformManifestParseOutcome<T>
    {
        public Rejected(PlatformManifestDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Diagnostic = diagnostic;
        }

        public PlatformManifestDiagnostic Diagnostic { get; }
    }

    public sealed record Incomplete : PlatformManifestParseOutcome<T>
    {
        public Incomplete(PlatformManifestDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Diagnostic = diagnostic;
        }

        public PlatformManifestDiagnostic Diagnostic { get; }
    }
}
