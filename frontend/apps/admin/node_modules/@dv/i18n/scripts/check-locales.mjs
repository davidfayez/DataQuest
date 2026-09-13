/**
 * Locale integrity check.
 *
 * Catches the two failure modes that are invisible at runtime until a user hits them:
 *   1. a key present in English but missing from another locale, which silently falls back;
 *   2. text from the wrong script leaking into a bundle (a Cyrillic word inside the Chinese
 *      file, for example), which reads as gibberish to that locale's users.
 *
 * Bundles are grouped into families: the JSON files at the root of `locales/` are the applicant
 * app, and each subdirectory (such as `admin/`) is its own family checked against its own English
 * reference. Plural suffixes are normalised before comparison because plural categories
 * legitimately differ per language — Arabic has six, Chinese has one.
 */
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const localesDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'locales');
const PLURAL_SUFFIX = /_(zero|one|two|few|many|other)$/;

/** Scripts each locale is expected to use. */
const SCRIPTS = {
  ar: /[\u0600-\u06FF]/,
  ru: /[\u0400-\u04FF]/,
  hi: /[\u0900-\u097F]/,
  zh: /[\u4E00-\u9FFF]/,
  // Japanese uses kana and/or kanji; either counts as a native script mark.
  ja: /[\u4E00-\u9FFF\u3040-\u309F\u30A0-\u30FF]/,
};

const ARABIC = /[\u0600-\u06FF]/;
const CYRILLIC = /[\u0400-\u04FF]/;
const DEVANAGARI = /[\u0900-\u097F]/;
const HAN = /[\u4E00-\u9FFF]/;

/** Scripts that must never appear in a given locale. */
const FORBIDDEN = {
  ar: { Cyrillic: CYRILLIC, Devanagari: DEVANAGARI, Han: HAN },
  ru: { Arabic: ARABIC, Devanagari: DEVANAGARI, Han: HAN },
  hi: { Arabic: ARABIC, Cyrillic: CYRILLIC, Han: HAN },
  zh: { Arabic: ARABIC, Cyrillic: CYRILLIC, Devanagari: DEVANAGARI },
  // Japanese shares the Han block with Chinese for kanji — do not forbid Han.
  ja: { Arabic: ARABIC, Cyrillic: CYRILLIC, Devanagari: DEVANAGARI },
  de: { Arabic: ARABIC, Cyrillic: CYRILLIC, Devanagari: DEVANAGARI, Han: HAN },
  en: { Arabic: ARABIC, Cyrillic: CYRILLIC, Devanagari: DEVANAGARI, Han: HAN },
  tr: { Arabic: ARABIC, Cyrillic: CYRILLIC, Devanagari: DEVANAGARI, Han: HAN },
  uz: { Arabic: ARABIC, Cyrillic: CYRILLIC, Devanagari: DEVANAGARI, Han: HAN },
  pl: { Arabic: ARABIC, Cyrillic: CYRILLIC, Devanagari: DEVANAGARI, Han: HAN },
};

function flatten(value, prefix = '', out = new Map()) {
  for (const [key, child] of Object.entries(value)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (child && typeof child === 'object' && !Array.isArray(child)) {
      flatten(child, path, out);
    } else {
      out.set(path, child);
    }
  }
  return out;
}

function normaliseKeys(keys) {
  return new Set([...keys].map((key) => key.replace(PLURAL_SUFFIX, '_PLURAL')));
}

function loadFamily(directory) {
  return new Map(
    readdirSync(directory)
      .filter((name) => name.endsWith('.json'))
      .map((name) => [
        name.replace('.json', ''),
        flatten(JSON.parse(readFileSync(join(directory, name), 'utf8'))),
      ]),
  );
}

const families = new Map([['app', loadFamily(localesDir)]]);

for (const entry of readdirSync(localesDir)) {
  const path = join(localesDir, entry);
  if (statSync(path).isDirectory()) {
    families.set(entry, loadFamily(path));
  }
}

let failures = 0;

for (const [familyName, bundles] of families) {
  const reference = bundles.get('en');

  if (!reference) {
    console.error(`${familyName}: en.json is missing — it is the reference bundle.`);
    failures++;
    continue;
  }

  const referenceKeys = normaliseKeys(reference.keys());

  for (const [locale, bundle] of bundles) {
    const keys = normaliseKeys(bundle.keys());

    for (const key of [...referenceKeys].filter((key) => !keys.has(key))) {
      console.error(`${familyName}/${locale}: missing key "${key}"`);
      failures++;
    }

    for (const key of [...keys].filter((key) => !referenceKeys.has(key))) {
      console.error(`${familyName}/${locale}: unexpected key "${key}"`);
      failures++;
    }

    for (const [key, value] of bundle) {
      if (typeof value !== 'string') continue;

      for (const [scriptName, pattern] of Object.entries(FORBIDDEN[locale] ?? {})) {
        if (pattern.test(value)) {
          console.error(`${familyName}/${locale}: "${key}" contains ${scriptName} text — "${value}"`);
          failures++;
        }
      }

      // A non-Latin locale whose value is pure ASCII is usually an untranslated string. Keys
      // holding placeholders, codes or product names are legitimately Latin, so they are skipped.
      const expected = SCRIPTS[locale];
      const exempt = /placeholder|appName|code|symbol/i.test(key);

      if (expected && !exempt && value.length > 3 && !expected.test(value) && /^[\x20-\x7E]+$/.test(value)) {
        console.error(`${familyName}/${locale}: "${key}" looks untranslated — "${value}"`);
        failures++;
      }
    }
  }

  console.log(`${familyName}: ${bundles.size} locales, ${referenceKeys.size} keys each.`);
}

if (failures > 0) {
  console.error(`\n${failures} locale problem(s) found.`);
  process.exit(1);
}

console.log('All locale bundles are consistent.');
