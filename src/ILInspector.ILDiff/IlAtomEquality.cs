using System.Collections.Immutable;

using Inspector.Findings;

namespace ILInspector.ILDiff;

/// <summary>
/// The IL kind's canonical-bytes equality of two atoms for Exists
/// (<c>docs/design/finding-census.md#the-cursor</c>): the equality
/// <see cref="IlBodyDiff.AtomsEqual"/> decides, read as the cursor contract
/// reads targets: a branch or switch target undecided on either side is left
/// out and the position is pending; a decided target that differs is a
/// difference the end of both streams decides, as Finding Diff Any's walk
/// decides targets once the instruction map is complete. A struct, so
/// lockstep's call is direct.
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
                equality = FindingAtomEquality.Undecided;
            else if (oldIndex != newIndex && equality == FindingAtomEquality.Equal)
                equality = FindingAtomEquality.Deferred;
        }
        return equality;
    }
}
