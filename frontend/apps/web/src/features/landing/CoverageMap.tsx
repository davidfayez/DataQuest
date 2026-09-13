import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { COUNTRY_COORDS } from './countryCoords';

export interface MarkedCountry {
  id: string;
  code: string;
  name: string;
  /** Chosen per country in the admin panel. Anything unrecognised is drawn as a spot. */
  marker: string;
}

/** Marker size in pixels. */
const PIN_WIDTH = 26;
const PIN_HEIGHT = 36;
const FLAG_WIDTH = 34;
const FLAG_HEIGHT = 24;

/** How far in fitBounds may go, so one country does not land the reader on a street corner. */
const MAX_FIT_ZOOM = 5;

/**
 * The classic map pin, in red, as inline SVG.
 *
 * Drawn rather than loaded: Leaflet's own marker images are PNGs resolved relative to the CSS,
 * which a bundler rewrites and which cannot be recoloured. An inline icon has no such problem and
 * stays sharp on any screen.
 */
function pinHtml(): string {
  const r = PIN_WIDTH / 2;
  const cy = PIN_HEIGHT - r;

  return `
    <svg width="${PIN_WIDTH}" height="${PIN_HEIGHT}" viewBox="0 0 ${PIN_WIDTH} ${PIN_HEIGHT}"
         xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <path d="M ${r} ${PIN_HEIGHT}
               C ${r * 0.3} ${cy + r * 0.55} 0 ${cy + r * 0.28} 0 ${cy}
               a ${r} ${r} 0 1 1 ${PIN_WIDTH} 0
               c 0 ${r * 0.28} ${-r * 0.7} ${r * 0.55} ${-r} ${r}
               Z"
            fill="#dc2626" stroke="#ffffff" stroke-width="2" stroke-linejoin="round" />
      <circle cx="${r}" cy="${cy}" r="${r * 0.34}" fill="#ffffff" />
    </svg>`;
}

/** The country's flag on a small white plate, for rows an administrator marked as Flag. */
function flagHtml(code: string): string {
  const src = `https://flagcdn.com/w80/${code.toLowerCase()}.png`;

  return `
    <span style="display:block;width:${FLAG_WIDTH}px;height:${FLAG_HEIGHT}px;border-radius:4px;
                 overflow:hidden;background:#fff;box-shadow:0 1px 4px rgb(0 0 0 / 0.35);
                 outline:2px solid #fff">
      <img src="${src}" alt="" width="${FLAG_WIDTH}" height="${FLAG_HEIGHT}"
           style="display:block;width:100%;height:100%;object-fit:cover" />
    </span>`;
}

/**
 * A real, pannable map of the world with the chosen countries pinned on it.
 *
 * OpenStreetMap rather than Google: the tiles carry every country's name and border already, the
 * map pans and zooms like any map a reader has used, and it needs no API key, no Google Cloud
 * project and no billing account — which is the whole of the operational difference between the
 * two here. Attribution is required by OSM's terms and is why the credit in the corner stays.
 *
 * Leaflet is driven directly rather than through a React wrapper: this map is created once and
 * told about a list of countries, which is not enough to justify another dependency.
 */
export function CoverageMap({ countries }: { countries: readonly MarkedCountry[] }) {
  const { t } = useTranslation();
  const holder = useRef<HTMLDivElement>(null);
  const map = useRef<L.Map | null>(null);

  useEffect(() => {
    if (!holder.current || map.current) return undefined;

    const instance = L.map(holder.current, {
      // Scrolling the page must not be captured by a map that happens to be under the pointer.
      // Ctrl+wheel still zooms, and the +/- buttons and dragging always work.
      scrollWheelZoom: false,
      worldCopyJump: true,
      minZoom: 1,
      attributionControl: true,
    });

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 12,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
    }).addTo(instance);

    map.current = instance;
    return () => {
      instance.remove();
      map.current = null;
    };
  }, []);

  useEffect(() => {
    const instance = map.current;
    if (!instance) return undefined;

    const markers: L.Marker[] = [];
    const points: L.LatLngExpression[] = [];

    for (const country of countries) {
      const stored = COUNTRY_COORDS[country.code.toUpperCase()];
      if (!stored) continue;

      // The table is frozen; Leaflet wants a pair it can hold on to.
      const coords: L.LatLngTuple = [stored[0], stored[1]];

      const isFlag = country.marker === 'Flag';
      const icon = L.divIcon({
        className: '',
        html: isFlag ? flagHtml(country.code) : pinHtml(),
        iconSize: isFlag ? [FLAG_WIDTH, FLAG_HEIGHT] : [PIN_WIDTH, PIN_HEIGHT],
        // A pin points at its place from above; a flag sits on it.
        iconAnchor: isFlag
          ? [FLAG_WIDTH / 2, FLAG_HEIGHT / 2]
          : [PIN_WIDTH / 2, PIN_HEIGHT],
      });

      const marker = L.marker(coords, { icon, title: country.name, alt: country.name })
        .bindTooltip(country.name, { direction: 'top', offset: [0, isFlag ? -14 : -34] })
        .addTo(instance);

      markers.push(marker);
      points.push(coords);
    }

    if (points.length > 0) {
      instance.fitBounds(L.latLngBounds(points), { padding: [48, 48], maxZoom: MAX_FIT_ZOOM });
    } else {
      instance.setView([20, 10], 2);
    }

    return () => {
      for (const marker of markers) marker.remove();
    };
  }, [countries]);

  return (
    <div
      ref={holder}
      // Tiles and controls are absolutely positioned inside, so the box needs a height of its own.
      className="h-[380px] w-full overflow-hidden rounded-2xl sm:h-[460px]"
      role="application"
      aria-label={t('coverage.mapLabel')}
    />
  );
}
