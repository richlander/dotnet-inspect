using System.Text;

using DotnetInspect.Cli.Options;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal sealed class WorkspacePatBindingException(string message)
    : Exception(message);

internal static class WorkspacePatCredentialResolver
{
    internal const int MaxSecretUtf8Bytes = 64 * 1024;

    private static readonly UTF8Encoding s_strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static async Task<PackageSource[]> ResolveAsync(
        IReadOnlyList<WorkspacePackageSourceDefinition> sourceDefinitions,
        IReadOnlyList<WorkspacePatBindingInput> bindings,
        CancellationToken cancellationToken) =>
        await ResolveAsync(
            sourceDefinitions,
            bindings,
            Console.IsInputRedirected,
            Console.OpenStandardInput,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<PackageSource[]> ResolveAsync(
        IReadOnlyList<WorkspacePackageSourceDefinition> sourceDefinitions,
        IReadOnlyList<WorkspacePatBindingInput> bindings,
        bool isInputRedirected,
        Func<Stream> openStandardInput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinitions);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(openStandardInput);

        var requirements = sourceDefinitions
            .Where(static source =>
                source.Authentication
                    == WorkspacePackageSourceAuthentication.BasicPat)
            .ToDictionary(static source => source.Id, StringComparer.Ordinal);
        var suppliedBindings = new Dictionary<
            string,
            WorkspacePatBindingInput>(StringComparer.Ordinal);
        foreach (WorkspacePatBindingInput binding in bindings)
        {
            if (!suppliedBindings.TryAdd(binding.SourceId, binding))
            {
                throw new WorkspacePatBindingException(
                    $"A PAT binding for source '{binding.SourceId}' was supplied more than once.");
            }
            if (!requirements.ContainsKey(binding.SourceId))
            {
                throw new WorkspacePatBindingException(
                    $"PAT binding source '{binding.SourceId}' is not a required "
                        + "PAT source in this Workspace.");
            }
        }

        string? missing = requirements.Keys.FirstOrDefault(
            id => !suppliedBindings.ContainsKey(id));
        if (missing is not null)
        {
            throw new WorkspacePatBindingException(
                $"Workspace source '{missing}' requires a PAT. Supply "
                    + $"--pat {missing}=env:NAME, --pat {missing}=stdin, or "
                    + $"--pat {missing}=file:PATH.");
        }

        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string id, WorkspacePatBindingInput binding)
            in suppliedBindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            secrets.Add(
                id,
                await ReadSecretAsync(
                        binding,
                        isInputRedirected,
                        openStandardInput,
                        cancellationToken)
                    .ConfigureAwait(false));
        }

        return
        [
            .. sourceDefinitions.Select(source =>
                new PackageSource(
                    source.Id,
                    source.Endpoint,
                    source.Authentication
                        == WorkspacePackageSourceAuthentication.BasicPat
                            ? new PackageSourceCredential(
                                source.Username!,
                                secrets[source.Id])
                            : null)),
        ];
    }

    internal static void ValidateBindings(
        IReadOnlyList<WorkspacePackageSourceDefinition> sourceDefinitions,
        IReadOnlyList<WorkspacePatBindingInput> bindings)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinitions);
        ArgumentNullException.ThrowIfNull(bindings);

        HashSet<string> requirements =
        [
            .. sourceDefinitions
                .Where(static source =>
                    source.Authentication
                        == WorkspacePackageSourceAuthentication.BasicPat)
                .Select(static source => source.Id),
        ];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkspacePatBindingInput binding in bindings)
        {
            if (!seen.Add(binding.SourceId))
            {
                throw new WorkspacePatBindingException(
                    $"A PAT binding for source '{binding.SourceId}' was supplied more than once.");
            }
            if (!requirements.Contains(binding.SourceId))
            {
                throw new WorkspacePatBindingException(
                    $"PAT binding source '{binding.SourceId}' is not a required "
                        + "PAT source in this Workspace.");
            }
        }
    }

    private static async Task<string> ReadSecretAsync(
        WorkspacePatBindingInput binding,
        bool isInputRedirected,
        Func<Stream> openStandardInput,
        CancellationToken cancellationToken)
    {
        string value = binding.Kind switch
        {
            WorkspacePatInputKind.Environment =>
                ReadEnvironment(binding.SourceId, binding.Value!),
            WorkspacePatInputKind.StandardInput =>
                await ReadStandardInputAsync(
                    binding.SourceId,
                    isInputRedirected,
                    openStandardInput,
                    cancellationToken).ConfigureAwait(false),
            WorkspacePatInputKind.File =>
                await ReadFileAsync(
                    binding.SourceId,
                    binding.Value!,
                    cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "Unknown Workspace PAT input kind."),
        };
        return Validate(
            binding.SourceId,
            value,
            trimTrailingLineEnding:
                binding.Kind is WorkspacePatInputKind.StandardInput
                    or WorkspacePatInputKind.File);
    }

    private static string ReadEnvironment(string sourceId, string variable)
    {
        if (variable.Contains('=') || variable.Contains('\0'))
        {
            throw new WorkspacePatBindingException(
                $"The environment variable name for source '{sourceId}' is invalid.");
        }

        return Environment.GetEnvironmentVariable(variable)
            ?? throw new WorkspacePatBindingException(
                $"Environment variable '{variable}' for Workspace source "
                    + $"'{sourceId}' is not set.");
    }

    private static async Task<string> ReadStandardInputAsync(
        string sourceId,
        bool isInputRedirected,
        Func<Stream> openStandardInput,
        CancellationToken cancellationToken)
    {
        if (!isInputRedirected)
        {
            throw new WorkspacePatBindingException(
                $"PAT input for Workspace source '{sourceId}' selected stdin, "
                    + "but stdin is a terminal. Pipe the credential instead.");
        }

        return await ReadUtf8Async(
            openStandardInput(),
            sourceId,
            "stdin",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadFileAsync(
        string sourceId,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await ReadUtf8Async(
                stream,
                sourceId,
                "file",
                cancellationToken).ConfigureAwait(false);
        }
        catch (WorkspacePatBindingException)
        {
            throw;
        }
        catch (Exception ex) when (ex is
            ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            throw new WorkspacePatBindingException(
                $"The PAT file for Workspace source '{sourceId}' could not be read.");
        }
    }

    private static async Task<string> ReadUtf8Async(
        Stream stream,
        string sourceId,
        string provider,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        using var payload = new MemoryStream();
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(
                    buffer,
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (payload.Length + read > MaxSecretUtf8Bytes)
                {
                    throw new WorkspacePatBindingException(
                        $"PAT input from {provider} for Workspace source "
                            + $"'{sourceId}' exceeds the "
                            + $"{MaxSecretUtf8Bytes}-byte limit.");
                }
                payload.Write(buffer, 0, read);
                Array.Clear(buffer, 0, read);
            }

            try
            {
                return s_strictUtf8.GetString(payload.GetBuffer(), 0, (int)payload.Length);
            }
            catch (DecoderFallbackException)
            {
                throw new WorkspacePatBindingException(
                    $"PAT input from {provider} for Workspace source "
                        + $"'{sourceId}' is not valid UTF-8.");
            }
        }
        finally
        {
            Array.Clear(buffer);
            if (payload.TryGetBuffer(out ArraySegment<byte> segment))
                Array.Clear(segment.Array!, segment.Offset, segment.Count);
        }
    }

    private static string Validate(
        string sourceId,
        string value,
        bool trimTrailingLineEnding)
    {
        try
        {
            if (s_strictUtf8.GetByteCount(value) > MaxSecretUtf8Bytes)
            {
                throw new WorkspacePatBindingException(
                    $"PAT input for Workspace source '{sourceId}' exceeds the "
                        + $"{MaxSecretUtf8Bytes}-byte limit.");
            }
        }
        catch (EncoderFallbackException)
        {
            throw new WorkspacePatBindingException(
                $"PAT input for Workspace source '{sourceId}' contains invalid Unicode.");
        }

        string validated = trimTrailingLineEnding
            ? value.EndsWith("\r\n", StringComparison.Ordinal)
                ? value[..^2]
                : value.EndsWith('\r') || value.EndsWith('\n')
                    ? value[..^1]
                    : value
            : value;
        if (validated.Length == 0)
        {
            throw new WorkspacePatBindingException(
                $"PAT input for Workspace source '{sourceId}' is empty.");
        }
        return validated;
    }
}
