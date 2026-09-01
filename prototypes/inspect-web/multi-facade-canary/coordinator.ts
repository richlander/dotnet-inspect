import {
  initializeRuntime as initializeAlpha,
} from "./facades/alpha.js";
import {
  initializeRuntime as initializeBeta,
} from "./facades/beta.js";

let initialization: Promise<void> | undefined;

async function initializeCore(): Promise<void> {
  await initializeAlpha();
  await initializeBeta();
}

export function initializeFacades(): Promise<void> {
  initialization ??= initializeCore();
  return initialization;
}
