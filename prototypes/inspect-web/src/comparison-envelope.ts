import type {
  OperationCancelReason,
  OperationProducerSink,
} from "./operation-authority.ts";

function cancellationReason(reason: string | null): OperationCancelReason {
  switch (reason) {
    case "user":
    case "superseded":
    case "disposed":
    case "feature-observer-failed":
    case "timeout":
    case "worker-restarted":
      return reason;
    default:
      throw new Error("Unknown comparison cancellation reason.");
  }
}

export type ComparisonSettlement<TValue> =
  | { readonly kind: "succeeded"; readonly value: TValue }
  | { readonly kind: "failed"; readonly failureKind: "expected" | "unexpected";
      readonly error: string; readonly diagnostic: string }
  | { readonly kind: "canceled"; readonly reason: OperationCancelReason };

// Transport success preserves the query's verdict, including unavailable evidence.
export function mapComparisonEnvelope<TValue>(
  subject: string,
  version: number,
  kind: string | number,
  value: TValue | null,
  failureKind: string | number | null,
  error: string | null,
  diagnostic: string | null,
  reason: string | null,
): ComparisonSettlement<TValue> {
  if (version !== 1)
    throw new Error(`Unsupported ${subject} result version.`);
  switch (kind) {
    case "Succeeded":
      if (value === null || typeof value !== "object" || Array.isArray(value))
        throw new Error(`A ${subject} success carries no value.`);
      if (failureKind !== null || error !== null || diagnostic !== null || reason !== null)
        throw new Error(`A ${subject} success carries failure or cancellation data.`);
      return { kind: "succeeded", value };
    case "Failed":
      if (typeof error !== "string" || typeof diagnostic !== "string")
        throw new Error(`A ${subject} failure has no error or diagnostic.`);
      if (value !== null || reason !== null)
        throw new Error(`A ${subject} failure carries value or cancellation data.`);
      if (failureKind !== "Expected" && failureKind !== "Unexpected")
        throw new Error(`Unknown ${subject} failure kind.`);
      return {
        kind: "failed", failureKind: failureKind === "Expected" ? "expected" : "unexpected",
        error, diagnostic,
      };
    case "Canceled":
      if (value !== null || failureKind !== null || error !== null || diagnostic !== null)
        throw new Error(`A ${subject} cancellation carries value or failure data.`);
      return { kind: "canceled", reason: cancellationReason(reason) };
    default:
      throw new Error(`Unknown ${subject} result kind.`);
  }
}

export function reportComparisonEnvelope<TValue>(
  sink: OperationProducerSink<TValue, unknown, never>,
  ...args: Parameters<typeof mapComparisonEnvelope<TValue>>
): undefined {
  try {
    const outcome = mapComparisonEnvelope(...args);
    if (outcome.kind !== "failed") sink.reportTerminal(outcome);
    else if (outcome.failureKind === "unexpected")
      sink.reportUnexpectedTerminal(new Error(outcome.error), outcome.diagnostic);
    else sink.reportTerminal({ kind: "failed", error: new Error(outcome.error) });
  } catch (failure: unknown) {
    sink.reportUnexpectedTerminal(failure, failure);
  }
  return undefined;
}
