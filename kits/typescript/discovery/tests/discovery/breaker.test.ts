import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { SlidingWindowBreaker } from '../../src/discovery/breaker.js';
import type { BreakerCallbacks, BreakerSettings } from '../../src/discovery/breaker.js';

const SETTINGS: BreakerSettings = {
  threshold: 3,
  windowMs: 17_500,
  resetMs: 30_000,
};

function makeCallbacks() {
  const trips: { count: number; reason: string }[] = [];
  const closes: number[] = [];
  const cbs: BreakerCallbacks = {
    onTrip: (count, reason) => trips.push({ count, reason }),
    onClose: () => closes.push(1),
  };
  return { cbs, trips, closes };
}

let now = 0;
const clock = () => now;

beforeEach(() => {
  vi.useFakeTimers();
  now = 0;
});

afterEach(() => {
  vi.useRealTimers();
});

describe('SlidingWindowBreaker', () => {
  it('starts closed', () => {
    const { cbs } = makeCallbacks();
    const breaker = new SlidingWindowBreaker(SETTINGS, cbs, clock);
    expect(breaker.isOpen()).toBe(false);
  });

  it('stays closed below the threshold', () => {
    const { cbs } = makeCallbacks();
    const breaker = new SlidingWindowBreaker(SETTINGS, cbs, clock);

    breaker.recordFailure('TypeError');
    breaker.recordFailure('TypeError');
    expect(breaker.isOpen()).toBe(false);
  });

  it('opens when threshold is reached within the window', () => {
    const { cbs, trips } = makeCallbacks();
    const breaker = new SlidingWindowBreaker(SETTINGS, cbs, clock);

    breaker.recordFailure('TypeError');
    breaker.recordFailure('TypeError');
    breaker.recordFailure('TypeError');

    expect(breaker.isOpen()).toBe(true);
    expect(trips).toEqual([{ count: 3, reason: 'TypeError' }]);
  });

  it('does not trip when failures slide out of the window', () => {
    const { cbs } = makeCallbacks();
    const breaker = new SlidingWindowBreaker(SETTINGS, cbs, clock);

    breaker.recordFailure('TypeError');
    breaker.recordFailure('TypeError');

    now += SETTINGS.windowMs; // both timestamps now outside the window
    breaker.recordFailure('TypeError');

    expect(breaker.isOpen()).toBe(false);
  });

  it('moves to half-open after resetMs and allows one call through', () => {
    const { cbs } = makeCallbacks();
    const breaker = new SlidingWindowBreaker(SETTINGS, cbs, clock);

    for (let i = 0; i < 3; i++) breaker.recordFailure('TypeError');
    expect(breaker.isOpen()).toBe(true);

    now += SETTINGS.resetMs;
    expect(breaker.isOpen()).toBe(false); // half-open, trial allowed
  });

  it('closes on a successful trial in half-open state', () => {
    const { cbs, closes } = makeCallbacks();
    const breaker = new SlidingWindowBreaker(SETTINGS, cbs, clock);

    for (let i = 0; i < 3; i++) breaker.recordFailure('TypeError');
    now += SETTINGS.resetMs;
    breaker.isOpen(); // transitions to half-open
    breaker.recordSuccess();

    expect(breaker.isOpen()).toBe(false);
    expect(closes).toHaveLength(1);
  });

  it('returns to open when the trial in half-open fails', () => {
    const { cbs, trips } = makeCallbacks();
    const breaker = new SlidingWindowBreaker(SETTINGS, cbs, clock);

    for (let i = 0; i < 3; i++) breaker.recordFailure('TypeError');
    now += SETTINGS.resetMs;
    breaker.isOpen(); // transitions to half-open
    breaker.recordFailure('TypeError');

    expect(breaker.isOpen()).toBe(true);
    expect(trips).toHaveLength(1); // only one trip event (the original)
  });

  it('recordSuccess is a no-op when closed', () => {
    const { cbs, closes } = makeCallbacks();
    const breaker = new SlidingWindowBreaker(SETTINGS, cbs, clock);

    breaker.recordSuccess();
    expect(closes).toHaveLength(0);
    expect(breaker.isOpen()).toBe(false);
  });

  it('stays open until exactly resetMs has passed', () => {
    const { cbs } = makeCallbacks();
    const breaker = new SlidingWindowBreaker(SETTINGS, cbs, clock);

    for (let i = 0; i < 3; i++) breaker.recordFailure('TypeError');

    now += SETTINGS.resetMs - 1;
    expect(breaker.isOpen()).toBe(true);

    now += 1;
    expect(breaker.isOpen()).toBe(false);
  });
});
