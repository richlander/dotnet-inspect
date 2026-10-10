// The page-side background analysis queue. It runs one task at a time, in
// order class, only while no foreground Worker operation is outstanding, and
// drops or cancels tasks the reader has left. Owned by
// docs/design/inspect-web-background-analysis.md.

export type BackgroundAnalysisOrder =
  | "visible-subject"
  | "library-baseline"
  | "library-ranking";

const orderRank: Readonly<Record<BackgroundAnalysisOrder, number>> = {
  "visible-subject": 0,
  "library-baseline": 1,
  "library-ranking": 2,
};

export interface BackgroundAnalysisRun {
  readonly settled: Promise<void>;
  cancel(): void;
}

export interface BackgroundAnalysisTask {
  /** Identity for de-duplication while the task is queued or running. */
  readonly key: string;
  readonly order: BackgroundAnalysisOrder;
  isRelevant(): boolean;
  start(): BackgroundAnalysisRun;
}

export interface BackgroundAnalysisQueue {
  /** Queues a task unless its key is already queued or running. */
  enqueue(task: BackgroundAnalysisTask): void;
  /**
   * Drops queued tasks that are no longer relevant and cancels a running one.
   * Pages call this after every render.
   */
  reconcile(): void;
  readonly pending: number;
}

export interface BackgroundAnalysisQueueDependencies {
  /** Resolves when no foreground Worker operation is outstanding. */
  whenForegroundIdle(): Promise<void>;
  reportError(error: unknown): void;
}

export function createBackgroundAnalysisQueue(
  dependencies: BackgroundAnalysisQueueDependencies,
): BackgroundAnalysisQueue {
  let sequence = 0;
  const queued: Array<{ task: BackgroundAnalysisTask; sequence: number }> = [];
  let running: {
    task: BackgroundAnalysisTask;
    run: BackgroundAnalysisRun;
    canceled: boolean;
  } | null = null;
  let pumping = false;

  const next = () => {
    let best = -1;
    for (let index = 0; index < queued.length; index++) {
      const candidate = queued[index]!;
      if (best < 0) {
        best = index;
        continue;
      }
      const current = queued[best]!;
      const rank = orderRank[candidate.task.order] - orderRank[current.task.order];
      if (rank < 0 || (rank === 0 && candidate.sequence < current.sequence))
        best = index;
    }
    return best < 0 ? null : queued.splice(best, 1)[0]!.task;
  };

  const dropIrrelevant = () => {
    for (let index = queued.length - 1; index >= 0; index--) {
      if (!queued[index]!.task.isRelevant()) queued.splice(index, 1);
    }
  };

  const pump = async (): Promise<void> => {
    if (pumping) return;
    pumping = true;
    try {
      for (;;) {
        dropIrrelevant();
        if (queued.length === 0) return;
        try {
          await dependencies.whenForegroundIdle();
        } catch (error: unknown) {
          // Idle tracking failure must not strand background work.
          dependencies.reportError(error);
        }
        // Choose after waiting, so the order covers every task queued
        // meanwhile and relevance is current.
        dropIrrelevant();
        const task = next();
        if (task === null) return;
        try {
          const run = task.start();
          running = { task, run, canceled: false };
          await run.settled;
        } catch (error: unknown) {
          dependencies.reportError(error);
        } finally {
          running = null;
        }
      }
    } finally {
      pumping = false;
    }
  };

  return {
    enqueue(task) {
      // A run reconciliation canceled no longer holds its key, so a reader
      // who returns before the cancellation settles queues the task again.
      if ((running?.task.key === task.key && !running.canceled)
        || queued.some(entry => entry.task.key === task.key)) {
        return;
      }
      queued.push({ task, sequence: sequence++ });
      void pump();
    },
    reconcile() {
      dropIrrelevant();
      if (running && !running.canceled && !running.task.isRelevant()) {
        running.canceled = true;
        running.run.cancel();
      }
    },
    get pending() {
      return queued.length + (running ? 1 : 0);
    },
  };
}
