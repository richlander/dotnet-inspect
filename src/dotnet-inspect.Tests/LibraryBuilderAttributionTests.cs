using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Gates <see cref="LibraryMetadataService.InferBuilder"/>'s claim that a self-declared source
/// URL is not evidence about who built a binary. Both the SourceLink map and the
/// <c>Company</c> attribute travel inside the artifact under inspection, so an assembly that
/// says "Microsoft" and points at <c>dotnet/runtime</c> has attested to nothing an attacker
/// could not also write. A symbol server that served the PDB is different: that is a third
/// party's statement, made outside the artifact.
/// </summary>
public class LibraryBuilderAttributionTests
{
    private const string DotnetRuntimeMap =
        """
        {"documents":{"/_/*":"https://raw.githubusercontent.com/dotnet/runtime/abc/*"}}
        """;

    [Fact]
    public void ASourceLinkMapPointingAtDotnet_IsNotEvidenceMicrosoftBuiltTheAssembly()
    {
        var inspection = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo { Company = "Microsoft Corporation" },
            HasSourceLink = true,
            SourceLinkJson = DotnetRuntimeMap,
        };

        Assert.Null(LibraryMetadataService.InferBuilder(inspection));

        // The same inputs with a third party's statement added must attribute, or the assertion
        // above would hold for an inference that had simply stopped working. Changing one field
        // is what makes this say the map is not evidence, rather than that nothing is.
        var served = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo { Company = "Microsoft Corporation" },
            HasSourceLink = true,
            SourceLinkJson = DotnetRuntimeMap,
            SymbolServer = "msdl.microsoft.com",
        };

        Assert.Equal("Microsoft", LibraryMetadataService.InferBuilder(served));
    }

    [Fact]
    public void ASymbolServerThatServedTheSymbols_IsEvidenceMicrosoftBuiltTheAssembly()
    {
        var inspection = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo { Company = "Microsoft Corporation" },
            HasSourceLink = true,
            SymbolServer = "msdl.microsoft.com",
        };

        Assert.Equal("Microsoft", LibraryMetadataService.InferBuilder(inspection));
    }

    [Fact]
    public void ANonMicrosoftAssembly_IsNotAttributedHoweverItsMapReads()
    {
        var inspection = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo { Company = "Contoso" },
            HasSourceLink = true,
            SourceLinkJson = DotnetRuntimeMap,
            SymbolServer = "msdl.microsoft.com",
        };

        Assert.Null(LibraryMetadataService.InferBuilder(inspection));
    }
}
