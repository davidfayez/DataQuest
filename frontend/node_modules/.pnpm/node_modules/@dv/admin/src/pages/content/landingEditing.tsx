import { SUPPORTED_LANGUAGES } from '@dv/i18n';
import { AdminPageHeader, Button, Field, Input, Spinner, cn } from '@dv/ui';
import {
  Award,
  BadgeCheck,
  Clock,
  FileText,
  Globe,
  Landmark,
  Languages,
  MessagesSquare,
  Receipt,
  ShieldCheck,
  Users,
} from 'lucide-react';
import { useCallback, useState, type ComponentType, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { Translations } from '@/features/content/api';

/**
 * Shared by the two editors on the landing page — the feature cards and the statistics strip.
 *
 * Both edit the same shape: a map of language code to text, with English as the guaranteed
 * fallback. Keeping the pieces in one place is what stops the two sections drifting into
 * different ideas of what "translated" means.
 */

export const DEFAULT_LANG = 'en';

const LANGUAGE_STORAGE_KEY = 'dv.admin.landingEditLanguage';

/**
 * The language currently being edited, remembered across the landing pages.
 *
 * Each section is its own page now, and an editor working through a translation would otherwise
 * be dropped back to English every time they moved between them.
 */
export function useEditingLanguage(): [string, (lang: string) => void] {
  const [lang, setLang] = useState(() => {
    try {
      return localStorage.getItem(LANGUAGE_STORAGE_KEY) ?? DEFAULT_LANG;
    } catch {
      // Private browsing can refuse storage; English is the safe default.
      return DEFAULT_LANG;
    }
  });

  const choose = useCallback((next: string) => {
    setLang(next);
    try {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, next);
    } catch {
      // The choice still applies for this visit; only remembering it failed.
    }
  }, []);

  return [lang, choose];
}

/**
 * The same glyphs the applicant site draws, so every editor previews the real thing rather than a
 * key name. Kept in step with `landingIcons.ts` in the web app.
 */
const ICON_GLYPHS: Record<string, ComponentType<{ className?: string }>> = {
  timeline: MessagesSquare,
  wallet: Receipt,
  language: Languages,
  security: ShieldCheck,
  shield: ShieldCheck,
  document: FileText,
  authority: Landmark,
  turnaround: Clock,
  users: Users,
  globe: Globe,
  check: BadgeCheck,
  award: Award,
};

/** One icon, drawn the way the landing page draws it. Renders nothing for an unknown key. */
export function LandingIcon({ icon, className }: { icon: string | null; className?: string }) {
  const Glyph = icon ? ICON_GLYPHS[icon] : undefined;
  if (!Glyph) return null;

  return (
    <span
      className={cn(
        'flex size-9 items-center justify-center rounded-full bg-brand-50 text-brand-600 ring-1 ring-brand-100',
        className,
      )}
    >
      <Glyph className="size-4" />
    </span>
  );
}

/** Immutably sets one language's value on a translations map. */
export function withLang(map: Translations, lang: string, value: string): Translations {
  return { ...map, [lang]: value };
}

/** Requested language, then English, then whatever exists — matches the server's fallback. */
export function pick(map: Translations | undefined, lang: string): string {
  if (!map) return '';
  return map[lang] || map[DEFAULT_LANG] || Object.values(map).find(Boolean) || '';
}

/** A wrapping row of language chips; a dot marks languages that already have content. */
export function LanguageTabs({
  value,
  onChange,
  filled,
}: {
  value: string;
  onChange: (lang: string) => void;
  filled: (lang: string) => boolean;
}) {
  return (
    <div className="flex flex-wrap gap-1.5" role="tablist" aria-label="Content language">
      {SUPPORTED_LANGUAGES.map((language) => {
        const active = value === language.code;
        return (
          <button
            key={language.code}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(language.code)}
            className={cn(
              'inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1 text-xs font-medium transition-colors',
              active ? 'bg-ink-950 text-white' : 'bg-ink-100 text-ink-600 hover:bg-ink-200',
            )}
          >
            <span>{language.name}</span>
            {language.code === DEFAULT_LANG && <span title="Required">*</span>}
            {!active && filled(language.code) && (
              <span className="size-1.5 rounded-full bg-brand-500" aria-hidden />
            )}
          </button>
        );
      })}
    </div>
  );
}

/**
 * The language chooser that sits at the top of every landing page.
 *
 * Repeated on each page rather than lifted into the layout: the pages are separate routes now, and
 * an editor arriving on one directly needs to see, and change, which language they are writing in
 * without going elsewhere first.
 */
export function LanguagePanel({
  lang,
  onChange,
  filled,
}: {
  lang: string;
  onChange: (lang: string) => void;
  filled: (lang: string) => boolean;
}) {
  const { t } = useTranslation();

  return (
    <div className="rounded-2xl bg-white p-4 shadow-soft ring-1 ring-ink-100">
      <p className="mb-2 text-sm font-medium text-ink-950">{t('landingContent.editLanguage')}</p>
      <LanguageTabs value={lang} onChange={onChange} filled={filled} />
      <p className="mt-2 text-xs text-ink-400">{t('landingContent.englishRequired')}</p>
    </div>
  );
}

/** Heading, language chooser and a place for notices — the shell every landing page shares. */
export function LandingPageShell({
  title,
  subtitle,
  lang,
  onLangChange,
  filled,
  children,
}: {
  title: string;
  subtitle: string;
  lang: string;
  onLangChange: (lang: string) => void;
  filled: (lang: string) => boolean;
  children: ReactNode;
}) {
  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={title} subtitle={subtitle} />
      <LanguagePanel lang={lang} onChange={onLangChange} filled={filled} />
      {children}
    </div>
  );
}

/**
 * The heading copy for one section, edited on that section's own page.
 *
 * The three headings used to share a "Section heading" page of their own, which meant editing the
 * words above the trust strip happened somewhere other than editing the strip. Each now sits with
 * what it labels, and saves independently — the server treats an omitted heading as "leave alone".
 */
export function SectionHeadingPanel({
  fields,
  canUpdate,
  complete,
  isPending,
  onSave,
}: {
  fields: { id: string; label: string; value: string; onChange: (value: string) => void }[];
  canUpdate: boolean;
  complete: boolean;
  isPending: boolean;
  onSave: () => void;
}) {
  const { t } = useTranslation();

  return (
    <section className="rounded-2xl bg-white p-6 shadow-soft ring-1 ring-ink-100">
      <div className={cn('grid gap-4', fields.length > 1 && 'sm:grid-cols-2')}>
        {fields.map((field) => (
          <Field key={field.id} label={field.label} htmlFor={field.id}>
            <Input
              id={field.id}
              value={field.value}
              disabled={!canUpdate}
              dir="auto"
              onChange={(event) => field.onChange(event.target.value)}
              data-testid={field.id}
            />
          </Field>
        ))}
      </div>

      {canUpdate && (
        <div className="mt-4 flex items-center justify-end gap-3">
          {!complete && (
            <span className="text-xs text-amber-600">{t('landingContent.englishRequired')}</span>
          )}
          <Button onClick={onSave} disabled={isPending || !complete} data-testid="save-heading">
            {isPending && <Spinner />}
            {t('landingContent.saveHeading')}
          </Button>
        </div>
      )}
    </section>
  );
}
