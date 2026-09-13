/** "3 min ago", for how old a stored result is. Coarse on purpose: it says how stale, not exactly when. */
export function ago(utc: string, now: number = Date.now()): string {
  const seconds = Math.max(0, Math.round((now - Date.parse(utc)) / 1000));
  if (Number.isNaN(seconds)) return '';
  if (seconds < 60) return 'less than a minute ago';

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 48) return `${hours} h ago`;

  return `${Math.floor(hours / 24)} days ago`;
}

/** The longest a result is kept, in seconds, as the choices the person is offered. */
export const CACHE_CHOICES: { seconds: number; label: string }[] = [
  { seconds: 0, label: 'Off (always live)' },
  { seconds: 60, label: '1 minute' },
  { seconds: 300, label: '5 minutes' },
  { seconds: 900, label: '15 minutes' },
  { seconds: 3600, label: '1 hour' },
  { seconds: 21600, label: '6 hours' },
  { seconds: 86400, label: '24 hours' },
];

/** A setting that is not one of the choices (set through the API) still gets a readable label. */
export function cacheLabel(seconds: number): string {
  const choice = CACHE_CHOICES.find((c) => c.seconds === seconds);
  if (choice) return choice.label;
  return seconds % 60 === 0 ? `${seconds / 60} minutes` : `${seconds} seconds`;
}
