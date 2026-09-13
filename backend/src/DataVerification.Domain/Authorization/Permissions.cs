namespace DataVerification.Domain.Authorization;

/// <summary>
/// One permission in the catalogue.
/// </summary>
/// <param name="Name">The stable identifier, <c>{Module}.{Action}</c>, seeded and used as a policy name.</param>
/// <param name="Module">The admin page/area this belongs to; the role editor groups by it.</param>
/// <param name="Action">View / Create / Update / Delete, or a module-specific verb.</param>
/// <param name="Description">One line shown next to the checkbox.</param>
/// <param name="ModuleOrder">Row order of the module in the role editor grid.</param>
public sealed record PermissionDefinition(
    string Name,
    string Module,
    string Action,
    string Description,
    int ModuleOrder);

/// <summary>
/// The complete permission catalogue. Every admin page gets its own set of actions, so a role can
/// grant, say, "view service types" without "delete service types". These constants are seeded into
/// the Permissions table and registered one-for-one as authorization policies, so a typo fails at
/// startup rather than silently granting access.
/// </summary>
public static class Permissions
{
    // Actions. Kept as constants so the grid can order columns consistently.
    public const string ActionView = "View";
    public const string ActionCreate = "Create";
    public const string ActionUpdate = "Update";
    public const string ActionDelete = "Delete";

    /// <summary>Canonical column order for the role editor grid.</summary>
    public static readonly IReadOnlyList<string> ActionOrder =
    [
        ActionView, ActionCreate, ActionUpdate, ActionDelete,
        "Review", "AttachResults", "OverrideStatus", "Credit", "Refund", "Withdraw", "ViewPassword",
        "Notify",
    ];

    // -- Dashboard ---------------------------------------------------------
    public const string DashboardView = "Dashboard.View";

    // -- Applications ------------------------------------------------------
    public const string ApplicationsView = "Applications.View";
    public const string ApplicationsReview = "Applications.Review";
    public const string ApplicationsAttachResults = "Applications.AttachResults";

    /// <summary>
    /// Lets an administrator drive an application through steps that normally belong to the
    /// applicant — submitting a draft, or putting an unpaid application into the review queue.
    /// Separate from Review because marking something paid moves no money.
    /// </summary>
    public const string ApplicationsOverrideStatus = "Applications.OverrideStatus";

    // -- Orders ------------------------------------------------------------
    public const string OrdersView = "Orders.View";
    public const string OrdersCredit = "Orders.Credit";
    public const string OrdersRefund = "Orders.Refund";

    /// <summary>Decides payout requests. Separate from Credit, because money leaves the platform.</summary>
    public const string OrdersWithdraw = "Orders.Withdraw";

    /// <summary>
    /// Reveals an order's sign-in password in clear text. Deliberately its own permission and not
    /// part of Orders.View: it hands over a live credential, so it is granted to support staff who
    /// need it and to nobody else.
    /// </summary>
    public const string OrdersViewPassword = "Orders.ViewPassword";

    // -- Clients -----------------------------------------------------------
    public const string ClientsView = "Clients.View";
    public const string ClientsCreate = "Clients.Create";
    public const string ClientsUpdate = "Clients.Update";
    public const string ClientsDelete = "Clients.Delete";

    // -- Lookups: one page each -------------------------------------------
    public const string CountriesView = "Countries.View";
    public const string CountriesCreate = "Countries.Create";
    public const string CountriesUpdate = "Countries.Update";
    public const string CountriesDelete = "Countries.Delete";

    public const string CurrenciesView = "Currencies.View";
    public const string CurrenciesCreate = "Currencies.Create";
    public const string CurrenciesUpdate = "Currencies.Update";
    public const string CurrenciesDelete = "Currencies.Delete";

    public const string AddresseesView = "Addressees.View";
    public const string AddresseesCreate = "Addressees.Create";
    public const string AddresseesUpdate = "Addressees.Update";
    public const string AddresseesDelete = "Addressees.Delete";

    public const string TransactionTypesView = "TransactionTypes.View";
    public const string TransactionTypesCreate = "TransactionTypes.Create";
    public const string TransactionTypesUpdate = "TransactionTypes.Update";
    public const string TransactionTypesDelete = "TransactionTypes.Delete";

    public const string SubTransactionTypesView = "SubTransactionTypes.View";
    public const string SubTransactionTypesCreate = "SubTransactionTypes.Create";
    public const string SubTransactionTypesUpdate = "SubTransactionTypes.Update";
    public const string SubTransactionTypesDelete = "SubTransactionTypes.Delete";

