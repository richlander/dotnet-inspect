using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

[Flags]
public enum PassAnalysisKind
{
    None = 0,
    BranchTargets = 1,
}

public enum PassAnalysisAcquisition
{
    None,
    Constructed,
    Reused,
}

public enum PassAnalysisDisposition
{
    Preserved,
    Invalidated,
}

/// <summary>
/// One completed pass boundary that constructed, reused, preserved, or
/// invalidated a manager-owned read-only analysis.
/// </summary>
public sealed record PassAnalysisReceipt(
    int Ordinal,
    string PassName,
    PassAnalysisKind Analysis,
    int Generation,
    PassAnalysisAcquisition Acquisition,
    PassAnalysisDisposition Disposition);

internal sealed class PassAnalysisManager
{
    readonly IrFunction _function;
    readonly List<PassAnalysisReceipt>? _receipts;
    ImmutableHashSet<int>? _branchTargets;
    int _branchTargetsGeneration;
    int _ordinal;
    IIrPass? _pass;
    PassAnalysisAcquisition _branchTargetsAcquisition;

    public PassAnalysisManager(
        IrFunction function,
        List<PassAnalysisReceipt>? receipts)
    {
        _function = function;
        _receipts = receipts;
    }

    public void BeginPass(int ordinal, IIrPass pass)
    {
        if (_pass is not null)
            throw new InvalidOperationException(
                $"Pass analysis state is already active for {_pass.Name}.");

        _ordinal = ordinal;
        _pass = pass;
        _branchTargetsAcquisition = PassAnalysisAcquisition.None;

        if (Requires(pass, PassAnalysisKind.BranchTargets))
        {
            if (_branchTargets is null)
            {
                _branchTargets =
                    ReferenceOwnership.CollectBranchTargets(_function)
                        .ToImmutableHashSet();
                _branchTargetsAcquisition =
                    PassAnalysisAcquisition.Constructed;
            }
            else
            {
                _branchTargetsAcquisition =
                    PassAnalysisAcquisition.Reused;
            }
        }
    }

    public IReadOnlySet<int> BranchTargets(
        IrFunction function)
    {
        EnsureFunction(function);
        if (_pass is null
            || !Requires(_pass, PassAnalysisKind.BranchTargets))
        {
            throw new InvalidOperationException(
                "The active pass did not declare the branch-target analysis.");
        }

        return _branchTargets
            ?? throw new InvalidOperationException(
                "The declared branch-target analysis was not constructed.");
    }

    public void CompletePass()
    {
        IIrPass pass = _pass
            ?? throw new InvalidOperationException(
                "No pass analysis state is active.");

        if (_branchTargets is not null)
        {
            bool preserved =
                Preserves(pass, PassAnalysisKind.BranchTargets);
            _receipts?.Add(new(
                _ordinal,
                pass.Name,
                PassAnalysisKind.BranchTargets,
                _branchTargetsGeneration,
                _branchTargetsAcquisition,
                preserved
                    ? PassAnalysisDisposition.Preserved
                    : PassAnalysisDisposition.Invalidated));
            if (!preserved)
            {
                _branchTargets = null;
                _branchTargetsGeneration++;
            }
        }

        _pass = null;
        _ordinal = 0;
        _branchTargetsAcquisition = PassAnalysisAcquisition.None;
    }

    void EnsureFunction(IrFunction function)
    {
        if (!ReferenceEquals(function, _function))
        {
            throw new InvalidOperationException(
                "Pass analysis state cannot cross function boundaries.");
        }
    }

    static bool Requires(
        IIrPass pass,
        PassAnalysisKind analysis)
        => (pass.RequiredAnalyses & analysis) != 0;

    static bool Preserves(
        IIrPass pass,
        PassAnalysisKind analysis)
        => (pass.PreservedAnalyses & analysis) != 0;
}
