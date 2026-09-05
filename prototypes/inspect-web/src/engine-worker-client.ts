import {
  createOperationAuthorityPage,
  type OperationDiagnostic,
} from "./operation-authority.ts";
import { createBrowserWorkerRuntimeHost } from "./worker-runtime-browser.ts";
import {
  engineWorkerCanaryKind,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "./engine-worker-contract.ts";
import {
  WorkerProducerClassRegistry,
  type WorkerRuntimeHostOptions,
  type WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";

function createEngineWorker(): Worker {
  return new Worker(new URL("./engine-worker-entry.ts", import.meta.url), {
    type: "module",
    name: "dotnet-inspect",
  });
}

export interface EngineWorkerProbeOptions {
  readonly callbacks: WorkerRuntimeHostOptions<string, string>["callbacks"];
  readonly operationDiagnostic: (diagnostic: OperationDiagnostic) => undefined;
  readonly startupBudgetMilliseconds?: number;
}

// The existing managed canary is an explicit diagnostic consumer, not a feature
// migration or a claim that application operations already run in this Worker.
export function createEngineWorkerProbe(options: EngineWorkerProbeOptions) {
  const host = createBrowserWorkerRuntimeHost(createEngineWorker, {
    ...engineWorkerPolicy,
    startupBudgetMilliseconds:
      options.startupBudgetMilliseconds ?? engineWorkerPolicy.startupBudgetMilliseconds,
    producerClasses: new WorkerProducerClassRegistry(
      engineWorkerPolicy.idleHeartbeatIntervalMilliseconds
        + engineWorkerPolicy.schedulingToleranceMilliseconds,
    ),
    bootstrap: { encode: engineWorkerText.decode, diagnostic: engineWorkerText },
    diagnostic: engineWorkerText,
    createDiagnostic: (kind, detail) => `${kind}: ${engineWorkerDiagnostic(detail)}`.slice(0, 4_096),
    callbacks: options.callbacks,
  });
  const adapter = host.registerOperation({
    kind: engineWorkerCanaryKind,
    allowance: { kind: "unbounded" },
    encodeInput: (input: string) => engineWorkerText.decode(input),
    value: engineWorkerText,
    error: engineWorkerText,
    diagnostic: engineWorkerText,
    progress: engineWorkerText,
    mapPreparationError: error => error,
    boundaryErrors: {
      startup: "Worker startup failed.",
      "worker-crash": "Worker realm was lost.",
      protocol: "Worker protocol failed.",
      watchdog: "Worker event loop stopped responding.",
      "control-response": "Worker control response was missing.",
      "probe-exhaustion": "Worker probe identity was exhausted.",
      "worker-declared": "Worker reported a runtime failure.",
      "worker-message": "Worker message delivery failed.",
    },
  });
  const page = createOperationAuthorityPage();
  const session = page.createSession<
    string, string, string, string, WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: options.operationDiagnostic },
  });
  return {
    host,
    probe: () => session.start("", adapter),
    dispose: () => {
      session.dispose();
      host.dispose();
    },
  };
}
