import { Alert, cn } from '@dv/ui';
import { ExternalLink, Loader2, Search, Sparkles } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { toEmbedUrl, toolImageSrc } from './api';
import { useAskAi, useKnowledgeSearch, type KnowledgeHit } from './knowledge';

type Mode = 'knowledge' | 'ai';

/**
 * The search box above the tools and guides: one field, two buttons.
 *
 * "DQ Knowledge" searches the guides an administrator has published. "AI Mode" sends the same words
 * to the assistant, which answers from those guides first and searches the web for what they do not
 * cover — so it cannot invent a process this platform's own guides would contradict, and it lists
 * the pages it used when it went outside them.
 *
 * Which button was pressed decides what is shown, so the two results never sit on screen together
 * claiming different things.
 */
export function KnowledgeSearch({ onActive }: { onActive?: (active: boolean) => void }) {
  const { t } = useTranslation();
  const [text, setText] = useState('');
  const [mode, setMode] = useState<Mode>('knowledge');

  const search = useKnowledgeSearch();
  const ask = useAskAi();

  const busy = search.isPending || ask.isPending;
  const typed = text.trim();

  // The page hides the full list of guides while an answer is on screen: showing both leaves the
  // matching guide printed twice, once as a result and once in the list under it.
  const showing = Boolean(search.data ?? ask.data ?? search.isError ?? ask.isError) || busy;
  useEffect(() => onActive?.(showing), [showing, onActive]);

  function run(next: Mode, event?: FormEvent) {
    event?.preventDefault();
    if (!typed || busy) return;

    setMode(next);

    // Only the results being replaced are cleared, so pressing a button never leaves the previous
    // answer sitting under a new question.
    if (next === 'knowledge') {
      ask.reset();
      search.mutate(typed);
    } else {
      search.reset();
      ask.mutate(typed);
    }
  }

  return (
    <section className="mb-10">
      <form onSubmit={(event) => run(mode, event)} className="mx-auto max-w-2xl">
        <div className="group flex items-center gap-3 rounded-full border border-ink-200 bg-white px-5 py-3 shadow-soft transition-shadow focus-within:border-ink-300 focus-within:shadow-lift hover:shadow-lift">
          <Search className="size-5 shrink-0 text-ink-400" aria-hidden />
          <input
            type="search"
            value={text}
            onChange={(event) => setText(event.target.value)}
            placeholder={t('knowledge.placeholder')}
            aria-label={t('knowledge.placeholder')}
            maxLength={200}
            // The browser's own clear button and search history are the point of type="search";
            // the ring is on the pill, so the field itself carries none.
            className="w-full bg-transparent text-base text-ink-900 outline-none placeholder:text-ink-400"
            data-testid="knowledge-input"
          />
          {busy && <Loader2 className="size-5 shrink-0 animate-spin text-ink-400" aria-hidden />}
        </div>

        <div className="mt-5 flex flex-wrap justify-center gap-3">
          <Button
            onClick={() => run('knowledge')}
            disabled={!typed || busy}
            testId="search-knowledge"
            icon={<Search className="size-4" aria-hidden />}
          >
            {t('knowledge.searchButton')}
          </Button>

          <Button
            onClick={() => run('ai')}
            disabled={!typed || busy}
            testId="search-ai"
            icon={<Sparkles className="size-4" aria-hidden />}
          >
            {t('knowledge.aiButton')}
          </Button>
        </div>
      </form>

      <div className="mx-auto mt-8 max-w-3xl">
        {mode === 'knowledge' && <Results state={search} />}
        {mode === 'ai' && <Answer state={ask} />}
      </div>
    </section>
  );
}

/**
 * The site a link points at, which is what tells a reader whether to trust it before clicking.
 * A URL that will not parse is shown as it is rather than dropped.
 */
function hostOf(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./, '');
  } catch {
    return url;
  }
}

/** Both buttons look alike, the way a search page's pair of buttons does. */
function Button({
  children,
  onClick,
  disabled,
  testId,
  icon,
}: {
  children: React.ReactNode;
  onClick: () => void;
  disabled: boolean;
  testId: string;
  icon: React.ReactNode;
}) {
  return (
    <button
      // Explicitly not a submit button: both sit in the form, and a button with no type submits it,
      // which would run whichever mode was last used instead of the one that was clicked.
      type="button"
      onClick={onClick}
      disabled={disabled}
      data-testid={testId}
      className={cn(
        'inline-flex items-center gap-2 rounded-lg border border-ink-200 bg-cream px-5 py-2.5 text-sm font-medium text-ink-700',
        'transition-colors hover:border-ink-300 hover:bg-ink-50 hover:text-ink-950',
        'disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:border-ink-200 disabled:hover:bg-cream',
      )}
    >
      {icon}
      {children}
    </button>
  );
}

