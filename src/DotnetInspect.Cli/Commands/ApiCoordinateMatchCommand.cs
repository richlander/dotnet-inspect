using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Output;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

internal static class ApiCoordinateMatchCommand
{
    internal static async Task<int> ExecuteAsync(
        ApiCoordinateMatchOptionsParser.Success options,
        CancellationToken cancellationToken)
    {
        await using var provider =
            new ConfiguredPackageRootPayloadProvider(
                HttpClientFactory.Shared.Timeout,
                options.SourceOptions);
        try
        {
            InspectionEnvelope<ApiCoordinateMatchContent> envelope =
                await ApiCoordinateMatchInspection.ExecuteAsync(
                    options.Request,
                    provider,
                    cancellationToken).ConfigureAwait(false);
            return ApiCoordinateMatchOutput.Write(envelope, options);
        }
        catch (Exception exception)
        {
            CommandError.Write(exception);
            return 1;
        }
    }
}
