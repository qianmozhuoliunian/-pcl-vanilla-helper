using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PclVersionBridge.Core.Friends;
namespace VanillaReach.Friends.Server;

public static class FriendsHost
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IPackReferenceValidator, ModrinthPackValidator>();
        configure?.Invoke(builder);
        var settings = builder.Configuration.GetSection("Friends").Get<FriendsSettings>() ?? new(); settings.Validate(builder.Environment.IsDevelopment());
        Directory.CreateDirectory(settings.DataDirectory);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<RequestGate>();
        builder.Services.AddHostedService<RetentionCleanup>(); builder.Services.AddHostedService<EmailDeliveryWorker>(); builder.Services.AddSingleton<EmailDispatcher>(); builder.Services.AddScoped<EmailQueue>();
        builder.Services.AddDbContext<FriendsDb>(o => o.UseSqlite("Data Source=" + Path.Combine(settings.DataDirectory, "friends.db")));
        builder.Services.AddIdentityCore<FriendUser>(o =>
        {
            o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 12; o.Password.RequireDigit = true; o.Password.RequireLowercase = true; o.Password.RequireUppercase = true; o.Password.RequireNonAlphanumeric = true;
            o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); o.SignIn.RequireConfirmedEmail = true;
        }).AddEntityFrameworkStores<FriendsDb>().AddDefaultTokenProviders();
        builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        var protection = builder.Services.AddDataProtection().SetApplicationName("VanillaReach.Friends.v1").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(settings.DataDirectory, "keys")));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        builder.Services.TryAddSingleton<IAccountEmailSender, AccountEmailSender>();
        builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<RequestOperation>(); builder.Services.AddScoped<SessionTickets>(); builder.Services.AddScoped<AccountService>(); builder.Services.AddScoped<SocialService>();
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, SessionAuthentication>("Bearer", _ => { }); builder.Services.AddAuthorization();
        builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 65536; o.Limits.MaxRequestHeadersTotalSize = 24576; });
        // Request logging excludes paths, payloads, email addresses and credentials.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning).AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning).AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        var app = builder.Build();
        if (app.Urls.Count == 0) foreach (var url in (builder.Configuration["urls"] ?? settings.PublicOrigin).Split(';', StringSplitOptions.RemoveEmptyEntries)) app.Urls.Add(url);
        if (settings.IsLocalDevelopment && app.Urls.Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var u) || !IPAddress.TryParse(u.Host.Trim('[', ']'), out var ip) || !IPAddress.IsLoopback(ip))) throw new InvalidOperationException("Development service must bind only literal loopback addresses.");
        using (var scope = app.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<FriendsDb>().Database.EnsureCreated();
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (settings.IsLocalDevelopment && (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? IPAddress.Any) || !IPAddress.IsLoopback(ctx.Connection.LocalIpAddress ?? IPAddress.Any))) { ctx.Response.StatusCode = 403; return; }
            if (!settings.IsLocalDevelopment && !ctx.Request.IsHttps) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new FriendApiError("https_required", "请使用安全的服务地址。")); return; }
            var operation = ctx.RequestServices.GetRequiredService<RequestOperation>();
            var originalBody = ctx.Response.Body; await using var responseBody = new MemoryStream(); ctx.Response.Body = responseBody;
            await operation.Begin(ctx.RequestAborted);
            try
            {

                try
                {
                    var social = ctx.RequestServices.GetRequiredService<SocialService>(); await social.Cleanup();
                    if (ctx.Request.Path.StartsWithSegments("/api/account"))
                    {
                        var route = ctx.Request.Path.Value ?? "";
                        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip + ":" + route)));
                        var emailRoute = route.EndsWith("forgot-password") || route.EndsWith("resend-confirmation");
                        await social.Rate("ip:" + key, emailRoute ? 10 : route.EndsWith("register") ? 20 : 120, TimeSpan.FromHours(1));
                    }
                    await next(ctx); await operation.Commit();
                }
                catch (ApiFailure ex)
                {
                    // Expected precondition failures can persist quota/Identity failed-password counters.
                    await operation.Commit(); responseBody.SetLength(0);
                    if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = ex.Status; if (ex.Retry is { } retry) ctx.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture); await ctx.Response.WriteAsJsonAsync(new FriendApiError(ex.Code, ex.Message, ex.Retry)); }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await operation.Rollback(); responseBody.SetLength(0);
                    if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = ex is BadHttpRequestException ? 400 : 503; await ctx.Response.WriteAsJsonAsync(new FriendApiError("unavailable", ex is BadHttpRequestException ? "输入内容无效，请检查后重试。" : "好友服务暂时不可用，请稍后重试。")); }
                }
            }
            finally { await operation.DisposeAsync(); ctx.Response.Body = originalBody; }
            responseBody.Position = 0; await responseBody.CopyToAsync(originalBody, ctx.RequestAborted);
        });
        app.UseAuthentication(); app.UseAuthorization(); MapRoutes(app); return app;
    }
    private static async Task<FriendUser> Me(HttpContext ctx, UserManager<FriendUser> users) => await users.FindByIdAsync(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "") ?? throw ApiFailure.Unauthorized();
    private static string Id(HttpContext ctx) => ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw ApiFailure.Unauthorized();
    private static void MapRoutes(WebApplication app)
    {
        app.MapGet("/health", (FriendsSettings settings) => Results.Ok(new { service = "VanillaReach.Friends", status = "ready", processId = settings.IsLocalDevelopment ? (int?)Environment.ProcessId : null }));
        app.MapPost("/api/account/register", async (RegisterFriendAccount x, AccountService s, CancellationToken ct) => { await s.Register(x, ct); return Results.NoContent(); });
        app.MapPost("/api/account/confirm", async (ConfirmFriendEmail x, AccountService s) => { await s.Confirm(x); return Results.NoContent(); });
        app.MapPost("/api/account/resend-confirmation", async (FriendEmail x, AccountService s, CancellationToken ct) => { await s.Email(x, false, ct); return Results.NoContent(); });
        app.MapPost("/api/account/login", async (FriendLogin x, AccountService s) => Results.Ok(await s.Login(x)));
        app.MapPost("/api/account/refresh", async (FriendRefresh x, AccountService s) => Results.Ok(await s.Refresh(x)));
        app.MapPost("/api/account/forgot-password", async (FriendEmail x, AccountService s, CancellationToken ct) => { await s.Email(x, true, ct); return Results.NoContent(); });
        app.MapPost("/api/account/reset-password", async (ResetFriendPassword x, AccountService s) => { await s.Reset(x); return Results.NoContent(); });
        var secured = app.MapGroup("/api").RequireAuthorization();
        foreach (var route in new[] { "logout", "logout-all" }) secured.MapPost("/account/" + route, async (HttpContext c, UserManager<FriendUser> u, SessionTickets s) => { await s.RevokeAsync(await Me(c, u)); return Results.NoContent(); });
        secured.MapPost("/account/change-password", async (ChangeFriendPassword x, HttpContext c, UserManager<FriendUser> u, AccountService s) => { await s.Change(await Me(c, u), x); return Results.NoContent(); });
        secured.MapPost("/account/delete", async (DeleteFriendAccount x, HttpContext c, UserManager<FriendUser> u, AccountService s) => { await s.Delete(await Me(c, u), x); return Results.NoContent(); });
        secured.MapGet("/me", async (HttpContext c, UserManager<FriendUser> u) => Results.Ok((await Me(c, u)).Profile()));
        secured.MapPatch("/me", async (UpdateFriendProfile x, HttpContext c, UserManager<FriendUser> users) => { var u = await Me(c, users); u.DisplayName = AccountService.Name(x.DisplayName); if (!(await users.UpdateAsync(u)).Succeeded) throw ApiFailure.Conflict(); return Results.Ok(u.Profile()); });
        secured.MapPost("/me/reset-code", async (HttpContext c, UserManager<FriendUser> users, AccountService s) => { var u = await Me(c, users); u.FriendCode = await s.NewCode(); if (!(await users.UpdateAsync(u)).Succeeded) throw ApiFailure.Conflict(); return Results.Ok(u.Profile()); });
        secured.MapGet("/profiles/{code}", async (string code, HttpContext c, SocialService s) => Results.Ok(await s.Lookup(Id(c), code)));
        secured.MapGet("/friends", async (int? page, HttpContext c, SocialService s) => Results.Ok(await s.ListFriends(Id(c), page ?? 0)));
        secured.MapDelete("/friends/{userId}", async (string userId, HttpContext c, SocialService s) => { await s.Remove(Id(c), userId); return Results.NoContent(); });
        secured.MapGet("/blocks", async (int? page, HttpContext c, SocialService s) => Results.Ok(await s.ListBlocks(Id(c), page ?? 0)));
        secured.MapPut("/blocks/{userId}", async (string userId, HttpContext c, SocialService s) => { await s.Block(Id(c), userId, true); return Results.NoContent(); });
        secured.MapDelete("/blocks/{userId}", async (string userId, HttpContext c, SocialService s) => { await s.Block(Id(c), userId, false); return Results.NoContent(); });
        secured.MapGet("/friend-requests", async (int? page, HttpContext c, SocialService s) => Results.Ok(await s.ListRequests(Id(c), page ?? 0)));
        secured.MapPost("/friend-requests", async (CreateFriendRequest x, HttpContext c, SocialService s) => { await s.Request(Id(c), x.FriendCode); return Results.NoContent(); });
        secured.MapPost("/friend-requests/{id}/{action}", async (string id, string action, HttpContext c, SocialService s) => { await s.ResolveRequest(Id(c), id, action); return Results.NoContent(); });
        secured.MapGet("/invitations", async (int? page, HttpContext c, SocialService s) => Results.Ok(await s.ListInvitations(Id(c), page ?? 0)));
        secured.MapGet("/invitations/{id}", async (string id, HttpContext c, SocialService s) => Results.Ok(await s.GetInvitation(Id(c), id)));
        secured.MapPost("/invitations", async (SendPackInvitation x, HttpContext c, SocialService s, CancellationToken ct) => Results.Ok(await s.SendInvitation(Id(c), x, ct)));
        secured.MapPost("/invitations/{id}/{action}", async (string id, string action, HttpContext c, SocialService s) => { await s.ResolveInvitation(Id(c), id, action); return Results.NoContent(); });
    }
}
internal sealed class RequestGate : IDisposable { public SemaphoreSlim Lock { get; } = new(1, 1); public void Dispose() => Lock.Dispose(); }
internal sealed class RetentionCleanup(IServiceScopeFactory scopes, RequestGate gate, FriendsSettings settings) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await gate.Lock.WaitAsync(stoppingToken); try
            {
                await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<SocialService>().Cleanup();
                if (settings.DevelopmentInbox is { } inbox && Directory.Exists(inbox)) foreach (var file in Directory.EnumerateFiles(inbox, "*.json")) if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1)) File.Delete(file);
            }
            finally { gate.Lock.Release(); }
        }
    }
}
public static class Program { public static Task Main(string[] args) => FriendsHost.Build(args).RunAsync(); }
