import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import type { PagedResult, SortParams } from '@/shared/ui/DataTable';

export interface ClientDto {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
  /** The one client whose orders are local. Exactly one client carries this. */
  isLocalOrder: boolean;
  /** How many orders belong to this client — a client with orders is deactivated, not deleted. */
  orderCount: number;
}

export interface UpsertClientBody {
  id?: string;
  code: string;
  name: string;
  isActive: boolean;
  /** Setting this moves the flag here; it cannot be cleared on its own. */
  isLocalOrder: boolean;
}

export interface ClientListParams extends SortParams {
  page?: number;
  pageSize?: number;
  search?: string;
  isActive?: boolean;
}

const clientsKey = (params: ClientListParams) => ['admin', 'clients', params] as const;

/** Paged, searchable list of clients (tenants). */
export function useClients(params: ClientListParams) {
  return useQuery({
    queryKey: clientsKey(params),
    queryFn: () =>
      apiClient.get<PagedResult<ClientDto>>('admin/clients', {
        query: {
          page: params.page ?? 1,
          pageSize: params.pageSize ?? 25,
          sortBy: params.sortBy,
          sortDescending: params.sortDescending,
          search: params.search || undefined,
          isActive: params.isActive,
        },
      }),
  });
}

/** Create or update a client. The API upserts on the presence of an id. */
export function useSaveClient() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: UpsertClientBody) => apiClient.post<ClientDto>('admin/clients', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'clients'] }),
  });
}

/** Outcome of a delete: 0 removed, 1 deactivated because the client still has orders. */
export type DeleteOutcome = 0 | 1;

export function useDeleteClient() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<DeleteOutcome>(`admin/clients/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'clients'] }),
  });
}
