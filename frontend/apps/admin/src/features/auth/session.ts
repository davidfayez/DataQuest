/**
 * Admin session state.
 *
 * As in the applicant app, the access token is held in memory only so an XSS payload cannot read
 * it back out of persistent storage. Permissions ride on the token and are mirrored here purely
 * to decide which navigation items to render — the server enforces every one of them regardless.
 */

export interface AdminSession {
  accessToken: string;
  expiresAtUtc: string;
  adminUserId: string;
  fullName: string;
  email: string;
  username: string;
  permissions: ReadonlySet<string>;
  roles: readonly string[];
  /** Whether a profile photo exists; drives whether the header fetches one. */
  hasAvatar: boolean;
  /**
   * A value that changes whenever the avatar does, appended to the avatar request so a freshly
   * uploaded photo is fetched again rather than served from cache.
   */
  avatarVersion: number;
  /** When this session's sign-in happened, shown in the header. */
  loginAtUtc: string;
}

let current: AdminSession | null = null;
const listeners = new Set<() => void>();

function notify() {
  for (const listener of listeners) listener();
}

export const adminSession = {
  subscribe(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },

  getSnapshot(): AdminSession | null {
    return current;
  },

  set(session: AdminSession): void {
    current = session;
    notify();
  },

  /** After an avatar upload: flag that a photo now exists and bust its cache. */
  markAvatarUpdated(): void {
    if (!current) return;
    current = { ...current, hasAvatar: true, avatarVersion: current.avatarVersion + 1 };
    notify();
  },

  clear(): void {
    current = null;
    notify();
  },

  getAccessToken(): string | null {
    if (!current) return null;

    // An expired token is treated as absent, so the UI redirects to sign-in instead of firing a
    // request that is certain to come back 401.
    if (new Date(current.expiresAtUtc).getTime() <= Date.now()) return null;

    return current.accessToken;
  },

  has(permission: string): boolean {
    return current?.permissions.has(permission) ?? false;
  },

  hasAny(...permissions: string[]): boolean {
    return permissions.some((permission) => this.has(permission));
  },
};

/**
 * Permission constants, mirroring the server's catalogue exactly. Every admin page has its own
 * View / Create / Update / Delete (plus a few module-specific actions), so a role can grant, say,
 * "view service types" without "delete service types". The server enforces each one regardless of
 * what the UI chooses to show.
 */
export const Permissions = {
  DashboardView: 'Dashboard.View',

  ApplicationsView: 'Applications.View',
  ApplicationsReview: 'Applications.Review',
  ApplicationsAttachResults: 'Applications.AttachResults',
  ApplicationsOverrideStatus: 'Applications.OverrideStatus',

  OrdersView: 'Orders.View',
  OrdersCredit: 'Orders.Credit',
  OrdersRefund: 'Orders.Refund',
  OrdersWithdraw: 'Orders.Withdraw',

  ClientsView: 'Clients.View',
  ClientsCreate: 'Clients.Create',
  ClientsUpdate: 'Clients.Update',
  ClientsDelete: 'Clients.Delete',

  CountriesView: 'Countries.View',
  CountriesCreate: 'Countries.Create',
  CountriesUpdate: 'Countries.Update',
  CountriesDelete: 'Countries.Delete',

  CurrenciesView: 'Currencies.View',
  CurrenciesCreate: 'Currencies.Create',
  CurrenciesUpdate: 'Currencies.Update',
  CurrenciesDelete: 'Currencies.Delete',

  AddresseesView: 'Addressees.View',
  AddresseesCreate: 'Addressees.Create',
  AddresseesUpdate: 'Addressees.Update',
  AddresseesDelete: 'Addressees.Delete',

  TransactionTypesView: 'TransactionTypes.View',
  TransactionTypesCreate: 'TransactionTypes.Create',
  TransactionTypesUpdate: 'TransactionTypes.Update',
  TransactionTypesDelete: 'TransactionTypes.Delete',

  SubTransactionTypesView: 'SubTransactionTypes.View',
  SubTransactionTypesCreate: 'SubTransactionTypes.Create',
  SubTransactionTypesUpdate: 'SubTransactionTypes.Update',
  SubTransactionTypesDelete: 'SubTransactionTypes.Delete',

  AuthoritiesView: 'Authorities.View',
  AuthoritiesCreate: 'Authorities.Create',
  AuthoritiesUpdate: 'Authorities.Update',
  AuthoritiesDelete: 'Authorities.Delete',

  ServiceTypesView: 'ServiceTypes.View',
  ServiceTypesCreate: 'ServiceTypes.Create',
  ServiceTypesUpdate: 'ServiceTypes.Update',
  ServiceTypesDelete: 'ServiceTypes.Delete',

  PaymentMethodsView: 'PaymentMethods.View',
  PaymentMethodsCreate: 'PaymentMethods.Create',
  PaymentMethodsUpdate: 'PaymentMethods.Update',
  PaymentMethodsDelete: 'PaymentMethods.Delete',

  TicketsView: 'Tickets.View',
  TicketsUpdate: 'Tickets.Update',
  /** Emailing a reply out is its own grant: writing it stays inside the platform, sending does not. */
  TicketsNotify: 'Tickets.Notify',
  TicketsDelete: 'Tickets.Delete',

  ContactDirectoryView: 'ContactDirectory.View',
  SocialLinksView: 'SocialLinks.View',
  ContactDirectoryCreate: 'ContactDirectory.Create',
  ContactDirectoryUpdate: 'ContactDirectory.Update',
  ContactDirectoryDelete: 'ContactDirectory.Delete',

  TicketCategoriesView: 'TicketCategories.View',
  TicketCategoriesCreate: 'TicketCategories.Create',
  TicketCategoriesUpdate: 'TicketCategories.Update',
  TicketCategoriesDelete: 'TicketCategories.Delete',

  RolesView: 'Roles.View',
  RolesCreate: 'Roles.Create',
  RolesUpdate: 'Roles.Update',
  RolesDelete: 'Roles.Delete',

  AdminUsersView: 'AdminUsers.View',
  AdminUsersCreate: 'AdminUsers.Create',
  AdminUsersUpdate: 'AdminUsers.Update',
  AdminUsersDelete: 'AdminUsers.Delete',

  LandingContentView: 'LandingContent.View',
  LandingContentCreate: 'LandingContent.Create',
  LandingContentUpdate: 'LandingContent.Update',
  LandingContentDelete: 'LandingContent.Delete',

  ToolsView: 'Tools.View',
  ToolsCreate: 'Tools.Create',
  ToolsUpdate: 'Tools.Update',
  ToolsDelete: 'Tools.Delete',

  AuditLogView: 'AuditLog.View',

  SettingsView: 'Settings.View',
  SettingsUpdate: 'Settings.Update',
} as const;

/** Every lookup "view" permission, for the sidebar's Lookups check. */
export const LOOKUP_VIEW_PERMISSIONS = [
  Permissions.CountriesView,
  Permissions.CurrenciesView,
  Permissions.AddresseesView,
  Permissions.TransactionTypesView,
  Permissions.SubTransactionTypesView,
  Permissions.AuthoritiesView,
  Permissions.ServiceTypesView,
] as const;
