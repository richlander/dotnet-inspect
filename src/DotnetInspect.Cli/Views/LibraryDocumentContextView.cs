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
    private readonly LibraryPresentationContext _context;
    private readonly LibraryScalarFields _scalars;

    internal LibraryDocumentContextView(
        LibraryPresentationContext context,
        LibraryDocumentInspection documentInspection)
    {
        _context = context;
        _scalars = LibraryScalarFields.From(
            context,
            documentInspection);
    }

    [MarkoutIgnore]
    public string? Tfm => null;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("File")]
    public string FileName =>
        LibraryViewText.Contain(_context.FileName);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Name =>
        LibraryViewText.Contain(_scalars.Name);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Version =>
        LibraryViewText.Contain(_scalars.Version);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("TFM")]
    [MarkoutSkipNull]
    public string? TargetFramework =>
        LibraryViewText.Contain(_scalars.TargetFramework);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Arch")]
    [MarkoutSkipNull]
    public string? Architecture =>
        LibraryViewText.Contain(_scalars.Architecture);

    [MarkoutPropertyName("Size")]
    [MarkoutSkipNull]
    public string? FileSize => _scalars.FileSize;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Source =>
        LibraryViewText.Contain(_context.Source);
}
