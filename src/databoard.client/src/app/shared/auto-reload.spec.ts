import { Injector, runInInjectionContext, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RELOAD_CHOICES, autoReload, clock, reloadChoices, reloadLabel } from './auto-reload';

describe('reload choices', () => {
  it('starts with never, and goes up in seconds, minutes and an hour', () => {
    expect(RELOAD_CHOICES.map((c) => c.label)).toEqual(['Never', '10 seconds', '30 seconds', '1 minute', '5 minutes', '15 minutes', '30 minutes', '1 hour']);
    expect(RELOAD_CHOICES[0].seconds).toBeNull();
  });

  it('labels a value set through the API, and adds it to the choices', () => {
    expect(reloadLabel(30)).toBe('30 seconds');
    expect(reloadLabel(120)).toBe('2 minutes');
    expect(reloadLabel(7200)).toBe('2 hours');
    expect(reloadLabel(45)).toBe('45 seconds');
    expect(reloadChoices(45).map((c) => c.seconds)).toContain(45);
    expect(reloadChoices(60)).toBe(RELOAD_CHOICES);
    expect(reloadChoices(null)).toBe(RELOAD_CHOICES);
  });

  it('writes the time as hours, minutes and seconds', () => {
    expect(clock(new Date(2026, 0, 1, 14, 3, 5))).toMatch(/\d{1,2}\D\d{2}\D\d{2}/);
  });
});

describe('autoReload', () => {
  let hidden = false;

  beforeEach(() => {
    vi.useFakeTimers();
    hidden = false;
    vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  const start = (seconds: () => number | null | undefined, paused?: () => boolean) => {
    const reload = vi.fn();
    const injector = TestBed.inject(Injector);
    runInInjectionContext(injector, () => autoReload(seconds, reload, paused));
    TestBed.tick();
    return reload;
  };

  it('calls reload every interval, and not before', () => {
    const reload = start(() => 10);

    vi.advanceTimersByTime(9_999);
    expect(reload).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(reload).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(20_000);
    expect(reload).toHaveBeenCalledTimes(3);
  });

  it.each([null, undefined, 0])('does nothing when the interval is %s', (seconds) => {
    const reload = start(() => seconds);

    vi.advanceTimersByTime(3_600_000);

    expect(reload).not.toHaveBeenCalled();
  });

  it('follows a change of interval, and stops when it is turned off', () => {
    const seconds = signal<number | null>(10);
    const reload = start(seconds);

    vi.advanceTimersByTime(10_000);
    expect(reload).toHaveBeenCalledTimes(1);

    seconds.set(60);
    TestBed.tick();
    vi.advanceTimersByTime(59_000);
    expect(reload).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1_000);
    expect(reload).toHaveBeenCalledTimes(2);

    seconds.set(null);
    TestBed.tick();
    vi.advanceTimersByTime(600_000);
    expect(reload).toHaveBeenCalledTimes(2);
  });

  it('skips a reload while paused and carries on afterwards', () => {
    let paused = true;
    const reload = start(() => 10, () => paused);

    vi.advanceTimersByTime(10_000);
    expect(reload).not.toHaveBeenCalled();

    paused = false;
    vi.advanceTimersByTime(10_000);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does not reload in a hidden tab, and reloads once as soon as it is shown again', () => {
    const reload = start(() => 10);

    hidden = true;
    vi.advanceTimersByTime(60_000);
    expect(reload).not.toHaveBeenCalled();

    hidden = false;
    document.dispatchEvent(new Event('visibilitychange'));
    expect(reload).toHaveBeenCalledTimes(1);

    document.dispatchEvent(new Event('visibilitychange'));
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('stops for good when the page is destroyed', () => {
    const reload = start(() => 10);

    TestBed.resetTestingModule();
    vi.advanceTimersByTime(60_000);

    expect(reload).not.toHaveBeenCalled();
  });
});
