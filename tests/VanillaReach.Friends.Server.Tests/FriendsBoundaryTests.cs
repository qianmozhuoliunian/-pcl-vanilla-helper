using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PclVersionBridge.Core.Friends;
using VanillaReach.Friends.Server;
namespace VanillaReach.Friends.Server.Tests;

public sealed class FriendsBoundaryTests
{
    [Fact]
    public void ProductionRefusesHttpOrDevelopmentInboxAndMissingSmtp()
    {
        foreach (var item in new[] { ("http://127.0.0.1/", (string?)null), ("https://friends.example.test/", TestHost.NewDirectory()), ("https://friends.example.test/", (string?)null) })
            Assert.Throws<InvalidOperationException>(() => FriendsHost.Build(["--environment", "Production"], b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Friends:PublicOrigin"] = item.Item1, ["Friends:DataDirectory"] = TestHost.NewDirectory(), ["Friends:DevelopmentInbox"] = item.Item2 })));
    }
    [Fact]
    public async Task HealthAndProfileResetWorkAndOldCodeImmediatelyStopsResolving()
    {
        await using var h = await TestHost.Start(); using var a = h.Client(); using var b = h.Client(); var sa = await h.Account(a, "a"); await h.Account(b, "b");
        Assert.Contains("VanillaReach.Friends", await a.GetStringAsync("/health"));
        Assert.Equal(HttpStatusCode.OK, (await a.PatchAsJsonAsync("/api/me", new UpdateFriendProfile("新名字"))).StatusCode);
        var updated = (await a.GetFromJsonAsync<FriendProfile>("/api/me"))!; Assert.Equal("新名字", updated.DisplayName);
        var reset = (await (await a.PostAsJsonAsync("/api/me/reset-code", new { })).Content.ReadFromJsonAsync<FriendProfile>())!;
        Assert.Equal(sa.Me.UserId, reset.UserId); Assert.NotEqual(sa.Me.FriendCode, reset.FriendCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/profiles/" + sa.Me.FriendCode)).StatusCode);
        var pretty = reset.FriendCode[..4] + "-" + reset.FriendCode[4..8] + "-" + reset.FriendCode[8..];
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/api/profiles/" + pretty.ToLowerInvariant())).StatusCode);
    }
    [Fact]
    public async Task InvitationsExpireAndDailyThirtyLimitCannotBeBypassedWithDifferentKeys()
    {
        await using var h = await TestHost.Start(); using var a = h.Client(); using var b = h.Client(); var sa = await h.Account(a, "a"); var sb = await h.Account(b, "b"); await Connect(a, b, sb);
        string? id = null;
        for (var i = 0; i < 31; i++)
        {
            var response = await a.PostAsJsonAsync("/api/invitations", new SendPackInvitation(sb.Me.UserId, TestHost.Pack, "key" + i));
            Assert.Equal(i == 30 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK, response.StatusCode);
            if (i == 0) id = (await response.Content.ReadFromJsonAsync<PackInvitation>())!.Id;
        }
        h.Clock.Advance(TimeSpan.FromDays(8)); await h.Login(b, "b");
        Assert.Equal(HttpStatusCode.Conflict, (await b.GetAsync("/api/invitations/" + id)).StatusCode);
        var page = (await b.GetFromJsonAsync<FriendPage<PackInvitation>>("/api/invitations?page=1"))!; Assert.Equal(30, page.Total); Assert.Equal(10, page.Items.Count); Assert.All(page.Items, x => Assert.Equal("expired", x.State));
    }
    [Fact]
    public async Task SourceFailureNeverProducesASavedInvitation()
    {
        await using var h = await TestHost.Start(); using var a = h.Client(); using var b = h.Client(); await h.Account(a, "a"); var sb = await h.Account(b, "b"); await Connect(a, b, sb);
        var response = await a.PostAsJsonAsync("/api/invitations", new SendPackInvitation(sb.Me.UserId, TestHost.Pack with { VersionId = "sourceUnavailable" }, "badsource"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, (await b.GetFromJsonAsync<FriendPage<PackInvitation>>("/api/invitations"))!.Total);
    }
    [Fact]
    public async Task RacingBlockAndSendCannotLeaveAnActiveInvitationOrRestoreFriendship()
    {
        await using var h = await TestHost.Start(); using var a = h.Client(); using var b = h.Client(); var sa = await h.Account(a, "a"); var sb = await h.Account(b, "b"); await Connect(a, b, sb);
        var send = a.PostAsJsonAsync("/api/invitations", new SendPackInvitation(sb.Me.UserId, TestHost.Pack, "racing"));
        var block = b.PutAsJsonAsync("/api/blocks/" + sa.Me.UserId, new { }); await Task.WhenAll(send, block);
        Assert.Equal(HttpStatusCode.NoContent, (await block).StatusCode);
        Assert.Contains((await send).StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Forbidden });
        Assert.Equal(0, (await a.GetFromJsonAsync<FriendPage<FriendProfile>>("/api/friends"))!.Total);
        var cards = (await b.GetFromJsonAsync<FriendPage<PackInvitation>>("/api/invitations"))!; Assert.DoesNotContain(cards.Items, x => x.State == "pending");
        await b.DeleteAsync("/api/blocks/" + sa.Me.UserId); Assert.Equal(0, (await b.GetFromJsonAsync<FriendPage<FriendProfile>>("/api/friends"))!.Total);
    }
    [Fact]
    public async Task PendingAndFriendCapacityApplyAtSendAndAcceptAndDeletionCleansSocialRows()
    {
        await using var h = await TestHost.Start(); using var a = h.Client(); using var b = h.Client(); var sa = await h.Account(a, "a"); var sb = await h.Account(b, "b");
        using (var scope = h.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FriendsDb>(); var now = h.Clock.GetUtcNow().ToUnixTimeMilliseconds();
            for (var i = 0; i < 20; i++) db.Requests.Add(new() { FromId = sa.Me.UserId, ToId = "seed" + i, PendingPair = "seed" + i, Created = now, Updated = now, Expires = now + 600000 }); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await a.PostAsJsonAsync("/api/friend-requests", new CreateFriendRequest(sb.Me.FriendCode))).StatusCode);
        using (var scope = h.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<FriendsDb>(); await db.Requests.ExecuteDeleteAsync(); }
        await a.PostAsJsonAsync("/api/friend-requests", new CreateFriendRequest(sb.Me.FriendCode)); var req = (await b.GetFromJsonAsync<FriendPage<FriendRequest>>("/api/friend-requests"))!.Items.Single();
        using (var scope = h.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<FriendsDb>(); for (var i = 0; i < 200; i++) db.Friendships.Add(new() { Pair = "capacity" + i, First = sb.Me.UserId, Second = "seed" + i }); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await b.PostAsJsonAsync($"/api/friend-requests/{req.Id}/accept", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsJsonAsync("/api/account/delete", new DeleteFriendAccount(TestHost.Password))).StatusCode);
        using (var scope = h.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<FriendsDb>(); Assert.Equal(0, await db.Friendships.CountAsync()); Assert.Equal(0, await db.Requests.CountAsync()); Assert.False(await db.Users.AnyAsync(x => x.Id == sb.Me.UserId)); }
    }
    [Fact]
    public async Task ChangePasswordRevokesEveryDeviceAndWrongPasswordAttemptsLockOut()
    {
        await using var h = await TestHost.Start(); using var a = h.Client(); using var second = h.Client(); var sa = await h.Account(a, "a"); var other = await h.Login(second, "a");
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/account/change-password", new ChangeFriendPassword(TestHost.Password, "Changed!Password2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.PostAsJsonAsync("/api/account/refresh", new FriendRefresh(other.RefreshToken))).StatusCode);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync("/api/account/login", new FriendLogin("a@example.test", "Wrong!Password2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync("/api/account/login", new FriendLogin("a@example.test", "Changed!Password2"))).StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowSourceDoesNotBlockOtherClientsAndRevocationDuringValidationDeniesTheCard(bool logout)
    {
        var validator = new PausedValidator(); await using var h = await TestHost.Start(validator: validator);
        using var a = h.Client(); using var b = h.Client(); var sa = await h.Account(a, "a"); var sb = await h.Account(b, "b"); await Connect(a, b, sb);
        var send = a.PostAsJsonAsync("/api/invitations", new SendPackInvitation(sb.Me.UserId, TestHost.Pack, "paused"));
        await validator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/health").WaitAsync(TimeSpan.FromSeconds(2))).StatusCode);
            if (logout) Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/account/logout-all", new { }).WaitAsync(TimeSpan.FromSeconds(2))).StatusCode);
            else Assert.Equal(HttpStatusCode.NoContent, (await b.PutAsJsonAsync("/api/blocks/" + sa.Me.UserId, new { }).WaitAsync(TimeSpan.FromSeconds(2))).StatusCode);
        }
        finally { validator.Resume.TrySetResult(); }
        Assert.Equal(logout ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, (await send).StatusCode);
        Assert.Equal(0, (await b.GetFromJsonAsync<FriendPage<PackInvitation>>("/api/invitations"))!.Total);
    }
    private sealed class PausedValidator : IPackReferenceValidator
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ValidateAsync(PackReference pack, CancellationToken ct) { Entered.TrySetResult(); await Resume.Task.WaitAsync(ct); }
    }
    [Fact]
    public async Task EmailProviderFailureDoesNotRevealAccountExistenceAndRegistrationCanBeRecovered()
    {
        await using var h = await TestHost.Start(sender: new FailedSender()); using var a = h.Client();
        var register = await a.PostAsJsonAsync("/api/account/register", new RegisterFriendAccount("a@example.test", TestHost.Password, "a"));
        Assert.Equal(HttpStatusCode.NoContent, register.StatusCode);
        var known = await a.PostAsJsonAsync("/api/account/resend-confirmation", new FriendEmail("a@example.test"));
        var unknown = await a.PostAsJsonAsync("/api/account/resend-confirmation", new FriendEmail("unknown@example.test"));
        Assert.Equal(HttpStatusCode.NoContent, known.StatusCode); Assert.Equal(known.StatusCode, unknown.StatusCode);
        using var scope = h.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<FriendsDb>(); Assert.True(await db.Users.AnyAsync(x => x.Email == "a@example.test"));
    }
    private sealed class FailedSender : IAccountEmailSender
    { public Task SendAsync(string email, string code, string action, CancellationToken ct) => throw new System.Net.Mail.SmtpException("Simulated offline SMTP"); }
    [Fact]
    public async Task FailedEmailRemainsEncryptedAndIsDeliveredAfterRestart()
    {
        var dir = TestHost.NewDirectory();
        await using (var h = await TestHost.Start(dir, sender: new FailedSender()))
        {
            using var a = h.Client(); await h.Register(a, "outbox");
            using var scope = h.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<FriendsDb>(); var row = await db.EmailOutbox.SingleAsync();
            Assert.DoesNotContain("outbox@example.test", row.ProtectedPayload); Assert.DoesNotContain("confirm-email", row.ProtectedPayload);
            // Make the retry due, simulating the one-minute backoff passing while the process is stopped.
            row.NextAttempt = 0; await db.SaveChangesAsync();
        }
        await using (var h = await TestHost.Start(dir))
        {
            var end = DateTime.UtcNow.AddSeconds(5);
            while (!System.IO.Directory.Exists(Path.Combine(dir, "inbox")) && DateTime.UtcNow < end) await Task.Delay(25);
            using var a = h.Client(); await h.ConfirmAndLogin(a, "outbox");
            using var scope = h.Services.CreateScope(); Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<FriendsDb>().EmailOutbox.CountAsync());
        }
    }
    [Fact]
    public async Task MalformedRequestStillReturnsSafeJsonError()
    {
        await using var h = await TestHost.Start(); using var a = h.Client();
        var response = await a.PostAsync("/api/account/login", new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var error = await response.Content.ReadFromJsonAsync<FriendApiError>(); Assert.NotNull(error); Assert.DoesNotContain("Exception", error.Message);
    }
    private static async Task Connect(HttpClient a, HttpClient b, FriendSession sb)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/friend-requests", new CreateFriendRequest(sb.Me.FriendCode))).StatusCode);
        var req = (await b.GetFromJsonAsync<FriendPage<FriendRequest>>("/api/friend-requests"))!.Items.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsJsonAsync($"/api/friend-requests/{req.Id}/accept", new { })).StatusCode);
    }
}
