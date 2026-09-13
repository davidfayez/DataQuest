import { cn } from '@dv/ui';
import { useEffect, useState } from 'react';
import { apiClient } from '@/shared/api/client';
import { useAdminSession } from './useAdminSession';

/**
 * The signed-in administrator's photo, or their initials when none is set.
 *
 * The photo is served from a protected endpoint, and the access token lives in memory only, so it
 * is fetched as a blob and shown through an object URL rather than a plain `<img src>` that could
 * not carry the token. The object URL is revoked on cleanup so a long session does not leak them.
 */
export function Avatar({ size = 36, className }: { size?: number; className?: string }) {
  const session = useAdminSession();
  const [url, setUrl] = useState<string | null>(null);

  const hasAvatar = session?.hasAvatar ?? false;
  const version = session?.avatarVersion ?? 0;

  useEffect(() => {
    if (!hasAvatar) {
      setUrl(null);
      return;
    }

    let objectUrl: string | null = null;
    let cancelled = false;

    apiClient
      .getBlob('admin/profile/avatar', { query: { v: version } })
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      // A failed fetch simply falls back to initials rather than surfacing an error.
      .catch(() => {
        if (!cancelled) setUrl(null);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [hasAvatar, version]);

  const initials = toInitials(session?.fullName ?? '');
  const dimension = { width: size, height: size };

  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full',
        'bg-primary-muted font-semibold text-primary ring-1 ring-primary-border',
        className,
      )}
      style={{ ...dimension, fontSize: Math.round(size * 0.4) }}
      aria-hidden="true"
    >
      {url ? (
        <img src={url} alt="" className="h-full w-full object-cover" style={dimension} />
      ) : (
        initials
      )}
    </span>
  );
}

/** First letters of the first and last words — "Platform Administrator" becomes "PA". */
function toInitials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  if (words.length === 1) return words[0]!.slice(0, 2).toUpperCase();
  return (words[0]![0]! + words[words.length - 1]![0]!).toUpperCase();
}
