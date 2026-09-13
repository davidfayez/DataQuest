import { useQuery } from '@tanstack/react-query';
import { apiClient, queryKeys } from '@/shared/api/client';
import type { PagedResult } from '../wallet/api';
import type { ApplicationStatus } from './types';
import type { ActorType } from './timelineApi';

export interface ApplicationStatusLogEntryDto {
  id: string;
  fromStatus: ApplicationStatus;
  fromStatusName: string;
  toStatus: ApplicationStatus;
  toStatusName: string;
  changedByType: ActorType;
  changedByTypeName: string;
  changedByName: string | null;
  note: string | null;
  createdAtUtc: string;
}

export interface ApplicationChangeLogEntryDto {
  id: string;
  /** Stable identifier such as `Application.StatusChanged`; localized at the edge. */
  action: string;
  actorType: ActorType;
  actorTypeName: string;
  actorName: string | null;
  /** Curated key/value pairs from the audit payload — never the raw stored JSON. */
  details: Record<string, string>;
  createdAtUtc: string;
}

export const CHANGE_LOG_PAGE_SIZE = 20;

export function useApplicationStatusLog(applicationId: string) {
  return useQuery({
    queryKey: queryKeys.statusLog(applicationId),
    queryFn: () =>
      apiClient.get<ApplicationStatusLogEntryDto[]>(`applications/${applicationId}/status-log`),
  });
}

export function useApplicationChangeLog(applicationId: string, page = 1) {
  return useQuery({
    queryKey: queryKeys.changeLog(applicationId, page),
    queryFn: () =>
      apiClient.get<PagedResult<ApplicationChangeLogEntryDto>>(
        `applications/${applicationId}/change-log`,
        { query: { page, pageSize: CHANGE_LOG_PAGE_SIZE } },
      ),
    // Paging through history should not blank the list between pages.
    placeholderData: (previous) => previous,
  });
}
