namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// A reference conversion the importer proved at an evaluation-stack join:
/// a path carrying <see cref="From"/> met another path and the join merged to
/// <see cref="To"/>, the nearest common supertype both paths are assignable to
/// without a cast. Issued only by the importer's join merge while metadata is
/// live; consumers such as <see cref="ReferenceAssignmentTargets"/> read it as
/// a fact and never widen it to a hierarchy inference of their own.
/// </summary>
public readonly record struct ReferenceWidening(TypeRef From, TypeRef To);
