function scenario() {
  const current = globalThis.__tsJsExportScenario;
  if (current === undefined) {
    throw new Error("The ts-jsexport runtime scenario is not configured.");
  }

  return current;
}

export const dotnet = {
  withDiagnosticTracing(value) {
    scenario().diagnosticTracing.push(value);
    return this;
  },

  withApplicationArguments(...args) {
    scenario().applicationArguments.push(args);
    return this;
  },

  async create() {
    const current = scenario();
    current.createCalls++;
    if (current.createError !== undefined) {
      throw current.createError;
    }

    return current.runtime;
  },
};
