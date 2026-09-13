import { AdminFilterBar, AdminFilterPill, AdminPageHeader, Alert, Button } from '@dv/ui';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAdminSession } from '@/features/auth/useAdminSession';
import type { LookupCaps } from './tabs/columns';
import { CountriesTab } from './tabs/CountriesTab';
import { CurrenciesTab } from './tabs/CurrenciesTab';
import { AddresseesTab } from './tabs/AddresseesTab';
import { TransactionTypesTab } from './tabs/TransactionTypesTab';
import { SubTransactionTypesTab } from './tabs/SubTransactionTypesTab';
import { AuthoritiesTab } from './tabs/AuthoritiesTab';
import { ServiceTypesTab } from './tabs/ServiceTypesTab';

const TABS = [
  'countries',
  'currencies',
  'addressees',
  'transactionTypes',
  'subTransactionTypes',
  'authorities',
  'serviceTypes',
] as const;

type Tab = (typeof TABS)[number];

/** The permission-name module prefix behind each tab. */
const TAB_MODULE: Record<Tab, string> = {
  countries: 'Countries',
  currencies: 'Currencies',
  addressees: 'Addressees',
  transactionTypes: 'TransactionTypes',
  subTransactionTypes: 'SubTransactionTypes',
  authorities: 'Authorities',
  serviceTypes: 'ServiceTypes',
};

/**
 * The lookups module. Each tab is a thin screen over the same table and dialog components, so a
 * new lookup is a small file rather than a new page.
 */
export function LookupsPage() {
  const { t } = useTranslation();
  const session = useAdminSession();
  const [tab, setTab] = useState<Tab>('countries');

  const has = (name: string) => session?.permissions.has(name) ?? false;
  const capsFor = (module: string): LookupCaps => ({
    canCreate: has(`${module}.Create`),
    canUpdate: has(`${module}.Update`),
    canDelete: has(`${module}.Delete`),
  });

  const caps = capsFor(TAB_MODULE[tab]);
  const canManageAny = caps.canCreate || caps.canUpdate || caps.canDelete;

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t('lookups.title')} subtitle={t('lookups.subtitle')} />

      <div role="tablist">
        <AdminFilterBar>
          {TABS.map((name) => (
            <span key={name} role="tab" aria-selected={tab === name} data-testid={`lookup-tab-${name}`}>
              <AdminFilterPill active={tab === name} onClick={() => setTab(name)}>
                {t(`lookups.${name}`)}
              </AdminFilterPill>
            </span>
          ))}
        </AdminFilterBar>
      </div>

      {!canManageAny && <Alert variant="info">{t('roles.viewOnlyNotice')}</Alert>}

      {tab === 'countries' && <CountriesTab caps={caps} />}
      {tab === 'currencies' && <CurrenciesTab caps={caps} />}
      {tab === 'addressees' && <AddresseesTab caps={caps} />}
      {tab === 'transactionTypes' && <TransactionTypesTab caps={caps} />}
      {tab === 'subTransactionTypes' && <SubTransactionTypesTab caps={caps} />}
      {tab === 'authorities' && <AuthoritiesTab caps={caps} />}
      {tab === 'serviceTypes' && <ServiceTypesTab caps={caps} />}
    </div>
  );
}

/** Shared "New …" button so every tab's primary action sits in the same place. */
export function NewButton({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <Button onClick={onClick} data-testid="lookup-new">
      <Plus className="size-4" aria-hidden="true" />
      {label}
    </Button>
  );
}
