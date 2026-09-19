import { createContext, useContext, useState, type ReactNode } from 'react';
import bundledLogoUrl from '../assets/logo.jpeg';

/**
 * The logo an administrator uploaded, or null to use the mark bundled with the build.
 *
 * A context rather than a prop on every <Logo>: the mark appears in headers, footers and sign-in
 * screens across both apps, and threading a URL through all of them would mean every caller
 * knowing where branding comes from. This package stays presentational — each app fetches its own
 * branding and provides the resolved URL once at its root.
 */
const LogoSourceContext = createContext<string | null>(null);

export function LogoSourceProvider({
  src,
  children,
}: {
  src: string | null;
  children: ReactNode;
}) {
  return <LogoSourceContext.Provider value={src}>{children}</LogoSourceContext.Provider>;
}

/**
 * Brand mark. Rendered as a circle so artwork with a dark background reads as an intentional disc
 * on both light and dark surfaces.
 *
 * Falls back to the bundled seal whenever no logo has been uploaded — so the header is never
 * empty, and removing the upload restores the original artwork rather than leaving a gap. The same
 * fallback applies when an uploaded logo fails to load, rather than drawing a broken image.
 */
export function Logo({ size = 32 }: { size?: number }) {
  const uploaded = useContext(LogoSourceContext);
  // Remembered per URL, so a replaced logo (which arrives under a new version) is tried afresh.
  const [failedSrc, setFailedSrc] = useState<string | null>(null);

  const src = uploaded && uploaded !== failedSrc ? uploaded : bundledLogoUrl;

  return (
    <img
      src={src}
      width={size}
      height={size}
      alt=""
      aria-hidden
      className="shrink-0 rounded-full object-cover"
      style={{ width: size, height: size }}
      onError={() => {
        if (src === uploaded) setFailedSrc(uploaded);
      }}
    />
  );
}
