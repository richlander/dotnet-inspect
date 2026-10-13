namespace ILInspector.Metadata.TypeDependencyCrossAssemblyFixtures;

public interface UnrelatedLeaf;

public interface CaseBranch : UnrelatedLeaf;

public interface Root : Casebranch;

// Reproduces an internal package root while keeping ordinary scan controls public.
internal interface InternalRoot : CaseBranch;

internal interface OtherInternalRoot : InternalRoot;
