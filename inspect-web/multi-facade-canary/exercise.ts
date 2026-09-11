import * as alpha from "./facades/alpha.js";
import * as beta from "./facades/beta.js";

export interface ExerciseOptions {
  readonly skipBetaSecondary?: boolean;
}

export interface ExerciseReceipt {
  readonly alphaAssembly: string;
  readonly betaAssembly: string;
  readonly alphaFlavor: alpha.Flavor;
  readonly betaFlavor: beta.Flavor;
}

function expect(actual: unknown, expected: unknown, operation: string): void {
  if (actual !== expected) {
    throw new Error(
      `${operation} returned ${String(actual)} instead of ${String(expected)}.`,
    );
  }
}

export async function exerciseFacades(
  options: ExerciseOptions = {},
): Promise<ExerciseReceipt> {
  expect(alpha.identity(), "alpha:primary", "Alpha primary identity");
  expect(beta.identity(), "beta:primary", "Beta primary identity");
  expect(alpha.describe(17), "alpha:int:17", "Alpha integer overload");
  expect(beta.describe(23), "beta:int:23", "Beta integer overload");
  expect(
    alpha.operation_87a32703("left"),
    "alpha:string:left",
    "Alpha string overload",
  );
  expect(
    beta.operation_87a32703("right"),
    "beta:string:right",
    "Beta string overload",
  );
  expect(
    alpha.operation_9a20007e(),
    "alpha:secondary",
    "Alpha secondary identity",
  );
  if (options.skipBetaSecondary !== true) {
    expect(
      beta.operation_9a20007e(),
      "beta:secondary",
      "Beta secondary identity",
    );
  }

  const [alphaEnvelope, betaEnvelope]: [
    alpha.Envelope,
    beta.Envelope,
  ] = await Promise.all([
    alpha.getEnvelopeAsync("left-async", "chocolate"),
    beta.getEnvelopeAsync("right-async", "vanilla"),
  ]);
  expect(alphaEnvelope.assembly, "alpha", "Alpha async envelope assembly");
  expect(alphaEnvelope.value, "left-async", "Alpha async envelope value");
  expect(alphaEnvelope.flavor, "Chocolate", "Alpha async envelope flavor");
  expect(betaEnvelope.assembly, "beta", "Beta async envelope assembly");
  expect(betaEnvelope.value, "right-async", "Beta async envelope value");
  expect(betaEnvelope.flavor, "Vanilla", "Beta async envelope flavor");

  const alphaEvents: string[] = [];
  const managedResult = await alpha.runManagedOperationCanary(
    "alpha-managed-operation",
    (kind, value) => {
      alphaEvents.push(`${kind}:${value}`);
      return undefined;
    },
  );
  expect(
    alphaEvents.join("|"),
    "0:search:1/3|1:Package.One|2:Package.Two",
    "Managed nonterminal event order",
  );
  expect(managedResult.kind, "Succeeded", "Managed terminal kind");
  expect(managedResult.value, 3, "Managed terminal value");

  alpha.reportRetainedManagedOperationCanaryEvent(1, "Package.Late");
  expect(
    alphaEvents.length,
    3,
    "Managed callback after operation release",
  );

  let betaCallbackCalls = 0;
  let betaFailure: unknown;
  try {
    await beta.runManagedOperationCanary(
      "beta-managed-operation",
      () => {
        betaCallbackCalls++;
        throw new Error("managed callback failure");
      },
    );
  } catch (error: unknown) {
    betaFailure = error;
  }
  const betaFailureText = String(betaFailure);
  expect(
    betaFailureText.includes("event-callback"),
    true,
    "Managed callback failure kind",
  );
  expect(
    betaFailureText.includes("beta-managed-operation"),
    true,
    "Managed callback failure operation identity",
  );
  beta.reportRetainedManagedOperationCanaryEvent(1, "Package.Late");
  expect(
    betaCallbackCalls,
    1,
    "Failed managed callback closes further events",
  );

  expect(
    alpha.verifyInvocations(),
    "alpha:invocations-ok",
    "Alpha invocation receipt",
  );
  expect(
    beta.verifyInvocations(),
    "beta:invocations-ok",
    "Beta invocation receipt",
  );

  return {
    alphaAssembly: alphaEnvelope.assembly,
    betaAssembly: betaEnvelope.assembly,
    alphaFlavor: alphaEnvelope.flavor,
    betaFlavor: betaEnvelope.flavor,
  };
}
