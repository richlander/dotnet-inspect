// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.RegularExpressions;

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
/// Verifies NuGet package signatures using dotnet nuget verify.
/// </summary>
public static partial class SignatureVerifier
{
    /// <summary>
    /// Verifies the signature of a NuGet package.
    /// </summary>
    /// <param name="nupkgPath">Path to the .nupkg file</param>
    /// <returns>Verification result, or null if verification could not be performed</returns>
    public static async Task<SignatureVerificationResult?> VerifyAsync(string nupkgPath)
    {
        if (!File.Exists(nupkgPath))
            return null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"nuget verify \"{nupkgPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Wait up to 10 seconds for verification
            var completed = await Task.Run(() => process.WaitForExit(10_000));
            if (!completed)
            {
                process.Kill();
                return new SignatureVerificationResult
                {
                    StatusMessage = "Verification timed out"
                };
            }

            var output = await outputTask;
            var error = await errorTask;

            return ParseVerificationOutput(output, error, process.ExitCode);
        }
        catch (Exception ex)
        {
            return new SignatureVerificationResult
            {
                StatusMessage = $"Verification failed: {ex.Message}"
            };
        }
    }

    internal static SignatureVerificationResult ParseVerificationOutput(string output, string error, int exitCode)
    {
        // Check for macOS skip message
        if (output.Contains("skipped") && output.Contains("macOS", StringComparison.OrdinalIgnoreCase))
        {
            return new SignatureVerificationResult
            {
                StatusMessage = "Skipped (macOS not supported)"
            };
        }

        // Check for unsigned package
        if (error.Contains("NU3004") || output.Contains("NU3004") ||
            error.Contains("not signed", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("not signed", StringComparison.OrdinalIgnoreCase))
        {
            return new SignatureVerificationResult
            {
                IsUnsigned = true,
                StatusMessage = "Package is not signed"
            };
        }

        // Parse author signature
        string? publisher = null;
        bool authorVerified = false;
        var authorMatch = AuthorSignatureRegex().Match(output);
        if (authorMatch.Success)
        {
            var subjectMatch = SubjectNameRegex().Match(output, authorMatch.Index);
            if (subjectMatch.Success)
            {
                publisher = ExtractCN(subjectMatch.Groups[1].Value);
                authorVerified = true;
            }
        }

        // Parse repository signature
        bool repositoryVerified = false;
        string? repository = null;
        var repoMatch = RepositorySignatureRegex().Match(output);
        if (repoMatch.Success)
        {
            repositoryVerified = true;
            // Check for nuget.org
            if (output.Contains("NuGet.org Repository", StringComparison.OrdinalIgnoreCase))
            {
                repository = "nuget.org";
            }
        }

        // Check overall success
        bool success = exitCode == 0 && output.Contains("Successfully verified", StringComparison.OrdinalIgnoreCase);

        if (!success && !authorVerified && !repositoryVerified)
        {
            return new SignatureVerificationResult
            {
                StatusMessage = "Verification failed"
            };
        }

        return new SignatureVerificationResult
        {
            Publisher = publisher,
            AuthorVerified = authorVerified,
            RepositoryVerified = repositoryVerified,
            Repository = repository
        };
    }

    private static string? ExtractCN(string subjectName)
    {
        // Extract CN from subject name like "CN=Json.NET (.NET Foundation), O=..."
        var cnMatch = CommonNameRegex().Match(subjectName);
        if (cnMatch.Success)
        {
            return cnMatch.Groups[1].Value.Trim();
        }
        return null;
    }

    [GeneratedRegex(@"Signature type:\s*Author", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorSignatureRegex();

    [GeneratedRegex(@"Signature type:\s*Repository", RegexOptions.IgnoreCase)]
    private static partial Regex RepositorySignatureRegex();

    [GeneratedRegex(@"Subject Name:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex SubjectNameRegex();

    [GeneratedRegex(@"CN=([^,]+)")]
    private static partial Regex CommonNameRegex();
}
