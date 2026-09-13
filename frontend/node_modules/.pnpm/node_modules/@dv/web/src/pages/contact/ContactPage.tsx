import { zodResolver } from '@hookform/resolvers/zod';
import {
  Alert,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Field,
  Input,
  LoadingState,
  SearchableSelect,
  Spinner,
  buttonVariants,
  cn,
} from '@dv/ui';
import { CheckCircle2, LifeBuoy } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { z } from 'zod';
import { useContactDirectory, useCreateTicket, useTicketCategories } from '@/entities/ticket/api';
import { AttachmentPicker } from '@/features/contact/AttachmentPicker';
import { useSession } from '@/features/auth/useSession';
import { ContactDirectory } from '@/features/contact/ContactDirectory';
import { DEFAULT_PHONE_COUNTRY, findPhoneCountry } from '@/shared/lib/phoneCountries';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { PhoneNumberInput } from '@/shared/ui/PhoneNumberInput';

/**
 * The public "contact us" form.
 *
 * Open to anyone, signed in or not: the person most likely to need support is the one who cannot
 * get in. Someone who *is* signed in has their order attached automatically by the server, which
 * saves support the guesswork without asking them to prove anything they have already proven.
 */
export function ContactPage() {
  const { t } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const toMessage = useApiErrorMessage();
  const session = useSession();

  const categories = useTicketCategories();
  const create = useCreateTicket();

  // Read here as well as inside the sidebar so the page can drop to one column when there is
  // nothing to put beside the form. The query is shared, so this costs no extra request.
  const directory = useContactDirectory();
  const hasDirectory = Boolean(
    directory.data
      && (directory.data.authorizedAgents.length > 0 || directory.data.administration.length > 0),
  );

  const [files, setFiles] = useState<File[]>([]);
  const [phoneCountry, setPhoneCountry] = useState(DEFAULT_PHONE_COUNTRY);
  const [raised, setRaised] = useState<{ ticketNumber: string; email: string } | null>(null);

  const schema = z.object({
    ticketCategoryId: z.string().min(1, t('contact.validation.category')),
    name: z.string().trim().min(1, t('contact.validation.name')).max(200),
    email: z.string().trim().min(1, t('contact.validation.email')).email(t('contact.validation.email')),
    phoneNumber: z.string().trim().max(32),
    subject: z.string().trim().min(1, t('contact.validation.subject')).max(300),
    description: z.string().trim().min(1, t('contact.validation.description')).max(5000),
  });

  type Values = z.infer<typeof schema>;

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      ticketCategoryId: '',
      name: '',
      email: '',
      phoneNumber: '',
      subject: '',
      description: '',
    },
  });

  function submit(values: Values) {
    create.mutate(
      {
        ...values,
        phoneCountryCode: findPhoneCountry(phoneCountry)?.dialCode ?? '',
        files,
      },
      {
        onSuccess: (result) => {
          setRaised(result);
          form.reset();
          setFiles([]);
        },
      },
    );
  }

  const sent = raised !== null;

  return (
    <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 lg:py-16">
      {/* The heading lives above both columns rather than inside the form card, so the directory
          beside it reads as part of the same page instead of an afterthought under it. */}
      <header className="mb-8 max-w-2xl">
        <div className="mb-4 flex size-12 items-center justify-center rounded-2xl bg-primary/10 ring-1 ring-primary/15">
          <LifeBuoy className="size-6 text-primary" aria-hidden="true" />
        </div>
        <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">{t('contact.title')}</h1>
        <p className="mt-3 text-base leading-7 text-muted-foreground">{t('contact.subtitle')}</p>
      </header>

      <div
        className={cn(
          'grid items-start gap-8',
          // One column when there is nothing to put beside the form, rather than a narrow form
          // with an empty half of the page next to it.
          hasDirectory && 'lg:grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)]',
        )}
      >
        <div className={cn(!hasDirectory && 'max-w-2xl')}>
          {sent ? (
            <Card>
              <CardHeader>
                <div className="mb-2 flex size-12 items-center justify-center rounded-2xl bg-success/10 ring-1 ring-success/15">
                  <CheckCircle2 className="size-6 text-success" aria-hidden="true" />
                </div>
                <CardTitle>{t('contact.sentTitle')}</CardTitle>
                <CardDescription>{t('contact.sentBody', { email: raised.email })}</CardDescription>
              </CardHeader>

              <CardContent className="space-y-6">
                {/* The reference is the one thing they have to keep, so it is the largest thing here. */}
                <div className="rounded-2xl border border-primary/15 bg-primary/5 p-5 text-center">
                  <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
                    {t('contact.yourReference')}
                  </p>
                  <p
                    className="mt-2 font-mono text-3xl font-bold tracking-wider text-primary"
                    dir="ltr"
                    data-testid="ticket-number"
                  >
                    {raised.ticketNumber}
                  </p>
                </div>

                <p className="text-sm leading-6 text-muted-foreground">{t('contact.sentNext')}</p>

                <div className="flex flex-wrap gap-3">
                  {/* Only offered to someone signed in — the page it leads to is theirs only. */}
                  {session && (
                    <Link to={`/${lang}/tickets`} className={buttonVariants()}>
                      {t('contact.viewMyTickets')}
                    </Link>
                  )}
                  <Button
                    variant="outline"
                    onClick={() => setRaised(null)}
                    data-testid="contact-again"
                  >
                    {t('contact.sendAnother')}
                  </Button>
                  <Link
                    to={`/${lang}`}
                    className={buttonVariants({ variant: session ? 'ghost' : 'primary' })}
                  >
                    {t('contact.backHome')}
                  </Link>
                </div>
              </CardContent>
            </Card>
          ) : (
            <Card>
              <CardContent className="pt-6">
          {categories.isPending ? (
            <LoadingState label={t('common.loading')} />
          ) : categories.isError ? (
            <Alert variant="error">{toMessage(categories.error)}</Alert>
          ) : (
            <form onSubmit={form.handleSubmit(submit)} className="space-y-5" noValidate>
              {create.isError && <Alert variant="error">{toMessage(create.error)}</Alert>}

              <Field
                label={t('contact.category')}
                htmlFor="contact-category"
                required
                error={form.formState.errors.ticketCategoryId?.message}
              >
                <SearchableSelect
                  id="contact-category"
                  options={(categories.data ?? []).map((category) => ({
                    value: category.id,
                    label: category.name,
                  }))}
                  value={form.watch('ticketCategoryId')}
                  onChange={(value) =>
                    form.setValue('ticketCategoryId', value, { shouldValidate: true })
                  }
                  placeholder={t('contact.categoryPlaceholder')}
                  searchPlaceholder={t('contact.categorySearch')}
                  invalid={Boolean(form.formState.errors.ticketCategoryId)}
                />
              </Field>

              <div className="grid gap-5 sm:grid-cols-2">
                <Field
                  label={t('contact.name')}
                  htmlFor="contact-name"
                  required
                  error={form.formState.errors.name?.message}
                >
                  <Input
                    id="contact-name"
                    autoComplete="name"
                    invalid={Boolean(form.formState.errors.name)}
                    {...form.register('name')}
                  />
                </Field>

                <Field
                  label={t('contact.email')}
                  htmlFor="contact-email"
                  required
                  hint={t('contact.emailHint')}
                  error={form.formState.errors.email?.message}
                >
                  <Input
                    id="contact-email"
                    type="email"
                    dir="ltr"
                    autoComplete="email"
                    invalid={Boolean(form.formState.errors.email)}
                    {...form.register('email')}
                  />
                </Field>
              </div>

              <Field
                label={t('contact.phone')}
                htmlFor="contact-phone"
                hint={t('contact.phoneHint')}
                error={form.formState.errors.phoneNumber?.message}
              >
                <PhoneNumberInput
                  id="contact-phone"
                  country={phoneCountry}
                  number={form.watch('phoneNumber')}
                  onCountryChange={setPhoneCountry}
                  onNumberChange={(digits) => form.setValue('phoneNumber', digits)}
                  invalid={Boolean(form.formState.errors.phoneNumber)}
                  placeholder={t('contact.phonePlaceholder')}
                />
              </Field>

              <Field
                label={t('contact.subject')}
                htmlFor="contact-subject"
                required
                error={form.formState.errors.subject?.message}
              >
                <Input
                  id="contact-subject"
                  invalid={Boolean(form.formState.errors.subject)}
                  placeholder={t('contact.subjectPlaceholder')}
                  {...form.register('subject')}
                />
              </Field>

              <Field
                label={t('contact.description')}
                htmlFor="contact-description"
                required
                hint={t('contact.descriptionHint')}
                error={form.formState.errors.description?.message}
              >
                <textarea
                  id="contact-description"
                  rows={6}
                  placeholder={t('contact.descriptionPlaceholder')}
                  className="w-full rounded-xl border border-border bg-background p-3 text-sm focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  {...form.register('description')}
                />
              </Field>

              <Field label={t('contact.attachments')} htmlFor="contact-files">
                <AttachmentPicker
                  files={files}
                  onChange={setFiles}
                  disabled={create.isPending}
                />
              </Field>

              <Button
                type="submit"
                size="lg"
                className="w-full"
                disabled={create.isPending}
                data-testid="contact-submit"
              >
                {create.isPending && <Spinner />}
                {t('contact.submit')}
              </Button>
            </form>
          )}
              </CardContent>
            </Card>
          )}
        </div>

        {/* Beside the form, and sticky on a wide screen so the numbers stay in view while the
            form is being filled in. It follows the reading direction: right in English, left in
            Arabic, because the grid's inline axis flips with the page. */}
        {hasDirectory && (
          <aside className="lg:sticky lg:top-24">
            <ContactDirectory />
          </aside>
        )}
      </div>
    </div>
  );
}
