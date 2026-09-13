import { AdminPageHeader } from '@dv/ui';
import { useTranslation } from 'react-i18next';
import { DefaultSenderCard } from './DefaultSenderCard';
import { EmailRoutingCard } from './EmailRoutingCard';

/**
 * Who each kind of outgoing email comes from, and who is blind-copied on it.
 *
 * Its own page rather than a card on Settings: there is one panel per kind of email and the list
 * grows with every new one the platform learns to send, which is more than a settings page should
 * be asked to carry alongside unrelated configuration.
 */
export function EmailConfigurationsPage() {
  const { t } = useTranslation();

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader
        title={t('emailConfigurations.title')}
        subtitle={t('emailConfigurations.subtitle')}
      />

      {/* The fallback first: it is what most email actually goes out as, and the panels below it
          are overrides of this one. */}
      <DefaultSenderCard />

      <EmailRoutingCard />
    </div>
  );
}
