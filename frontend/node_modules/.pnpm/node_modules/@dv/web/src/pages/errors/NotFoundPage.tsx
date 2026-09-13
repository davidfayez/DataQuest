import { buttonVariants } from '@dv/ui';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';

export function NotFoundPage() {
  const { t } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();

  return (
    <div className="mx-auto flex max-w-lg flex-col items-start gap-4 px-4 py-24 sm:px-6">
      <h1 className="text-2xl font-semibold">{t('errors.pageNotFoundTitle')}</h1>
      <p className="text-muted-foreground">{t('errors.pageNotFoundBody')}</p>
      <Link to={`/${lang}`} className={buttonVariants({ variant: 'outline' })}>
        {t('errors.goHome')}
      </Link>
    </div>
  );
}
