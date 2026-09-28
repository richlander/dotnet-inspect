using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public static class PackageFileInventoryCapability
{
    public const string DocumentIdentity =
        "package-file-inventory/document/v1";
    public const string ProductModuleIdentity =
        "package-file-inventory/product/v1";

    public static InspectionDocumentRegistration<
        PackageFileInventoryDocument> Document { get; } =
        new(
            new(
                DocumentIdentity,
                "Package file inventory",
                "A detached, deterministically ordered inventory of one "
                    + "acquired package generation.",
                PackageFileInventoryQuery.FileRowsResultContract));

    public static InspectionRouteRegistration<
        PackageFileInventoryInspectionRequest,
        PackageFileInventoryDocument> Route { get; } =
        new(
            new(
                PackageFileInventoryQuery.OperationRouteIdentity,
                "Package file inventory",
                "Borrows one acquired package generation and returns detached "
                    + "package-file rows."),
            Document,
            PackageFileInventoryQuery.QuerySpace,
            static (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(
                    PackageFileInventoryInspection.Execute(request));
            });

    public static InspectionCapabilityModule ProductModule { get; } =
        new(
            ProductModuleIdentity,
            documents: [Document],
            routes: [Route],
            adoptionRequirements:
            [
                new(Route, InspectionConsumerKind.Cli),
            ]);
}
