import { cn } from '@dv/ui';
import { ChevronDown, LogOut, UserRound } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

/**
 * The signed-in applicant's corner of the header: which order they are in, and the way out.
 *
 * An order number is the only identity an applicant has here — no name, no avatar — so the trigger
 * carries the number itself where there is room, and the panel always repeats it in full.
 */
export function AccountMenu({
  orderNumber,
  onSignOut,
}: {
  orderNumber: string;
  onSignOut: () => void;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const firstItemRef = useRef<HTMLButtonElement>(null);
  const menuId = useId();

  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: MouseEvent) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    }

    function onKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key !== 'Escape') return;
      setOpen(false);
      triggerRef.current?.focus();
    }

    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    // Focus moves into the menu so a keyboard user can act on it straight away.
    const handle = requestAnimationFrame(() => firstItemRef.current?.focus());

    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
      cancelAnimationFrame(handle);
    };
  }, [open]);

  return (
    <div ref={rootRef} className="relative">
      <button
        ref={triggerRef}
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={menuId}
        aria-label={`${t('login.orderNumberLabel')}: ${orderNumber}`}
        onClick={() => setOpen((previous) => !previous)}
        className={cn(
          'flex h-9 cursor-pointer items-center gap-2 rounded-full bg-white ps-1 pe-2.5 text-ink-800',
          'ring-1 ring-ink-200 transition-shadow hover:ring-ink-300',
          open && 'ring-2 ring-brand-500 hover:ring-brand-500',
        )}
      >
        <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-brand-50 text-brand-700">
          <UserRound size={15} aria-hidden />
        </span>
        <span className="reference hidden text-xs font-semibold tracking-wider sm:inline lg:hidden xl:inline">
          {orderNumber}
        </span>
        <ChevronDown
          size={14}
          aria-hidden
          className={cn('shrink-0 text-ink-400 transition-transform', open && 'rotate-180')}
        />
      </button>

      {open && (
        <div
          id={menuId}
          role="menu"
          className="absolute end-0 top-[calc(100%+0.5rem)] z-50 w-64 overflow-hidden rounded-xl bg-white shadow-lift ring-1 ring-ink-100"
        >
          <div className="border-b border-ink-100 bg-ink-50/60 px-4 py-3">
            <p className="text-2xs font-medium text-ink-500">{t('login.orderNumberLabel')}</p>
            <p className="reference mt-0.5 text-sm font-semibold tracking-wider text-ink-950">
              {orderNumber}
            </p>
          </div>

          <div className="p-1.5">
            <button
              ref={firstItemRef}
              type="button"
              role="menuitem"
              onClick={() => {
                setOpen(false);
                onSignOut();
              }}
              className="flex w-full cursor-pointer items-center gap-2.5 rounded-lg px-2.5 py-2 text-start text-sm font-medium text-ink-700 transition-colors hover:bg-red-50 hover:text-red-700 focus-visible:bg-red-50 focus-visible:text-red-700"
            >
              <LogOut size={16} aria-hidden className="shrink-0 rtl:-scale-x-100" />
              {t('common.signOut')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
