namespace DataVerification.Domain.Payments;

/// <summary>How a gateway setting is entered and checked.</summary>
public enum GatewayFieldType
{
    Text = 0,
    /// <summary>Encrypted at rest and never sent back unless an administrator asks to see it.</summary>
    Secret = 1,
    Url = 2,
    Email = 3,
    Number = 4,
    Select = 5,
    Boolean = 6,
}

/// <summary>Groups the catalogue in the admin dropdown.</summary>
public enum PaymentGatewayCategory
{
    /// <summary>Card processors and online checkouts that work across many countries.</summary>
    Global = 0,
    /// <summary>Gateways serving the Middle East and North Africa.</summary>
    MiddleEastAfrica = 1,
    /// <summary>Gateways serving Asia and Latin America.</summary>
    AsiaLatinAmerica = 2,
    /// <summary>Bank payment schemes: SEPA, ACH, SWIFT and the like.</summary>
    BankTransfer = 3,
    /// <summary>Pay-later providers.</summary>
    BuyNowPayLater = 4,
    /// <summary>Wallets layered over another processor, such as Apple Pay.</summary>
    Wallet = 5,
    /// <summary>Anything the catalogue does not list.</summary>
    Other = 6,
}

public sealed record GatewayFieldOption(string Value, string LabelEn, string LabelAr);

/// <summary>One setting a gateway needs, with the rules its value must meet.</summary>
public sealed record GatewayField(
    string Key,
    string LabelEn,
    string LabelAr,
    GatewayFieldType Type,
    bool IsRequired = false,
    string? HintEn = null,
    string? HintAr = null,
    string? Pattern = null,
    string? Placeholder = null,
    /// <summary>Certificates and private keys span several lines.</summary>
    bool Multiline = false,
    IReadOnlyList<GatewayFieldOption>? Options = null)
{
    public IReadOnlyList<GatewayFieldOption> Options { get; init; } = Options ?? [];

    /// <summary>A copy that must be filled in.</summary>
    public GatewayField Required() => this with { IsRequired = true };

    public GatewayField WithHint(string en, string ar) => this with { HintEn = en, HintAr = ar };

    public GatewayField WithPattern(string pattern, string? placeholder = null) =>
        this with { Pattern = pattern, Placeholder = placeholder ?? Placeholder };

    /// <summary>Longest value accepted: keys and certificates run long, everything else does not.</summary>
    public int MaxLength => Type == GatewayFieldType.Secret ? 10000 : Multiline ? 4000 : 500;
}

/// <summary>A payment gateway or scheme the platform can hold configuration for.</summary>
public sealed record PaymentGatewayDefinition(
    string Code,
    string Name,
    PaymentGatewayCategory Category,
    string RegionEn,
    string RegionAr,
    string? Website,
    IReadOnlyList<GatewayField> Fields);

/// <summary>
/// The gateways and payment schemes an administrator can choose from, each with the settings it
/// needs. Code rather than data: the fields are what an integration would be written against, so a
/// new gateway arrives with the code that uses it. "Other" covers anything not listed.
/// </summary>
public static class PaymentGatewayCatalog
{
    public const string CustomCode = "custom";

    // ------------------------------------------------------------------ field building blocks

    private static GatewayField Text(string key, string en, string ar) => new(key, en, ar, GatewayFieldType.Text);

    private static GatewayField Secret(string key, string en, string ar) => new(key, en, ar, GatewayFieldType.Secret);

    private static GatewayField Pem(string key, string en, string ar) =>
        new(key, en, ar, GatewayFieldType.Secret, Multiline: true);

    private static GatewayField Link(string key, string en, string ar) => new(key, en, ar, GatewayFieldType.Url);

    private static GatewayField Choice(string key, string en, string ar, params GatewayFieldOption[] options) =>
        new(key, en, ar, GatewayFieldType.Select, Options: options);

    private static GatewayFieldOption Opt(string value, string en, string ar) => new(value, en, ar);

    private static GatewayFieldOption Opt(string value) => new(value, value, value);

    private static GatewayField ApiKey => Secret("apiKey", "API key", "مفتاح API");

    private static GatewayField SecretKey => Secret("secretKey", "Secret key", "المفتاح السري");

    private static GatewayField PublicKey => Text("publicKey", "Public key", "المفتاح العام");

