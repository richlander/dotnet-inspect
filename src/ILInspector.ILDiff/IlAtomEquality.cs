using System.Collections.Immutable;

using Inspector.Findings;

namespace ILInspector.ILDiff;

/// <summary>
/// The IL kind's canonical-bytes equality of two atoms for Exists
/// (<c>docs/design/finding-census.md#the-cursor</c>): the equality
/// <see cref="IlBodyDiff.AtomsEqual"/> decides, with the one exclusion the
/// cursor contract names: a branch or switch target undecided on either side
/// is left out, and the position is pending when the rest is equal. A struct,
/// so lockstep's call is direct.
/// </summary>
public readonly struct IlAtomEquality : IFindingAtomEquality<CanonicalIlOperation>
{
    public FindingAtomEquality Compare(CanonicalIlOperation oldAtom, CanonicalIlOperation newAtom)
    {
        if (oldAtom.OpcodeFamily != newAtom.OpcodeFamily)
            return FindingAtomEquality.Unequal;
        switch (oldAtom.Operand?.Kind)
        {
            case IlOperandIdentityKind.BranchTarget:
                // The family fixes the operand kind; the target is compared below.
                break;
            case IlOperandIdentityKind.SwitchTargets:
                if (newAtom.Operand?.Kind is not IlOperandIdentityKind.SwitchTargets)
                    return FindingAtomEquality.Unequal;
                break;
            default:
                if (!Equals(oldAtom.Operand, newAtom.Operand))
                    return FindingAtomEquality.Unequal;
                break;
        }

        ImmutableArray<int> oldIndexes = CanonicalIlOperation.IndexesOf(oldAtom);
        ImmutableArray<int> newIndexes = CanonicalIlOperation.IndexesOf(newAtom);
        if (oldIndexes.Length != newIndexes.Length)
            return FindingAtomEquality.Unequal;
        var equality = FindingAtomEquality.Equal;
        for (int i = 0; i < oldIndexes.Length; i++)
        {
            int oldIndex = oldIndexes[i];
            int newIndex = newIndexes[i];
            if (oldIndex == CanonicalIlOperation.UndecidedTarget || newIndex == CanonicalIlOperation.UndecidedTarget)
                equality = FindingAtomEquality.EqualPending;
            else if (oldIndex != newIndex)
                return FindingAtomEquality.Unequal;
        }
        return equality;
    }
}