    public const string AuthoritiesView = "Authorities.View";
    public const string AuthoritiesCreate = "Authorities.Create";
    public const string AuthoritiesUpdate = "Authorities.Update";
    public const string AuthoritiesDelete = "Authorities.Delete";

    public const string ServiceTypesView = "ServiceTypes.View";
    public const string ServiceTypesCreate = "ServiceTypes.Create";
    public const string ServiceTypesUpdate = "ServiceTypes.Update";
    public const string ServiceTypesDelete = "ServiceTypes.Delete";

    /// <summary>
    /// Covers the payment methods page as a whole: the methods themselves, their receiving
    /// accounts, and the payment-type catalogue behind them. They are edited on one screen and are
    /// meaningless apart, so splitting them into separate grants would only produce roles that can
    /// half-configure a channel.
    /// </summary>
    public const string PaymentMethodsView = "PaymentMethods.View";
    public const string PaymentMethodsCreate = "PaymentMethods.Create";
    public const string PaymentMethodsUpdate = "PaymentMethods.Update";
    public const string PaymentMethodsDelete = "PaymentMethods.Delete";

    // -- Support tickets ---------------------------------------------------

    /// <summary>Opens the ticket queue and reads ticket detail, including internal notes.</summary>
    public const string TicketsView = "Tickets.View";

    /// <summary>
    /// Answers a ticket: posting actions, attaching documents, moving its status, and linking it to
    /// an order or application. Held by the support staff a ticket can be assigned to — the
    /// assignee list is drawn from who holds <see cref="TicketsView"/>.
    /// </summary>
    public const string TicketsUpdate = "Tickets.Update";

    /// <summary>
    /// Emails an action to the person who raised the ticket. Separate from
    /// <see cref="TicketsUpdate"/> on purpose: writing a reply is internal until someone sends it,
    /// and sending is the step that leaves the platform.
    /// </summary>
    public const string TicketsNotify = "Tickets.Notify";

    public const string TicketsDelete = "Tickets.Delete";

    /// <summary>The category list the public contact form offers.</summary>
    public const string TicketCategoriesView = "TicketCategories.View";
    public const string TicketCategoriesCreate = "TicketCategories.Create";
    public const string TicketCategoriesUpdate = "TicketCategories.Update";
    public const string TicketCategoriesDelete = "TicketCategories.Delete";

    // -- Administration ----------------------------------------------------
    public const string RolesView = "Roles.View";
    public const string RolesCreate = "Roles.Create";
    public const string RolesUpdate = "Roles.Update";
    public const string RolesDelete = "Roles.Delete";

    public const string AdminUsersView = "AdminUsers.View";
    public const string AdminUsersCreate = "AdminUsers.Create";
    public const string AdminUsersUpdate = "AdminUsers.Update";
    public const string AdminUsersDelete = "AdminUsers.Delete";

    public const string LandingContentView = "LandingContent.View";
    public const string LandingContentCreate = "LandingContent.Create";
    public const string LandingContentUpdate = "LandingContent.Update";
    public const string LandingContentDelete = "LandingContent.Delete";

    // -- Tools: the public "how to use the platform" videos and images ------
    public const string ToolsView = "Tools.View";
    public const string ToolsCreate = "Tools.Create";
    public const string ToolsUpdate = "Tools.Update";
    public const string ToolsDelete = "Tools.Delete";

    /// <summary>The agents and administration details shown on the public contact page.</summary>
    public const string ContactDirectoryView = "ContactDirectory.View";
    public const string ContactDirectoryCreate = "ContactDirectory.Create";
    public const string ContactDirectoryUpdate = "ContactDirectory.Update";
    public const string ContactDirectoryDelete = "ContactDirectory.Delete";

    /// <summary>The "Follow us" and "Message us" channels in the public footer.</summary>
    public const string SocialLinksView = "SocialLinks.View";
    public const string SocialLinksCreate = "SocialLinks.Create";
    public const string SocialLinksUpdate = "SocialLinks.Update";
    public const string SocialLinksDelete = "SocialLinks.Delete";

    public const string AuditLogView = "AuditLog.View";

    // -- Settings ----------------------------------------------------------

    /// <summary>Opens the platform settings page. Never exposes a secret in clear text.</summary>
    public const string SettingsView = "Settings.View";

    /// <summary>Changes a platform setting, including the SendGrid API key used to send email.</summary>
    public const string SettingsUpdate = "Settings.Update";