function Results({ state }: { state: ReturnType<typeof useKnowledgeSearch> }) {
  const { t } = useTranslation();

  if (state.isError) return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  if (!state.data) return null;

  if (state.data.length === 0) {
    return <Alert variant="info">{t('knowledge.noResults')}</Alert>;
  }

  return (
    <ul className="space-y-4" data-testid="knowledge-results">
      {state.data.map((hit) => (
        <li key={hit.id} className="rounded-2xl bg-white p-5 shadow-soft ring-1 ring-ink-100">
          <Hit hit={hit} />
        </li>
      ))}
    </ul>
  );
}

function Hit({ hit }: { hit: KnowledgeHit }) {
  const { t } = useTranslation();
  const embed = hit.videoUrl ? toEmbedUrl(hit.videoUrl) : null;

  return (
    <>
      <h2 className="font-display text-lg font-semibold text-ink-950">{hit.name}</h2>
      {hit.snippet && (
        <p className="mt-2 whitespace-pre-line text-sm leading-relaxed text-ink-600">
          {hit.snippet}
        </p>
      )}

      {embed && (
        <div className="mt-4 aspect-video overflow-hidden rounded-xl bg-ink-950">
          <iframe
            src={embed}
            title={hit.name}
            className="size-full"
            allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
            allowFullScreen
          />
        </div>
      )}

      {hit.imageUrl && (
        <img
          src={toolImageSrc(hit.imageUrl)}
          alt={hit.name || t('tools.imageAlt')}
          loading="lazy"
          className="mt-4 w-full rounded-xl"
        />
      )}
    </>
  );
}

function Answer({ state }: { state: ReturnType<typeof useAskAi> }) {
  const { t } = useTranslation();

  if (state.isError) return <Alert variant="error">{t('knowledge.aiFailed')}</Alert>;
  if (!state.data) return null;

  // Two different silences: nobody has configured a key, or the provider had nothing to say.
  if (!state.data.isConfigured) {
    return <Alert variant="info">{t('knowledge.aiUnavailable')}</Alert>;
  }

  if (!state.data.answer.trim()) {
    return <Alert variant="info">{t('knowledge.aiFailed')}</Alert>;
  }

  return (
    <div data-testid="ai-answer">
      <div className="rounded-2xl bg-white p-5 shadow-soft ring-1 ring-ink-100">
        <p className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-brand-700">
          <Sparkles className="size-4" aria-hidden />
          {t('knowledge.aiHeading')}
        </p>
        <p className="mt-3 whitespace-pre-line leading-relaxed text-ink-800">
          {state.data.answer}
        </p>
        {/* Said plainly rather than in the footer: a reader deciding whether to act on this needs
            to know a machine wrote it. */}
        <p className="mt-4 text-xs text-ink-400">{t('knowledge.aiDisclaimer')}</p>
      </div>

      {state.data.sources.length > 0 && (
        <div className="mt-5">
          <h2 className="text-sm font-semibold text-ink-700">{t('knowledge.aiSources')}</h2>
          <ul className="mt-2 space-y-2">
            {state.data.sources.map((source) => (
              <li
                key={source.id}
                className="rounded-xl bg-cream/70 px-4 py-3 text-sm text-ink-600 ring-1 ring-ink-100"
              >
                <span className="font-medium text-ink-800">{source.name}</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {state.data.webSources.length > 0 && (
        <div className="mt-5" data-testid="ai-web-sources">
          <h2 className="text-sm font-semibold text-ink-700">{t('knowledge.webSources')}</h2>
          <ul className="mt-2 space-y-2">
            {state.data.webSources.map((source) => (
              <li key={source.url}>
                <a
                  href={source.url}
                  // Somebody else's page: it opens away from the site, and the relationship is
                  // declared so a search engine does not read this as an endorsement.
                  target="_blank"
                  rel="noopener noreferrer nofollow"
                  className="group flex items-start gap-2 rounded-xl bg-white px-4 py-3 text-sm ring-1 ring-ink-100 transition-colors hover:bg-cream/70"
                >
                  <ExternalLink
                    className="mt-0.5 size-4 shrink-0 text-ink-400 group-hover:text-brand-600"
                    aria-hidden
                  />
                  <span className="min-w-0">
                    <span className="block font-medium text-ink-800 group-hover:text-brand-700">
                      {source.title}
                    </span>
                    <span className="mt-0.5 block truncate text-xs text-ink-400" dir="ltr">
                      {hostOf(source.url)}
                    </span>
                  </span>
                </a>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
