using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Google.Apis.Auth;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MimeKit;

public sealed class TymUser : IdentityUser { }

public sealed class AccountDb(DbContextOptions<AccountDb> options) : IdentityDbContext<TymUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
}

public sealed class AccountSettings
{
    public bool Enabled { get; init; }
    public bool RequireLoginForText { get; init; }
    public string? GoogleClientId { get; init; }
    public required string DataPath { get; init; }
    public string? SqlConnectionString { get; init; }
    public required string PublicApiUrl { get; init; }
    public required string PublicUiUrl { get; init; }
    public required string[] BrowserOrigins { get; init; }
    public string? MailHost { get; init; }
    public int MailPort { get; init; }
    public string? MailUser { get; init; }
    public string? MailPassword { get; init; }
    public string? MailFrom { get; init; }
    public string? MailPickupPath { get; init; }
    public bool EmailEnabled => Enabled && (MailPickupPath is not null || (MailHost is not null && MailFrom is not null));
    public bool GoogleEnabled => Enabled && !string.IsNullOrWhiteSpace(GoogleClientId);
}

public static class TymAccounts
{
    public const string ReaderPolicy = "signed-in-reader";

    public static AccountSettings AddAccounts(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        string? Value(string key) => string.IsNullOrWhiteSpace(config[key]) ? null : config[key]!.Trim();
        var enabled = bool.TryParse(config["TYM_AUTH_ENABLED"], out var value) && value;
        var dataPath = Value("TYM_AUTH_DATA_PATH");
        var sqlConnection = Value("TYM_AUTH_SQL_CONNECTION");
        if (enabled && sqlConnection is not null && !builder.Environment.IsDevelopment())
        {
            var sql = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sqlConnection);
            if (sql.Authentication != Microsoft.Data.SqlClient.SqlAuthenticationMethod.ActiveDirectoryManagedIdentity
                || sql.TrustServerCertificate || sql.Encrypt == Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Optional)
                throw new InvalidOperationException("Production account SQL requires managed identity, encrypted transport and certificate validation.");
            // Do not retain idle sessions that prevent a free serverless database from auto-pausing.
            sql.Pooling = false;
            sqlConnection = sql.ToString();
        }
        var apiUrl = Value("TYM_PUBLIC_API_URL");
        var uiUrl = Value("TYM_PUBLIC_UI_URL");
        if (enabled && !builder.Environment.IsDevelopment() && ((dataPath is null && sqlConnection is null) || apiUrl is null || uiUrl is null))
            throw new InvalidOperationException("Accounts require a persistent SQL database or private data path, and public API/UI URLs in production.");
        dataPath = Path.GetFullPath(dataPath ?? Path.Combine(builder.Environment.ContentRootPath, "private-models", "accounts"));
        static string PublicUrl(string? url, string fallback, bool development)
        {
            var uri = new Uri(url ?? fallback, UriKind.Absolute);
            if (uri.Scheme != "https" && !(development && uri.IsLoopback && uri.Scheme == "http"))
                throw new InvalidOperationException("Account URLs must use HTTPS (HTTP loopback is allowed in Development).");
            if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
                throw new InvalidOperationException("Account URLs must be origins without paths, credentials or query strings.");
            return uri.GetLeftPart(UriPartial.Authority);
        }
        var publicUiUrl = PublicUrl(uiUrl, "http://127.0.0.1:8780", builder.Environment.IsDevelopment() || !enabled);
        var browserOrigins = new List<string> { publicUiUrl };
        foreach (var origin in (Value("TYM_AUTH_ALLOWED_ORIGINS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            browserOrigins.Add(PublicUrl(origin, publicUiUrl, builder.Environment.IsDevelopment()));
        if (builder.Environment.IsDevelopment()) browserOrigins.Add("http://localhost:8780");
        var settings = new AccountSettings
        {
            Enabled = enabled, RequireLoginForText = bool.TryParse(config["TYM_REQUIRE_LOGIN_FOR_TEXT"], out var requireLogin) && requireLogin,
            DataPath = dataPath, SqlConnectionString = sqlConnection, GoogleClientId = Value("TYM_GOOGLE_CLIENT_ID"),
            PublicApiUrl = PublicUrl(apiUrl, "http://127.0.0.1:8765", builder.Environment.IsDevelopment() || !enabled),
            PublicUiUrl = publicUiUrl, BrowserOrigins = browserOrigins.Distinct(StringComparer.Ordinal).ToArray(),
            MailHost = Value("TYM_SMTP_HOST"), MailFrom = Value("TYM_SMTP_FROM"), MailUser = Value("TYM_SMTP_USER"),
            MailPassword = config["TYM_SMTP_PASSWORD"], MailPort = int.TryParse(config["TYM_SMTP_PORT"], out var port) ? port : 587,
            MailPickupPath = builder.Environment.IsDevelopment() ? Value("TYM_MAIL_PICKUP_PATH") : null
        };
        builder.Services.AddSingleton(settings);
        if (enabled) builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        if (enabled && sqlConnection is null) Directory.CreateDirectory(dataPath);
        var protection = builder.Services.AddDataProtection().SetApplicationName("TYM.Accounts.v1");
        if (enabled && sqlConnection is not null) protection.PersistKeysToDbContext<AccountDb>();
        else if (enabled) protection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));
        builder.Services.AddDbContext<AccountDb>(options =>
        {
            if (sqlConnection is not null)
                options.UseSqlServer(sqlConnection, sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null));
            else options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                { DataSource = Path.Combine(dataPath, "accounts.db") }.ToString());
        });
        builder.Services.AddIdentityApiEndpoints<TymUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.SignIn.RequireConfirmedEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AccountDb>();
        builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
        {
            options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
            options.RefreshTokenExpiration = TimeSpan.FromHours(8);
        });
        builder.Services.AddSingleton<IEmailSender<TymUser>, AccountMailSender>();
        builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
        builder.Services.AddSingleton<GoogleChallenges>();
        builder.Services.AddAuthorizationBuilder().AddPolicy(ReaderPolicy, policy =>
        {
            policy.AddAuthenticationSchemes(IdentityConstants.BearerScheme).RequireAuthenticatedUser();
            policy.RequireAssertion(async context =>
            {
                if (context.Resource is not HttpContext http || !settings.Enabled) return false;
                var manager = http.RequestServices.GetRequiredService<UserManager<TymUser>>();
                var user = await manager.GetUserAsync(context.User);
                // Check the stamp on every source read so sign-out/password reset invalidates old access tokens.
                var stampClaim = http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
                return user is { EmailConfirmed: true } && !await manager.IsLockedOutAsync(user)
                    && context.User.FindFirstValue(stampClaim) == await manager.GetSecurityStampAsync(user);
            });
        });
        return settings;
    }

    public static async Task MapAccounts(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<AccountSettings>();
        // SQL schema is initialized by the deployment administrator, not the runtime identity.
        // A paused account database must not prevent anonymous diagram/reading startup.
        if (settings.Enabled && settings.SqlConnectionString is null)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AccountDb>().Database.EnsureCreatedAsync();
        }
        var group = app.MapGroup("/v1/account").RequireCors("account-browser").RequireRateLimiting(TymRateLimit.AuthPolicyName);
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            var path = http.Request.Path.Value!;
            if (path.EndsWith("/config", StringComparison.Ordinal)) return await next(context);
            if (!settings.Enabled) return Results.Json(new ErrorResponse("Sign-in is not configured."), statusCode: 503);
            if ((http.Request.Query.TryGetValue("useCookies", out var cookie) && cookie != "false")
                || http.Request.Query.ContainsKey("useSessionCookies"))
                return Results.BadRequest(new ErrorResponse("Only bearer-token sign-in is supported."));
            if (!settings.EmailEnabled && (path.EndsWith("/register") || path.EndsWith("/forgotPassword")
                || path.EndsWith("/resendConfirmationEmail") || (path.EndsWith("/manage/info") && http.Request.Method == "POST")))
                return Results.Json(new ErrorResponse("Email registration and recovery are unavailable until a mail sender is configured."), statusCode: 503);
            try { return await next(context); }
            catch (AccountMailUnavailableException)
            { return Results.Json(new ErrorResponse("Email delivery is temporarily unavailable. Retry confirmation or recovery later."), statusCode: 503); }
        });
        group.MapGet("/config", () => Results.Json(new
        {
            enabled = settings.Enabled, email_enabled = settings.EmailEnabled,
            email_delivery = settings.EmailEnabled ? settings.MailPickupPath is null ? "smtp" : "local_test" : "unavailable",
            text_requires_login = settings.RequireLoginForText,
            google_client_id = settings.GoogleEnabled ? settings.GoogleClientId : null
        })).RequireCors(policy => policy.AllowAnyOrigin().WithMethods("GET"));
        group.MapIdentityApi<TymUser>();
        group.MapGet("/me", async (ClaimsPrincipal principal, UserManager<TymUser> users) =>
        {
            var user = (await users.GetUserAsync(principal))!;
            return Results.Json(new { email = user.Email, email_confirmed = user.EmailConfirmed });
        }).RequireAuthorization(ReaderPolicy);
        group.MapPost("/logout", async (ClaimsPrincipal principal, UserManager<TymUser> users) =>
        {
            var user = (await users.GetUserAsync(principal))!;
            var updated = await users.UpdateSecurityStampAsync(user);
            return updated.Succeeded ? Results.NoContent()
                : Results.Json(new ErrorResponse("Server sign-out failed. Please retry."), statusCode: 503);
        }).RequireAuthorization(ReaderPolicy);
        group.MapPost("/google/nonce", (GoogleChallenges challenges) => settings.GoogleEnabled
            ? Results.Json(new { nonce = challenges.Create() })
            : Results.Json(new ErrorResponse("Google sign-in is not configured."), statusCode: 503));
        group.MapPost("/google", async (GoogleLogin request, GoogleChallenges challenges, UserManager<TymUser> users,
            SignInManager<TymUser> signIn) =>
        {
            if (!settings.GoogleEnabled) return Results.Json(new ErrorResponse("Google sign-in is not configured."), statusCode: 503);
            if (string.IsNullOrWhiteSpace(request.Credential) || request.Credential.Length > 16_384 || !challenges.Consume(request.Nonce))
                return Results.Unauthorized();
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(request.Credential,
                    new GoogleJsonWebSignature.ValidationSettings { Audience = [settings.GoogleClientId!] });
                using var json = JsonDocument.Parse(WebEncoders.Base64UrlDecode(request.Credential.Split('.')[1]));
                if (!json.RootElement.TryGetProperty("nonce", out var nonce) || nonce.GetString() != request.Nonce) return Results.Unauthorized();
            }
            catch (Exception error) when (error is InvalidJwtException or FormatException or JsonException
                or Newtonsoft.Json.JsonException or ArgumentException or IndexOutOfRangeException)
            { return Results.Unauthorized(); }
            catch (HttpRequestException)
            { return Results.Json(new ErrorResponse("Google verification is temporarily unavailable."), statusCode: 503); }
            if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Subject)
                || !(payload.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(payload.HostedDomain)))
                return Results.BadRequest(new ErrorResponse("Use a verified Gmail or Google Workspace account, or register with email."));
            var user = await users.FindByLoginAsync("Google", payload.Subject);
            if (user is null)
            {
                // Never merge accounts merely because a provider supplies the same email address.
                if (await users.FindByEmailAsync(payload.Email) is not null)
                    return Results.Conflict(new ErrorResponse("This email already has an account. Sign in with its existing method."));
                user = new TymUser { UserName = payload.Email, Email = payload.Email, EmailConfirmed = true };
                var created = await users.CreateAsync(user);
                if (!created.Succeeded) return Results.BadRequest(new ErrorResponse("Unable to create this account. Use email sign-in."));
                var linked = await users.AddLoginAsync(user, new UserLoginInfo("Google", payload.Subject, "Google"));
                if (!linked.Succeeded) { await users.DeleteAsync(user); return Results.Conflict(new ErrorResponse("Unable to link Google sign-in.")); }
            }
            if (await users.IsLockedOutAsync(user) || !user.EmailConfirmed) return Results.Unauthorized();
            signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
            await signIn.SignInAsync(user, isPersistent: false);
            return Results.Empty;
        });
    }
}

