import { ApplicationStatus } from './types';

/**
 * Translation key per status. Kept out of the badge component so that file exports only a
 * component, which is what React Fast Refresh requires to hot-reload it cleanly.
 */
const LABEL_KEYS: Record<ApplicationStatus, string> = {
  [ApplicationStatus.Draft]: 'status.draft',
  [ApplicationStatus.PendingPayment]: 'status.pendingPayment',
  [ApplicationStatus.Pending]: 'status.pending',
  [ApplicationStatus.InProgress]: 'status.inProgress',
  [ApplicationStatus.MissedInfo]: 'status.missedInfo',
  [ApplicationStatus.Success]: 'status.success',
  [ApplicationStatus.Failed]: 'status.failed',
  [ApplicationStatus.Refunded]: 'status.refunded',
};

export function statusLabelKey(status: ApplicationStatus): string {
  return LABEL_KEYS[status] ?? LABEL_KEYS[ApplicationStatus.Draft];
}
