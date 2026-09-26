using System.Collections.Immutable;
using NuGet.Frameworks;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>How one framework-reference projection chose its target.</summary>
public abstract record PackageHouseFrameworkReferenceTargetBasis
{
    private PackageHouseFrameworkReferenceTargetBasis()
    {
    }

    public sealed record Exact :
        PackageHouseFrameworkReferenceTargetBasis
    {
        internal Exact(string targetFramework)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
            TargetFramework = targetFramework;
        }

        public string TargetFramework { get; }
    }

    public sealed record CompileSelection :
        PackageHouseFrameworkReferenceTargetBasis
    {
        internal CompileSelection(string targetFramework)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
            TargetFramework = targetFramework;
        }

        public string TargetFramework { get; }
    }

    public sealed record Unavailable :
        PackageHouseFrameworkReferenceTargetBasis
    {
        internal Unavailable()
        {
        }
    }
}

/// <summary>
/// Exact resource-free association for framework-reference evidence.
/// </summary>
public sealed class PackageHouseFrameworkReferenceAssociation
{
    internal PackageHouseFrameworkReferenceAssociation(
        PackageHouseResult result,
        PackageHouseRealizationReceipt.Compile realization,
        PackageHouseFrameworkReferenceTargetBasis targetBasis)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentNullException.ThrowIfNull(targetBasis);
        if (!ReferenceEquals(result.Evidence.Realization, realization))
        {
            throw new ArgumentException(
                "Framework-reference evidence must retain the result's exact compile realization.",
                nameof(realization));
        }

        Result = result;
        Realization = realization;
        TargetBasis = targetBasis;
    }

    public PackageHouseResult Result { get; }

    public PackageHouseAcquisitionReceipt Acquisition =>
        Realization.Acquisition;

    public PackageHouseRealizationReceipt.Compile Realization { get; }

    public PackageCompileAssetSelectionReceipt CompileSelection =>
        Realization.Receipt;

    public PackageContentGenerationIdentity Generation =>
        Acquisition.Generation;

    public PackageHouseFrameworkReferenceTargetBasis TargetBasis { get; }
}

/// <summary>Why an acquired manifest was unavailable to the projection.</summary>
public enum PackageHouseManifestUnavailableReason
{
    MissingRootManifest,
    InvalidRootManifestSet,
    EntryNotMaterialized,
    EntryUnavailable,
    PayloadReadUnavailable,
    PayloadInvalid,
    ReadFailed,
}

/// <summary>One typed framework-reference projection failure.</summary>
public abstract record PackageHouseFrameworkReferenceFailure
{
    private PackageHouseFrameworkReferenceFailure()
    {
    }

    public sealed record Manifest(PackageManifestFailure Failure) :
        PackageHouseFrameworkReferenceFailure;

    public sealed record FrameworkSection(
        PackageManifestFrameworkReferenceFailure Failure) :
        PackageHouseFrameworkReferenceFailure;

    public sealed record InvalidTargetFramework(string TargetFramework) :
        PackageHouseFrameworkReferenceFailure;
}

/// <summary>
/// Resource-free framework-reference evidence for one exact acquired compile
/// realization.
/// </summary>
public sealed class PackageHouseFrameworkReferenceEvidence
{
    internal PackageHouseFrameworkReferenceEvidence(
        PackageHouseFrameworkReferenceAssociation association,
        ImmutableArray<PackageManifestFrameworkReferenceGroup> groups,
        PackageManifestFrameworkReferenceGroup selectedGroup)
    {
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(selectedGroup);
        if (groups.IsDefault
            || !groups.Any(group => ReferenceEquals(group, selectedGroup)))
        {
            throw new ArgumentException(
                "Selected framework-reference evidence must retain one exact manifest group.",
                nameof(selectedGroup));
        }

        Association = association;
        Groups = groups;
        SelectedOccurrence = selectedGroup;
    }

    public PackageHouseFrameworkReferenceAssociation Association { get; }

    public ImmutableArray<PackageManifestFrameworkReferenceGroup> Groups
    { get; }

    public PackageManifestFrameworkReferenceGroup SelectedOccurrence
    { get; }

    public string SelectedTargetFramework =>
        SelectedOccurrence.CanonicalTargetFramework;

    public ImmutableArray<PackageFrameworkReferenceOccurrence> Occurrences =>
        SelectedOccurrence.Occurrences;

    public ImmutableArray<PackageFrameworkReferenceIdentity> References =>
        SelectedOccurrence.References;
}

/// <summary>
/// Closed result of projecting framework references from one exact acquired
/// compile realization.
/// </summary>
public abstract class PackageHouseFrameworkReferenceOutcome
{
    private protected PackageHouseFrameworkReferenceOutcome(
        PackageHouseFrameworkReferenceAssociation association)
    {
        ArgumentNullException.ThrowIfNull(association);
        Association = association;
    }

    public PackageHouseFrameworkReferenceAssociation Association { get; }

    public sealed class NotRequested :
        PackageHouseFrameworkReferenceOutcome
    {
        internal NotRequested(
            PackageHouseFrameworkReferenceAssociation association)
            : base(association)
        {
        }
    }