    private static GatewayField PublishableKey => Text("publishableKey", "Publishable key", "المفتاح القابل للنشر");

    private static GatewayField MerchantId => Text("merchantId", "Merchant ID", "معرّف التاجر");

    private static GatewayField ClientId => Text("clientId", "Client ID", "معرّف العميل");

    private static GatewayField ClientSecret => Secret("clientSecret", "Client secret", "سر العميل");

    private static GatewayField WebhookSecret =>
        Secret("webhookSecret", "Webhook signing secret", "سر توقيع الويب هوك")
            .WithHint(
                "Used to verify that notifications really come from the gateway.",
                "يُستخدم للتحقق من أن الإشعارات صادرة فعلًا من البوابة.");

    private static GatewayField AccessToken => Secret("accessToken", "Access token", "رمز الوصول");

    private static GatewayField Username => Text("username", "Username", "اسم المستخدم");

    private static GatewayField Password => Secret("password", "Password", "كلمة المرور");

    /// <summary>Where the gateway returns the applicant; shared by every online checkout.</summary>
    private static readonly GatewayField[] ReturnUrls =
    [
        Link("returnUrl", "Return URL", "رابط العودة")
            .WithHint("Where the applicant lands after paying.", "الصفحة التي يصل إليها مقدم الطلب بعد الدفع."),
        Link("cancelUrl", "Cancel URL", "رابط الإلغاء")
            .WithHint("Where the applicant lands if they cancel.", "الصفحة التي يصل إليها مقدم الطلب عند الإلغاء."),
        Link("webhookUrl", "Webhook URL", "رابط الويب هوك")
            .WithHint(
                "The address registered with the gateway for payment notifications.",
                "العنوان المسجّل لدى البوابة لاستقبال إشعارات الدفع."),
    ];

    private static PaymentGatewayDefinition Online(
        string code,
        string name,
        PaymentGatewayCategory category,
        string regionEn,
        string regionAr,
        string website,
        params GatewayField[] fields) =>
        new(code, name, category, regionEn, regionAr, website, [.. fields, .. ReturnUrls]);

    private static PaymentGatewayDefinition Bank(
        string code,
        string name,
        string regionEn,
        string regionAr,
        string? website,
        params GatewayField[] fields) =>
        new(code, name, PaymentGatewayCategory.BankTransfer, regionEn, regionAr, website, fields);

    private const string Worldwide = "Worldwide";
    private const string WorldwideAr = "عالمي";

    // ------------------------------------------------------------------ the catalogue

