// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using NuGetFetch;

namespace DotnetInspector.Services;

/// <summary>
/// Result of NuGet package signature verification.
/// </summary>
public record SignatureVerificationResult
{
    /// <summary>
    /// Author/publisher identity from the author signature CN.
    /// </summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// Whether the author signature was verified successfully.
    /// </summary>
    public bool AuthorVerified { get; init; }

    /// <summary>
    /// Whether the repository signature was verified successfully.
    /// </summary>
    public bool RepositoryVerified { get; init; }

    /// <summary>
    /// Identity from the verified repository signature certificate.
    /// </summary>
    public string? Repository { get; init; }

    /// <summary>
    /// Status message when verification was skipped or failed.
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Whether the package has no signatures at all.
    /// </summary>
    public bool IsUnsigned { get; init; }
}

/// <summary>
/// Verifies NuGet package signatures using in-process CMS/X.509 verification
/// via NuGetFetch's PackageSignatureVerifier.
/// </summary>
public static class SignatureVerifier
{
    /// <summary>
    /// Verifies the signature of a NuGet package.
    /// </summary>
    /// <param name="nupkgPath">Path to the .nupkg file</param>
    /// <returns>Verification result, or null if verification could not be performed</returns>
    public static SignatureVerificationResult? Verify(string nupkgPath)
    {
        if (!File.Exists(nupkgPath))
            return null;

        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);
            return FromNuGetResult(result);
        }
        catch (Exception ex)
        {
            return new SignatureVerificationResult
            {
                StatusMessage = $"Verification failed: {ex.Message}"
            };
        }
    }

    internal static SignatureVerificationResult FromNuGetResult(
        NuGetFetch.SignatureVerificationResult result)
        => result.Status switch
        {
            SignatureStatus.Unsigned => new SignatureVerificationResult
            {
                IsUnsigned = true,
                StatusMessage = "Package is not signed"
            },
            SignatureStatus.Invalid => new SignatureVerificationResult
            {
                StatusMessage = $"Verification failed: {result.Reason}"
            },
            SignatureStatus.Valid => ValidResult(result),
            _ => new SignatureVerificationResult
            {
                StatusMessage = "Unknown verification result"
            }
        };

    private static SignatureVerificationResult ValidResult(
        NuGetFetch.SignatureVerificationResult result)
    {
        bool authorVerified = result.SignatureType == SignatureType.Author;
        NuGetFetch.SignatureVerificationResult? repositorySignature =
            result.SignatureType == SignatureType.Repository
                ? result
                : result.CounterSignature is
                {
                    IsValid: true,
                    SignatureType: SignatureType.Repository
                } counterSignature
                    ? counterSignature
                    : null;

        return new SignatureVerificationResult
        {
            Publisher = authorVerified ? result.Publisher : null,
            AuthorVerified = authorVerified,
            RepositoryVerified = repositorySignature is not null,
            Repository = repositorySignature?.Publisher,
        };
    }

    /// <summary>
    /// Async wrapper for compatibility with existing callers.
    /// The underlying verification is synchronous (in-process crypto).
    /// </summary>
    public static Task<SignatureVerificationResult?> VerifyAsync(string nupkgPath) =>
        Task.FromResult(Verify(nupkgPath));
}