    public sealed class Selected : PackageHouseFrameworkReferenceOutcome
    {
        internal Selected(
            PackageHouseFrameworkReferenceAssociation association,
            PackageHouseFrameworkReferenceEvidence evidence)
            : base(association)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            if (!ReferenceEquals(evidence.Association, association))
            {
                throw new ArgumentException(
                    "Selected evidence must retain the outcome's exact association.",
                    nameof(evidence));
            }
            Evidence = evidence;
        }

        public PackageHouseFrameworkReferenceEvidence Evidence { get; }
    }

    public sealed class NoFrameworkReferenceGroups :
        PackageHouseFrameworkReferenceOutcome
    {
        internal NoFrameworkReferenceGroups(
            PackageHouseFrameworkReferenceAssociation association)
            : base(association)
        {
        }
    }

    public sealed class NoMatchingTargetFramework :
        PackageHouseFrameworkReferenceOutcome
    {
        internal NoMatchingTargetFramework(
            PackageHouseFrameworkReferenceAssociation association,
            string targetFramework)
            : base(association)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
            TargetFramework = targetFramework;
        }

        public string TargetFramework { get; }
    }

    public sealed class TargetUnavailable :
        PackageHouseFrameworkReferenceOutcome
    {
        internal TargetUnavailable(
            PackageHouseFrameworkReferenceAssociation association)
            : base(association)
        {
        }
    }

    public sealed class ManifestUnavailable :
        PackageHouseFrameworkReferenceOutcome
    {
        internal ManifestUnavailable(
            PackageHouseFrameworkReferenceAssociation association,
            PackageHouseManifestUnavailableReason reason)
            : base(association)
        {
            if (!Enum.IsDefined(reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }

        public PackageHouseManifestUnavailableReason Reason { get; }
    }

    public sealed class HouseFailed :
        PackageHouseFrameworkReferenceOutcome
    {
        internal HouseFailed(
            PackageHouseFrameworkReferenceAssociation association)
            : base(association)
        {
        }
    }

    public sealed class Failed :
        PackageHouseFrameworkReferenceOutcome
    {
        internal Failed(
            PackageHouseFrameworkReferenceAssociation association,
            PackageHouseFrameworkReferenceFailure failure)
            : base(association)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public PackageHouseFrameworkReferenceFailure Failure { get; }
    }
}

/// <summary>
/// Projects framework-reference evidence from one exact acquired compile
/// realization without retaining the live payload.
/// </summary>
public static class PackageHouseFrameworkReferenceProjection
{
    public static PackageHouseFrameworkReferenceOutcome Project(
        PackageHouseSettlement.Acquired settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        PackageHouseResult result = settlement.Result;
        PackageHouseRealizationReceipt.Compile realization =
            result.Evidence.Realization
                as PackageHouseRealizationReceipt.Compile
            ?? throw new ArgumentException(
                "Framework-reference evidence requires a PackageHouse compile realization.",
                nameof(settlement));
        PackageHouseFrameworkReferenceTargetBasis targetBasis =
            GetTargetBasis(result.Request, realization);
        var association = new PackageHouseFrameworkReferenceAssociation(
            result,
            realization,
            targetBasis);

        if (result.Request.EvidenceDemand
            != PackageHouseEvidenceDemand.FrameworkReferences)
        {
            return new PackageHouseFrameworkReferenceOutcome.NotRequested(
                association);
        }

        if (result is PackageHouseResult.Failed)
        {
            return new PackageHouseFrameworkReferenceOutcome.HouseFailed(
                association);
        }

        string? manifestPath;
        try
        {
            manifestPath = PackageManifestContent.FindRootManifest(
                settlement.Payload.Content);
        }
        catch (InvalidDataException)
        {
            return ManifestUnavailable(
                association,
                PackageHouseManifestUnavailableReason.InvalidRootManifestSet);
        }
        if (manifestPath is null)
        {
            return ManifestUnavailable(
                association,
                PackageHouseManifestUnavailableReason.MissingRootManifest);
        }

        byte[] manifestBytes;
        try
        {
            using PackageHousePayloadRead read = settlement.OpenPayloadRead(
                manifestPath,
                PackageManifestFactsProjection.MaxManifestBytes);
            using var buffer = new MemoryStream();
            read.CopyTo(buffer);
            manifestBytes = buffer.ToArray();
        }
        catch (PackageEntryNotMaterializedException)
        {
            return ManifestUnavailable(
                association,
                PackageHouseManifestUnavailableReason.EntryNotMaterialized);
        }
        catch (FileNotFoundException)
        {
            return ManifestUnavailable(
                association,
                PackageHouseManifestUnavailableReason.EntryUnavailable);
        }
        catch (NotSupportedException)
        {
            return ManifestUnavailable(
                association,
                PackageHouseManifestUnavailableReason.PayloadReadUnavailable);
        }
        catch (InvalidDataException)
        {
            return ManifestUnavailable(
                association,
                PackageHouseManifestUnavailableReason.PayloadInvalid);
        }
        catch (IOException)
        {
            return ManifestUnavailable(
                association,
                PackageHouseManifestUnavailableReason.ReadFailed);
        }

        PackageManifestFactsResult manifest =
            PackageManifestFactsProjection.Execute(
                manifestBytes,
                association.Acquisition.Candidate.Coordinate);
        if (manifest is PackageManifestFactsResult.Failed manifestFailure)
        {
            return new PackageHouseFrameworkReferenceOutcome.Failed(
                association,
                new PackageHouseFrameworkReferenceFailure.Manifest(
                    manifestFailure.Failure));
        }

        PackageManifestFacts facts =
            ((PackageManifestFactsResult.Available)manifest).Value;
        if (facts.FrameworkReferences
            is PackageManifestFrameworkReferenceFactsResult.Failed
                sectionFailure)
        {
            return new PackageHouseFrameworkReferenceOutcome.Failed(
                association,
                new PackageHouseFrameworkReferenceFailure.FrameworkSection(
                    sectionFailure.Failure));
        }

        ImmutableArray<PackageManifestFrameworkReferenceGroup> groups =
            ((PackageManifestFrameworkReferenceFactsResult.Available)
                facts.FrameworkReferences).Value.Groups;
        if (groups.IsEmpty)
        {
            return new
                PackageHouseFrameworkReferenceOutcome
                    .NoFrameworkReferenceGroups(association);
        }

        string? targetFramework = targetBasis switch
        {
            PackageHouseFrameworkReferenceTargetBasis.Exact exact =>
                exact.TargetFramework,
            PackageHouseFrameworkReferenceTargetBasis.CompileSelection selected =>
                selected.TargetFramework,
            _ => null,
        };
        if (targetFramework is null)
        {
            return new PackageHouseFrameworkReferenceOutcome.TargetUnavailable(
                association);
        }

        GroupSelectionStatus selection = SelectGroup(
            groups,
            targetFramework,
            out PackageManifestFrameworkReferenceGroup? selectedGroup);
        if (selection == GroupSelectionStatus.NoMatch)
        {
            return new
                PackageHouseFrameworkReferenceOutcome
                    .NoMatchingTargetFramework(
                        association,
                        targetFramework);
        }
        if (selection == GroupSelectionStatus.InvalidTarget)
        {
            return new PackageHouseFrameworkReferenceOutcome.Failed(
                association,
                new PackageHouseFrameworkReferenceFailure
                    .InvalidTargetFramework(targetFramework));
        }

        var evidence = new PackageHouseFrameworkReferenceEvidence(
            association,
            groups,
            selectedGroup!);
        return new PackageHouseFrameworkReferenceOutcome.Selected(
            association,
            evidence);
    }

    private static PackageHouseFrameworkReferenceOutcome.ManifestUnavailable
        ManifestUnavailable(
            PackageHouseFrameworkReferenceAssociation association,
            PackageHouseManifestUnavailableReason reason) =>
        new(association, reason);

    private static PackageHouseFrameworkReferenceTargetBasis GetTargetBasis(
        PackageHouseRequest request,
        PackageHouseRealizationReceipt.Compile realization) =>
        request.TargetContext is
            { Mode: PackageHouseTargetSelectionMode.Exact,
              RequestedFramework: { } requested }
            ? new PackageHouseFrameworkReferenceTargetBasis.Exact(requested)
            : realization.Selection.TargetFramework is { } selected
                ? new PackageHouseFrameworkReferenceTargetBasis
                    .CompileSelection(selected)
                : new PackageHouseFrameworkReferenceTargetBasis.Unavailable();

    private static GroupSelectionStatus SelectGroup(
        ImmutableArray<PackageManifestFrameworkReferenceGroup> groups,
        string targetFramework,
        out PackageManifestFrameworkReferenceGroup? selected)
    {
        selected = null;
        NuGetFramework target;
        try
        {
            target = NuGetFramework.ParseFolder(targetFramework);
        }
        catch (Exception exception) when (
            exception is ArgumentException or FrameworkException)
        {
            return GroupSelectionStatus.InvalidTarget;
        }
        if (target.IsUnsupported)
            return GroupSelectionStatus.InvalidTarget;

        var parsed = new List<NuGetFramework>(groups.Length);
        foreach (PackageManifestFrameworkReferenceGroup group in groups)
        {
            NuGetFramework framework =
                NuGetFramework.ParseFolder(group.CanonicalTargetFramework);
            parsed.Add(framework);
        }

        NuGetFramework? nearest =
            new FrameworkReducer().GetNearest(target, parsed);
        if (nearest is null)
            return GroupSelectionStatus.NoMatch;

        for (int i = 0; i < parsed.Count; i++)
        {
            if (NuGetFrameworkFullComparer.Instance.Equals(parsed[i], nearest))
            {
                selected = groups[i];
                return GroupSelectionStatus.Selected;
            }
        }

        throw new InvalidOperationException(
            "NuGet returned a framework that was not one of the candidates.");
    }

    private enum GroupSelectionStatus
    {
        Selected,
        NoMatch,
        InvalidTarget,
    }
}
