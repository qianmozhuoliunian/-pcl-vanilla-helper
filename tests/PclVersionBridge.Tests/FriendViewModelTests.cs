using System.Net;
using PclVersionBridge.Core.Friends;
using static PclVersionBridge.Tests.FriendApiClientTests;

namespace PclVersionBridge.Tests;

public sealed class FriendViewModelTests
{
    [Fact]
    public async Task Failed_mutation_preserves_authoritative_list_and_never_reports_success()
    {
        using var fixture = new Fixture();
        await fixture.Vm.LoginAsync(new("a@example.com", "secret"), false);
        Assert.Single(fixture.Vm.Friends);
        fixture.MutationsFail = true;
        await fixture.Vm.RemoveFriendAsync("bob");
        Assert.Single(fixture.Vm.Friends);
        Assert.NotEmpty(fixture.Vm.Error);
        Assert.DoesNotContain("已删除", fixture.Vm.Status);
        Assert.False(fixture.Vm.IsBusy);
    }

    [Fact]
    public async Task Logout_clears_lists_notes_and_lookup_and_late_refresh_cannot_restore_them()
    {
        using var fixture = new Fixture();
        await fixture.Vm.LoginAsync(new("a@example.com", "secret"), false);
        await fixture.Vm.SetNoteAsync("bob", "本机备注");
        await fixture.Vm.LookupAsync("0123456789AB");
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayFriends = delayed;
        var refresh = fixture.Vm.RefreshAsync();
        await fixture.FriendsStarted.Task;
        await fixture.Vm.LogoutAsync();
        delayed.TrySetResult(Json(new FriendPage<FriendProfile>([new("bob", "Bob", "0123456789AC")], 0, 1)));
        await refresh;
        Assert.False(fixture.Vm.IsLoggedIn);
        Assert.Null(fixture.Vm.Me);
        Assert.Empty(fixture.Vm.Friends);
        Assert.Empty(fixture.Vm.Notes);
        Assert.Null(fixture.Vm.LookupResult);
        Assert.False(fixture.Vm.IsBusy);
        Assert.Empty(fixture.Vm.Error);
    }

    [Fact]
    public async Task Busy_guard_suppresses_duplicate_mutations_and_preserves_submitted_page()
    {
        using var fixture = new Fixture();
        await fixture.Vm.LoginAsync(new("a@example.com", "secret"), false);
        await fixture.Vm.LoadFriendsAsync(2);
        Assert.Equal(2, fixture.Vm.FriendsPage);
        Assert.Equal(41, fixture.Vm.FriendsTotal);
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayFriends = delayed;
        var refresh = fixture.Vm.RefreshAsync();
        await fixture.FriendsStarted.Task;
        await fixture.Vm.SendRequestAsync("0123456789AB");
        Assert.Equal(0, fixture.RequestSubmissions);
        delayed.TrySetResult(Json(new FriendPage<FriendProfile>([new("bob", "Bob", "0123456789AC")], 2, 41)));
        await refresh;
        Assert.Equal(2, fixture.Vm.FriendsPage);
    }

    [Fact]
    public async Task External_account_change_discards_delayed_page_from_old_identity()
    {
        using var fixture = new Fixture();
        await fixture.Vm.LoginAsync(new("a@example.com", "secret"), false);
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayFriends = delayed;
        var refresh = fixture.Vm.RefreshAsync();
        await fixture.FriendsStarted.Task;
        await fixture.Api.LogoutAsync(default);
        delayed.TrySetResult(Json(new FriendPage<FriendProfile>([Alice], 0, 1)));
        await refresh;
        Assert.False(fixture.Vm.IsLoggedIn);
        Assert.Empty(fixture.Vm.Friends);
        Assert.Null(fixture.Vm.Me);
    }

    [Fact]
    public async Task Notes_stay_local_and_failed_invitation_recheck_returns_null()
    {
        using var fixture = new Fixture();
        await fixture.Vm.LoginAsync(new("a@example.com", "secret"), false);
        await fixture.Vm.SetNoteAsync("bob", "只存本机");
        Assert.Equal("只存本机", fixture.Vm.Notes["bob"]);
        Assert.Equal("alice", fixture.Notes.Owner);
        fixture.MutationsFail = true;
        Assert.Null(await fixture.Vm.GetInvitationAsync("gone"));
        Assert.NotEmpty(fixture.Vm.Error);
        Assert.DoesNotContain(fixture.RequestBodies, s => s.Contains("只存本机"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        public FriendApiClient Api { get; }
        public FriendsViewModel Vm { get; }
        public NotesStore Notes { get; } = new();
        public bool MutationsFail { get; set; }
        public int RequestSubmissions { get; private set; }
        public List<string> RequestBodies { get; } = [];
        public TaskCompletionSource<HttpResponseMessage>? DelayFriends { get; set; }
        public TaskCompletionSource FriendsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Fixture()
        {
            _http = new(new Handler(async (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync());
                if (path == "/api/account/login") return Json(Session());
                if (path == "/api/account/logout") return new(HttpStatusCode.NoContent);
                if (MutationsFail) return new(HttpStatusCode.Conflict);
                if (path == "/api/friend-requests" && request.Method == HttpMethod.Post) { RequestSubmissions++; return new(HttpStatusCode.NoContent); }
                if (path is "/api/me" || path.StartsWith("/api/profiles/")) return Json(Alice);
                var page = request.RequestUri.Query.Contains("page=2") ? 2 : 0;
                if (path == "/api/friends")
                {
                    if (DelayFriends is { } delayed) { FriendsStarted.TrySetResult(); return await delayed.Task; }
                    return Json(new FriendPage<FriendProfile>([new("bob", "Bob", "0123456789AC")], page, 41));
                }
                if (path == "/api/blocks") return Json(new FriendPage<FriendProfile>([], page, 0));
                if (path == "/api/friend-requests") return Json(new FriendPage<FriendRequest>([], page, 0));
                if (path == "/api/invitations") return Json(new FriendPage<PackInvitation>([], page, 0));
                return new(HttpStatusCode.NoContent);
            }));
            Api = new(_http, new(new Uri("https://friends.example/")), new MemorySessionStore());
            Vm = new(Api, Notes);
        }
        public void Dispose() { Vm.Dispose(); Api.Dispose(); _http.Dispose(); }
    }
    private sealed class NotesStore : IFriendNotesStore
    {
        private IReadOnlyDictionary<string, string> _notes = new Dictionary<string, string>();
        public string? Owner { get; private set; }
        public Task<IReadOnlyDictionary<string, string>> ReadAsync(string ownerUserId, CancellationToken token) => Task.FromResult(_notes);
        public Task WriteAsync(string ownerUserId, IReadOnlyDictionary<string, string> notes, CancellationToken token) { Owner = ownerUserId; _notes = notes; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken token) { _notes = new Dictionary<string, string>(); return Task.CompletedTask; }
    }
}
