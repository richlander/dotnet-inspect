type PackageQueryAnnouncementInput = {
  catalogError: string;
  navigationError: string;
  failures: readonly string[];
  terminalFailure: string;
};

export type PackageQueryAnnouncementTracker = {
  beginNavigationAttempt: () => void;
  take: (input: PackageQueryAnnouncementInput) => string;
  reset: () => void;
};

type PackageQueryAnnouncementTarget = {
  textContent: string | null;
};

type PackageQueryAnnouncementSchedule = (action: () => void) => void;

export type PackageQueryLiveAnnouncer = {
  enqueue: (announcement: string) => void;
  reset: () => void;
};

export function createPackageQueryLiveAnnouncer(
  target: () => PackageQueryAnnouncementTarget | null,
  schedule: PackageQueryAnnouncementSchedule =
    action => setTimeout(action, 0),
): PackageQueryLiveAnnouncer {
  let generation = 0;
  let scheduledGeneration: number | null = null;
  let pending: string[] = [];

  function drain() {
    if (scheduledGeneration !== null || pending.length === 0) return;
    const drainGeneration = generation;
    scheduledGeneration = drainGeneration;
    schedule(() => {
      if (scheduledGeneration !== drainGeneration
        || generation !== drainGeneration) {
        return;
      }
      const liveRegion = target();
      if (!liveRegion) {
        scheduledGeneration = null;
        return;
      }
      const announcements = pending;
      pending = [];
      liveRegion.textContent = "";
      schedule(() => {
        if (scheduledGeneration !== drainGeneration
          || generation !== drainGeneration) {
          return;
        }
        liveRegion.textContent = announcements.join(" ");
        scheduledGeneration = null;
        drain();
      });
    });
  }

  return {
    enqueue(announcement) {
      if (!announcement) return;
      pending.push(announcement);
      drain();
    },
    reset() {
      generation++;
      scheduledGeneration = null;
      pending = [];
      const liveRegion = target();
      if (liveRegion) liveRegion.textContent = "";
    },
  };
}

export function createPackageQueryAnnouncementTracker():
  PackageQueryAnnouncementTracker {
  let catalogError = "";
  let navigationError = "";
  let failureCount = 0;
  let terminalFailure = "";

  return {
    beginNavigationAttempt() {
      navigationError = "";
    },
    take(input) {
      const announcement: string[] = [];
      if (input.catalogError && input.catalogError !== catalogError) {
        announcement.push(input.catalogError);
      }
      catalogError = input.catalogError;

      if (input.navigationError
        && input.navigationError !== navigationError) {
        announcement.push(input.navigationError);
      }
      navigationError = input.navigationError;

      if (input.failures.length < failureCount) failureCount = 0;
      announcement.push(...input.failures.slice(failureCount));
      failureCount = input.failures.length;

      if (input.terminalFailure
        && input.terminalFailure !== terminalFailure) {
        announcement.push(input.terminalFailure);
      }
      terminalFailure = input.terminalFailure;
      return announcement.join(" ");
    },
    reset() {
      catalogError = "";
      navigationError = "";
      failureCount = 0;
      terminalFailure = "";
    },
  };
}
