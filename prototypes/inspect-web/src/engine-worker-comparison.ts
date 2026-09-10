import type {
  BrowserMethodBodyComparison,
  BrowserMethodBodyComparisonRequest,
  BrowserMethodBodyTargets,
  BrowserSourceComparison,
  BrowserSourceComparisonRequest,
} from "./facades/inspect-web-source.d.ts";
import type { MethodBodyComparisonContext } from "./method-body-comparison.ts";
import { mapComparisonEnvelope } from "./comparison-envelope.ts";
import {
  encodeEngineOperationValue,
  engineOperationInputDecoder,
  engineOperationValueDecoder,
} from "./engine-worker-operations.ts";
import { engineWorkerBoundaryErrors, engineWorkerDiagnostic, engineWorkerText } from "./engine-worker-contract.ts";
import { engineWorkerTypeSourceCancellationIsRunning, engineWorkerTypeSourceProgress } from "./engine-worker-source.ts";
import type { WorkerRuntimeHost } from "./worker-runtime-core.ts";
import type { BoundedPayloadDecoder, WorkerOperationCancelReason } from "./worker-runtime-protocol.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

type SourceFacade = typeof import("./facades/inspect-web-source.d.ts");
export type EngineWorkerComparisonFacade = Pick<SourceFacade,
  "queryMethodBodyComparisonTargets" | "queryMethodBodyComparison" | "queryMemberSourceComparison"
  | "cancelMethodBodyComparison" | "cancelMemberSourceComparison">;

interface Envelope<T> {
  readonly version: number;
  readonly kind: string | number;
  readonly value: T | null;
  readonly failureKind: string | number | null;
  readonly error: string | null;
  readonly diagnostic: string | null;
  readonly reason: string | null;
}

interface ComparisonOperation<I, V> {
  readonly kind: string;
  readonly input: BoundedPayloadDecoder<I>;
  readonly value: BoundedPayloadDecoder<V>;
  invoke(facade: EngineWorkerComparisonFacade, id: string, input: I): Promise<Envelope<V>>;
  cancel(facade: EngineWorkerComparisonFacade, id: string, reason: WorkerOperationCancelReason): boolean;
}

const targetsArguments = engineOperationInputDecoder<
  Parameters<SourceFacade["queryMethodBodyComparisonTargets"]>
>(["string", "string", "string", "string", "string", "string", "string", "string", "number"]);

const targets: ComparisonOperation<MethodBodyComparisonContext, BrowserMethodBodyTargets> = {
  kind: "source-method-targets",
  input: engineOperationValueDecoder<MethodBodyComparisonContext>("object"),
  value: engineOperationValueDecoder<BrowserMethodBodyTargets>("object"),
  invoke(facade, id, context) {
    const args = targetsArguments.decode(encodeEngineOperationValue([
      id, context.packageId, context.version, context.framework, context.assembly,
      context.typeIdentity, context.memberName, context.selectorKey, context.metadataToken,
    ]));
    if (args.kind === "rejected") throw new Error(args.message);
    return facade.queryMethodBodyComparisonTargets(...args.value);
  },
  cancel: (facade, id, reason) =>
    engineWorkerTypeSourceCancellationIsRunning(facade.cancelMethodBodyComparison(id, reason), reason),
};
const method: ComparisonOperation<BrowserMethodBodyComparisonRequest, BrowserMethodBodyComparison> = {
  kind: "source-method-comparison",
  input: engineOperationValueDecoder<BrowserMethodBodyComparisonRequest>("object"),
  value: engineOperationValueDecoder<BrowserMethodBodyComparison>("object"),
  invoke: (facade, id, request) => facade.queryMethodBodyComparison(id, encodeEngineOperationValue(request)),
  cancel: (facade, id, reason) => targets.cancel(facade, id, reason),
};
const source: ComparisonOperation<BrowserSourceComparisonRequest, BrowserSourceComparison> = {
  kind: "source-member-comparison",
  input: engineOperationValueDecoder<BrowserSourceComparisonRequest>("object"),
  value: engineOperationValueDecoder<BrowserSourceComparison>("object"),
  invoke: (facade, id, request) => facade.queryMemberSourceComparison(id, encodeEngineOperationValue(request)),
  cancel: (facade, id, reason) =>
    engineWorkerTypeSourceCancellationIsRunning(facade.cancelMemberSourceComparison(id, reason), reason),
};

export function registerEngineWorkerComparisonOperations(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerComparisonFacade,
): void {
  function register<I, V>(operation: ComparisonOperation<I, V>) {
    const envelope = engineOperationValueDecoder<Envelope<V>>("object");
    operations.register({
      kind: operation.kind,
      allowance: { kind: "unbounded" },
      input: operation.input,
      rejectInvalidPayload: failure => ({ error: failure.message, diagnostic: failure.message }),
      async invoke(input, context) {
        try {
          // Validate bounded own data before reading any generated envelope fields.
          const decoded = envelope.decode(encodeEngineOperationValue(
            await operation.invoke(facade(), context.operation.operationId, input)));
          if (decoded.kind === "rejected") throw new Error(decoded.message);
          const result = decoded.value;
          const terminal = mapComparisonEnvelope(
            operation.kind, result.version, result.kind, result.value,
            result.failureKind, result.error, result.diagnostic, result.reason);
          if (terminal.kind === "failed") {
            for (const text of [terminal.error, terminal.diagnostic]) {
              const checked = engineWorkerText.decode(text);
              if (checked.kind === "rejected") throw new Error(checked.message);
            }
          }
          if (terminal.kind !== "succeeded") return terminal;
          const encoded = encodeEngineOperationValue(terminal.value);
          const checked = operation.value.decode(encoded);
          if (checked.kind === "rejected") throw new Error(checked.message);
          return { kind: "succeeded", value: encoded };
        } catch (error: unknown) {
          const message = engineWorkerDiagnostic(error);
          return { kind: "failed", failureKind: "unexpected", error: message, diagnostic: message };
        }
      },
      cancel: (identity, reason) => operation.cancel(facade(), identity.operationId, reason),
    });
  }
  register(targets);
  register(method);
  register(source);
}

export function registerEngineWorkerComparisonAdapters(host: WorkerRuntimeHost<string, string>) {
  function register<I, V>(operation: ComparisonOperation<I, V>) {
    return host.registerOperation({
      kind: operation.kind,
      allowance: { kind: "unbounded" },
      encodeInput(input: I) {
        try {
          const json = encodeEngineOperationValue(input);
          const checked = operation.input.decode(json);
          return checked.kind === "rejected" ? checked : { kind: "decoded", value: json };
        } catch (error: unknown) {
          return { kind: "rejected", reason: "invalid", message: engineWorkerDiagnostic(error) };
        }
      },
      value: operation.value,
      error: engineWorkerText, diagnostic: engineWorkerText, progress: engineWorkerTypeSourceProgress,
      mapPreparationError: error => error, boundaryErrors: engineWorkerBoundaryErrors,
    });
  }
  return {
    methodBodyTargetsAdapter: register(targets),
    methodBodyComparisonAdapter: register(method),
    memberSourceComparisonAdapter: register(source),
  };
}
