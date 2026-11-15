using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// Host-neutral display spellings for Library document Image and Description
/// facts, shared by CLI <c>Library Info</c> and Inspect Web Library Overview
/// so the two hosts cannot spell one fact differently.
/// </summary>
public static class LibraryFactLabels
{
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
