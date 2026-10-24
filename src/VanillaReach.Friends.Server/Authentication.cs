using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PclVersionBridge.Core.Friends;
namespace VanillaReach.Friends.Server;

internal sealed class SessionTickets(IDataProtectionProvider protection, TimeProvider clock, FriendsDb db, UserManager<FriendUser> users)
{
    private TicketDataFormat Format(string purpose) => new(protection.CreateProtector("VanillaReach.Friends.v1", purpose));
    private static string Nonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public async Task<FriendSession> CreateAsync(FriendUser user, AccountSession? existing = null)
    {
        var now = clock.GetUtcNow(); var session = existing ?? new AccountSession { UserId = user.Id, Expires = now.AddDays(14).ToUnixTimeMilliseconds() };
        session.RefreshNonce = Nonce(); if (existing is null) db.Sessions.Add(session); await db.SaveChangesAsync();
        var expires = now.AddMinutes(15);
        string Make(string purpose, DateTimeOffset end) => Format(purpose).Protect(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim("stamp", user.SecurityStamp!), new Claim("session", session.Id), new Claim("nonce", session.RefreshNonce) }, "Bearer")), new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = end }, "Bearer"));
        return new(Make("access", expires), Make("refresh", DateTimeOffset.FromUnixTimeMilliseconds(session.Expires)), expires, user.Profile());
    }
    public async Task<(FriendUser User, AccountSession Session, ClaimsPrincipal Principal)?> ValidateAsync(string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 12000) return null;
        AuthenticationTicket? ticket;
        try { ticket = Format(purpose).Unprotect(token); } catch (CryptographicException) { return null; }
        if (ticket?.Properties.ExpiresUtc is not { } expiry || expiry <= clock.GetUtcNow()) return null;
        var id = ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier); var sid = ticket.Principal.FindFirstValue("session");
        var user = id is null ? null : await users.FindByIdAsync(id);
        if (user is null || !user.EmailConfirmed || user.SecurityStamp != ticket.Principal.FindFirstValue("stamp")) return null;
        var session = await db.Sessions.FindAsync(sid);
        if (session is null || session.UserId != user.Id || session.Expires <= clock.GetUtcNow().ToUnixTimeMilliseconds()) return null;
        if (purpose == "refresh" && session.RefreshNonce != ticket.Principal.FindFirstValue("nonce")) return null;
        return (user, session, ticket.Principal);
    }
    public async Task RevokeAsync(FriendUser user)
    {
        await users.UpdateSecurityStampAsync(user);
        await db.Sessions.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
    }
}
internal sealed class SessionAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, SessionTickets tickets) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var session = await tickets.ValidateAsync(header[7..], "access");
        return session is null ? AuthenticateResult.Fail("Invalid session") : AuthenticateResult.Success(new AuthenticationTicket(session.Value.Principal, "Bearer"));
    }
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    { Response.StatusCode = 401; await Response.WriteAsJsonAsync(new FriendApiError("unauthorized", "登录已失效，请重新登录。")); }
    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    { Response.StatusCode = 403; await Response.WriteAsJsonAsync(new FriendApiError("forbidden", "没有权限执行此操作。")); }
}
internal sealed class AccountService(FriendsDb db, UserManager<FriendUser> users, SessionTickets tickets, EmailQueue email)
{
    public static string ValidEmail(string? value)
    {
        var result = value?.Trim() ?? "";
        if (result.Length is < 3 or > 254 || !System.Net.Mail.MailAddress.TryCreate(result, out var parsed) || parsed.Address != result) throw ApiFailure.Invalid(); return result;
    }
    public static string Name(string? value)
    { var name = value?.Trim() ?? ""; if (name.Length is < 1 or > 40 || name.Any(char.IsControl)) throw ApiFailure.Invalid(); return name; }
    public static void Password(string? value) { if (value is null || value.Length is < 12 or > 128) throw ApiFailure.Invalid(); }
    public async Task<string> NewCode()
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        for (var attempt = 0; attempt < 10; attempt++) { var code = new string(RandomNumberGenerator.GetBytes(12).Select(x => alphabet[x % 32]).ToArray()); if (!await db.Users.AnyAsync(x => x.FriendCode == code)) return code; }
        throw new ApiFailure(503, "unavailable", "服务暂时不可用，请稍后重试。");
    }
    public async Task Register(RegisterFriendAccount input, CancellationToken ct)
    {
        var address = ValidEmail(input.Email); Password(input.Password); var name = Name(input.DisplayName);
        if (await users.FindByEmailAsync(address) is not null) return;
        var u = new FriendUser { UserName = address, Email = address, DisplayName = name, FriendCode = await NewCode() };
        if (!(await users.CreateAsync(u, input.Password)).Succeeded) throw ApiFailure.Invalid();
        var code = await users.GenerateEmailConfirmationTokenAsync(u);
        await email.Enqueue(u.Id, address, code, "confirm-email", ct);
    }
    public async Task Confirm(ConfirmFriendEmail input)
    {
        var u = await users.FindByEmailAsync(ValidEmail(input.Email));
        if (u is null || string.IsNullOrEmpty(input.Code) || input.Code.Length > 6000 || !(await users.ConfirmEmailAsync(u, input.Code)).Succeeded) throw ApiFailure.Invalid();
    }
    public async Task Email(FriendEmail input, bool reset, CancellationToken ct)
    {
        var u = await users.FindByEmailAsync(ValidEmail(input.Email)); if (u is null || (!reset && u.EmailConfirmed) || (reset && !u.EmailConfirmed)) return;
        var code = reset ? await users.GeneratePasswordResetTokenAsync(u) : await users.GenerateEmailConfirmationTokenAsync(u);
        await email.Enqueue(u.Id, u.Email!, code, reset ? "reset-password" : "confirm-email", ct);
    }
    public async Task<FriendSession> Login(FriendLogin input)
    {
        var address = ValidEmail(input.Email); Password(input.Password); var u = await users.FindByEmailAsync(address);
        if (u is null)
        { // Keep missing-account password work comparable to a real verification.
            _ = new PasswordHasher<FriendUser>().HashPassword(new FriendUser(), input.Password); throw ApiFailure.Unauthorized();
        }
        if (await users.IsLockedOutAsync(u)) throw ApiFailure.Unauthorized();
        if (!await users.CheckPasswordAsync(u, input.Password)) { await users.AccessFailedAsync(u); throw ApiFailure.Unauthorized(); }
        if (!u.EmailConfirmed) throw ApiFailure.Unauthorized(); await users.ResetAccessFailedCountAsync(u); return await tickets.CreateAsync(u);
    }
    public async Task<FriendSession> Refresh(FriendRefresh input)
    { var valid = await tickets.ValidateAsync(input.RefreshToken, "refresh") ?? throw ApiFailure.Unauthorized(); return await tickets.CreateAsync(valid.User, valid.Session); }
    public async Task Reset(ResetFriendPassword input)
    {
        Password(input.NewPassword); var u = await users.FindByEmailAsync(ValidEmail(input.Email));
        if (u is null || string.IsNullOrEmpty(input.Code) || input.Code.Length > 6000 || !(await users.ResetPasswordAsync(u, input.Code, input.NewPassword)).Succeeded) throw ApiFailure.Invalid(); await tickets.RevokeAsync(u);
    }
    public async Task Change(FriendUser u, ChangeFriendPassword input)
    { Password(input.NewPassword); Password(input.CurrentPassword); if (!(await users.ChangePasswordAsync(u, input.CurrentPassword, input.NewPassword)).Succeeded) throw ApiFailure.Invalid(); await tickets.RevokeAsync(u); }
    public async Task Delete(FriendUser u, DeleteFriendAccount input)
    {
        Password(input.Password); if (!await users.CheckPasswordAsync(u, input.Password)) throw ApiFailure.Unauthorized();
        await db.Friendships.Where(x => x.First == u.Id || x.Second == u.Id).ExecuteDeleteAsync();
        await db.Blocks.Where(x => x.FromId == u.Id || x.ToId == u.Id).ExecuteDeleteAsync();
        await db.Requests.Where(x => x.FromId == u.Id || x.ToId == u.Id).ExecuteDeleteAsync();
        await db.Invitations.Where(x => x.FromId == u.Id || x.ToId == u.Id).ExecuteDeleteAsync();
        await db.Rates.Where(x => x.Id.StartsWith("user:" + u.Id + ":")).ExecuteDeleteAsync();
        if (!(await users.DeleteAsync(u)).Succeeded) throw ApiFailure.Conflict();
    }
}
