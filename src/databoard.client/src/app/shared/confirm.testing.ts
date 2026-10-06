import { of } from 'rxjs';
import { vi } from 'vitest';
import { ConfirmOptions, ConfirmService } from './confirm.service';

/** What a test can look at and change after calling `answerConfirm`. */
export interface ConfirmStub {
  /** The spy on `ConfirmService.ask`: how many questions were asked, and with what. */
  ask: ReturnType<typeof vi.spyOn>;
  /** From now on every question gets this answer. */
  answer(value: boolean): void;
  /** The next questions get these answers, in order; after them, the answer given before. */
  answers(...values: boolean[]): void;
  /** What each question asked, as text. */
  readonly messages: string[];
  /** What each question asked, with its title and button labels. */
  readonly questions: ConfirmOptions[];
}

/**
 * Answers every question the page asks in a Material dialog without showing one, so a test says "the person agreed" or
 * "the person said no" in one line. Questions asked are kept for checking.
 */
export function answerConfirm(answer = true): ConfirmStub {
  let current = answer;
  const ask = vi.spyOn(ConfirmService.prototype, 'ask').mockImplementation(() => of(current));
  const asked = () => ask.mock.calls.map(([options]) => (typeof options === 'string' ? { message: options } : options) as ConfirmOptions);

  return {
    ask,
    answer(value) {
      current = value;
      ask.mockImplementation(() => of(current));
    },
    answers(...values) {
      for (const value of values) ask.mockImplementationOnce(() => of(value));
    },
    get messages() {
      return asked().map((q) => q.message);
    },
    get questions() {
      return asked();
    },
  };
}
