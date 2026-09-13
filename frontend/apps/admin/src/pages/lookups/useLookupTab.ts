import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useDeleteLookup,
  useSaveLookup,
  type DeleteOutcome,
  type ListParams,
} from '@/features/lookups/api';

/**
 * The behaviour every lookup tab shares: paging and search state, which record is being edited,
 * the delete confirmation, and the "deactivated instead of deleted" outcome message.
 */
export function useLookupTab<TRow extends { id: string }, TBody>(path: string) {
  const { t } = useTranslation();

  const [params, setParams] = useState<ListParams>({ page: 1 });
  const [editing, setEditing] = useState<TRow | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [toDelete, setToDelete] = useState<TRow | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const save = useSaveLookup<TBody, TRow>(path);
  const remove = useDeleteLookup(path);

  function closeDialog() {
    setEditing(null);
    setIsCreating(false);
    save.reset();
  }

  function submit(body: TBody) {
    save.mutate(body, {
      onSuccess: () => {
        closeDialog();
        setNotice(t('lookups.saved'));
      },
    });
  }

  function confirmDelete() {
    if (!toDelete) return;

    remove.mutate(toDelete.id, {
      onSuccess: (outcome: DeleteOutcome) => {
        setToDelete(null);
        // The API deactivates rather than deletes anything still referenced; say which happened.
        setNotice(outcome === 1 ? t('lookups.deactivated') : t('lookups.deleted'));
      },
    });
  }

  return {
    params,
    setParams,
    setSearch: (search: string) => setParams((p) => ({ ...p, search, page: 1 })),
    setPage: (page: number) => setParams((p) => ({ ...p, page })),
    setPageSize: (pageSize: number) => setParams((p) => ({ ...p, pageSize, page: 1 })),
    // Back to the first page: the row that sorts first is on page one, not wherever the reader
    // happened to be.
    setSort: (sort: { key: string; descending: boolean } | null) =>
      setParams((p) => ({
        ...p,
        sortBy: sort?.key,
        sortDescending: sort?.descending,
        page: 1,
      })),
    editing,
    setEditing,
    isCreating,
    setIsCreating,
    isDialogOpen: isCreating || editing !== null,
    closeDialog,
    submit,
    save,
    toDelete,
    setToDelete,
    confirmDelete,
    remove,
    notice,
    setNotice,
    dismissNotice: () => setNotice(null),
  };
}
