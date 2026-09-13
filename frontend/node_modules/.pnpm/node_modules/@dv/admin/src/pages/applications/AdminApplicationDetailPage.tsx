import {
  Alert,
  Button,
  Card,
  CardContent,
  Field,
  Select,
  LoadingState,
  Spinner,
  cn,
} from '@dv/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Lock, Upload } from 'lucide-react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { Permissions } from '@/features/auth/session';
import { usePermission } from '@/features/auth/useAdminSession';
import { adminKeys, apiClient } from '@/shared/api/client';
import {
  formatCalendarDate,
  formatDateTime,
  formatFileSize,
  formatNumber,
} from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { useLanguage } from '@/shared/lib/useLanguage';
import {
  AttachedDocumentsEditor,
  DOCUMENT_LIMITS,
  appendDocumentsToForm,
  countFiles,
  type AttachedDocumentInput,
} from './AttachedDocumentsEditor';
import { FilePreview } from './FilePreview';

interface Comment {
  id: string;
  authorType: number;
  authorName: string | null;
  visibility: number;
  body: string;
  createdAtUtc: string;
}

interface AdminFile {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  kind: number;
  requiredFileName: string | null;
  uploadedAtUtc: string;
  downloadUrl: string;
  /**
   * What to call the file once it is saved: application, order and the document it satisfies.
   * The blob download names itself, so this is the name that actually reaches the disk.
   */
  downloadName: string;
}

interface AdminDocumentField {
  id: string;
  name: string;
  fieldType: number;
  isRequired: boolean;
  value: string | null;
  displayValue: string | null;
}

interface AdminDocument {
  id: string;
  name: string;
  isVisibleToApplicant: boolean;
  attachedAtStatus: number | null;
  uploadedByName: string | null;
  attachedAtUtc: string;
  files: AdminFile[];
  fields: AdminDocumentField[];
}

interface AdminApplicationDetails {
  id: string;
  applicationNumber: string;
  addressedTo: string;
  birthDate: string;
  status: number;
  statusName: string;
  isPaid: boolean;
  totalCost: number;
  currencyCode: string;
  orderNumber: string;
  orderEmail: string;
  orderId: string;
  createdAtUtc: string;
  transactionTypeName: string;
  subTransactionTypeName: string;
  authorityName: string;
  names: Array<{ languageType: number; firstName: string; middleName: string | null; lastName: string }>;
  services: Array<{ id: string; serviceName: string; quantity: number; isExpress: boolean; lineTotal: number }>;
  files: AdminFile[];
  documents: AdminDocument[];
  comments: Comment[];
}

const STATUS = {
  Draft: 0,
  PendingPayment: 1,
  Pending: 2,
  InProgress: 3,
  MissedInfo: 4,
  Success: 5,
  Failed: 6,
  Refunded: 7,
} as const;

const STATUS_KEY: Record<number, string> = {
  0: 'draft',
  1: 'pendingPayment',
  2: 'pending',
  3: 'inProgress',
  4: 'missedInfo',
  5: 'success',
  6: 'failed',
  7: 'refunded',
};

// The statuses a reviewer may move to from each current status, mirroring the domain state machine
// so the panel never offers a transition the server would refuse.
const NEXT_STATUSES: Record<number, number[]> = {
  [STATUS.Pending]: [STATUS.InProgress],
  [STATUS.InProgress]: [STATUS.MissedInfo, STATUS.Success, STATUS.Failed],
  [STATUS.MissedInfo]: [STATUS.InProgress],
};

/**
 * Steps that belong to the applicant, offered only to an admin holding
 * Applications.OverrideStatus. Submitting a draft still fails if documents are missing, and
 * moving to Pending marks the application paid without charging anything — which is why it is a
 * separate grant rather than part of the review flow.
 *
 * Refunded is absent by design: it is reached through the Refund action below, which credits the
 * wallet. A bare status change would mark it refunded and keep the money.
 */
