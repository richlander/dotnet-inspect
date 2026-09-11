using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.DependencyManifests;

public enum ApplicationDependencyLibraryKind
{
    Unknown,
    Package,
    Project,
    Reference,
    RuntimePack,
    ReferenceAssembly,
}

public enum ApplicationDependencyAssetRole
{
    Compile,
    Runtime,
}

public sealed class ApplicationDependencyManifestCoordinate :
    IEquatable<ApplicationDependencyManifestCoordinate>
{
    private ApplicationDependencyManifestCoordinate(
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
        [NotNullWhen(true)]
        out ApplicationDependencyManifestCoordinate? coordinate)
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
        coordinate = new ApplicationDependencyManifestCoordinate(
            value,
            separator < 0 ? value : value[(separator + 1)..]);
        return true;
    }

    public bool Equals(ApplicationDependencyManifestCoordinate? other) =>
        other is not null
        && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is ApplicationDependencyManifestCoordinate other
        && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

public sealed record ApplicationDependencyManifestAsset
{
    internal ApplicationDependencyManifestAsset(
        ApplicationDependencyAssetRole role,
        ApplicationDependencyManifestCoordinate coordinate,
        ApplicationDependencyManifestCoordinate? localPath)
    {
        Role = role;
        Coordinate = coordinate;
        LocalPath = localPath;
    }

    public ApplicationDependencyAssetRole Role { get; }

    public ApplicationDependencyManifestCoordinate Coordinate { get; }

    public ApplicationDependencyManifestCoordinate? LocalPath { get; }
}

public sealed record ApplicationDependencyManifestLibrary
{
    internal ApplicationDependencyManifestLibrary(
        string key,
        ApplicationDependencyLibraryKind kind,
        ApplicationDependencyManifestCoordinate? declaredPath,
        IReadOnlyList<ApplicationDependencyManifestAsset> assets)
    {
        Key = key;
        Kind = kind;
        DeclaredPath = declaredPath;
        Assets = assets;
    }

    public string Key { get; }

    public ApplicationDependencyLibraryKind Kind { get; }

    public ApplicationDependencyManifestCoordinate? DeclaredPath { get; }

    public IReadOnlyList<ApplicationDependencyManifestAsset> Assets { get; }
}

public sealed record ApplicationDependencyManifest
{
    internal ApplicationDependencyManifest(
        string runtimeTargetName,
        string compilationTargetName,
        IReadOnlyList<ApplicationDependencyManifestLibrary> libraries)
    {
        RuntimeTargetName = runtimeTargetName;
        CompilationTargetName = compilationTargetName;
        Libraries = libraries;
    }

    public string RuntimeTargetName { get; }

    public string CompilationTargetName { get; }

    public IReadOnlyList<ApplicationDependencyManifestLibrary> Libraries { get; }
}

public sealed record ApplicationDependencyManifestParseBudget
{
    public static ApplicationDependencyManifestParseBudget Default { get; } =
        new(
            maxBytes: 4 * 1024 * 1024,
            maxScalarCharacters: 1024,
            maxLibraries: 8192,
            maxAssets: 16384);

    public ApplicationDependencyManifestParseBudget(
        int maxBytes,
        int maxScalarCharacters,
        int maxLibraries,
        int maxAssets)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxScalarCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLibraries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAssets);
        MaxBytes = maxBytes;
        MaxScalarCharacters = maxScalarCharacters;
        MaxLibraries = maxLibraries;
        MaxAssets = maxAssets;
    }

    public int MaxBytes { get; }

    public int MaxScalarCharacters { get; }

    public int MaxLibraries { get; }

    public int MaxAssets { get; }
}

public enum ApplicationDependencyManifestDiagnosticKind
{
    MalformedJson,
    InvalidDocumentShape,
    MissingRuntimeTarget,
    MissingCompilationTarget,
    InvalidLibraryMetadata,
    InvalidAssetCoordinate,
    WorkLimitExceeded,
}

public sealed record ApplicationDependencyManifestDiagnostic(
    ApplicationDependencyManifestDiagnosticKind Kind,
    string Summary);

public abstract record ApplicationDependencyManifestParseOutcome
{
    private protected ApplicationDependencyManifestParseOutcome()
    {
    }

    public sealed record Succeeded : ApplicationDependencyManifestParseOutcome
    {
        public Succeeded(ApplicationDependencyManifest value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public ApplicationDependencyManifest Value { get; }
    }

    public sealed record Rejected : ApplicationDependencyManifestParseOutcome
    {
        public Rejected(ApplicationDependencyManifestDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Diagnostic = diagnostic;
        }

        public ApplicationDependencyManifestDiagnostic Diagnostic { get; }
    }

    public sealed record Incomplete : ApplicationDependencyManifestParseOutcome
    {
        public Incomplete(ApplicationDependencyManifestDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Diagnostic = diagnostic;
        }

        public ApplicationDependencyManifestDiagnostic Diagnostic { get; }
    }
}
