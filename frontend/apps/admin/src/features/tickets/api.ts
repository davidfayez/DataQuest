import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { DeleteOutcome, ListParams } from '@/features/lookups/api';
import { adminKeys, apiClient } from '@/shared/api/client';
import { useLanguage } from '@/shared/lib/useLanguage';
import type { PagedResult } from '@/shared/ui/DataTable';

/** Mirrors the API's TicketStatus. Every ticket opens at Pending. */
export enum TicketStatus {
  Pending = 0,
  InProgress = 1,
  Answered = 2,
  Closed = 3,
}

export const TICKET_STATUSES = [
  TicketStatus.Pending,
  TicketStatus.InProgress,
  TicketStatus.Answered,
  TicketStatus.Closed,
] as const;

/** The i18n key suffix for a status, so the label lives in the locale files rather than here. */
export const ticketStatusKey = (status: TicketStatus): string =>
  ({
    [TicketStatus.Pending]: 'pending',
    [TicketStatus.InProgress]: 'inProgress',
    [TicketStatus.Answered]: 'answered',
    [TicketStatus.Closed]: 'closed',
  })[status];

export interface TicketCategoryDto {
  id: string;
  name: string;
  nameAr: string;
  nameEn: string;
  sortOrder: number;
  isActive: boolean;
}

export interface TicketFileDto {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
}

export interface TicketActionDocumentDto {
  id: string;
  title: string;
  description: string | null;
  files: TicketFileDto[];
}

export interface TicketActionDto {
  id: string;
  body: string | null;
  /** True for a note the sender must never see. The server refuses to email these. */
  isInternal: boolean;
  /** True when the applicant wrote it, so the thread can show both sides. */
  isFromApplicant: boolean;
  changedStatusTo: TicketStatus | null;
  changedStatusToName: string | null;
  authorName: string | null;
  createdAtUtc: string;
  notifiedAtUtc: string | null;
  notifiedEmail: string | null;
  /** Server-computed: public, carries something, and has not been sent yet. */
  canNotify: boolean;
  documents: TicketActionDocumentDto[];
}

export interface TicketListItemDto {
  id: string;
  ticketNumber: string;
  subject: string;
  name: string;
  email: string;
  categoryName: string;
  status: TicketStatus;
  statusName: string;
  assignedToName: string | null;
  orderNumber: string | null;
  applicationNumber: string | null;
  fileCount: number;
  actionCount: number;
  lastRepliedAtUtc: string | null;
  createdAtUtc: string;
}

export interface TicketDetailsDto {
  id: string;
  ticketNumber: string;
  subject: string;
  description: string;
  name: string;
  email: string;
  phoneCountryCode: string | null;
  phoneNumber: string | null;
  fullPhone: string | null;
  languageCode: string;
  ticketCategoryId: string;
  categoryName: string;
  status: TicketStatus;
  statusName: string;
  assignedToAdminUserId: string | null;
  assignedToName: string | null;
  assignedAtUtc: string | null;
  orderId: string | null;
  orderNumber: string | null;
  applicationId: string | null;
  applicationNumber: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  files: TicketFileDto[];
  actions: TicketActionDto[];
}

export interface TicketAssigneeDto {
  id: string;
  fullName: string;
  email: string;
  /** Shown beside the name so a ticket goes to whoever has room for it. */
  openTickets: number;
}

export interface TicketOrderOptionDto {
  id: string;
  orderNumber: string;
  email: string;
}

export interface TicketApplicationOptionDto {
  id: string;
  applicationNumber: string;
  statusName: string;
  addressedTo: string | null;
}

export interface TicketListParams extends ListParams {
  status?: TicketStatus;
  ticketCategoryId?: string;
  assignedToAdminUserId?: string;
  unassigned?: boolean;
}

/** One document being composed on a reply, before it is sent. */
export interface TicketDocumentDraft {
  title: string;
  description: string;
  /** True to keep it with the team's note instead of sending it to the person who wrote in. */
  isInternal: boolean;
  files: File[];
}

function toQuery(params: object): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value));
    }
  }

  return search.toString();
}

// -- The queue ---------------------------------------------------------------

export function useTickets(params: TicketListParams) {
  const language = useLanguage();

  return useQuery({
    queryKey: adminKeys.tickets(params, language),
    queryFn: () =>
      apiClient.get<PagedResult<TicketListItemDto>>(`admin/tickets?${toQuery(params)}`),
  });
}

export function useTicket(id: string) {
  const language = useLanguage();

  return useQuery({
    queryKey: adminKeys.ticket(id, language),
    queryFn: () => apiClient.get<TicketDetailsDto>(`admin/tickets/${id}`),
    enabled: id !== '',
  });
}

export function useTicketAssignees() {
  return useQuery({
    queryKey: adminKeys.ticketAssignees,
    queryFn: () => apiClient.get<TicketAssigneeDto[]>('admin/tickets/assignees'),
  });
}

/**
 * Orders matching what has been typed into the link picker.
 *
 * Enabled only once something has been typed: the picker is a search, not a list of every order
 * the platform has ever taken.
 */
/**
 * Orders the ticket can be linked to.
 *
 * Runs with an empty search as well: the endpoint answers that with the most recent orders, and
 * gating it on typing meant opening the picker showed an empty list, which reads as "there are no
 * orders" rather than "say which one".
 */
export function useTicketOrderSearch(search: string) {
  return useQuery({
    queryKey: adminKeys.ticketOrders(search),
    queryFn: () =>
      apiClient.get<TicketOrderOptionDto[]>(`admin/tickets/orders?${toQuery({ search })}`),
    // Typing replaces the list rather than blanking it while the next answer is in flight.
    placeholderData: (previous) => previous,
  });
}

