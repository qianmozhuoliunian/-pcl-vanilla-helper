using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PclVersionBridge.Core.Friends;
using VanillaReach.Friends.Server;
namespace VanillaReach.Friends.Server.Tests;

public sealed class FriendsHttpTests
{
    [Fact]
    public async Task RegistrationRequiresConfirmationAndLogoutRevokesBothTickets()
    {
        await using var host = await TestHost.Start();
        using var a = host.Client();
        await host.Register(a, "alice");
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync("/api/account/login", new FriendLogin("alice@example.test", TestHost.Password))).StatusCode);
        var session = await host.ConfirmAndLogin(a, "alice");
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/me")).StatusCode);
        Assert.DoesNotContain("email", await (await a.GetAsync("/api/me")).Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/account/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync("/api/account/refresh", new FriendRefresh(session.RefreshToken))).StatusCode);
    }
    [Fact]
    public async Task ConsentIsRequiredAndThirdPartyCannotAcceptOrReadCards()
    {
        await using var host = await TestHost.Start();
        using var a = host.Client(); using var b = host.Client(); using var c = host.Client();
        var sa = await host.Account(a, "a"); var sb = await host.Account(b, "b"); await host.Account(c, "c");
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/friend-requests", new CreateFriendRequest(sb.Me.FriendCode))).StatusCode);
        Assert.Equal(0, (await a.GetFromJsonAsync<FriendPage<FriendProfile>>("/api/friends"))!.Total);
        var request = (await b.GetFromJsonAsync<FriendPage<FriendRequest>>("/api/friend-requests"))!.Items.Single();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync($"/api/friend-requests/{request.Id}/accept", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsJsonAsync($"/api/friend-requests/{request.Id}/accept", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsJsonAsync($"/api/friend-requests/{request.Id}/accept", new { })).StatusCode);
        Assert.Equal(1, (await a.GetFromJsonAsync<FriendPage<FriendProfile>>("/api/friends"))!.Total);
        var card = await (await a.PostAsJsonAsync("/api/invitations", new SendPackInvitation(sb.Me.UserId, TestHost.Pack, "first"))).Content.ReadFromJsonAsync<PackInvitation>();
        Assert.NotNull(card);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/invitations/{card.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync($"/api/invitations/{card.Id}")).StatusCode);
        await b.PutAsJsonAsync($"/api/blocks/{sa.Me.UserId}", new { });
        Assert.Equal(0, (await a.GetFromJsonAsync<FriendPage<FriendProfile>>("/api/friends"))!.Total);
        Assert.Equal(HttpStatusCode.Conflict, (await b.GetAsync($"/api/invitations/{card.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/profiles/{sb.Me.FriendCode}")).StatusCode);
        await b.DeleteAsync($"/api/blocks/{sa.Me.UserId}");
        Assert.Equal(0, (await a.GetFromJsonAsync<FriendPage<FriendProfile>>("/api/friends"))!.Total);
    }
    [Fact]
    public async Task DuplicateAndCrossedRequestsNeverAutoAcceptAndRejectedRequestHasCooldown()
    {
        await using var host = await TestHost.Start(); using var a = host.Client(); using var b = host.Client();
        var sa = await host.Account(a, "a"); var sb = await host.Account(b, "b");
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => a.PostAsJsonAsync("/api/friend-requests", new CreateFriendRequest(sb.Me.FriendCode))));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Equal(HttpStatusCode.Conflict, (await b.PostAsJsonAsync("/api/friend-requests", new CreateFriendRequest(sa.Me.FriendCode))).StatusCode);
        var req = (await b.GetFromJsonAsync<FriendPage<FriendRequest>>("/api/friend-requests"))!.Items.Single();
        Assert.Equal(0, (await b.GetFromJsonAsync<FriendPage<FriendProfile>>("/api/friends"))!.Total);
        await b.PostAsJsonAsync($"/api/friend-requests/{req.Id}/reject", new { });
        var retry = await a.PostAsJsonAsync("/api/friend-requests", new CreateFriendRequest(sb.Me.FriendCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, retry.StatusCode); Assert.NotNull(retry.Headers.RetryAfter);
        host.Clock.Advance(TimeSpan.FromDays(2));
        await host.Login(a, "a"); await host.Login(b, "b");
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/friend-requests", new CreateFriendRequest(sb.Me.FriendCode))).StatusCode);
        host.Clock.Advance(TimeSpan.FromDays(8));
        // Refresh after advancing time, because access credentials must also expire.
        await host.Login(a, "a"); await host.Login(b, "b");
        var expired = (await b.GetFromJsonAsync<FriendPage<FriendRequest>>("/api/friend-requests"))!.Items.First(x => x.State == "expired");
        Assert.Equal(HttpStatusCode.Conflict, (await b.PostAsJsonAsync($"/api/friend-requests/{expired.Id}/accept", new { })).StatusCode);
    }
    [Fact]
    public async Task RefreshRotatesAndPasswordResetRevokesAndDeleteErasesPublicIdentity()
    {
        await using var host = await TestHost.Start(); using var a = host.Client();
        var session = await host.Account(a, "a");
        var refresh = await a.PostAsJsonAsync("/api/account/refresh", new FriendRefresh(session.RefreshToken)); Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync("/api/account/refresh", new FriendRefresh(session.RefreshToken))).StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<FriendSession>())!; a.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
        await a.PostAsJsonAsync("/api/account/forgot-password", new FriendEmail("a@example.test"));
        var code = host.Code("a@example.test", "reset-password");
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/account/reset-password", new ResetFriendPassword("a@example.test", code, "Another!Password2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/me")).StatusCode);
        var login = (await (await a.PostAsJsonAsync("/api/account/login", new FriendLogin("a@example.test", "Another!Password2"))).Content.ReadFromJsonAsync<FriendSession>())!;
        a.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/account/delete", new DeleteFriendAccount("Another!Password2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/me")).StatusCode);
    }
    [Fact]
    public async Task RelationshipsAndSessionsSurviveRestartAndInvitationsAreIdempotent()
    {
        var dir = TestHost.NewDirectory(); FriendSession sa, sb; string cardId;
        await using (var h = await TestHost.Start(dir))
        {
            using var a = h.Client(); using var b = h.Client(); sa = await h.Account(a, "a"); sb = await h.Account(b, "b");
            await a.PostAsJsonAsync("/api/friend-requests", new CreateFriendRequest(sb.Me.FriendCode)); var req = (await b.GetFromJsonAsync<FriendPage<FriendRequest>>("/api/friend-requests"))!.Items.Single(); await b.PostAsJsonAsync($"/api/friend-requests/{req.Id}/accept", new { });
            var send = new SendPackInvitation(sb.Me.UserId, TestHost.Pack, "same"); var one = (await (await a.PostAsJsonAsync("/api/invitations", send)).Content.ReadFromJsonAsync<PackInvitation>())!; var two = (await (await a.PostAsJsonAsync("/api/invitations", send)).Content.ReadFromJsonAsync<PackInvitation>())!;
            Assert.Equal(one.Id, two.Id); cardId = one.Id;
            Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/invitations", send with { Pack = TestHost.Pack with { ArchiveSha512 = "invalid" }, IdempotencyKey = "bad" })).StatusCode);
        }
        await using (var h = await TestHost.Start(dir))
        {
            using var a = h.Client(); using var b = h.Client(); a.DefaultRequestHeaders.Authorization = new("Bearer", sa.AccessToken); b.DefaultRequestHeaders.Authorization = new("Bearer", sb.AccessToken);
            Assert.Equal(1, (await a.GetFromJsonAsync<FriendPage<FriendProfile>>("/api/friends"))!.Total); Assert.Equal(HttpStatusCode.OK, (await b.GetAsync($"/api/invitations/{cardId}")).StatusCode);
            await a.DeleteAsync($"/api/friends/{sb.Me.UserId}"); Assert.Equal(HttpStatusCode.Conflict, (await b.GetAsync($"/api/invitations/{cardId}")).StatusCode);
        }
    }
    [Fact]
    public async Task LookupQuotaAndDailyRequestQuotaAreServerEnforced()
    {
        await using var h = await TestHost.Start(); using var a = h.Client(); var sa = await h.Account(a, "a");
        for (var i = 0; i < 30; i++) Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/profiles/{sa.Me.FriendCode}")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await a.GetAsync($"/api/profiles/{sa.Me.FriendCode}")).StatusCode);
        for (var i = 0; i < 11; i++) { using var b = h.Client(); var sb = await h.Account(b, "u" + i); var r = await a.PostAsJsonAsync("/api/friend-requests", new CreateFriendRequest(sb.Me.FriendCode)); Assert.Equal(i == 10 ? HttpStatusCode.TooManyRequests : HttpStatusCode.NoContent, r.StatusCode); }
    }
    [Fact]
    public async Task ForgedExpiredAndOversizedTokensAreDenied()
    {
        await using var h = await TestHost.Start(); using var a = h.Client(); var s = await h.Account(a, "a");
        a.DefaultRequestHeaders.Authorization = new("Bearer", s.AccessToken + "bad"); Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/me")).StatusCode);
        a.DefaultRequestHeaders.Authorization = new("Bearer", s.AccessToken); h.Clock.Advance(TimeSpan.FromMinutes(16)); Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync("/api/account/refresh", new FriendRefresh(s.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync("/api/account/refresh", new FriendRefresh(new string('x', 20000)))).StatusCode);
    }
}
internal sealed class TestHost : IAsyncDisposable
{
    public const string Password = "Strong!Password1";
    public static readonly PackReference Pack = new("fixtureProject", "fixtureVersion", new string('a', 128));
    public string DirectoryPath { get; private init; } = "";
    public TestClock Clock { get; private init; } = new();
    private WebApplication App { get; init; } = null!;
    public static string NewDirectory() => Path.GetFullPath(Path.Combine(".artifacts", "friends-tests", Guid.NewGuid().ToString("N")));
    public static async Task<TestHost> Start(string? dir = null, IPackReferenceValidator? validator = null, IAccountEmailSender? sender = null)
    {
        dir ??= NewDirectory(); var clock = new TestClock();
        var app = FriendsHost.Build(["--environment", "Development"], b =>
        {
            b.WebHost.UseUrls("http://127.0.0.1:0"); b.Logging.ClearProviders();
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Friends:PublicOrigin"] = "http://127.0.0.1", ["Friends:DataDirectory"] = dir, ["Friends:DevelopmentInbox"] = Path.Combine(dir, "inbox") });
            b.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            b.Services.Replace(ServiceDescriptor.Singleton<IPackReferenceValidator>(validator ?? new FixtureValidator()));
            if (sender is not null) b.Services.AddSingleton(sender);
        });
        await app.StartAsync(); return new() { App = app, DirectoryPath = dir, Clock = clock };
    }
    public IServiceProvider Services => App.Services;
    public HttpClient Client() => new() { BaseAddress = new Uri(App.Urls.Single()) };
    public async Task Register(HttpClient c, string name) => Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/account/register", new RegisterFriendAccount(name + "@example.test", Password, name))).StatusCode);
    public string Code(string email, string action) => System.IO.Directory.GetFiles(Path.Combine(DirectoryPath, "inbox"), "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Select(File.ReadAllText).Select(x => JsonSerializer.Deserialize<InboxMessage>(x, new JsonSerializerOptions(JsonSerializerDefaults.Web))!).First(x => x.Email == email && x.Action == action).Code;
    public async Task<FriendSession> ConfirmAndLogin(HttpClient c, string name) { Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/account/confirm", new ConfirmFriendEmail(name + "@example.test", Code(name + "@example.test", "confirm-email")))).StatusCode); return await Login(c, name); }
    public async Task<FriendSession> Login(HttpClient c, string name) { var r = await c.PostAsJsonAsync("/api/account/login", new FriendLogin(name + "@example.test", Password)); Assert.Equal(HttpStatusCode.OK, r.StatusCode); var session = (await r.Content.ReadFromJsonAsync<FriendSession>())!; c.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken); return session; }
    public async Task<FriendSession> Account(HttpClient c, string name) { await Register(c, name); return await ConfirmAndLogin(c, name); }
    public ValueTask DisposeAsync() => App.DisposeAsync();
    private sealed record InboxMessage(string Email, string Code, string Action);
    private sealed class FixtureValidator : IPackReferenceValidator { public Task ValidateAsync(PackReference pack, CancellationToken ct) { if (pack.VersionId == "sourceUnavailable") throw new ApiFailure(503, "source_unavailable", "来源暂时不可用。"); if (pack != Pack) throw new ApiFailure(400, "invalid_pack", "整合包版本无效。"); return Task.CompletedTask; } }
}
internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}
