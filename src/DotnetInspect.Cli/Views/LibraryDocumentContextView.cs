using DotnetInspect.Cli.Commands;
using Markout;

namespace DotnetInspect.Cli.Views;

internal sealed record LibraryPresentationContext(
    string FileName,
    string? Source,
    string? PlatformVersion = null);

[MarkoutSerializable(
    TitleProperty = nameof(FileName),
    TitleContextProperty = nameof(Tfm),
    AutoFieldsCount = 6,
    FieldLayout = FieldLayout.Inline)]
public sealed class LibraryDocumentContextView
{
    private readonly string _fileName;
    private readonly string? _name;
    private readonly string? _version;
    private readonly string? _targetFramework;
    private readonly string? _architecture;
    private readonly string? _fileSize;
    private readonly string? _source;

    internal LibraryDocumentContextView(
        LibraryPresentationContext context,
        LibraryDocumentInspection documentInspection)
    {
        LibraryScalarFields scalars = LibraryScalarFields.From(
            context,
            documentInspection);
        _fileName = context.FileName;
        _name = scalars.Name;
        _version = scalars.Version;
        _targetFramework = scalars.TargetFramework;
        _architecture = scalars.Architecture;
        _fileSize = scalars.FileSize;
        _source = context.Source;
    }

    public LibraryDocumentContextView(
        string fileName,
        string? name,
        string? version,
        string? targetFramework,
        string? architecture,
        string? fileSize,
        string? source)
    {
        _fileName = fileName;
        _name = name;
        _version = version;
        _targetFramework = targetFramework;
        _architecture = architecture;
        _fileSize = fileSize;
        _source = source;
    }

    [MarkoutIgnore]
    public string? Tfm => null;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("File")]
    public string FileName =>
        LibraryViewText.Contain(_fileName);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Name =>
        LibraryViewText.Contain(_name);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Version =>
        LibraryViewText.Contain(_version);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("TFM")]
    [MarkoutSkipNull]
    public string? TargetFramework =>
        LibraryViewText.Contain(_targetFramework);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Arch")]
    [MarkoutSkipNull]
    public string? Architecture =>
        LibraryViewText.Contain(_architecture);

    [MarkoutPropertyName("Size")]
    [MarkoutSkipNull]
    public string? FileSize =>
        LibraryViewText.Contain(_fileSize);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Source =>
        LibraryViewText.Contain(_source);
}
