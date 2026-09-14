using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection;

public abstract class SourceSelector
{
    private SourceSelector()
    {
    }

    public sealed class PlatformGroup : SourceSelector;

    public abstract class PackageSource : SourceSelector
    {
        private protected PackageSource()
        {
        }
    }

    public sealed class Package : PackageSource
    {
        public Package(PackageCoordinate coordinate) =>
            Coordinate = ValidateCoordinate(coordinate);

        public PackageCoordinate Coordinate { get; }
    }

    public sealed class PackageReference : PackageSource
    {
        public PackageReference(string packageId, string? version = null)
        {
            ValidateCoordinate(new PackageCoordinate(packageId));
            if (!PackageReferenceParser.IsValidVersion(version)
                || version?.Contains('\0') == true)
            {
                throw new ArgumentException("Invalid package reference version.", nameof(version));
            }

            PackageId = packageId;
            Version = version;
        }

        public string PackageId { get; }
        public string? Version { get; }
    }

    public sealed class PackageArchive : PackageSource
    {
        public PackageArchive(string path) => Path = ValidateText(path, nameof(path));

        public string Path { get; }
    }

    public sealed class PackageGroup : SourceSelector
    {
        public PackageGroup(IEnumerable<PackageCoordinate> coordinates)
        {
            ArgumentNullException.ThrowIfNull(coordinates);
            Coordinates = Array.AsReadOnly(
                coordinates.Select(ValidateCoordinate).ToArray());
        }

        public IReadOnlyList<PackageCoordinate> Coordinates { get; }
    }

    public sealed class PackagePrefix : SourceSelector
    {
        public PackagePrefix(PackagePrefixRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            Request = request;
        }

        public PackagePrefixRequest Request { get; }
    }

    public sealed class Library : SourceSelector
    {
        public Library(string path) => Path = ValidateText(path, nameof(path));

        public string Path { get; }
    }

    public sealed class PlatformLibrary : SourceSelector
    {
        public PlatformLibrary(string name) => Name = ValidateText(name, nameof(name));

        public string Name { get; }
    }

    public sealed class Project : SourceSelector
    {
        public Project(string path) => Path = ValidateText(path, nameof(path));

        public string Path { get; }
    }

    public sealed class BinaryDirectory : SourceSelector
    {
        public BinaryDirectory(string path) => Path = ValidateText(path, nameof(path));

        public string Path { get; }
    }

    private static PackageCoordinate ValidateCoordinate(PackageCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        if (PackageCoordinateResolver.Validate(coordinate) is { } invalid)
            throw new ArgumentException(invalid.Message, nameof(coordinate));

        return coordinate;
    }

    private static string ValidateText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Contains('\0'))
            throw new ArgumentException("Source text cannot contain NUL.", parameterName);

        return value;
    }
}
