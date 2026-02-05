using DotnetInspector.Inspectors;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for SignatureVerifier parsing logic.
/// </summary>
public class SignatureVerifierTests
{
    [Fact]
    public void ParseVerificationOutput_AuthorAndRepositorySigned_ExtractsPublisher()
    {
        var output = """
            Verifying Newtonsoft.Json.13.0.4
            Content hash: pdgNNMai3zv51W5aq268sujXUyx7SNdE2bj1wZcWjAQrKMFZV260lbqYop1d2GM67JI1huLRwxo9ZqnfF/lC6A==

            Signature type: Author
              Subject Name: CN=Json.NET (.NET Foundation), O=Json.NET (.NET Foundation), L=Redmond, S=Washington, C=US
              SHA256 hash: DB1BD14AC8A4FA504ADEF36D1EAD72A14E3F61E937D80BF09DC27473C8682083
              Valid from: 11/3/2024 4:00:00 PM to 11/3/2027 4:59:59 PM

            Signature type: Repository
              Subject Name: CN=NuGet.org Repository by Microsoft, O=NuGet.org Repository by Microsoft, L=Redmond, S=Washington, C=US
              SHA256 hash: 1F4B311D9ACC115C8DC8018B5A49E00FCE6DA8E2855F9F014CA6F34570BC482D
              Valid from: 2/22/2024 4:00:00 PM to 5/18/2027 4:59:59 PM

            Successfully verified package 'Newtonsoft.Json.13.0.4'.
            """;

        var result = SignatureVerifier.ParseVerificationOutput(output, "", exitCode: 0);

        Assert.Equal("Json.NET (.NET Foundation)", result.Publisher);
        Assert.True(result.AuthorVerified);
        Assert.True(result.RepositoryVerified);
        Assert.Equal("nuget.org", result.Repository);
        Assert.False(result.IsUnsigned);
        Assert.Null(result.StatusMessage);
    }

    [Fact]
    public void ParseVerificationOutput_MicrosoftPackage_ExtractsPublisher()
    {
        var output = """
            Verifying Microsoft.Extensions.Logging.10.0.2
            Content hash: a0EWuBs6D3d7XMGroDXm+WsAi5CVVfjOJvyxurzWnuhBN9CO+1qHKcrKV1JK7H/T4ZtHIoVCOX/YyWM8K87qtw==

            Signature type: Author
              Subject Name: CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US
              SHA256 hash: 566A31882BE208BE4422F7CFD66ED09F5D4524A5994F50CCC8B05EC0528C1353
              Valid from: 7/26/2023 5:00:00 PM to 10/17/2026 4:59:59 PM

            Signature type: Repository
              Subject Name: CN=NuGet.org Repository by Microsoft, O=NuGet.org Repository by Microsoft, L=Redmond, S=Washington, C=US
              SHA256 hash: 1F4B311D9ACC115C8DC8018B5A49E00FCE6DA8E2855F9F014CA6F34570BC482D
              Valid from: 2/22/2024 4:00:00 PM to 5/18/2027 4:59:59 PM

            Successfully verified package 'Microsoft.Extensions.Logging.10.0.2'.
            """;

        var result = SignatureVerifier.ParseVerificationOutput(output, "", exitCode: 0);

        Assert.Equal("Microsoft Corporation", result.Publisher);
        Assert.True(result.AuthorVerified);
        Assert.True(result.RepositoryVerified);
    }

    [Fact]
    public void ParseVerificationOutput_UnsignedPackage_ReturnsUnsigned()
    {
        var error = "error NU3004: The package is not signed.";

        var result = SignatureVerifier.ParseVerificationOutput("", error, exitCode: 1);

        Assert.True(result.IsUnsigned);
        Assert.Equal("Package is not signed", result.StatusMessage);
        Assert.Null(result.Publisher);
        Assert.False(result.AuthorVerified);
    }

    [Fact]
    public void ParseVerificationOutput_MacOSSkipped_ReturnsSkipMessage()
    {
        var output = "Package signature verification skipped on macOS.";

        var result = SignatureVerifier.ParseVerificationOutput(output, "", exitCode: 0);

        Assert.Equal("Skipped (macOS not supported)", result.StatusMessage);
        Assert.Null(result.Publisher);
    }

    [Fact]
    public void ParseVerificationOutput_VerificationFailed_ReturnsFailure()
    {
        var output = "Verification failed for some reason";

        var result = SignatureVerifier.ParseVerificationOutput(output, "", exitCode: 1);

        Assert.Equal("Verification failed", result.StatusMessage);
        Assert.False(result.AuthorVerified);
        Assert.False(result.RepositoryVerified);
    }
}
