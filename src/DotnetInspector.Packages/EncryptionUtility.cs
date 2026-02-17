// Forked from NuGet.Client — src/NuGet.Core/NuGet.Configuration/Utility/EncryptionUtility.cs
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
//
// Minimal fork: decrypt-only, for reading DPAPI-encrypted passwords from nuget.config.

using System.Security.Cryptography;
using System.Text;

namespace DotnetInspector.Packages;

/// <summary>
/// Decrypts DPAPI-encrypted passwords stored in nuget.config (Windows only).
/// </summary>
internal static class EncryptionUtility
{
    private static readonly byte[] EntropyBytes = Encoding.UTF8.GetBytes("NuGet");

    public static string DecryptString(string encryptedString)
    {
        if (!OperatingSystem.IsWindows())
            throw new NotSupportedException("Encrypted NuGet credentials are only supported on Windows. Use ClearTextPassword instead.");

        var encryptedByteArray = Convert.FromBase64String(encryptedString);
        var decryptedByteArray = ProtectedData.Unprotect(encryptedByteArray, EntropyBytes, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decryptedByteArray);
    }
}
