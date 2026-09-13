# Urgent: PUT and DELETE are blocked in production

## Symptom

"Check your connection and try again." on **Set up your order**, and the same on anything that
saves via PUT or deletes — saving the SendGrid key, editing an application, replacing a country's
currencies, every delete in the admin panel. Registering and signing in keep working, so the site
looks healthy.

## Proof, from your live server

```
GET     /api/v1/health         ->  HTTP 200
POST    /api/v1/orders/login   ->  HTTP 415   (reaches the application)
PUT     /api/v1/orders/setup   ->  HTTP 405   Allow: GET, HEAD, OPTIONS, TRACE
DELETE  /api/v1/applications/… ->  HTTP 405   Allow: GET, HEAD, OPTIONS, TRACE
```

`Allow: GET, HEAD, OPTIONS, TRACE` is IIS answering, not the application. IIS ships a **WebDAV**
module and handler that claim `PUT` and `DELETE` before ASP.NET Core sees them.

## The fix — two lines in the API's web.config

Edit `E:\inetpub\DataVerification\api\web.config`. Inside `<system.webServer>`, make the handlers
and modules sections read exactly this:

```xml
<handlers>
  <remove name="WebDAV" />
  <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
</handlers>

<modules runAllManagedModulesForAllRequests="false">
  <remove name="WebDAVModule" />
</modules>
```

Both are needed, and `<remove name="WebDAV" />` must come **before** the `aspNetCore` handler.
Leave everything else — especially `<environmentVariables>` — exactly as it is.

Then:

```powershell
Restart-WebAppPool -Name "<your api pool>"
```

`templates\api-web.config` in this bundle is a complete file with this already applied, if you
prefer to start from it. It also carries every environment variable the API needs.

## Confirm it worked

```powershell
try { Invoke-WebRequest -Uri 'https://vdataapi.nen-global.org/api/v1/orders/setup' -Method Put -UseBasicParsing }
catch { "PUT now returns HTTP $([int]$_.Exception.Response.StatusCode)" }
```

- **401** — fixed. The verb now reaches the application, which correctly rejects an unauthenticated
  request.
- **405** — still blocked. The edit did not take, or the pool was not restarted.

This call sends no credentials, so it cannot change any data.

## If it is still 405 after that

WebDAV may be enabled server-wide. Removing the whole feature is the most reliable cure and needs
no site-level configuration:

```powershell
Disable-WindowsOptionalFeature -Online -FeatureName IIS-WebDAV -NoRestart
iisreset
```

Nothing in this platform uses WebDAV.

## Note

I could not reproduce this locally — there is no IIS on the build machine, and against Kestrel the
same PUT succeeds. The diagnosis above comes from your live server's own responses, and the fix is
the standard remedy for it. The verification command tells you definitively whether it worked.
