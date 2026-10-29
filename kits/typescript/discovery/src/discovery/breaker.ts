export type BreakerSettings = {
  threshold: number;
  windowMs: number;
  resetMs: number;
};

export type BreakerCallbacks = {
  onTrip(failuresInWindow: number, lastReason: string): void;
  onClose(): void;
};

/**
 * Sliding-window circuit breaker. States: closed → open → half-open → closed.
 *
 * Counts failures within a rolling window. When the count reaches the threshold
 * the breaker opens and isOpen() returns true. After resetMs it moves to
 * half-open and allows one trial call through (isOpen() returns false). A
 * successful trial closes it; a failed trial returns it to open.
 */
export class SlidingWindowBreaker {
  private timestamps: number[] = [];
  private state: 'closed' | 'open' | 'half-open' = 'closed';
  private openedAt = 0;

  constructor(
    private readonly settings: BreakerSettings,
    private readonly cbs: BreakerCallbacks,
    private readonly now: () => number = Date.now,
  ) {}

  isOpen(): boolean {
    if (this.state === 'closed') return false;

    if (this.state === 'open') {
      if (this.now() - this.openedAt >= this.settings.resetMs) {
        this.state = 'half-open';
        return false;
      }
      return true;
    }

    // half-open: let the trial call through
    return false;
  }

  recordSuccess(): void {
    if (this.state === 'half-open') {
      this.state = 'closed';
      this.timestamps = [];
      this.cbs.onClose();
    }
  }

  recordFailure(reason: string): void {
    const t = this.now();
    this.timestamps = this.timestamps.filter((ts) => t - ts < this.settings.windowMs);
    this.timestamps.push(t);

    if (this.state === 'half-open') {
      this.state = 'open';
      this.openedAt = t;
      return;
    }

    if (this.state === 'closed' && this.timestamps.length >= this.settings.threshold) {
      this.state = 'open';
      this.openedAt = t;
      this.cbs.onTrip(this.timestamps.length, reason);
    }
  }
}
