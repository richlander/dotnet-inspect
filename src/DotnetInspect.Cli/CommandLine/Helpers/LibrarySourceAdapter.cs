using DotnetInspect.Cli.Options;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Cli.CommandLine;

internal sealed record LibrarySourceBinding(
    SourceIntent Intent,
    SourceSelector? Selector,
    string? AssemblyName,
    PackageReferenceTarget? PackageTarget,
    string? PlatformAssembly)
{
    internal string? PackageArgument =>
        PackageTarget?.OriginalArgument;

    internal string Target =>
        Path.GetFileName(
            AssemblyName
                ?? PackageArgument
                ?? PlatformAssembly
                ?? string.Empty);

    internal LibraryOptions ApplyTo(LibraryOptions options) =>
        options with
        {
            SourceIntent = Intent,
            AssemblyName = AssemblyName,
            PackagePath = PackageArgument,
            PlatformAssembly = PlatformAssembly,
        };
}

internal static class LibrarySourceAdapter
{
    internal static bool TryDeclare(
        string? assemblyName,
        string? packagePath,
        string? platformAssembly,
        string assemblyLabel,
        out SourceIntent intent,
        out string? error)
    {
        string? value;
        string label;
        Func<string, SourceSelector> create;
        if (platformAssembly is not null)
        {
            value = platformAssembly;
            label = "--platform";
            create = static source =>
                new SourceSelector.PlatformLibrary(source);
        }
        else if (packagePath is not null)
        {
            value = packagePath;
            label = "--package";
            create = CliSourceSelectorFactory.CreatePackageSource;
        }
        else if (assemblyName is not null)
        {
            value = assemblyName;
            label = assemblyLabel;
            create = static source => new SourceSelector.Library(source);
        }
        else
        {
            intent = SourceIntent.Empty;
            error = null;
            return true;
        }

        try
        {
            intent = SourceIntent.Create([create(value)]);
            error = null;
            return true;
        }
        catch (ArgumentException)
        {
            intent = SourceIntent.Empty;
            error = $"Invalid value '{value}' for {label}.";
            return false;
        }
    }

    internal static bool TryBind(
        LibraryOptions options,
        out LibrarySourceBinding? binding,
        out string? error)
    {
        SourceIntent intent = options.SourceIntent;
        if (intent.Selectors.Count == 0
            && !TryDeclare(
                options.AssemblyName,
                options.PackagePath,
                options.PlatformAssembly,
                "Library source",
                out intent,
                out error))
        {
            binding = null;
            return false;
        }

        if (intent.Selectors.Count > 1)
        {
            binding = null;
            error =
                "Library inspection accepts exactly one source selector.";
            return false;
        }

        SourceSelector? selector =
            intent.Selectors.Count == 0 ? null : intent.Selectors[0];
        switch (selector)
        {
            case null:
                binding = new(
                    intent,
                    Selector: null,
                    AssemblyName: null,
                    PackageTarget: null,
                    PlatformAssembly: null);
                error = null;
                return true;
            case SourceSelector.Library library:
                binding = new(
                    intent,
                    selector,
                    library.Path,
                    PackageTarget: null,
                    PlatformAssembly: null);
                error = null;
                return true;
            case SourceSelector.PackageArchive archive:
                binding = new(
                    intent,
                    selector,
                    options.AssemblyName,
                    new PackageReferenceTarget(
                        archive.Path,
                        IsLocalFile: true,
                        Path.GetFileNameWithoutExtension(
                            archive.Path),
                        Version: "local"),
                    PlatformAssembly: null);
                error = null;
                return true;
            case SourceSelector.PackageReference package:
                binding = new(
                    intent,
                    selector,
                    options.AssemblyName,
                    new PackageReferenceTarget(
                        PackageReference(package),
                        IsLocalFile: false,
                        package.PackageId,
                        package.Version ?? string.Empty),
                    PlatformAssembly: null);
                error = null;
                return true;
            case SourceSelector.PlatformLibrary platform:
                binding = new(
                    intent,
                    selector,
                    AssemblyName: null,
                    PackageTarget: null,
                    platform.Name);
                error = null;
                return true;
            default:
                binding = null;
                error =
                    $"Library inspection does not support source selector "
                    + $"'{selector.GetType().Name}'.";
                return false;
        }
    }

    private static string PackageReference(
        SourceSelector.PackageReference package) =>
        package.Version is null
            ? package.PackageId
            : $"{package.PackageId}@{package.Version}";
}
