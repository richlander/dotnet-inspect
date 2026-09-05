import type { BoundedPayloadDecoder } from "./worker-runtime-protocol.ts";

export const engineWorkerPolicy = {
  idleHeartbeatIntervalMilliseconds: 500,
  schedulingToleranceMilliseconds: 1_000,
  startupBudgetMilliseconds: 60_000,
  controlResponseGraceMilliseconds: 5_000,
  drainBudgetMilliseconds: 2_000,
} as const;

export const engineWorkerText: BoundedPayloadDecoder<string> = {
  decode(value) {
    if (typeof value !== "string") {
      return { kind: "rejected", reason: "invalid", message: "Expected Worker text." };
    }
    if (value.length > 4_096) {
      return { kind: "rejected", reason: "oversized", message: "Worker text exceeds 4096 characters." };
    }
    return { kind: "decoded", value };
  },
};

export function engineWorkerDiagnostic(detail: unknown): string {
  const message = detail instanceof Error
    ? detail.message
    : typeof detail === "string"
      ? detail
      : "Worker runtime boundary failure.";
  return message.slice(0, 4_096);
}

export const engineWorkerCanaryKind = "runtime-async-lowering-canary";
