using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Commands;

/// <summary>CLI admission of the Package owner's opaque transport token.</summary>
internal static class PackageRootRequestAdmission
{
    internal static async Task<PackageRootBinding?> AcquireAsync(
        string token,
        WorkspaceContextLoadOptions loadOptions,
        PackageRootHouseExecution rootHouse,
        CancellationToken cancellationToken,
        string sourceLabel = "--root-request")
    {
        if (!PackageRootReacquisitionRequest.TryDecode(
                token,
                out PackageRootReacquisitionRequest? request))
        {
            CommandError.Write(
                sourceLabel == "--root-request"
                    ? "--root-request must be a package Root reopening token issued by this tool."
                    : "Package reference must be an exact Package reference issued by this tool.",
                [
                    $"Expected a '{PackageRootReacquisitionRequest.TokenPrefix}' token of at most "
                    + $"{PackageRootReacquisitionRequest.MaxEncodedLength} characters, as printed in the "
                    + "Root column of 'package query ... --where \"library-literal=TEXT\" --tfm TFM'.",
                ]);
            return null;
        }

        PackageRootAcquisitionOutcome outcome =
            await PackageRootAcquisition.AcquireAsync(
                request,
                rootHouse,
                cancellationToken).ConfigureAwait(false);
        if (outcome is PackageRootAcquisitionOutcome.Failed failed)
            return Failure(failed.Kind, failed.Message);

        var acquired = (PackageRootAcquisitionOutcome.Acquired)outcome;
        PackageRootBinding binding = acquired.Binding;
        PackagePayloadOrigin origin = acquired.Payload.Origin;

        loadOptions.Log?.Invoke(
            $"Reopened {binding.Coordinate.PackageId}@{binding.Coordinate.Version} "
            + $"from producer '{binding.Root.ProducerKey}' ({origin}); "
            + $"compile {request.CompileTargetFramework ?? "(none)"}, "
            + $"implementation {request.SelectionTargetFramework ?? "(none)"} "
            + $"resolved {binding.Root.AssetSelection.Status}.");
        return binding;

        PackageRootBinding? Failure(
            PackageRootAcquisitionFailureKind kind,
            string message)
        {
            CommandError.Write(
                $"The Package named by {sourceLabel} could not be reopened: {request}.",
                [$"{kind}: {message}"]);
            return null;
        }
    }
}
