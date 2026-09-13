using System.Text;
using Asp.Versioning;
using DataVerification.API;
using DataVerification.API.Authorization;
using DataVerification.API.Infrastructure;
using DataVerification.API.Services;
using DataVerification.Application;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Infrastructure;
using DataVerification.Infrastructure.Identity;
using DataVerification.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// A hard ceiling on request bodies. Uploads are capped at 5 MB by the domain and by a
// per-endpoint limit; this stops anything else from streaming an unbounded body into the process.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = ApplicationFile.MaxFileSizeBytes + 8192;
    options.ValueLengthLimit = 1024 * 1024;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = ApplicationFile.MaxFileSizeBytes + 65536;
    options.AddServerHeader = false;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Every timestamp leaves as ISO-8601 UTC with its Z. Without it, a browser reads the value
        // as local time and shows the UTC clock reading as though it were the viewer's own.
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Data Verification API",
        Version = "v1",
        Description = "Applicant and administration endpoints for the Data Verification platform.",
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token returned by an order or admin login.",
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer")] = [],
    });

    // A couple of endpoints deliberately share one route and are separated by content type —
    // POST admin/applications/{id}/status accepts either JSON or multipart. Routing resolves that
    // at request time, but OpenAPI cannot put two operations under one path + verb, so without a
    // resolver the whole document fails to generate. Document the JSON form: it is the common case,
    // and the multipart variant differs only by carrying files.
    options.ResolveConflictingActions(descriptions => descriptions
        .OrderBy(description => description.SupportedRequestFormats
            .Any(format => format.MediaType == "multipart/form-data") ? 1 : 0)
        .First());
});

// RFC 7807 for every error response, with a trace id for support.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var signingKey = jwtSection["SigningKey"];

if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey must be at least 32 characters. Set it with user secrets or an environment variable.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            // Tokens expire when they say they expire; the default five-minute grace is too loose.
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddPermissionPolicies();
builder.Services.AddDataVerificationRateLimiting(builder.Configuration);

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

const string CorsPolicy = "DataVerificationCors";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// Railway (and most container hosts) terminate TLS at the edge and forward plain HTTP. Without
// this, Request.IsHttps is false in production and the refresh cookie cannot be marked Secure /
// SameSite=None — which is required for the cross-origin Vercel SPAs to keep a session across reloads.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// The database is brought up to the schema and permission catalogue this build expects on every
// boot, in every environment: deploying is copy the files and restart the app pool, with no
// migration script run by hand and no window where new binaries serve traffic against the old
// schema. One instance does the work — the initializer holds a SQL application lock, so an
// overlapped recycle cannot migrate twice.
//
// Set `Database:MigrateOnStartup` to false where a DBA applies migrations out of band; the API
// then starts against whatever schema it finds, which is only safe if that schema is current.
var migrateOnStartup = app.Configuration.GetValue("Database:MigrateOnStartup", true);

// Seeding is a different matter: it writes demo data and resets the SuperAdmin password, so it
// stays Development-only unless a host explicitly opts in for a fresh managed database.
var seedOnStartup = app.Environment.IsDevelopment()
    || app.Configuration.GetValue("Database:SeedOnStartup", false);

if (migrateOnStartup || seedOnStartup)
{
    using var scope = app.Services.CreateScope();

    if (migrateOnStartup)
    {
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    if (seedOnStartup)
    {
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
    }

    // Scenery for the review workflow: an order holding an application in every status. Strictly
    // Development, because it fabricates paid applications no wallet ever paid for.
    if (app.Environment.IsDevelopment()
        && app.Configuration.GetValue("Database:SeedDemoApplications", true))
    {
        await scope.ServiceProvider.GetRequiredService<DemoApplicationSeeder>().SeedAsync();
    }
}

app.UseForwardedHeaders();
app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

//if (app.Environment.IsDevelopment())
//{
app.UseSwagger();
app.UseSwaggerUI();
//}

app.UseCors(CorsPolicy);
app.UseRateLimiter();
// Runs before authentication so a hostile file is rejected on the way in, whoever sent it.
app.UseMiddleware<UploadSecurityMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can bootstrap the API in integration tests.</summary>
public partial class Program;
