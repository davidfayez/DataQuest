/**
 * The organisation's contact points, mirroring the footer on itep.nen-global.org — the same
 * company, so the same channels. Kept in one place because these are the lines most likely to
 * need editing, and none of them belong in a component.
 */
export const CONTACT_EMAIL = 'itep@nen-global.org';

export const ORGANISATION_URL = 'https://nen-global.org';
export const ORGANISATION_NAME = 'NEN | National Education Network';

/** The year the organisation dates its copyright from. */
export const COPYRIGHT_FROM = 2004;

/*
 * The "Follow us" and "Message us" rows used to be two hard-coded arrays here. They are rows in the
 * database now, edited under Content → Footer channels in the admin panel, and fetched by
 * `useFooterChannels`. The icon and brand colour for each platform stayed in SiteFooter, because
 * artwork is not something an operator should have to supply.
 */

/*
 * The Organisation column was a hard-coded array here. All three footer columns are rows in the
 * database now, edited under Footer → Links in the admin panel, and fetched by `useFooterContent`.
 */
