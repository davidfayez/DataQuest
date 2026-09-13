import { Alert, Card, CardContent, LoadingState } from '@dv/ui';
import { ExternalLink } from 'lucide-react';
import { useCallback, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { toEmbedUrl, toolImageSrc, useTools, type ToolDto } from '@/features/tools/api';
import { KnowledgeSearch } from '@/features/tools/KnowledgeSearch';

/**
 * The public "how to use the platform" page: the videos and illustrations an administrator has
 * published, in the order they set, in the visitor's language.
 */
export function ToolsPage() {
  const { t } = useTranslation();
  const tools = useTools();

  // While a search or an answer is on screen the full list steps aside, so the page shows one
  // response to what was asked rather than the response and the whole catalogue under it.
  const [searching, setSearching] = useState(false);
  const onActive = useCallback((active: boolean) => setSearching(active), []);

  return (
    <div className="mx-auto max-w-4xl px-4 py-12 sm:px-6">
      <header className="mb-10 text-center">
        <h1 className="font-display text-3xl font-semibold tracking-tight text-ink-950 sm:text-4xl">
          {t('tools.title')}
        </h1>
        <p className="mx-auto mt-3 max-w-2xl text-ink-500">{t('tools.subtitle')}</p>
      </header>

      <KnowledgeSearch onActive={onActive} />

      {!searching && tools.isPending && <LoadingState label={t('common.loading')} />}

      {!searching && tools.isError && <Alert variant="error">{t('errors.genericTitle')}</Alert>}

      {!searching && tools.data && tools.data.length === 0 && (
        <Alert variant="info">{t('tools.empty')}</Alert>
      )}

      {!searching && tools.data && tools.data.length > 0 && (
        <div className="space-y-8">
          {tools.data.map((entry) => (
            <ToolCard key={entry.id} entry={entry} />
          ))}
        </div>
      )}
    </div>
  );
}

function ToolCard({ entry }: { entry: ToolDto }) {
  const { t } = useTranslation();

  return (
    <Card>
      <CardContent className="space-y-4 p-5 sm:p-6">
        <div>
          <h2 className="font-display text-xl font-semibold text-ink-950">{entry.name}</h2>
          {entry.description && (
            <p className="mt-2 whitespace-pre-line text-ink-600">{entry.description}</p>
          )}
        </div>

        {entry.kind === 'Video' && entry.videoUrl && <VideoBlock url={entry.videoUrl} />}

        {entry.kind === 'Image' && entry.imageUrl && (
          <img
            src={toolImageSrc(entry.imageUrl)}
            alt={entry.name || t('tools.imageAlt')}
            loading="lazy"
            className="w-full rounded-xl ring-1 ring-ink-200"
          />
        )}
      </CardContent>
    </Card>
  );
}

function VideoBlock({ url }: { url: string }) {
  const { t } = useTranslation();
  const embed = toEmbedUrl(url);

  // Only YouTube and Vimeo are embedded. Anything else is opened in a new tab rather than
  // dropped into an iframe, so an arbitrary URL never renders inside the page.
  if (!embed) {
    return (
      <a
        href={url}
        target="_blank"
        rel="noreferrer noopener"
        className="inline-flex items-center gap-2 font-medium text-primary hover:underline"
      >
        <ExternalLink className="size-4" aria-hidden />
        {t('tools.watch')}
      </a>
    );
  }

  return (
    <div className="aspect-video w-full overflow-hidden rounded-xl bg-ink-950 ring-1 ring-ink-200">
      <iframe
        src={embed}
        title={t('tools.videoTitle')}
        allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
        allowFullScreen
        loading="lazy"
        referrerPolicy="strict-origin-when-cross-origin"
        className="size-full border-0"
      />
    </div>
  );
}
