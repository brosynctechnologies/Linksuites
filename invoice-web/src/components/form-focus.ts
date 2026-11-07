"use client";

import { useEffect, useRef, type RefObject } from "react";

/**
 * After a failed submit, move focus to the first field marked invalid, so a screen-reader user hears which field and
 * why. `failure` must be a new value on every failed submit (an error object, or a counter).
 */
export function useFocusFirstInvalid(root: RefObject<HTMLElement | null>, failure: unknown) {
  useEffect(() => {
    if (failure) root.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [root, failure]);
}

/**
 * When a form switches step (password, email code, confirm), focus the new step's first field or button, so focus
 * never drops to the page. Does nothing on the first render.
 */
export function useFocusOnStep(root: RefObject<HTMLElement | null>, step: unknown) {
  const shown = useRef(step);
  useEffect(() => {
    if (shown.current === step) return;
    shown.current = step;
    root.current?.querySelector<HTMLElement>("input:not([disabled]):not([readonly]), button:not([disabled])")?.focus();
  }, [root, step]);
}