    public static readonly IReadOnlyList<PermissionDefinition> All = BuildCatalogue();

    public static IEnumerable<string> Names => All.Select(p => p.Name);

    /// <summary>Every permission a lookup page needs to read, in one place for the nav check.</summary>
    public static readonly IReadOnlyList<string> AllLookupViews =
    [
        CountriesView, CurrenciesView, AddresseesView, TransactionTypesView,
        SubTransactionTypesView, AuthoritiesView, ServiceTypesView,
        PaymentMethodsView,
    ];

    private static List<PermissionDefinition> BuildCatalogue()
    {
        var list = new List<PermissionDefinition>
        {
            new(DashboardView, "Dashboard", ActionView, "Open the dashboard.", 0),

            new(ApplicationsView, "Applications", ActionView, "Open the queue and application details.", 1),
            new(ApplicationsReview, "Applications", "Review", "Change status and post review comments.", 1),
            new(ApplicationsAttachResults, "Applications", "AttachResults", "Attach the verified result files on success.", 1),
            new(ApplicationsOverrideStatus, "Applications", "OverrideStatus", "Submit a draft or mark an application paid on the applicant's behalf.", 1),

            new(OrdersView, "Orders", ActionView, "Search orders and view their wallets and ledgers.", 2),
            new(OrdersCredit, "Orders", "Credit", "Credit an order's wallet.", 2),
            new(OrdersRefund, "Orders", "Refund", "Refund a paid application back to the wallet.", 2),
            new(OrdersWithdraw, "Orders", "Withdraw", "Approve or refuse a wallet payout request.", 2),
            new(OrdersViewPassword, "Orders", "ViewPassword", "Reveal an order's sign-in password in clear text.", 2),
        };

        AddCrud(list, "Clients", "clients", 3);

        // Each lookup page gets the four standard actions.
        AddCrud(list, "Countries", "countries", 4);
        AddCrud(list, "Addressees", "the addressee list offered on a new application", 4);
        AddCrud(list, "Currencies", "currencies", 5);
        AddCrud(list, "TransactionTypes", "transaction types", 7);
        AddCrud(list, "SubTransactionTypes", "sub-transaction types", 8);
        AddCrud(list, "Authorities", "verification authorities", 9);
        AddCrud(list, "ServiceTypes", "service types", 10);
        AddCrud(list, "PaymentMethods", "payment methods and the payment types behind them", 6);

        list.Add(new(TicketsView, "Tickets", ActionView, "Open the support queue and read ticket detail.", 16));
        list.Add(new(TicketsUpdate, "Tickets", ActionUpdate, "Answer, assign and link tickets.", 16));
        list.Add(new(TicketsNotify, "Tickets", "Notify", "Email a reply to the person who raised the ticket.", 16));
        list.Add(new(TicketsDelete, "Tickets", ActionDelete, "Delete a ticket and its history.", 16));

        AddCrud(list, "TicketCategories", "the categories the contact form offers", 17);
        AddCrud(list, "ContactDirectory", "the agents and office details on the contact page", 18);
        AddCrud(list, "SocialLinks", "the follow and message channels in the site footer", 18);

        AddCrud(list, "Roles", "roles", 11);
        AddCrud(list, "AdminUsers", "admin users", 12);

        AddCrud(list, "LandingContent", "landing page content", 13);
        AddCrud(list, "Tools", "the tools and guides shown to applicants", 13);

        list.Add(new(AuditLogView, "AuditLog", ActionView, "Read the global audit trail.", 14));

        list.Add(new(SettingsView, "Settings", ActionView, "Open the platform settings page.", 15));
        list.Add(new(SettingsUpdate, "Settings", ActionUpdate, "Change platform settings, including the email API key.", 15));

        return list;
    }

    private static void AddCrud(List<PermissionDefinition> list, string module, string label, int order)
    {
        list.Add(new($"{module}.{ActionView}", module, ActionView, $"View {label}.", order));
        list.Add(new($"{module}.{ActionCreate}", module, ActionCreate, $"Create {label}.", order));
        list.Add(new($"{module}.{ActionUpdate}", module, ActionUpdate, $"Edit {label}.", order));
        list.Add(new($"{module}.{ActionDelete}", module, ActionDelete, $"Delete {label}.", order));
    }
}

/// <summary>Names of the roles created by the seeder.</summary>
public static class SystemRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Reviewer = "Reviewer";
}