export function useTicketOrderApplications(orderId: string | null) {
  return useQuery({
    queryKey: adminKeys.ticketOrderApplications(orderId ?? ''),
    queryFn: () =>
      apiClient.get<TicketApplicationOptionDto[]>(
        `admin/tickets/orders/${orderId}/applications`,
      ),
    enabled: Boolean(orderId),
  });
}

/**
 * Every write returns the whole ticket, so one shared success handler keeps the detail page and
 * the queue in step without each mutation having to know what it changed.
 */
function useTicketMutation<TInput>(
  request: (input: TInput) => Promise<TicketDetailsDto>,
  ticketId: string,
) {
  const client = useQueryClient();
  const language = useLanguage();

  return useMutation({
    mutationFn: request,
    onSuccess: (ticket) => {
      client.setQueryData(adminKeys.ticket(ticketId, language), ticket);
      void client.invalidateQueries({ queryKey: ['admin', 'tickets'] });
    },
  });
}

export function useAssignTicket(ticketId: string) {
  return useTicketMutation(
    (adminUserId: string | null) =>
      apiClient.post<TicketDetailsDto>(`admin/tickets/${ticketId}/assign`, { adminUserId }),
    ticketId,
  );
}

export function useLinkTicket(ticketId: string) {
  return useTicketMutation(
    (input: { orderId: string | null; applicationId: string | null }) =>
      apiClient.post<TicketDetailsDto>(`admin/tickets/${ticketId}/link`, input),
    ticketId,
  );
}

export function useChangeTicketStatus(ticketId: string) {
  return useTicketMutation(
    (status: TicketStatus) =>
      apiClient.post<TicketDetailsDto>(`admin/tickets/${ticketId}/status`, { status }),
    ticketId,
  );
}

/**
 * One submission may carry both audiences.
 *
 * The server splits it into one action per audience, so an internal note stays a row the
 * applicant's queries never select rather than a field on a shared row.
 */
export interface CreateTicketActionInput {
  /** What the applicant reads. Visible in their account as soon as it is saved. */
  replyToSender: string;
  /** What the team reads. Never shown to the applicant, never emailed. */
  internalNote: string;
  changeStatusTo: TicketStatus | null;
  documents: TicketDocumentDraft[];
}

export function useCreateTicketAction(ticketId: string) {
  return useTicketMutation((input: CreateTicketActionInput) => {
    const form = new FormData();
    form.set('ReplyToSender', input.replyToSender);
    form.set('InternalNote', input.internalNote);

    if (input.changeStatusTo !== null) {
      form.set('ChangeStatusTo', String(input.changeStatusTo));
    }

    // Indexed names, because the server binds a list of documents each carrying its own files.
    input.documents.forEach((document, index) => {
      form.set(`Documents[${index}].Title`, document.title);
      form.set(`Documents[${index}].Description`, document.description);
      form.set(`Documents[${index}].IsInternal`, String(document.isInternal));

      for (const file of document.files) {
        form.append(`Documents[${index}].Files`, file, file.name);
      }
    });

    return apiClient.upload<TicketDetailsDto>(`admin/tickets/${ticketId}/actions`, form);
  }, ticketId);
}

export function useNotifyTicketAction(ticketId: string) {
  return useTicketMutation(
    (actionId: string) =>
      apiClient.post<TicketDetailsDto>(`admin/tickets/${ticketId}/actions/${actionId}/notify`),
    ticketId,
  );
}

/**
 * Fetches an attachment's bytes.
 *
 * Attachments sit behind an authenticated endpoint and the access token lives in memory, so a
 * plain `<img src>` would arrive without it and 401. Anything that wants to show one has to go
 * through here and render an object URL instead.
 */
export function fetchTicketFile(ticketId: string, fileId: string): Promise<Blob> {
  return apiClient.getBlob(`admin/tickets/${ticketId}/files/${fileId}`);
}

/** True for the types the browser can render inline; everything else stays a download. */
export const isPreviewable = (contentType: string): boolean =>
  contentType.startsWith('image/');

/** Attachments carry the bearer token, so they are fetched and handed over as an object URL. */
export async function downloadTicketFile(
  ticketId: string,
  file: TicketFileDto,
): Promise<void> {
  const blob = await apiClient.getBlob(`admin/tickets/${ticketId}/files/${file.id}`);
  const url = URL.createObjectURL(blob);

  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  document.body.append(link);
  link.click();
  link.remove();

  // Revoked on the next tick: revoking synchronously can beat the click in some browsers.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

// -- Categories --------------------------------------------------------------

export function useTicketCategories(params: ListParams) {
  const language = useLanguage();

  return useQuery({
    queryKey: adminKeys.ticketCategories(params, language),
    queryFn: () =>
      apiClient.get<PagedResult<TicketCategoryDto>>(`admin/ticket-categories?${toQuery(params)}`),
  });
}

export interface UpsertTicketCategoryInput {
  id?: string;
  nameAr: string;
  nameEn: string;
  sortOrder: number;
  isActive: boolean;
}

export function useSaveTicketCategory() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input: UpsertTicketCategoryInput) =>
      apiClient.post<TicketCategoryDto>('admin/ticket-categories', input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['admin', 'ticket-categories'] }),
  });
}

export function useDeleteTicketCategory() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete<DeleteOutcome>(`admin/ticket-categories/${id}`),
    onSuccess: () => client.invalidateQueries({ queryKey: ['admin', 'ticket-categories'] }),
  });
}
