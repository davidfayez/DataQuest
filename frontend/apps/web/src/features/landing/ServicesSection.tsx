import { cn } from '@dv/ui';
import { BadgeCheck, Clock, Fingerprint, GraduationCap, Zap } from 'lucide-react';
import type { ComponentType } from 'react';
import { useTranslation } from 'react-i18next';
import { useLandingServices } from './api';
import { SectionHeading } from './SectionHeading';

/** Icons and background tints rotated across cards so a data-driven list still looks varied. */
const ICONS: ComponentType<{ className?: string }>[] = [GraduationCap, BadgeCheck, Fingerprint];
const TINTS = ['from-emerald-400/20', 'from-sky-400/20', 'from-amber-400/20'];

interface Track {
  key: string;
  icon: ComponentType<{ className?: string }>;
  title: string;
  body: string;
  express: boolean;
  executionDays: number | null;
  tint: string;
}

export function ServicesSection() {
  const { t } = useTranslation();
  const services = useLandingServices();

  const managed = services.data ?? [];
  const hasManaged = managed.length > 0;

  // Built-in copy, used until services are flagged for the landing (or if the API is unreachable).
  const fallbackTracks: Track[] = [
    { key: 'edu', icon: GraduationCap, title: t('landing.services.eduTitle'), body: t('landing.services.eduBody'), express: true, executionDays: null, tint: TINTS[0]! },
    { key: 'pro', icon: BadgeCheck, title: t('landing.services.proTitle'), body: t('landing.services.proBody'), express: false, executionDays: null, tint: TINTS[1]! },
    { key: 'sec', icon: Fingerprint, title: t('landing.services.secTitle'), body: t('landing.services.secBody'), express: true, executionDays: null, tint: TINTS[2]! },
  ];

  const tracks: Track[] = hasManaged
    ? managed.map((service, index) => ({
        key: service.id,
        icon: ICONS[index % ICONS.length]!,
        title: service.title,
        body: service.description ?? '',
        express: service.enableExpress,
        executionDays: service.executionTimeDays,
        tint: TINTS[index % TINTS.length]!,
      }))
    : fallbackTracks;

  return (
    <section id="services" className="scroll-mt-24 bg-ink-950 py-24 text-white">
      <div className="mx-auto max-w-6xl px-4 sm:px-6">
        <SectionHeading dark eyebrow={t('landing.services.eyebrow')} title={t('landing.services.title')} />
        <div className="mt-14 grid gap-5 md:grid-cols-3">
          {tracks.map((track) => (
            <div
              key={track.key}
              className={cn(
                'group relative overflow-hidden rounded-2xl bg-gradient-to-b to-white/[0.04] p-7 ring-1 ring-white/10 transition-all duration-300 hover:ring-white/25',
                track.tint,
              )}
            >
              <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-white/10 ring-1 ring-white/15">
                <track.icon className="size-[23px]" aria-hidden />
              </div>
              <h3 className="mt-6 font-display text-xl font-semibold">{track.title}</h3>
              {track.body && <p className="mt-3 text-sm leading-relaxed text-white/60">{track.body}</p>}
              {(track.executionDays !== null || track.express) && (
                <div className="mt-6 flex flex-wrap items-center gap-3 border-t border-white/10 pt-5">
                  {track.executionDays !== null && (
                    <p className="inline-flex items-center gap-1.5 text-xs text-white/45">
                      <Clock className="size-[13px]" aria-hidden />
                      {t('landing.services.turnaround', { days: track.executionDays })}
                    </p>
                  )}
                  {track.express && (
                    <span className="inline-flex items-center gap-1 rounded-full bg-amber-400/15 px-2.5 py-1 text-xs font-semibold text-amber-300 ring-1 ring-amber-400/25">
                      <Zap className="size-[11px]" aria-hidden />
                      {t('landing.services.express')}
                    </span>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
