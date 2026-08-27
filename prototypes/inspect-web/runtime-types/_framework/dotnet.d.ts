interface DotnetRuntime {
  getAssemblyExports(assemblyName: string): Promise<unknown>;
  runMain(): Promise<number>;
}

export declare const dotnet: {
  create(): Promise<DotnetRuntime>;
};
