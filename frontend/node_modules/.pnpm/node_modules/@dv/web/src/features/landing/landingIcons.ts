import {
  Award,
  BadgeCheck,
  Clock,
  FileCheck2,
  FileText,
  Globe,
  Landmark,
  MailPlus,
  Languages,
  MessagesSquare,
  Receipt,
  Search,
  ShieldCheck,
  Users,
} from 'lucide-react';
import type { ComponentType } from 'react';

/**
 * Icon keys an administrator can choose, mapped to the glyph the landing page draws.
 *
 * Shared by the feature cards and the statistics strip so both realms agree on one vocabulary —
 * the admin panel offers exactly these keys, and anything else that reaches the site (an older
 * row, a hand-edited value) simply draws no icon rather than breaking the card.
 */
export const LANDING_ICONS: Record<string, ComponentType<{ className?: string }>> = {
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
  mail: MailPlus,
  file: FileCheck2,
  search: Search,
};

/** The glyph for a key, or undefined when there is none to draw. */
export function landingIcon(key: string | null | undefined) {
  return key ? LANDING_ICONS[key] : undefined;
}
