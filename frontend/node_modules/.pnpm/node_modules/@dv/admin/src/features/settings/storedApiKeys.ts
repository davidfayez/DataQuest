import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';

interface StoredApiKeyDto {
  /** The saved key in full, or null when none is saved from the settings page. */
  apiKey: string | null;
}

/**
 * Nested under the settings keys, so a save that refreshes the settings refreshes these too.
 */
export const storedEmailApiKeyKey = ['admin', 'settings', 'email', 'api-key'] as const;
export const storedAiApiKeyKey = ['admin', 'settings', 'ai', 'api-key'] as const;

/**
 * A provider key saved in Settings, in full, so the field can hold it behind the eye button.
 *
 * Only asked for when a key is saved and this admin may change it — the endpoint refuses anyone
 * else. Never kept once the card is gone, so the secret does not linger in the query cache, and
 * not refetched on focus, which would overwrite whatever is being typed into the field.
 */
function useStoredApiKey(queryKey: readonly string[], path: string, enabled: boolean) {
  return useQuery({
    queryKey,
    queryFn: async () => (await apiClient.get<StoredApiKeyDto>(path)).apiKey ?? '',
    enabled,
    gcTime: 0,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}

export function useStoredEmailApiKey(enabled: boolean) {
  return useStoredApiKey(storedEmailApiKeyKey, 'admin/settings/email/api-key', enabled);
}

export function useStoredAiApiKey(enabled: boolean) {
  return useStoredApiKey(storedAiApiKeyKey, 'admin/settings/ai/api-key', enabled);
}
