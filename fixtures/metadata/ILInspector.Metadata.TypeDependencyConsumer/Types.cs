namespace ILInspector.Metadata.TypeDependencyCrossAssemblyFixtures;

public interface UnrelatedLeaf;

public interface CaseBranch : UnrelatedLeaf;

public interface Root : Casebranch;
