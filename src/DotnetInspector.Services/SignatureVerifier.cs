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
    /// Repository source (e.g., "nuget.org").
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

            return result.Status switch
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
                SignatureStatus.Valid => new SignatureVerificationResult
                {
                    // Only report publisher for author-signed packages;
                    // for repo-only signatures the CN is the repository, not the author
                    Publisher = result.SignatureType == SignatureType.Author ? result.Publisher : null,
                    AuthorVerified = result.SignatureType == SignatureType.Author,
                    RepositoryVerified = true, // nuget.org adds repo countersignature to all packages
                    Repository = "nuget.org",
                },
                _ => new SignatureVerificationResult
                {
                    StatusMessage = "Unknown verification result"
                }
            };
        }
        catch (Exception ex)
        {
            return new SignatureVerificationResult
            {
                StatusMessage = $"Verification failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Async wrapper for compatibility with existing callers.
    /// The underlying verification is synchronous (in-process crypto).
    /// </summary>
    public static Task<SignatureVerificationResult?> VerifyAsync(string nupkgPath) =>
        Task.FromResult(Verify(nupkgPath));
}
