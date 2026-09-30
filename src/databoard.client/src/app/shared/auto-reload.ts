import { effect } from '@angular/core';

/** The intervals offered for reloading an open dashboard. Null is off. */
export const RELOAD_CHOICES: { seconds: number | null; label: string }[] = [
  { seconds: null, label: 'Never' },
  { seconds: 10, label: '10 seconds' },
  { seconds: 30, label: '30 seconds' },
  { seconds: 60, label: '1 minute' },
  { seconds: 300, label: '5 minutes' },
  { seconds: 900, label: '15 minutes' },
  { seconds: 1800, label: '30 minutes' },
  { seconds: 3600, label: '1 hour' },
];

/** "30 seconds", "5 minutes": the usual label, or one worked out for a value that was set through the API. */
export function reloadLabel(seconds: number): string {
  const known = RELOAD_CHOICES.find((c) => c.seconds === seconds);
  if (known) return known.label;
  if (seconds % 3600 === 0) return `${seconds / 3600} hours`;
  if (seconds % 60 === 0) return `${seconds / 60} minutes`;
  return `${seconds} seconds`;
}

/** The usual choices, plus the current one if it is something else. */
export function reloadChoices(current: number | null | undefined) {
  return current == null || RELOAD_CHOICES.some((c) => c.seconds === current)
    ? RELOAD_CHOICES
    : [...RELOAD_CHOICES, { seconds: current, label: reloadLabel(current) }];
}

/** The time a reload finished, for "Updated 14:03:15". */
export const clock = (when: Date = new Date()): string =>
  when.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });

/**
 * Calls `reload` every `seconds` for as long as the page is open (call it where `effect` may be called: a field or the
 * constructor). Nothing happens while the tab is hidden or `paused()` is true; a reload that was due then happens as soon
 * as the tab is shown again. A new interval starts counting from now.
 */
export function autoReload(seconds: () => number | null | undefined, reload: () => void, paused: () => boolean = () => false): void {
  effect((onCleanup) => {
    const every = seconds();
    if (!every || every < 1) return;

    let missed = false;
    const due = () => {
      if (document.hidden || paused()) {
        missed = true;
        return;
      }
      missed = false;
      reload();
    };
    const shown = () => {
      if (!document.hidden && missed) due();
    };

    const timer = setInterval(due, every * 1000);
    document.addEventListener('visibilitychange', shown);
    onCleanup(() => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', shown);
    });
  });
}
