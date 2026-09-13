/** Locales the public web app ships with. Arabic is right-to-left. */
export const SUPPORTED_LANGUAGES = [
  { code: 'ar', name: 'العربية', englishName: 'Arabic', dir: 'rtl' },
  { code: 'en', name: 'English', englishName: 'English', dir: 'ltr' },
  { code: 'ru', name: 'Русский', englishName: 'Russian', dir: 'ltr' },
  { code: 'tr', name: 'Türkçe', englishName: 'Turkish', dir: 'ltr' },
  { code: 'uz', name: "Oʻzbekcha", englishName: 'Uzbek', dir: 'ltr' },
  { code: 'de', name: 'Deutsch', englishName: 'German', dir: 'ltr' },
  { code: 'hi', name: 'हिन्दी', englishName: 'Hindi', dir: 'ltr' },
  { code: 'zh', name: '中文', englishName: 'Chinese', dir: 'ltr' },
  { code: 'ja', name: '日本語', englishName: 'Japanese', dir: 'ltr' },
  { code: 'pl', name: 'Polski', englishName: 'Polish', dir: 'ltr' },
] as const;

export type LanguageCode = (typeof SUPPORTED_LANGUAGES)[number]['code'];
export type Direction = 'ltr' | 'rtl';

/** Locales the admin panel exposes. The infrastructure supports every web language. */
export const ADMIN_LANGUAGES: readonly LanguageCode[] = ['ar', 'en'];

export const DEFAULT_LANGUAGE: LanguageCode = 'en';

const RTL_LANGUAGES = new Set<string>(
  SUPPORTED_LANGUAGES.filter((l) => l.dir === 'rtl').map((l) => l.code),
);

export function isRtl(code: string): boolean {
  return RTL_LANGUAGES.has(code);
}

export function directionOf(code: string): Direction {
  return isRtl(code) ? 'rtl' : 'ltr';
}

export function isSupportedLanguage(code: string): code is LanguageCode {
  return SUPPORTED_LANGUAGES.some((l) => l.code === code);
}
