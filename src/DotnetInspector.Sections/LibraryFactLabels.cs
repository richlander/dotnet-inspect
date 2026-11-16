using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// Host-neutral display spellings for Library document Image and Description
/// facts, shared by CLI <c>Library Info</c> and Inspect Web Library Overview
/// so the two hosts cannot spell one fact differently.
/// </summary>
public static class LibraryFactLabels
{
    /// <summary>
    /// The Image group with every value spelled, so a host projects plain
    /// strings and never depends on the Metadata fact types.
    /// </summary>
    public static LibraryImageFactLabels Image(LibraryImageFacts image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new(
            image.ImageBytes,
            Text(image.TargetFramework),
            Compilation(image.Compilation),
            Architecture(image.Architecture),
            image.StrongNameSigned,
            Reproducibility(image.Reproducibility));
    }

    /// <summary>The Description group with every value spelled.</summary>
    public static LibraryDescriptionFactLabels Description(
        LibraryDescriptionFacts description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return new(
            Text(description.InformationalVersion),
            Text(description.Company),
            Text(description.Product),
            Text(description.Copyright));
    }

    public static string? Compilation(LibraryCompilationForm? form) =>
        form switch
        {
            LibraryCompilationForm.IL => "CoreCLR",
            LibraryCompilationForm.ReadyToRun => "ReadyToRun",
            _ => null,
        };

    public static string? Architecture(LibraryArchitecture? architecture) =>
        architecture switch
        {
            LibraryArchitecture.AnyCpu => "AnyCPU",
            LibraryArchitecture.AnyCpuPrefers32Bit => "AnyCPU (32-bit preferred)",
            LibraryArchitecture.X86 => "x86",
            LibraryArchitecture.X64 => "x64",
            LibraryArchitecture.Arm => "ARM",
            LibraryArchitecture.Arm64 => "ARM64",
            _ => null,
        };

    public static string? Reproducibility(LibraryReproducibility? reproducibility) =>
        reproducibility switch
        {
            LibraryReproducibility.Reproducible => "Yes",
            LibraryReproducibility.NotReproducible => "No",
            LibraryReproducibility.Unavailable => "unavailable",
            _ => null,
        };

    /// <summary>
    /// A present value, <c>unavailable (reason)</c> for an unreadable value,
    /// or null when the image does not carry the fact.
    /// </summary>
    public static string? Text(LibraryTextFact? fact) =>
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
}

/// <summary>A spelled Image fact group; null means the image does not carry the fact.</summary>
public sealed record LibraryImageFactLabels(
    int ImageBytes,
    string? TargetFramework,
    string? Compilation,
    string? Architecture,
    bool StrongNameSigned,
    string? Reproducible);

/// <summary>A spelled Description fact group; null means the image does not carry the fact.</summary>
public sealed record LibraryDescriptionFactLabels(
    string? InformationalVersion,
    string? Company,
    string? Product,
    string? Copyright);
