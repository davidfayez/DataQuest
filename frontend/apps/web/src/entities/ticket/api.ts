import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';

/** One subject the contact form offers, resolved to the page's language by the server. */
export interface TicketCategoryDto {
  id: string;
  name: string;
  nameAr: string;
  nameEn: string;
  sortOrder: number;
  isActive: boolean;
}

/**
 * What comes back once an enquiry lands.
 *
 * Only the reference and the address it was sent from. The form is open to anyone, so echoing the
 * stored record back would let a stranger confirm what the platform holds about an address they
 * merely typed.
 */
export interface TicketSubmittedDto {
  ticketNumber: string;
  email: string;
}

export interface CreateTicketInput {
  ticketCategoryId: string;
  name: string;
  email: string;
  phoneCountryCode: string;
  phoneNumber: string;
  subject: string;
  description: string;
  files: File[];
}

/** Attachment rules, mirrored from the server so the form can refuse a file before uploading it. */
export const TICKET_FILE_RULES = {
  maxBytes: 5 * 1024 * 1024,
  maxFiles: 5,
  accept: '.pdf,.jpg,.jpeg,.png',
  extensions: ['pdf', 'jpg', 'jpeg', 'png'],
} as const;

/** Mirrors the API's ContactEntryKind. */
export enum ContactEntryKind {
  AuthorizedAgent = 0,
  Administration = 1,
}

/** One way to reach the organisation, already resolved to the page's language by the server. */
export interface ContactDirectoryEntryDto {
  id: string;
  kind: ContactEntryKind;
  kindName: string;
  countryName: string | null;
  countryCode: string | null;
  title: string | null;
  address: string | null;
  phone: string | null;
  email: string | null;
}

export interface ContactDirectoryDto {
  authorizedAgents: ContactDirectoryEntryDto[];
  administration: ContactDirectoryEntryDto[];
}

/** The agents and office details an administrator maintains for the contact page. */
export function useContactDirectory() {
  return useQuery({
    queryKey: ['contact-directory'],
    queryFn: () => apiClient.get<ContactDirectoryDto>('content/contact-directory'),
  });
}

export function useTicketCategories() {
  return useQuery({
    queryKey: ['ticket-categories'],
    queryFn: () => apiClient.get<TicketCategoryDto[]>('tickets/categories'),
  });
}

/** Mirrors the API's TicketStatus. Every ticket opens at Pending. */
export enum TicketStatus {
  Pending = 0,
  InProgress = 1,
  Answered = 2,
  Closed = 3,
}

/** The i18n key suffix for a status, so labels live in the locale files rather than here. */
export const ticketStatusKey = (status: TicketStatus): string =>
  ({
    [TicketStatus.Pending]: 'pending',
    [TicketStatus.InProgress]: 'inProgress',
    [TicketStatus.Answered]: 'answered',
    [TicketStatus.Closed]: 'closed',
  })[status];

export interface MyTicketListItemDto {
  id: string;
  ticketNumber: string;
  subject: string;
  categoryName: string;
  status: TicketStatus;
  statusName: string;
  replyCount: number;
  lastReplyAtUtc: string | null;
  createdAtUtc: string;
}

export interface MyTicketFileDto {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface MyTicketDocumentDto {
  id: string;
  title: string;
  description: string | null;
  files: MyTicketFileDto[];
}

/**
 * One message on the thread — from support, or from the applicant themselves.
 *
 * There is no visibility flag here by design: the server only ever sends what this applicant may
 * read, so nothing on this type could describe an internal note. `fromSupport` says which side
 * wrote it, never which person.
 */
export interface MyTicketMessageDto {
  id: string;
  body: string | null;
  fromSupport: boolean;
  createdAtUtc: string;
  /** When a copy also went to their mailbox, or null when it was only published here. */
  emailedAtUtc: string | null;
  documents: MyTicketDocumentDto[];
}

export interface MyTicketDetailsDto {
  id: string;
  ticketNumber: string;
  subject: string;
  description: string;
  categoryName: string;
  status: TicketStatus;
  statusName: string;
  createdAtUtc: string;
  /** False once the ticket is closed, which is the one status that ends the thread. */
  canReply: boolean;
  files: MyTicketFileDto[];
  messages: MyTicketMessageDto[];
}

export function useMyTickets() {
  return useQuery({
    queryKey: ['my-tickets'],
    queryFn: () => apiClient.get<MyTicketListItemDto[]>('tickets/mine'),
  });
}

/** The applicant writing back. Refused once the ticket is closed. */
export function useReplyToMyTicket(ticketId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: { body: string; files: File[] }) => {
      const form = new FormData();
      form.set('Body', input.body);

      for (const file of input.files) {
        form.append('Files', file, file.name);
      }

      return apiClient.upload<MyTicketDetailsDto>(`tickets/mine/${ticketId}/replies`, form);
    },
    onSuccess: (ticket) => {
      // The server returns the redrawn thread, so the page shows its version rather than a guess.
      queryClient.setQueryData(['my-tickets', ticketId], ticket);
      void queryClient.invalidateQueries({ queryKey: ['my-tickets'] });
    },
  });
}

export function useMyTicket(id: string) {
  return useQuery({
    queryKey: ['my-tickets', id],
    queryFn: () => apiClient.get<MyTicketDetailsDto>(`tickets/mine/${id}`),
    enabled: id !== '',
  });
}

/**
 * Fetches an attachment's bytes so it can be shown.
 *
 * The endpoint is authenticated and the access token lives in memory, so a plain `<img src>` would
 * arrive without it and 401 — anything that displays one has to come through here.
 */
export function fetchMyTicketFile(ticketId: string, fileId: string): Promise<Blob> {
  return apiClient.getBlob(`tickets/mine/${ticketId}/files/${fileId}`);
}

/**
 * Downloads an attachment.
 *
 * Fetched through the API client rather than linked: the access token lives in memory, so a plain
 * href would arrive without it and 401.
 */
export async function downloadMyTicketFile(
  ticketId: string,
  file: MyTicketFileDto,
): Promise<void> {
  const blob = await apiClient.getBlob(`tickets/mine/${ticketId}/files/${file.id}`);
  const url = URL.createObjectURL(blob);

  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  document.body.append(link);
  link.click();
  link.remove();

  // Revoked on the next tick: doing it synchronously can beat the click in some browsers.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

export function useCreateTicket() {
  return useMutation({
    mutationFn: (input: CreateTicketInput) => {
      const form = new FormData();
      form.set('TicketCategoryId', input.ticketCategoryId);
      form.set('Name', input.name);
      form.set('Email', input.email);
      form.set('Subject', input.subject);
      form.set('Description', input.description);

      // Only sent when there is a number to go with it: a dial code on its own says nothing, and
      // the server rejects the pair as incomplete.
      if (input.phoneNumber.trim() !== '') {
        form.set('PhoneCountryCode', input.phoneCountryCode);
        form.set('PhoneNumber', input.phoneNumber);
      }

      for (const file of input.files) {
        form.append('Files', file, file.name);
      }

      return apiClient.upload<TicketSubmittedDto>('tickets', form);
    },
  });
}
