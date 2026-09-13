import { Input, SearchableSelect } from '@dv/ui';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { PHONE_COUNTRIES, flagUrl } from '@/shared/lib/phoneCountries';
import { useLanguage } from '@/shared/lib/useLanguage';

/** Off-network or on an unknown code the image simply disappears, leaving the dial code alone. */
function Flag({ code, name }: { code: string; name: string }) {
  return (
    <img
      src={flagUrl(code)}
      srcSet={`${flagUrl(code, 40)} 2x`}
      width={20}
      height={15}
      loading="lazy"
      alt=""
      title={name}
      aria-hidden
      className="h-[15px] w-5 shrink-0 rounded-[2px] object-cover ring-1 ring-ink-200/70"
      onError={(event) => {
        event.currentTarget.style.visibility = 'hidden';
      }}
    />
  );
}

export interface PhoneNumberInputProps {
  /** Id of the dial-code control; the number field takes `${id}Number`. */
  id: string;
  /** ISO 3166-1 alpha-2 of the selected dial code. */
  country: string;
  /** The national part, digits only. */
  number: string;
  onCountryChange: (code: string) => void;
  onNumberChange: (digits: string) => void;
  invalid?: boolean;
  disabled?: boolean;
  placeholder?: string;
}

/**
 * A dial code chosen from a searchable, flagged list, beside the national number. The two are kept
 * apart rather than parsed out of one string: `+1` alone cannot say whether the number is American
 * or Canadian, and the server stores the country so the flag can be shown again later.
 */
export function PhoneNumberInput({
  id,
  country,
  number,
  onCountryChange,
  onNumberChange,
  invalid,
  disabled,
  placeholder,
}: PhoneNumberInputProps) {
  const { t } = useTranslation();
  const lang = useLanguage();

  // Names come from the browser in the page's language, so the list reads and searches natively in
  // every locale. Older runtimes without region display names fall back to the English name.
  const options = useMemo(() => {
    let display: Intl.DisplayNames | undefined;
    try {
      display = new Intl.DisplayNames([lang], { type: 'region' });
    } catch {
      display = undefined;
    }

    return PHONE_COUNTRIES.map((entry) => {
      const name = display?.of(entry.code) ?? entry.englishName;
      // `of` returns the code itself for regions it does not know, e.g. XK.
      const label = name === entry.code ? entry.englishName : name;

      // A leading left-to-right mark keeps the plus in front of the digits on the Arabic page —
      // bidi would otherwise take the neutral '+' as Arabic and render '+93' as '93+'.
      const dialCode = `‎${entry.dialCode}`;

      return {
        value: entry.code,
        // Rows name the country — several countries share a dial code, so `+1` alone would not
        // say which one was picked. The narrow trigger shows the code once chosen.
        label: `${label} ${dialCode}`,
        triggerLabel: dialCode,
        icon: <Flag code={entry.code} name={label} />,
        keywords: `${label} ${entry.englishName} ${entry.code}`,
      };
    });
  }, [lang]);

  // Sorted by the localised name so the list reads alphabetically in the page's own language.
  const sorted = useMemo(() => {
    const collator = new Intl.Collator(lang);
    return [...options].sort((a, b) => collator.compare(a.keywords, b.keywords));
  }, [options, lang]);

  return (
    <div className="flex items-start gap-2">
      <SearchableSelect
        id={id}
        className="w-32 shrink-0"
        // The trigger is too narrow to list countries in, so the panel is given its own width and
        // anchored to the field's leading edge.
        menuClassName="start-0 w-[min(20rem,calc(100vw-3rem))]"
        disabled={disabled}
        invalid={invalid}
        value={country}
        onChange={onCountryChange}
        aria-label={t('phone.codeLabel')}
        placeholder={t('phone.codePlaceholder')}
        searchPlaceholder={t('common.search')}
        emptyMessage={t('common.noResults')}
        options={sorted}
      />

      <Input
        id={`${id}Number`}
        type="tel"
        inputMode="numeric"
        autoComplete="tel-national"
        // A phone number is Latin digits in every locale, so it stays left-to-right on the
        // Arabic page rather than reordering around the dial code beside it.
        dir="ltr"
        disabled={disabled}
        invalid={invalid}
        value={number}
        placeholder={placeholder}
        aria-label={t('phone.numberLabel')}
        // Anything but digits is dropped as it is typed: the dial code is carried separately, so a
        // pasted "+20 100 123 4567" would otherwise duplicate the prefix.
        onChange={(event) => onNumberChange(event.target.value.replace(/\D/g, '').slice(0, 15))}
      />
    </div>
  );
}
