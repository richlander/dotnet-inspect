using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Views;

/// <summary>
/// The scalar fields the <c>library</c> view renders in Library Info, the
/// <c>-v:q</c> summary, and the Library summary field, each from its one named
/// source (<c>docs/design/library-info-composition.md</c>). A managed assembly
/// reads the Library document; a native or manifestless image keeps the legacy
/// reading, including its <c>Native</c>, <c>NativeAOT</c>, and <c>Unknown</c>
/// compilation labels.
/// </summary>
internal sealed record LibraryScalarFields(
    string? Name,
    string? AssemblyVersion,
    string? PublicKeyToken,
    string? FileSize,
    string? TargetFramework,
    string? Compilation,
    string? Architecture,
    bool Signed,
    string? Reproducible,
    string? InformationalVersion,
    string? Company,
    string? Product,
    string? Copyright,
    string? Enabled,
    string? Version,
    string? DocumentFailure)
{
    internal static LibraryScalarFields? From(
        LibraryInspection data,
        LibraryDocumentInspection? documentInspection = null)
    {
        // Views that carry no assembly identity render no scalar fields, as before.
        if (data.AssemblyInfo is not { } info)
            return null;
        if (documentInspection?.Document is { } document)
            return FromDocument(data, document);
        if (documentInspection?.Failure is { } failure)
        {
            return new(
                null, null, null, null, null, null, null, false, null,
                null, null, null, null, null,
                data.PlatformVersion,
                $"unavailable ({failure})");
        }

        return FromLegacy(data, info);
    }

    private static LibraryScalarFields FromDocument(LibraryInspection data, LibraryDocument document)
    {
        LibraryImageFacts? image = document.Image;
        LibraryDescriptionFacts? description = document.Description;
        LibraryTextFact? informationalVersion = description?.InformationalVersion;
        string assemblyVersion = document.Assembly.Version.ToString();
        return new(
            document.Assembly.Name.ToString(),
            assemblyVersion,
            document.Assembly.PublicKeyToken?.ToString(),
            image is null ? null : ByteSizeFormatter.FormatBytes(image.ImageBytes),
            Text(image?.TargetFramework),
            image?.Compilation switch
            {
                LibraryCompilationForm.IL => "CoreCLR",
                LibraryCompilationForm.ReadyToRun => "ReadyToRun",
                _ => null,
            },
            image?.Architecture switch
            {
                LibraryArchitecture.AnyCpu => "AnyCPU",
                LibraryArchitecture.AnyCpuPrefers32Bit => "AnyCPU (32-bit preferred)",
                LibraryArchitecture.X86 => "x86",
                LibraryArchitecture.X64 => "x64",
                LibraryArchitecture.Arm => "ARM",
                LibraryArchitecture.Arm64 => "ARM64",
                _ => null,
            },
            image?.StrongNameSigned == true,
            image?.Reproducibility switch
            {
                LibraryReproducibility.Reproducible => "Yes",
                LibraryReproducibility.NotReproducible => "No",
                LibraryReproducibility.Unavailable => "unavailable",
                _ => null,
            },
            Text(informationalVersion),
            Text(description?.Company),
            Text(description?.Product),
            Text(description?.Copyright),
            EnabledLabels(document.Enablements),
            ResolveVersion(
                data.PlatformVersion,
                informationalVersion is LibraryTextFact.Present present ? present.Value.ToString() : null,
                assemblyVersion),
            null);
    }

    private static LibraryScalarFields FromLegacy(LibraryInspection data, AssemblyInfo info) =>
        new(
            info.AssemblyName,
            info.AssemblyVersion,
            info.PublicKeyToken,
            data.FileSize > 0 ? ByteSizeFormatter.FormatBytes(data.FileSize) : null,
            info.TargetFramework,
            info.CompilationType,
            info.Architecture,
            info.IsSigned,
            data.HasReproducibleFlag ? "Yes" : "No",
            info.InformationalVersion,
            info.Company,
            info.Product,
            info.Copyright,
            null,
            LibraryInspectionDisplay.ResolveVersion(data),
            null);

    private static string? Text(LibraryTextFact? fact) =>
        fact switch
        {
            LibraryTextFact.Present present => present.Value.ToString(),
            LibraryTextFact.Unavailable unavailable =>
                $"unavailable ({Reason(unavailable.Reason)})",
            _ => null,
        };

    private static string Reason(LibraryTextFactUnavailableReason reason) =>
        reason switch
        {
            LibraryTextFactUnavailableReason.UndecodableMetadata => "undecodable-metadata",
            LibraryTextFactUnavailableReason.ConflictingValues => "conflicting-values",
            _ => throw new InvalidOperationException("Unknown text fact reason."),
        };

    /// <summary>
    /// Enabled labels joined by <c> · </c>; null when none is Enabled. A failed
    /// Enablements group reads as unavailable with its reason.
    /// </summary>
    private static string? EnabledLabels(LibraryEnablementsOutcome? enablements) =>
        enablements switch
        {
            LibraryEnablementsOutcome.Available available =>
                available.Facts.Enabled().Select(LibraryEnablementFacts.Label).ToArray() is { Length: > 0 } labels
                    ? string.Join(" · ", labels)
                    : null,
            LibraryEnablementsOutcome.Failed failed => $"unavailable ({failed.Reason})",
            _ => null,
        };

    /// <summary>
    /// Platform version, then the numeric Informational Version without build
    /// metadata (a prerelease suffix is kept), then the Assembly Version.
    /// </summary>
    internal static string ResolveVersion(
        string? platformVersion,
        string? informationalVersion,
        string assemblyVersion)
    {
        if (!string.IsNullOrEmpty(platformVersion))
            return platformVersion;
        if (!string.IsNullOrEmpty(informationalVersion))
        {
            string version = informationalVersion;
            int plus = version.IndexOf('+');
            if (plus > 0)
                version = version[..plus];
            int dash = version.IndexOf('-');
            string numeric = dash > 0 ? version[..dash] : version;
            if (numeric.Split('.').All(part => int.TryParse(part, out _)))
                return dash > 0 ? version : numeric;
        }

        return assemblyVersion;
    }
}
