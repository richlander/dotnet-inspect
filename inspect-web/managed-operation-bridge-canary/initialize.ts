import { initializeRuntime } from "./facades/bridge.js";

let initialization: Promise<void> | undefined;

export function initializeCanary(): Promise<void> {
  initialization ??= initializeRuntime();
  return initialization;
}