public sealed record GoogleLogin(string Credential, string Nonce);
public sealed class AccountMailUnavailableException() : Exception("Email delivery unavailable.");

public sealed class GoogleChallenges : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 2_000 });
    private readonly object gate = new();
    public string Create()
    {
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        cache.Set(nonce, true, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5), Size = 1 });
        return nonce;
    }
    public bool Consume(string? nonce)
    {
        if (nonce is null || nonce.Length != 64) return false;
        lock (gate)
        {
            if (!cache.TryGetValue(nonce, out bool _)) return false;
            cache.Remove(nonce);
            return true;
        }
    }
    public void Dispose() => cache.Dispose();
}

public sealed class AccountMailSender(AccountSettings settings) : IEmailSender<TymUser>
{
    public Task SendConfirmationLinkAsync(TymUser user, string email, string confirmationLink)
    {
        var supplied = new Uri(System.Net.WebUtility.HtmlDecode(confirmationLink));
        var canonical = settings.PublicApiUrl + supplied.PathAndQuery;
        return Send(email, "Confirm your TYM email", $"Confirm your email: <a href=\"{HtmlEncoder.Default.Encode(canonical)}\">Confirm email</a>");
    }
    public Task SendPasswordResetLinkAsync(TymUser user, string email, string resetLink) =>
        Send(email, "Reset your TYM password", $"<a href=\"{HtmlEncoder.Default.Encode(resetLink)}\">Reset password</a>");
    public Task SendPasswordResetCodeAsync(TymUser user, string email, string resetCode)
    {
        var link = settings.PublicUiUrl + "/#resetEmail=" + Uri.EscapeDataString(email)
            + "&resetCode=" + Uri.EscapeDataString(resetCode);
        return Send(email, "Reset your TYM password", $"<a href=\"{HtmlEncoder.Default.Encode(link)}\">Reset password</a>");
    }
    private async Task Send(string email, string subject, string html)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("TYM", settings.MailFrom ?? "no-reply@localhost"));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = html };
        if (settings.MailPickupPath is { } pickup)
        {
            Directory.CreateDirectory(pickup);
            await message.WriteToAsync(Path.Combine(pickup, Guid.NewGuid().ToString("N") + ".eml"));
            return;
        }
        if (!settings.EmailEnabled) throw new InvalidOperationException("Email sender not configured.");
        try
        {
            using var smtp = new SmtpClient { Timeout = 15_000 };
            await smtp.ConnectAsync(settings.MailHost!, settings.MailPort,
                settings.MailPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);
            if (!string.IsNullOrWhiteSpace(settings.MailUser)) await smtp.AuthenticateAsync(settings.MailUser, settings.MailPassword ?? "");
            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);
        }
        catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException
            or MailKit.Security.AuthenticationException or MailKit.Security.SslHandshakeException
            or SmtpCommandException or SmtpProtocolException or TimeoutException)
        { throw new AccountMailUnavailableException(); }
    }
}