const OVERRIDE_NEXT_STATUSES: Record<number, number[]> = {
  [STATUS.Draft]: [STATUS.PendingPayment],
  [STATUS.PendingPayment]: [STATUS.Pending, STATUS.Draft],
};

type Tab = 'details' | 'files' | 'comments';

export function AdminApplicationDetailPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const lang = useLanguage();
  const { id = '' } = useParams<{ id: string }>();
  const toMessage = useApiErrorMessage();
  const queryClient = useQueryClient();

  const canReview = usePermission(Permissions.ApplicationsReview);
  const canAttach = usePermission(Permissions.ApplicationsAttachResults);
  const canOverride = usePermission(Permissions.ApplicationsOverrideStatus);
  const canRefund = usePermission(Permissions.OrdersRefund);

  const [tab, setTab] = useState<Tab>('details');
  const [targetStatus, setTargetStatus] = useState<number | null>(null);
  const [statusUserComment, setStatusUserComment] = useState('');
  const [statusInternalComment, setStatusInternalComment] = useState('');
  const [documents, setDocuments] = useState<AttachedDocumentInput[]>([]);
  const [commentBody, setCommentBody] = useState('');
  const [isInternal, setIsInternal] = useState(false);
  const [preview, setPreview] = useState<AdminFile | null>(null);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const resultInput = useRef<HTMLInputElement>(null);

  const application = useQuery({
    queryKey: adminKeys.application(id, lang),
    queryFn: () => apiClient.get<AdminApplicationDetails>(`admin/applications/${id}`),
  });

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['admin', 'applications'] });
  }

  /** Authenticated download — a plain anchor cannot send the in-memory access token. */
  async function downloadFile(file: AdminFile) {
    setDownloadError(null);
    try {
      const blob = await apiClient.getBlob(`admin/applications/${id}/files/${file.id}`);
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = file.downloadName;
      // The anchor must be in the document for the click to trigger a download in Firefox, and the
      // object URL must outlive the click — revoking it synchronously aborts the download in
      // Chromium, which is why the revoke is deferred rather than run on the next line.
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } catch (error) {
      setDownloadError(toMessage(error));
    }
  }

  /**
   * One request carries the transition, both notes and any attached documents — so a rejected
   * transition cannot leave documents behind, and an accepted one cannot lose them. Without
   * documents it stays the plain JSON post it has always been.
   */
  const changeStatus = useMutation({
    mutationFn: (toStatus: number) => {
      const userComment = statusUserComment.trim();
      const internalComment = statusInternalComment.trim();

      if (documents.length === 0) {
        return apiClient.post(`admin/applications/${id}/status`, {
          toStatus,
          userComment: userComment || null,
          internalComment: internalComment || null,
        });
      }

      const form = new FormData();
      form.append('ToStatus', String(toStatus));
      if (userComment) form.append('UserComment', userComment);
      if (internalComment) form.append('InternalComment', internalComment);
      appendDocumentsToForm(form, documents);

      return apiClient.upload(`admin/applications/${id}/status`, form);
    },
    onSuccess: () => {
      setStatusUserComment('');
      setStatusInternalComment('');
      setDocuments([]);
      setTargetStatus(null);
      invalidate();
    },
  });

  const addComment = useMutation({
    mutationFn: () =>
      apiClient.post(`admin/applications/${id}/comments`, {
        body: commentBody.trim(),
        visibility: isInternal ? 1 : 0,
      }),
    onSuccess: () => {
      setCommentBody('');
      invalidate();
    },
  });

  // Files the review card's notes and documents when there is no status change to carry them —
  // e.g. on a completed application. Each non-empty note is filed with its own visibility.
  const postReviewComments = useMutation({
    mutationFn: async () => {
      // Documents go first: a refused upload should not leave a comment about it already posted.
      const attachedDocuments = documents.length;
      if (attachedDocuments > 0) {
        const form = new FormData();
        appendDocumentsToForm(form, documents);
        await apiClient.upload(`admin/applications/${id}/documents`, form);
      }

      const user = statusUserComment.trim();
      const internal = statusInternalComment.trim();
      if (user) {
        await apiClient.post(`admin/applications/${id}/comments`, { body: user, visibility: 0 });
      }
      if (internal) {
        await apiClient.post(`admin/applications/${id}/comments`, { body: internal, visibility: 1 });
      }

      // Reported back so the confirmation says what actually happened — "comments posted" would be
      // a lie when only documents were filed.
      return { attachedDocuments, postedComments: (user ? 1 : 0) + (internal ? 1 : 0) };
    },
    onSuccess: () => {
      setStatusUserComment('');
      setStatusInternalComment('');
      setDocuments([]);
      invalidate();
    },
  });

  /**
   * Returns the money and moves the application to Refunded, in one server-side transaction.
   * Deliberately not a status option: setting Refunded directly would mark it refunded and leave
   * the applicant's balance untouched.
   */
  const refund = useMutation({
    mutationFn: (orderId: string) =>
      apiClient.post(`admin/orders/${orderId}/applications/${id}/refund`, { note: null }),
    onSuccess: () => {
      setStatusUserComment('');
      setStatusInternalComment('');
      invalidate();
    },
  });

  const uploadResult = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append('file', file);
      return apiClient.upload(`admin/applications/${id}/results`, form);
    },
    onSuccess: invalidate,
  });

  if (application.isPending) return <LoadingState label={t('common.loading')} />;

  const details = application.data!;
  const status = details.status;

  // Offered transitions mirror the domain state machine, so the panel never presents an action
  // the server would refuse. The applicant's own steps are appended only for an admin holding
  // the override grant; the server checks the same permission regardless of what is rendered.
  const nextStatuses = [
    ...(NEXT_STATUSES[status] ?? []),
    ...(canOverride ? OVERRIDE_NEXT_STATUSES[status] ?? [] : []),
  ];
  const selectedStatus = targetStatus ?? nextStatuses[0] ?? null;
  const canChangeStatus = nextStatuses.length > 0;

  // Refunding is a wallet operation, not a status edit — it returns the money and then moves the
  // application. Only offered where the domain allows it: paid, and not yet started.
  const canRefundNow = canRefund && status === STATUS.Pending;

  const hasReviewComment =
    statusUserComment.trim() !== '' || statusInternalComment.trim() !== '';

  /**
   * The same conditions the server enforces, checked here so a half-filled document is caught
   * before the files are uploaded rather than after. Returns the message to show, or null.
   */
  const documentsProblem = (() => {
    if (documents.length === 0) return null;

    if (documents.some((doc) => !doc.nameAr.trim() || !doc.nameEn.trim())) {
      return t('applications.documents.errors.nameRequired');
    }
    if (documents.some((doc) => doc.files.length === 0)) {
      return t('applications.documents.errors.fileRequired');
    }
    if (documents.some((doc) => doc.files.some((f) => f.size > DOCUMENT_LIMITS.maxFileSizeBytes))) {
      return t('applications.documents.errors.fileTooLarge');
    }
    if (countFiles(documents) > DOCUMENT_LIMITS.maxFiles) {
      return t('applications.documents.errors.tooManyFiles', { max: DOCUMENT_LIMITS.maxFiles });
    }
    if (
      documents.some((doc) =>
        doc.fields.some((field) => !field.nameAr.trim() || !field.nameEn.trim()),
      )
    ) {
      return t('applications.documents.errors.fieldNameRequired');
    }
    if (
      documents.some((doc) =>
        doc.fields.some((field) => field.isRequired && field.value.trim() === ''),
      )
    ) {
      return t('applications.documents.errors.fieldValueRequired');
    }
    if (
      documents.some((doc) =>
        doc.fields.some(
          (field) => field.fieldType === 3 && field.options.every((o) => !o.value.trim()),
        ),
      )
    ) {
      return t('applications.documents.errors.optionRequired');
    }

    return null;
  })();
  const reviewPending = changeStatus.isPending || postReviewComments.isPending;
  const reviewError = changeStatus.isError
    ? toMessage(changeStatus.error)
    : postReviewComments.isError
      ? toMessage(postReviewComments.error)
      : refund.isError
        ? toMessage(refund.error)
        : null;
  const reviewSuccess = changeStatus.isSuccess
    ? t('applications.statusChanged')
    : postReviewComments.isSuccess
      ? postReviewComments.data.postedComments === 0
        ? t('applications.documents.attached')
        : t('applications.commentsPosted')
      : refund.isSuccess
        ? t('applications.refunded')
        : null;

  // With a status change selected, the transition carries both notes and emails the applicant;
  // otherwise the notes are posted on their own so a reviewer can always comment.
  function submitReview() {
    if (canChangeStatus && selectedStatus !== null) {
      changeStatus.mutate(selectedStatus);
    } else {
      postReviewComments.mutate();
    }
  }

  return (
    <div className="space-y-6">
      <Link
        to="/applications"
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
        {t('applications.title')}
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="font-mono text-xl font-semibold" dir="ltr">
            {details.applicationNumber}
          </h1>
          <p className="text-sm text-muted-foreground">{details.addressedTo}</p>
          <Link
            to={`/orders/${details.orderId}`}
            className="font-mono text-xs text-primary hover:underline"
            dir="ltr"
          >
            {details.orderNumber}
          </Link>
        </div>

        <div className="text-end">
          <p className="font-medium" data-testid="admin-status">
            {t(`status.${STATUS_KEY[status] ?? 'draft'}`)}
          </p>
          <p className="text-lg font-semibold">
            {formatNumber(details.totalCost, locale)} {details.currencyCode}
          </p>
        </div>
      </div>

      {canReview && (
        <Card>
          <CardContent className="space-y-3 p-5" data-testid="review-panel">
            <h2 className="font-medium">{t('applications.reviewActions')}</h2>

            {reviewError ? <Alert variant="error">{reviewError}</Alert> : null}
            {reviewSuccess ? <Alert variant="success">{reviewSuccess}</Alert> : null}

            {canChangeStatus ? (
              <Field label={t('applications.newStatus')} htmlFor="new-status">
                <Select
                  id="new-status"
                  data-testid="status-select"
                  value={selectedStatus === null ? '' : String(selectedStatus)}
                  onChange={(e) => setTargetStatus(Number(e.target.value))}
                >
                  {nextStatuses.map((next) => (
                    <option key={next} value={next}>
                      {t(`status.${STATUS_KEY[next]}`)}
                    </option>
                  ))}
                </Select>
              </Field>
            ) : (
              <p className="rounded-lg bg-muted px-3 py-2 text-sm text-muted-foreground">
                {t('applications.noStatusChange')}
              </p>
            )}

            <Field label={t('applications.commentToUser')} htmlFor="status-user-comment">
              <textarea
                id="status-user-comment"
                rows={2}
                value={statusUserComment}
                onChange={(event) => setStatusUserComment(event.target.value)}
                placeholder={t('applications.commentToUserHint')}
                data-testid="status-user-comment"
                className="w-full rounded-lg border border-border bg-background p-3 text-sm focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              />
            </Field>

            <Field label={t('applications.commentToAdmins')} htmlFor="status-internal-comment">
              <textarea
                id="status-internal-comment"
                rows={2}
                value={statusInternalComment}
                onChange={(event) => setStatusInternalComment(event.target.value)}
                placeholder={t('applications.commentToAdminsHint')}
                data-testid="status-internal-comment"
                className="w-full rounded-lg border border-warning/50 bg-warning/10 p-3 text-sm focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              />
            </Field>

            <div className="border-t border-border pt-4">
              <AttachedDocumentsEditor
                documents={documents}
                onChange={setDocuments}
                disabled={reviewPending}
              />
            </div>

            {documentsProblem ? <Alert variant="error">{documentsProblem}</Alert> : null}

            <p className="text-xs text-muted-foreground">
              {canChangeStatus ? t('applications.statusEmailHint') : t('applications.commentsHint')}
            </p>

            <div className="flex flex-wrap items-center gap-2">
              <Button
                disabled={
                  reviewPending ||
                  documentsProblem !== null ||
                  (!canChangeStatus && !hasReviewComment && documents.length === 0)
                }
                onClick={submitReview}
                data-testid="apply-status"
              >
                {reviewPending && <Spinner />}
                {canChangeStatus
                  ? t('applications.updateStatus')
                  : hasReviewComment
                    ? t('applications.postComments')
                    : t('applications.documents.attachAction')}
              </Button>

              {/* Separate from the status control on purpose: this moves money as well as state. */}
              {canRefundNow && (
                <Button
                  variant="destructive"
                  disabled={refund.isPending}
                  onClick={() => refund.mutate(details.orderId)}
                  data-testid="refund-application"
                >
                  {refund.isPending && <Spinner />}
                  {t('applications.refundToWallet')}
                </Button>
              )}
            </div>

            {/* Says plainly that the wallet is not involved, so nobody mistakes it for a payment. */}
            {canOverride && OVERRIDE_NEXT_STATUSES[status] !== undefined && (
              <p className="text-xs text-warning">{t('applications.overrideHint')}</p>
            )}
          </CardContent>
        </Card>
      )}

      {canAttach && status === STATUS.Success && (
        <Card data-testid="result-uploader">
          <CardContent className="space-y-3 p-5">
            <h2 className="font-medium">{t('applications.results')}</h2>

            {uploadResult.isError ? (
              <Alert variant="error">{toMessage(uploadResult.error)}</Alert>
            ) : null}
            {uploadResult.isSuccess ? (
              <Alert variant="success">{t('applications.resultUploaded')}</Alert>
            ) : null}

            <input
              ref={resultInput}
              type="file"
              className="sr-only"
              accept=".pdf,.jpg,.jpeg,.png"
              onChange={(event) => {
                const file = event.target.files?.[0];
                if (file) uploadResult.mutate(file);
                event.target.value = '';
              }}
            />

            <Button
              disabled={uploadResult.isPending}
              onClick={() => resultInput.current?.click()}
              data-testid="upload-result"
            >
              {uploadResult.isPending ? <Spinner /> : <Upload className="size-4" aria-hidden="true" />}
              {t('applications.uploadResult')}
            </Button>
          </CardContent>
        </Card>
      )}

      <div role="tablist" className="flex flex-wrap gap-1 border-b border-border">
        {(['details', 'files', 'comments'] as Tab[]).map((name) => (
          <button
            key={name}
            role="tab"
            type="button"
            aria-selected={tab === name}
            onClick={() => setTab(name)}
            data-testid={`admin-tab-${name}`}
            className={cn(
              '-mb-px border-b-2 px-4 py-2 text-sm transition-colors',
              tab === name
                ? 'border-primary font-medium text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground',
            )}
          >
            {t(`applications.tabs.${name}`)}
          </button>
        ))}
      </div>

      {tab === 'details' && (
        <Card>
          <CardContent className="space-y-4 p-5">
            <dl className="space-y-2">
              {[
                [t('applications.applicantName'), details.names.map((n) => `${n.firstName} ${n.lastName}`).join(' / ')],
                [t('applications.birthDate'), formatCalendarDate(details.birthDate, locale)],
                [t('applications.applicantEmail'), details.orderEmail],
                [t('lookups.transactionType'), details.transactionTypeName],
                [t('lookups.subTransactionTypes'), details.subTransactionTypeName],
                [t('applications.authority'), details.authorityName],
                [t('applications.created'), formatDateTime(details.createdAtUtc, locale)],
              ].map(([label, value]) => (
                <div key={label} className="flex flex-wrap justify-between gap-2 border-b border-border py-2 last:border-0">
                  <dt className="text-sm text-muted-foreground">{label}</dt>
                  <dd className="text-sm font-medium">{value}</dd>
                </div>
              ))}
            </dl>

            <div>
              <h3 className="mb-2 font-medium">{t('applications.services')}</h3>
              <ul className="space-y-1 text-sm">
                {details.services.map((service) => (
                  <li key={service.id} className="flex justify-between gap-3">
                    <span>
                      {service.serviceName} × {service.quantity}
                      {service.isExpress ? ' ⚡' : ''}
                    </span>
                    <span>{formatNumber(service.lineTotal, locale)}</span>
                  </li>
                ))}
              </ul>
            </div>
          </CardContent>
        </Card>
      )}

      {tab === 'files' && (
        <div className="space-y-4">
          <Card>
            <CardContent className="p-5">
              {downloadError ? (
                <Alert variant="error" className="mb-3">
                  {downloadError}
                </Alert>
              ) : null}
              {details.files.length === 0 ? (
                <p className="text-center text-sm text-muted-foreground">
                  {t('applications.noFiles')}
                </p>
              ) : (
                <ul className="space-y-2">
                  {details.files.map((file) => (
                    <li
                      key={file.id}
                      className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border p-3"
                    >
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium">{file.fileName}</p>
                        <p className="text-xs text-muted-foreground">
                          {file.requiredFileName ? `${file.requiredFileName} · ` : ''}
                          {formatFileSize(file.sizeBytes, locale)} ·{' '}
                          {formatDateTime(file.uploadedAtUtc, locale)}
                        </p>
                      </div>

                      <div className="flex gap-2">
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => setPreview(file)}
                          data-testid={`preview-${file.fileName}`}
                        >
                          {t('applications.preview')}
                        </Button>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => void downloadFile(file)}
                          data-testid={`download-${file.fileName}`}
                        >
                          {t('applications.download')}
                        </Button>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>

          {/* Documents the review team attached, each with the details recorded beside it. A
              withheld one is amber and padlocked, exactly as an internal comment is. */}
          {details.documents.length > 0 && (
            <Card data-testid="attached-documents">
              <CardContent className="space-y-3 p-5">
                <h2 className="font-medium">{t('applications.documents.attachedTitle')}</h2>

                {details.documents.map((document) => (
                  <div
                    key={document.id}
                    data-testid={
                      document.isVisibleToApplicant ? 'document-visible' : 'document-internal'
                    }
                    className={cn(
                      'space-y-3 rounded-xl border p-4',
                      document.isVisibleToApplicant
                        ? 'border-border'
                        : 'border-warning/50 bg-warning/10',
                    )}
                  >
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div>
                        <p className="font-medium">{document.name}</p>
                        <p className="text-xs text-muted-foreground">
                          {document.uploadedByName ?? '—'} ·{' '}
                          {formatDateTime(document.attachedAtUtc, locale)}
                          {document.attachedAtStatus !== null
                            ? ` · ${t(`status.${STATUS_KEY[document.attachedAtStatus] ?? 'draft'}`)}`
                            : ''}
                        </p>
                      </div>

                      <span className="flex items-center gap-1.5 text-xs font-medium">
                        {document.isVisibleToApplicant ? (
                          t('applications.documents.visible')
                        ) : (
                          <>
                            <Lock className="size-3.5 text-warning" aria-hidden="true" />
                            {t('applications.documents.internal')}
                          </>
                        )}
                      </span>
                    </div>

                    {document.fields.length > 0 && (
                      <dl className="grid gap-x-6 gap-y-1 sm:grid-cols-2">
                        {document.fields.map((field) => (
                          <div key={field.id} className="flex justify-between gap-3 text-sm">
                            <dt className="text-muted-foreground">{field.name}</dt>
                            <dd className="font-medium">{field.displayValue || '—'}</dd>
                          </div>
                        ))}
                      </dl>
                    )}

                    <ul className="space-y-2">
                      {document.files.map((file) => (
                        <li
                          key={file.id}
                          className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-background p-3"
                        >
                          <div className="min-w-0">
                            <p className="truncate text-sm font-medium">{file.fileName}</p>
                            <p className="text-xs text-muted-foreground">
                              {formatFileSize(file.sizeBytes, locale)} ·{' '}
                              {formatDateTime(file.uploadedAtUtc, locale)}
                            </p>
                          </div>

                          <div className="flex gap-2">
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => setPreview(file)}
                              data-testid={`preview-${file.fileName}`}
                            >
                              {t('applications.preview')}
                            </Button>
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => void downloadFile(file)}
                              data-testid={`download-${file.fileName}`}
                            >
                              {t('applications.download')}
                            </Button>
                          </div>
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </CardContent>
            </Card>
          )}
        </div>
      )}

      {tab === 'comments' && (
        <Card>
          <CardContent className="space-y-4 p-5">
            {details.comments.length === 0 ? (
              <p className="text-center text-sm text-muted-foreground">
                {t('applications.noComments')}
              </p>
            ) : (
              <ul className="space-y-3">
                {details.comments.map((comment) => {
                  const internal = comment.visibility === 1;

                  return (
                    <li
                      key={comment.id}
                      data-testid={internal ? 'comment-internal' : 'comment-foruser'}
                      className={cn(
                        'rounded-lg border p-3 text-sm',
                        // Internal notes are unmistakable: amber, bordered and padlocked.
                        internal
                          ? 'border-warning/50 bg-warning/10'
                          : 'border-border bg-background',
                      )}
                    >
                      <div className="mb-1 flex items-center gap-2 text-xs text-muted-foreground">
                        {internal && <Lock className="size-3.5 text-warning" aria-hidden="true" />}
                        <span className="font-medium">
                          {internal ? t('applications.internal') : t('applications.forUser')}
                        </span>
                        <span>·</span>
                        <span>{comment.authorName ?? '—'}</span>
                        <span>·</span>
                        <span>{formatDateTime(comment.createdAtUtc, locale)}</span>
                      </div>
                      <p className="whitespace-pre-wrap">{comment.body}</p>
                    </li>
                  );
                })}
              </ul>
            )}

            {canReview && (
              <div className="space-y-3 border-t border-border pt-4">
                {addComment.isError ? (
                  <Alert variant="error">{toMessage(addComment.error)}</Alert>
                ) : null}

                <label htmlFor="comment" className="sr-only">
                  {t('applications.commentPlaceholder')}
                </label>
                <textarea
                  id="comment"
                  rows={3}
                  value={commentBody}
                  onChange={(event) => setCommentBody(event.target.value)}
                  placeholder={t('applications.commentPlaceholder')}
                  data-testid="comment-box"
                  className="w-full rounded-lg border border-border bg-background p-3 text-sm focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                />

                <div className="flex flex-wrap items-center justify-between gap-3">
                  <fieldset className="flex items-center gap-3">
                    <legend className="sr-only">{t('applications.visibility')}</legend>

                    <label className="flex items-center gap-2 text-sm">
                      <input
                        type="radio"
                        name="visibility"
                        checked={!isInternal}
                        onChange={() => setIsInternal(false)}
                        data-testid="visibility-foruser"
                      />
                      {t('applications.forUser')}
                    </label>

                    <label className="flex items-center gap-2 text-sm">
                      <input
                        type="radio"
                        name="visibility"
                        checked={isInternal}
                        onChange={() => setIsInternal(true)}
                        data-testid="visibility-internal"
                      />
                      <Lock className="size-3.5 text-warning" aria-hidden="true" />
                      {t('applications.internal')}
                    </label>
                  </fieldset>

                  <Button
                    disabled={!commentBody.trim() || addComment.isPending}
                    onClick={() => addComment.mutate()}
                    data-testid="post-comment"
                  >
                    {addComment.isPending && <Spinner />}
                    {t('applications.postComment')}
                  </Button>
                </div>

                <p className="text-xs text-muted-foreground">
                  {isInternal ? t('applications.internalHint') : t('applications.forUserWarning')}
                </p>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      <FilePreview
        file={preview}
        applicationId={id}
        onClose={() => setPreview(null)}
      />
    </div>
  );
}
