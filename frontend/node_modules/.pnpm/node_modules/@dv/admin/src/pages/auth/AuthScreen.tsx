import { Card, Logo } from '@dv/ui';
import { ShieldAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { LanguageSwitcher } from '@/features/language/LanguageSwitcher';

/**
 * The shared frame for the signed-out screens — sign in, forgot password, reset password — so
 * they present as one entrance rather than three loosely related pages.
 */
export function AuthScreen({
  title,
  description,
  children,
}: {
  title: string;
  description: string;
  children: ReactNode;
}) {
  const { t } = useTranslation();

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-ink-950 px-4 py-16">
      <div className="mb-8 flex items-center gap-3">
        <Logo size={34} />
        <div>
          <p className="font-display text-lg font-semibold leading-tight text-white">{t('common.appName')}</p>
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-brand-400">
            {t('common.adminPanel')}
          </p>
        </div>
      </div>

      <Card className="animate-fade-up w-full max-w-md p-8">
        <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-ink-950 text-brand-400">
          <ShieldAlert className="size-[22px]" aria-hidden />
        </div>
        <h1 className="mt-5 font-display text-2xl font-semibold tracking-tight text-ink-950">{title}</h1>
        <p className="mt-2 text-sm text-ink-500">{description}</p>
        <div className="mt-7">{children}</div>
      </Card>

      <div className="mt-8 [&_svg]:text-white/60">
        <LanguageSwitcher />
      </div>
    </div>
  );
}