    public static IReadOnlyList<PaymentGatewayDefinition> All { get; } =
    [
        // Global processors ------------------------------------------------------------------
        Online("paypal", "PayPal", PaymentGatewayCategory.Global, Worldwide, WorldwideAr, "https://developer.paypal.com",
            ClientId.Required(), ClientSecret.Required(),
            Text("webhookId", "Webhook ID", "معرّف الويب هوك")),
        Online("stripe", "Stripe", PaymentGatewayCategory.Global, Worldwide, WorldwideAr, "https://stripe.com/docs",
            PublishableKey.Required().WithPattern("^pk_(test|live)_", "pk_live_…"),
            SecretKey.Required(), WebhookSecret),
        Online("adyen", "Adyen", PaymentGatewayCategory.Global, Worldwide, WorldwideAr, "https://docs.adyen.com",
            Text("merchantAccount", "Merchant account", "حساب التاجر").Required(),
            ApiKey.Required(),
            Text("clientKey", "Client key", "مفتاح العميل"),
            Secret("hmacKey", "HMAC key", "مفتاح HMAC"),
            Text("liveUrlPrefix", "Live endpoint prefix", "بادئة عنوان البيئة الحية")),
        Online("checkout_com", "Checkout.com", PaymentGatewayCategory.Global, Worldwide, WorldwideAr, "https://www.checkout.com/docs",
            PublicKey.Required(), SecretKey.Required(),
            Text("processingChannelId", "Processing channel ID", "معرّف قناة المعالجة"),
            Secret("webhookAuthKey", "Webhook authorization key", "مفتاح تفويض الويب هوك")),
        Online("braintree", "Braintree", PaymentGatewayCategory.Global, Worldwide, WorldwideAr, "https://developer.paypal.com/braintree/docs",
            MerchantId.Required(), PublicKey.Required(),
            Secret("privateKey", "Private key", "المفتاح الخاص").Required()),
        Online("square", "Square", PaymentGatewayCategory.Global, "North America, UK, Europe, Japan, Australia", "أمريكا الشمالية وبريطانيا وأوروبا واليابان وأستراليا", "https://developer.squareup.com",
            Text("applicationId", "Application ID", "معرّف التطبيق").Required(),
            AccessToken.Required(),
            Text("locationId", "Location ID", "معرّف الموقع").Required(),
            Secret("webhookSignatureKey", "Webhook signature key", "مفتاح توقيع الويب هوك")),
        Online("authorize_net", "Authorize.Net", PaymentGatewayCategory.Global, "United States, Canada, UK, Europe, Australia", "الولايات المتحدة وكندا وبريطانيا وأوروبا وأستراليا", "https://developer.authorize.net",
            Text("apiLoginId", "API login ID", "معرّف دخول API").Required(),
            Secret("transactionKey", "Transaction key", "مفتاح المعاملات").Required(),
            Secret("signatureKey", "Signature key", "مفتاح التوقيع")),
        Online("worldpay", "Worldpay", PaymentGatewayCategory.Global, Worldwide, WorldwideAr, "https://developer.worldpay.com",
            Text("merchantCode", "Merchant code", "رمز التاجر").Required(),
            Username.Required(), Password.Required(),
            Text("installationId", "Installation ID", "معرّف التثبيت")),
        Online("mollie", "Mollie", PaymentGatewayCategory.Global, "Europe", "أوروبا", "https://docs.mollie.com",
            ApiKey.Required(),
            Text("profileId", "Profile ID", "معرّف الملف")),
        Online("twocheckout", "2Checkout (Verifone)", PaymentGatewayCategory.Global, Worldwide, WorldwideAr, "https://verifone.cloud/docs/2checkout",
            Text("merchantCode", "Merchant code", "رمز التاجر").Required(),
            SecretKey.Required(),
            Secret("buyLinkSecretWord", "Buy-link secret word", "الكلمة السرية لرابط الشراء")),
        Online("payu", "PayU", PaymentGatewayCategory.Global, "Europe, India, Latin America, Africa", "أوروبا والهند وأمريكا اللاتينية وأفريقيا", "https://developers.payu.com",
            Text("posId", "POS ID", "معرّف نقطة البيع").Required(),
            ClientId.Required(), ClientSecret.Required(),
            Secret("secondKey", "Second key (MD5)", "المفتاح الثاني (MD5)")),
        Online("amazon_pay", "Amazon Pay", PaymentGatewayCategory.Global, "United States, Europe, UK, Japan", "الولايات المتحدة وأوروبا وبريطانيا واليابان", "https://developer.amazon.com/docs/amazon-pay",
            MerchantId.Required(),
            Text("publicKeyId", "Public key ID", "معرّف المفتاح العام").Required(),
            Pem("privateKey", "Private key", "المفتاح الخاص").Required(),
            Text("storeId", "Store ID", "معرّف المتجر").Required(),
            Choice("region", "Region", "المنطقة", Opt("us", "United States", "الولايات المتحدة"), Opt("eu", "Europe / UK", "أوروبا / بريطانيا"), Opt("jp", "Japan", "اليابان")).Required()),

        // Middle East and Africa -------------------------------------------------------------
        Online("fawry", "Fawry", PaymentGatewayCategory.MiddleEastAfrica, "Egypt", "مصر", "https://developer.fawrystaging.com",
            Text("merchantCode", "Merchant code", "رمز التاجر").Required(),
            Secret("securityKey", "Security key", "مفتاح الأمان").Required(),
            new GatewayField("paymentExpiryHours", "Payment expiry (hours)", "مدة صلاحية الدفع (بالساعات)", GatewayFieldType.Number,
                HintEn: "How long a Fawry reference code stays payable.", HintAr: "المدة التي يظل فيها الرقم المرجعي لفوري صالحًا للدفع.")),
        Online("paymob", "Paymob", PaymentGatewayCategory.MiddleEastAfrica, "Egypt, UAE, Saudi Arabia, Oman, Pakistan", "مصر والإمارات والسعودية وعُمان وباكستان", "https://developers.paymob.com",
            ApiKey.Required(), SecretKey, PublicKey,
            Text("integrationId", "Integration ID", "معرّف التكامل").Required()
                .WithHint("One per payment channel (card, wallet, kiosk).", "واحد لكل قناة دفع (بطاقة، محفظة، كشك)."),
            Text("iframeId", "iFrame ID", "معرّف الإطار"),
            Secret("hmacSecret", "HMAC secret", "سر HMAC")),
        Online("kashier", "Kashier", PaymentGatewayCategory.MiddleEastAfrica, "Egypt", "مصر", "https://developers.kashier.io",
            MerchantId.Required(), ApiKey.Required(), SecretKey),
        Online("paytabs", "PayTabs", PaymentGatewayCategory.MiddleEastAfrica, "Middle East, North Africa", "الشرق الأوسط وشمال أفريقيا", "https://site.paytabs.com/en/developers",
            Text("profileId", "Profile ID", "معرّف الملف").Required(),
            Secret("serverKey", "Server key", "مفتاح الخادم").Required(),
            Text("clientKey", "Client key", "مفتاح العميل"),
            Choice("region", "Region", "المنطقة",
                Opt("ARE", "United Arab Emirates", "الإمارات"), Opt("SAU", "Saudi Arabia", "السعودية"),
                Opt("OMN", "Oman", "عُمان"), Opt("JOR", "Jordan", "الأردن"),
                Opt("EGY", "Egypt", "مصر"), Opt("IRQ", "Iraq", "العراق"), Opt("GLOBAL", "Global", "عالمي")).Required()),
        Online("hyperpay", "HyperPay", PaymentGatewayCategory.MiddleEastAfrica, "Saudi Arabia, UAE, Jordan, Egypt", "السعودية والإمارات والأردن ومصر", "https://wordpresshyperpay.docs.oppwa.com",
            Text("entityId", "Entity ID (cards)", "معرّف الكيان (البطاقات)").Required(),
            AccessToken.Required(),
            Text("madaEntityId", "Entity ID (mada)", "معرّف الكيان (مدى)"),
            Text("applePayEntityId", "Entity ID (Apple Pay)", "معرّف الكيان (Apple Pay)")),
        Online("moyasar", "Moyasar", PaymentGatewayCategory.MiddleEastAfrica, "Saudi Arabia", "السعودية", "https://docs.moyasar.com",
            PublishableKey.Required(), SecretKey.Required(), WebhookSecret),
        Online("tap", "Tap Payments", PaymentGatewayCategory.MiddleEastAfrica, "GCC, Egypt, Jordan", "دول الخليج ومصر والأردن", "https://developers.tap.company",
            PublicKey.Required(), SecretKey.Required(), MerchantId),
        Online("myfatoorah", "MyFatoorah", PaymentGatewayCategory.MiddleEastAfrica, "GCC, Jordan, Egypt", "دول الخليج والأردن ومصر", "https://docs.myfatoorah.com",
            Secret("apiToken", "API token", "رمز API").Required(),
            Choice("country", "Account country", "دولة الحساب",
                Opt("KWT", "Kuwait", "الكويت"), Opt("SAU", "Saudi Arabia", "السعودية"),
                Opt("ARE", "United Arab Emirates", "الإمارات"), Opt("QAT", "Qatar", "قطر"),
                Opt("BHR", "Bahrain", "البحرين"), Opt("OMN", "Oman", "عُمان"),
                Opt("JOR", "Jordan", "الأردن"), Opt("EGY", "Egypt", "مصر")).Required()),
        Online("telr", "Telr", PaymentGatewayCategory.MiddleEastAfrica, "UAE, Saudi Arabia, Jordan, Bahrain", "الإمارات والسعودية والأردن والبحرين", "https://docs.telr.com",
            Text("storeId", "Store ID", "معرّف المتجر").Required(),
            Secret("authKey", "Authentication key", "مفتاح المصادقة").Required()),
        Online("ngenius", "Network International (N-Genius)", PaymentGatewayCategory.MiddleEastAfrica, "Middle East, Africa", "الشرق الأوسط وأفريقيا", "https://docs.ngenius-payments.com",
            Text("outletReference", "Outlet reference", "مرجع المنفذ").Required(),
            ApiKey.Required(),
            Text("realm", "Realm", "النطاق")),
        Online("geidea", "Geidea", PaymentGatewayCategory.MiddleEastAfrica, "Saudi Arabia, UAE, Egypt", "السعودية والإمارات ومصر", "https://docs.geidea.net",
            Text("merchantPublicKey", "Merchant public key", "المفتاح العام للتاجر").Required(),
            Secret("apiPassword", "API password", "كلمة مرور API").Required()),
        Online("aps", "Amazon Payment Services (PayFort)", PaymentGatewayCategory.MiddleEastAfrica, "UAE, Saudi Arabia, Egypt, Jordan, Qatar, Kuwait", "الإمارات والسعودية ومصر والأردن وقطر والكويت", "https://paymentservices.amazon.com/docs",
            Text("merchantIdentifier", "Merchant identifier", "معرّف التاجر").Required(),
            Text("accessCode", "Access code", "رمز الوصول").Required(),
            Secret("shaRequestPhrase", "SHA request phrase", "عبارة SHA للطلب").Required(),
            Secret("shaResponsePhrase", "SHA response phrase", "عبارة SHA للاستجابة").Required(),
            Choice("shaType", "SHA type", "نوع SHA", Opt("SHA-256"), Opt("SHA-512")).Required()),
        Online("stc_pay", "STC Pay", PaymentGatewayCategory.MiddleEastAfrica, "Saudi Arabia, Kuwait, Bahrain", "السعودية والكويت والبحرين", "https://stcpay.com.sa",
            MerchantId.Required(),
            Pem("clientCertificate", "Client certificate", "شهادة العميل").Required(),
            Secret("certificatePassword", "Certificate password", "كلمة مرور الشهادة")),
        Online("paystack", "Paystack", PaymentGatewayCategory.MiddleEastAfrica, "Nigeria, Ghana, Kenya, South Africa", "نيجيريا وغانا وكينيا وجنوب أفريقيا", "https://paystack.com/docs",
            PublicKey.Required(), SecretKey.Required()),
        Online("flutterwave", "Flutterwave", PaymentGatewayCategory.MiddleEastAfrica, "Africa", "أفريقيا", "https://developer.flutterwave.com",
            PublicKey.Required(), SecretKey.Required(),
            Secret("encryptionKey", "Encryption key", "مفتاح التشفير"),
            Secret("secretHash", "Webhook secret hash", "بصمة سر الويب هوك")),
        Online("opay", "OPay", PaymentGatewayCategory.MiddleEastAfrica, "Egypt, Nigeria", "مصر ونيجيريا", "https://documentation.opayweb.com",
            MerchantId.Required(), PublicKey.Required(), SecretKey.Required()),

        // Asia and Latin America -------------------------------------------------------------
        Online("razorpay", "Razorpay", PaymentGatewayCategory.AsiaLatinAmerica, "India, Malaysia, Singapore", "الهند وماليزيا وسنغافورة", "https://razorpay.com/docs",
            Text("keyId", "Key ID", "معرّف المفتاح").Required(),
            Secret("keySecret", "Key secret", "سر المفتاح").Required(),
            WebhookSecret),
        Online("paytm", "Paytm", PaymentGatewayCategory.AsiaLatinAmerica, "India", "الهند", "https://developer.paytm.com",
            MerchantId.Required(),
            Secret("merchantKey", "Merchant key", "مفتاح التاجر").Required(),
            Text("website", "Website name", "اسم الموقع").WithHint("WEBSTAGING in sandbox, DEFAULT in live.", "WEBSTAGING في البيئة التجريبية وDEFAULT في الحية.")),
        Online("alipay", "Alipay", PaymentGatewayCategory.AsiaLatinAmerica, "China, worldwide", "الصين وعالمي", "https://global.alipay.com/docs",
            Text("appId", "App ID", "معرّف التطبيق").Required(),
            Pem("appPrivateKey", "App private key", "المفتاح الخاص للتطبيق").Required(),
            new GatewayField("alipayPublicKey", "Alipay public key", "المفتاح العام لـ Alipay", GatewayFieldType.Text, IsRequired: true, Multiline: true)),
        Online("wechat_pay", "WeChat Pay", PaymentGatewayCategory.AsiaLatinAmerica, "China, worldwide", "الصين وعالمي", "https://pay.weixin.qq.com",
            Text("appId", "App ID", "معرّف التطبيق").Required(),
            Text("mchId", "Merchant ID (mch_id)", "معرّف التاجر (mch_id)").Required(),
            Secret("apiV3Key", "API v3 key", "مفتاح API v3").Required(),
            Text("certificateSerialNumber", "Certificate serial number", "الرقم التسلسلي للشهادة"),
            Pem("privateKey", "Merchant private key", "المفتاح الخاص للتاجر").Required()),
        Online("xendit", "Xendit", PaymentGatewayCategory.AsiaLatinAmerica, "Indonesia, Philippines, Thailand, Malaysia, Vietnam", "إندونيسيا والفلبين وتايلاند وماليزيا وفيتنام", "https://docs.xendit.co",
            SecretKey.Required(),
            Secret("callbackToken", "Callback verification token", "رمز التحقق من الاستدعاء")),
        Online("midtrans", "Midtrans", PaymentGatewayCategory.AsiaLatinAmerica, "Indonesia", "إندونيسيا", "https://docs.midtrans.com",
            Secret("serverKey", "Server key", "مفتاح الخادم").Required(),
            Text("clientKey", "Client key", "مفتاح العميل").Required(),
            MerchantId),
        Online("mercado_pago", "Mercado Pago", PaymentGatewayCategory.AsiaLatinAmerica, "Latin America", "أمريكا اللاتينية", "https://www.mercadopago.com/developers",
            PublicKey.Required(), AccessToken.Required(), WebhookSecret),

        // Buy now, pay later -----------------------------------------------------------------
        Online("klarna", "Klarna", PaymentGatewayCategory.BuyNowPayLater, "Europe, North America, Australia", "أوروبا وأمريكا الشمالية وأستراليا", "https://docs.klarna.com",
            Username.Required().WithHint("The API username (UID).", "اسم مستخدم API (UID)."),
            Password.Required(),
            Choice("region", "Region", "المنطقة", Opt("eu", "Europe", "أوروبا"), Opt("na", "North America", "أمريكا الشمالية"), Opt("oc", "Oceania", "أوقيانوسيا")).Required()),
        Online("afterpay", "Afterpay / Clearpay", PaymentGatewayCategory.BuyNowPayLater, "US, UK, Canada, Australia, New Zealand", "الولايات المتحدة وبريطانيا وكندا وأستراليا ونيوزيلندا", "https://developers.afterpay.com",
            MerchantId.Required(), SecretKey.Required()),
        Online("tabby", "Tabby", PaymentGatewayCategory.BuyNowPayLater, "UAE, Saudi Arabia, Kuwait, Bahrain, Qatar, Egypt", "الإمارات والسعودية والكويت والبحرين وقطر ومصر", "https://docs.tabby.ai",
            PublicKey.Required(), SecretKey.Required(),
            Text("merchantCode", "Merchant code", "رمز التاجر").Required()),
        Online("tamara", "Tamara", PaymentGatewayCategory.BuyNowPayLater, "Saudi Arabia, UAE, Kuwait, Bahrain, Qatar, Oman", "السعودية والإمارات والكويت والبحرين وقطر وعُمان", "https://docs.tamara.co",
            Secret("apiToken", "API token", "رمز API").Required(),
            Secret("notificationToken", "Notification token", "رمز الإشعارات"),
            PublicKey),
        Online("valu", "valU", PaymentGatewayCategory.BuyNowPayLater, "Egypt", "مصر", "https://www.valu.com.eg",
            MerchantId.Required(), ApiKey.Required(), SecretKey),

        // Wallets ----------------------------------------------------------------------------
        Online("apple_pay", "Apple Pay", PaymentGatewayCategory.Wallet, Worldwide, WorldwideAr, "https://developer.apple.com/apple-pay",
            Text("merchantIdentifier", "Merchant identifier", "معرّف التاجر").Required().WithPattern("^merchant\\.", "merchant.com.example"),
            Text("displayName", "Display name", "الاسم المعروض").Required(),
            Text("domain", "Verified domain", "النطاق الموثّق"),
            Pem("merchantIdentityCertificate", "Merchant identity certificate", "شهادة هوية التاجر").Required(),
            Choice("processor", "Processed by", "تتم المعالجة عبر", Opt("stripe", "Stripe", "Stripe"), Opt("checkout_com", "Checkout.com", "Checkout.com"), Opt("adyen", "Adyen", "Adyen"), Opt("hyperpay", "HyperPay", "HyperPay"), Opt("paytabs", "PayTabs", "PayTabs"), Opt("other", "Other", "أخرى"))),
        Online("google_pay", "Google Pay", PaymentGatewayCategory.Wallet, Worldwide, WorldwideAr, "https://developers.google.com/pay",
            MerchantId.Required(),
            Text("merchantName", "Merchant name", "اسم التاجر").Required(),
            Text("gateway", "Gateway name", "اسم البوابة").Required()
                .WithHint("As Google Pay knows your processor, e.g. stripe or checkoutltd.", "كما يعرفه Google Pay، مثل stripe أو checkoutltd."),
            Text("gatewayMerchantId", "Gateway merchant ID", "معرّف التاجر لدى البوابة").Required()),

        // Bank payment schemes ----------------------------------------------------------------
        Bank("sepa", "SEPA", "European Union, EEA, Switzerland, UK", "الاتحاد الأوروبي والمنطقة الاقتصادية الأوروبية وسويسرا وبريطانيا", "https://www.europeanpaymentscouncil.eu",
            Choice("scheme", "Scheme", "المخطط",
                Opt("SCT", "SEPA Credit Transfer", "تحويل SEPA الدائن"),
                Opt("SCT_INST", "SEPA Instant Credit Transfer", "تحويل SEPA الفوري"),
                Opt("SDD_CORE", "SEPA Direct Debit Core", "خصم SEPA المباشر (الأساسي)"),
                Opt("SDD_B2B", "SEPA Direct Debit B2B", "خصم SEPA المباشر (بين الشركات)")).Required(),
            Text("creditorName", "Account holder name", "اسم صاحب الحساب").Required(),
            Text("iban", "IBAN", "رقم الآيبان").Required()
                .WithPattern("^[A-Z]{2}[0-9]{2}[A-Z0-9 ]{11,34}$", "DE89 3704 0044 0532 0130 00"),
            Text("bic", "BIC / SWIFT", "رمز BIC / SWIFT").WithPattern("^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$", "COBADEFFXXX"),
            Text("creditorIdentifier", "Creditor identifier (CI)", "معرّف الدائن (CI)")
                .WithHint("Needed for direct debit.", "مطلوب للخصم المباشر."),
            Text("bankName", "Bank name", "اسم البنك")),
        Bank("ach", "ACH", "United States", "الولايات المتحدة", "https://www.nacha.org",
            Text("companyName", "Company name", "اسم الشركة").Required(),
            Text("routingNumber", "Routing number (ABA)", "رقم التوجيه (ABA)").Required().WithPattern("^[0-9]{9}$", "021000021"),
            Text("accountNumber", "Account number", "رقم الحساب").Required().WithPattern("^[0-9]{4,17}$"),
            Choice("accountType", "Account type", "نوع الحساب", Opt("checking", "Checking", "جاري"), Opt("savings", "Savings", "توفير")).Required(),
            Text("companyId", "Company ID", "معرّف الشركة").WithHint("Issued by your bank for ACH origination.", "يصدره البنك لإنشاء معاملات ACH."),
            Choice("secCode", "SEC code", "رمز SEC",
                Opt("PPD", "PPD – consumer", "PPD – أفراد"), Opt("CCD", "CCD – corporate", "CCD – شركات"),
                Opt("WEB", "WEB – online authorization", "WEB – تفويض إلكتروني"), Opt("TEL", "TEL – phone authorization", "TEL – تفويض هاتفي")),
            Text("bankName", "Bank (ODFI) name", "اسم البنك المُنشئ (ODFI)")),
        Bank("swift", "SWIFT international wire", Worldwide, WorldwideAr, "https://www.swift.com",
            Text("beneficiaryName", "Beneficiary name", "اسم المستفيد").Required(),
            Text("accountNumber", "Account number or IBAN", "رقم الحساب أو الآيبان").Required(),
            Text("swiftCode", "SWIFT / BIC", "رمز SWIFT / BIC").Required().WithPattern("^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$", "NBEGEGCXXXX"),
            Text("bankName", "Bank name", "اسم البنك").Required(),
            Text("bankAddress", "Bank address", "عنوان البنك"),
            Text("intermediarySwiftCode", "Intermediary bank SWIFT", "رمز SWIFT للبنك الوسيط")),
        Bank("bacs", "Bacs / Faster Payments", "United Kingdom", "المملكة المتحدة", "https://www.wearepay.uk",
            Text("accountName", "Account name", "اسم الحساب").Required(),
            Text("sortCode", "Sort code", "رمز الفرع").Required().WithPattern("^[0-9]{2}-?[0-9]{2}-?[0-9]{2}$", "20-00-00"),
            Text("accountNumber", "Account number", "رقم الحساب").Required().WithPattern("^[0-9]{8}$", "12345678"),
            Text("serviceUserNumber", "Service user number (SUN)", "رقم مستخدم الخدمة (SUN)")
                .WithHint("Needed to collect by Direct Debit.", "مطلوب للتحصيل بالخصم المباشر.")),
        Bank("pix", "Pix", "Brazil", "البرازيل", "https://www.bcb.gov.br/estabilidadefinanceira/pix",
            Choice("keyType", "Key type", "نوع المفتاح",
                Opt("cpf", "CPF", "CPF"), Opt("cnpj", "CNPJ", "CNPJ"), Opt("email", "Email", "البريد الإلكتروني"),
                Opt("phone", "Phone", "الهاتف"), Opt("random", "Random key", "مفتاح عشوائي")).Required(),
            Text("pixKey", "Pix key", "مفتاح Pix").Required(),
            Text("merchantName", "Merchant name", "اسم التاجر").Required(),
            Text("merchantCity", "Merchant city", "مدينة التاجر").Required()),
        Bank("upi", "UPI", "India", "الهند", "https://www.npci.org.in",
            Text("vpa", "UPI ID (VPA)", "معرّف UPI (VPA)").Required().WithPattern("^[A-Za-z0-9.\\-_]{2,256}@[A-Za-z]{2,64}$", "business@okbank"),
            Text("payeeName", "Payee name", "اسم المستفيد").Required(),
            Text("merchantCategoryCode", "Merchant category code", "رمز فئة التاجر")),
        Bank("interac", "Interac e-Transfer", "Canada", "كندا", "https://www.interac.ca",
            Text("recipientName", "Recipient name", "اسم المستلم").Required(),
            new GatewayField("recipientEmail", "Recipient email", "بريد المستلم", GatewayFieldType.Email, IsRequired: true),
            new GatewayField("autodeposit", "Autodeposit enabled", "الإيداع التلقائي مفعّل", GatewayFieldType.Boolean,
                HintEn: "Senders need no security question when this is on.", HintAr: "لا يحتاج المرسل إلى سؤال أمان عند التفعيل.")),
        Online("gocardless", "GoCardless (direct debit)", PaymentGatewayCategory.BankTransfer, "UK, Europe, US, Canada, Australia", "بريطانيا وأوروبا والولايات المتحدة وكندا وأستراليا", "https://developer.gocardless.com",
            AccessToken.Required(), WebhookSecret),
        Online("wise", "Wise Business", PaymentGatewayCategory.BankTransfer, Worldwide, WorldwideAr, "https://docs.wise.com",
            Secret("apiToken", "API token", "رمز API").Required(),
            Text("profileId", "Profile ID", "معرّف الملف").Required()),

        // Anything else ----------------------------------------------------------------------
        new(CustomCode, "Other / custom gateway", PaymentGatewayCategory.Other, Worldwide, WorldwideAr, null,
        [
            Text("providerName", "Provider name", "اسم المزوّد").Required(),
            Link("baseUrl", "API base URL", "عنوان API الأساسي").Required(),
            MerchantId,
            ApiKey,
            Secret("apiSecret", "API secret", "سر API"),
            WebhookSecret,
            .. ReturnUrls,
            new GatewayField("notes", "Integration notes", "ملاحظات التكامل", GatewayFieldType.Text, Multiline: true),
        ]),
    ];

    private static readonly Dictionary<string, PaymentGatewayDefinition> ByCode =
        All.ToDictionary(gateway => gateway.Code, StringComparer.OrdinalIgnoreCase);

    public static PaymentGatewayDefinition? Find(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var gateway) ? gateway : null;
}
