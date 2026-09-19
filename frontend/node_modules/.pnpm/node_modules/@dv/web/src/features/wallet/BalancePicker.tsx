import { cn } from '@dv/ui';
import { Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { WalletBalanceDto } from '@/entities/wallet/api';
import { formatCurrency } from '@/shared/lib/format';

interface Props {
  balances: WalletBalanceDto[];
  selectedId: string;
  onSelect: (currencyId: string) => void;
  /** Greys out balances that cannot be chosen here, such as an empty one on the Withdraw page. */
  isDisabled?: (balance: WalletBalanceDto) => boolean;
  legend: string;
}

/**
 * Which of the order's balances an action is for — one card per currency the order can hold, each
 * showing what is in it. The whole card is the control, like the payment pickers.
 */
export function BalancePicker({ balances, selectedId, onSelect, isDisabled, legend }: Props) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';

  return (
    <fieldset>
      <legend className="mb-3 text-sm font-medium">{legend}</legend>

      <div className="grid gap-2 sm:grid-cols-2" data-testid="balance-picker">
        {balances.map((balance) => {
          const checked = balance.currencyId === selectedId;
          const disabled = isDisabled?.(balance) ?? false;

          return (
            <label
              key={balance.currencyId}
              className={cn(
                'relative flex items-center gap-3 rounded-xl border p-3 transition',
                'focus-within:ring-2 focus-within:ring-primary focus-within:ring-offset-1',
                disabled
                  ? 'cursor-not-allowed opacity-50'
                  : checked
                    ? 'cursor-pointer border-primary bg-primary/5 shadow-sm'
                    : 'cursor-pointer border-border hover:border-primary/60 hover:bg-muted',
              )}
            >
              <input
                type="radio"
                name="wallet-balance"
                className="sr-only"
                checked={checked}
                disabled={disabled}
                onChange={() => onSelect(balance.currencyId)}
                data-testid={`balance-${balance.currencyCode}`}
              />

              <span
                className={cn(
                  'flex size-11 shrink-0 items-center justify-center rounded-lg font-mono text-sm font-semibold',
                  checked ? 'bg-primary/10 text-primary' : 'bg-muted text-muted-foreground',
                )}
                dir="ltr"
              >
                {balance.currencyCode}
              </span>

              <span className="min-w-0 flex-1">
                <span className="flex items-center gap-2">
                  <span className="truncate font-medium">{balance.currencyName}</span>
                  {balance.isMain && (
                    <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-semibold uppercase text-muted-foreground">
                      {t('wallet.mainCurrency')}
                    </span>
                  )}
                </span>
                <span className="block text-sm text-muted-foreground">
                  {formatCurrency(balance.balance, balance.currencyCode, locale)}
                </span>
              </span>

              <span
                className={cn(
                  'flex size-5 shrink-0 items-center justify-center rounded-full border transition',
                  checked ? 'border-primary bg-primary text-white' : 'border-border',
                )}
                aria-hidden="true"
              >
                {checked && <Check className="size-3.5" strokeWidth={3} />}
              </span>
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}
