import { AdminPageHeader, Alert } from '@dv/ui';
import type { ComponentType } from 'react';
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

/** The lookup modules, each now its own sidebar entry and route. */
export type LookupKey =
  | 'countries'
  | 'currencies'
  | 'addressees'
  | 'transactionTypes'
  | 'subTransactionTypes'
  | 'authorities'
  | 'serviceTypes';

/** The permission-name module prefix behind each lookup. */
const MODULE: Record<LookupKey, string> = {
  countries: 'Countries',
  currencies: 'Currencies',
  addressees: 'Addressees',
  transactionTypes: 'TransactionTypes',
  subTransactionTypes: 'SubTransactionTypes',
  authorities: 'Authorities',
  serviceTypes: 'ServiceTypes',
};

const COMPONENT: Record<LookupKey, ComponentType<{ caps: LookupCaps }>> = {
  countries: CountriesTab,
  currencies: CurrenciesTab,
  addressees: AddresseesTab,
  transactionTypes: TransactionTypesTab,
  subTransactionTypes: SubTransactionTypesTab,
  authorities: AuthoritiesTab,
  serviceTypes: ServiceTypesTab,
};

/**
 * A single lookup as its own page. The tab components are unchanged — this just gives each one a
 * header and its own route so it can live in the sidebar rather than behind a tab strip.
 */
export function LookupModulePage({ module }: { module: LookupKey }) {
  const { t } = useTranslation();
  const session = useAdminSession();

  const has = (name: string) => session?.permissions.has(name) ?? false;
  const prefix = MODULE[module];
  const caps: LookupCaps = {
    canCreate: has(`${prefix}.Create`),
    canUpdate: has(`${prefix}.Update`),
    canDelete: has(`${prefix}.Delete`),
  };
  const canManageAny = caps.canCreate || caps.canUpdate || caps.canDelete;

  const Tab = COMPONENT[module];

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t(`lookups.${module}`)} subtitle={t('lookups.subtitle')} />

      {!canManageAny && <Alert variant="info">{t('roles.viewOnlyNotice')}</Alert>}

      <Tab caps={caps} />
    </div>
  );
}
