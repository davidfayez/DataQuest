import { useEffect, useRef, type ReactNode } from 'react';
import { cn } from '../lib/cn';

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  className?: string;
}

/**
 * Built on the native `<dialog>` element, which gives focus trapping, Escape-to-close, inertness
 * of the background and top-layer stacking without reimplementing any of it.
 */
export function Dialog({ open, onClose, title, children, footer, className }: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const element = ref.current;
    if (!element) return;

    if (open && !element.open) {
      element.showModal();
    } else if (!open && element.open) {
      element.close();
    }
  }, [open]);

  return (
    <dialog
      ref={ref}
      onCancel={(event) => {
        // Let React own the open state rather than the DOM closing itself.
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        // A click on the backdrop lands on the dialog element itself.
        if (event.target === ref.current) onClose();
      }}
      className={cn(
        // A modal <dialog> is centred by the browser's default `margin: auto`; the framework's
        // reset zeroes that, which pins it to the top-left, so the auto margin is restored here.
        // dvh rather than vh so a mobile browser's collapsing address bar cannot push the actions
        // off the bottom of the screen.
        'm-auto max-h-[calc(100dvh-2rem)] w-[min(32rem,calc(100vw-2rem))] overflow-hidden',
        'rounded-2xl border-0 bg-white p-0 text-ink-900 shadow-lift',
        'backdrop:bg-ink-950/40 backdrop:backdrop-blur-sm',
        className,
      )}
    >
      {/*
        The column lives on this wrapper and NEVER on the <dialog> itself. A closed dialog is
        hidden by the user-agent rule `dialog:not([open]) { display: none }`, and any `display`
        an author stylesheet puts on the element beats that rule outright — origin wins over
        specificity — so a `flex` class here would render every closed dialog on the page inline,
        confirmation prompts and all.
      */}
      <div className="flex max-h-[calc(100dvh-2rem)] flex-col">
        <h2 className="shrink-0 px-6 pb-4 pt-6 font-display text-lg font-semibold text-ink-950">
          {title}
        </h2>

        {/* Only the body scrolls. Scrolling the whole dialog took the footer with it, which on a
            long form left the submit button below the fold with nothing to say so. */}
        <div className="min-h-0 flex-1 overflow-y-auto px-6 pb-6 text-sm">{children}</div>

        {footer && (
          <div className="flex shrink-0 flex-wrap justify-end gap-3 border-t border-border px-6 py-4">
            {footer}
          </div>
        )}
      </div>
    </dialog>
  );
}
