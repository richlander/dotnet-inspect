using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection;

public abstract class SourceSelector
{
    private SourceSelector()
    {
    }

    public sealed class PlatformGroup : SourceSelector;

    public sealed class Package : SourceSelector
    {
        public Package(PackageCoordinate coordinate) =>
            Coordinate = ValidateCoordinate(coordinate);

        public PackageCoordinate Coordinate { get; }
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
