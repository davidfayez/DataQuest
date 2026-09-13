import type { TFunction } from 'i18next';

/**
 * Turns an audit action code into something a registrar can read.
 *
 * The API records actions as stable identifiers (`Application.StatusChanged`) because they are
 * filtered on and must not drift with wording. Those identifiers are for the system, not for the
 * person reading the log, so they are translated at the edge rather than shown raw.
 *
 * i18next treats a dot as a nesting separator, so the code is flattened with an underscore before
 * lookup. Anything the catalogue does not cover falls back to a readable form rather than an
 * error string — a newly added action should look unpolished, never broken.
 */
export function auditActionLabel(action: string, t: TFunction): string {
  const key = `auditAction.${action.replace(/\./g, '_')}`;
  const translated = t(key);

  if (translated !== key) {
    return translated;
  }

  const [scope, event] = action.split('.');
  if (!event) return action;

  // "StatusChanged" -> "Status changed", giving "Application · Status changed".
  const spaced = event.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return `${scope} · ${spaced.charAt(0).toUpperCase()}${spaced.slice(1)}`;
}
