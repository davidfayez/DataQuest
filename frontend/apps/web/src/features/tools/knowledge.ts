import { useMutation } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/shared/api/client';

/** One tool or guide that matched a search. */
export interface KnowledgeHit {
  id: string;
  kind: string;
  name: string;
  /** The words around the match, so a reader can see why it came back. */
  snippet: string;
  videoUrl: string | null;
  imageUrl: string | null;
}

/** One page on the open web the answer drew on. */
export interface AiWebSource {
  title: string;
  url: string;
}

export interface AiAnswer {
  answer: string;
  /** False when no API key has been configured, which the page explains rather than hiding. */
  isConfigured: boolean;
  /** The platform's own guides it used. */
  sources: KnowledgeHit[];
  /** Pages on the web it used, when the guides did not cover the question. */
  webSources: AiWebSource[];
}

/**
 * The two searches behind the box, as mutations rather than queries: nothing runs until somebody
 * presses a button, and the answer belongs to that press rather than to a cache key.
 */
export function useKnowledgeSearch() {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';

  return useMutation({
    mutationFn: (query: string) =>
      apiClient.get<KnowledgeHit[]>('content/knowledge/search', {
        query: { q: query },
        language,
      }),
  });
}

export function useAskAi() {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';

  return useMutation({
    mutationFn: (question: string) =>
      apiClient.post<AiAnswer>('content/knowledge/ask', { question }, { language }),
  });
}
