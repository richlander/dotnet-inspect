using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

public static class PackageFileInventoryCommandCapability
{
    public const string BindingIdentity =
        "package-file-inventory/consumer/cli/v1";
    public const string ModuleIdentity =
        "package-file-inventory/cli/v1";

    public static InspectionConsumerBinding<
        PackageFileInventoryInspectionRequest,
        PackageFileInventoryDocument> Binding { get; } =
        new(
            new(
                BindingIdentity,
                InspectionConsumerKind.Cli,
                "DotnetInspect.Cli package command",
                "package <id> -S \"Package files\""),
            PackageFileInventoryCapability.Route,
            exposedQueryTerms: []);

    public static InspectionCapabilityModule Module { get; } =
        new(
            ModuleIdentity,
            bindings: [Binding]);
}
