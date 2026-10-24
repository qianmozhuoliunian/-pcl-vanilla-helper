using System.Net;
using System.Text;
using System.Text.Json;
using PclVersionBridge.Core.Friends;

namespace PclVersionBridge.Tests;

public sealed class FriendApiClientTests
{
    internal static readonly FriendProfile Alice = new("alice", "Alice", "0123456789AB");
    internal static FriendSession Session(string access = "access", DateTimeOffset? expiry = null) =>
        new(access, "refresh", expiry ?? DateTimeOffset.UtcNow.AddHours(1), Alice);

    [Theory]
    [InlineData("http://example.com/", false)]
    [InlineData("http://127.0.0.1/", false)]
    [InlineData("http://localhost/", true)]
    [InlineData("http://127.0.0.1.example.com/", true)]
    [InlineData("https://user:password@example.com/", false)]
    [InlineData("https://example.com/path", false)]
    [InlineData("https://example.com/?key=secret", false)]
    public void Rejects_unsafe_service_origins(string uri, bool development) =>
        Assert.Throws<ArgumentException>(() => new FriendServiceOptions(new Uri(uri), development));

    [Theory]
    [InlineData("https://friends.example/", false)]
    [InlineData("http://127.0.0.1:5123/", true)]
    [InlineData("http://[::1]:5123/", true)]
    public void Allows_https_and_explicit_literal_loopback(string uri, bool development) =>
        Assert.Equal(new Uri(uri), new FriendServiceOptions(new Uri(uri), development).ServiceUri);

    [Fact]
    public async Task Login_only_remembers_when_requested_and_bearer_stays_on_protected_routes()
    {
        var seen = new List<(string Uri, string? Bearer, string Body)>();
        var store = new MemorySessionStore();
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            seen.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync()));
            return request.RequestUri.AbsolutePath.EndsWith("login") ? Json(Session()) : Json(Alice);
        }));
        using var api = new FriendApiClient(http, new(new Uri("https://friends.example/")), store);
        await api.LoginAsync(new("a@example.com", "secret"), false, default);
        await api.LookupAsync("0123-4567 89ab", default);
        Assert.Null(store.Value);
        Assert.Null(seen[0].Bearer);
        Assert.Equal("Bearer access", seen[1].Bearer);
        Assert.Equal("https://friends.example/api/profiles/0123456789AB", seen[1].Uri);
        Assert.DoesNotContain("secret", seen[1].Body);
        await api.LoginAsync(new("a@example.com", "secret"), true, default);
        Assert.Equal("access", store.Value!.AccessToken);
    }

    [Fact]
    public async Task Restore_refreshes_saved_credentials_and_revoked_session_clears_storage()
    {
        var store = new MemorySessionStore { Value = Session("expired", DateTimeOffset.UtcNow.AddMinutes(-1)) };
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("refresh") ? Json(Session("fresh")) :
            new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("<html>private detail</html>") })));
        using var api = new FriendApiClient(http, new(new Uri("https://friends.example/")), store);
        await api.RestoreAsync(default);
        Assert.Equal("fresh", api.Session!.AccessToken);
        var error = await Assert.ThrowsAsync<FriendServiceException>(() => api.GetMeAsync(default));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Null(api.Session);
        Assert.Null(store.Value);
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public async Task Logout_wins_over_late_login_and_clears_saved_credentials()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemorySessionStore();
        using var http = new HttpClient(new Handler((_, _) => { started.TrySetResult(); return response.Task; }));
        using var api = new FriendApiClient(http, new(new Uri("https://friends.example/")), store);
        var login = api.LoginAsync(new("a@example.com", "secret"), true, default);
        await started.Task;
        await api.LogoutAsync(default);
        response.SetResult(Json(Session()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
        Assert.Null(api.Session);
        Assert.Null(store.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_refreshes_retiring_credentials_without_restoring_local_login(bool all)
    {
        var store=new MemorySessionStore();FriendApiClient? api=null;var refreshes=0;var revoked=false;
        using var http=new HttpClient(new Handler((request,_)=>
        {
            var route=request.RequestUri!.AbsolutePath;
            if(route.EndsWith("login"))return Task.FromResult(Json(Session("old-access")));
            if(route.EndsWith("refresh"))
            {
                Assert.Null(api!.Session);Assert.Null(store.Value);refreshes++;
                return Task.FromResult(Json(Session("fresh-revocation-access")));
            }
            revoked=request.Headers.Authorization?.Parameter=="fresh-revocation-access";
            return Task.FromResult(new HttpResponseMessage(revoked?HttpStatusCode.NoContent:HttpStatusCode.Unauthorized));
        }));
        using var client=new FriendApiClient(http,new(new Uri("https://friends.example/")),store);api=client;
        await client.LoginAsync(new("a@example.com","secret"),true,default);
        if(all)await client.LogoutAllAsync(default);else await client.LogoutAsync(default);
        Assert.True(revoked);Assert.Equal(1,refreshes);Assert.Null(client.Session);Assert.Null(store.Value);
    }

    [Fact]
    public async Task Pending_logout_revokes_only_retiring_owner_after_another_account_logs_in()
    {
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bob=new FriendProfile("bob","Bob","0123456789AC");var seen=new List<string?>();var store=new MemorySessionStore();
        using var http=new HttpClient(new Handler(async(request,_)=>
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("login"))
                return (await request.Content!.ReadAsStringAsync()).Contains("bob@example.com")?Json(Session("bob-access") with {Me=bob}):Json(Session("old-access"));
            if(request.RequestUri.AbsolutePath.EndsWith("refresh")){started.TrySetResult();return await reply.Task;}
            seen.Add(request.Headers.Authorization?.Parameter);
            return new(request.Headers.Authorization?.Parameter=="alice-fresh"?HttpStatusCode.NoContent:HttpStatusCode.Unauthorized);
        }));
        using var api=new FriendApiClient(http,new(new Uri("https://friends.example/")),store);
        await api.LoginAsync(new("alice@example.com","secret"),true,default);
        var logout=api.LogoutAllAsync(default);
        if(logout.IsCompleted)await logout;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await api.LoginAsync(new("bob@example.com","secret"),true,default);
        reply.SetResult(Json(Session("alice-fresh")));await logout;
        Assert.DoesNotContain("bob-access",seen);Assert.Equal("bob",api.Session!.Me.UserId);Assert.Equal("bob",store.Value!.Me.UserId);
    }

    [Theory]
    [InlineData("<html>stack trace password</html>")]
    [InlineData("{\"code\":\"unknown\",\"message\":\"access-token secret\"}")]
    public async Task Untrusted_error_text_is_never_shown(string body)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(body) })));
        using var api = new FriendApiClient(http, new(new Uri("https://friends.example/")), new MemorySessionStore());
        var error = await Assert.ThrowsAsync<FriendServiceException>(() => api.RegisterAsync(new("a@example.com", "secret", "A"), default));
        Assert.DoesNotContain("secret", error.Message);
        Assert.DoesNotContain("stack", error.Message);
    }

    [Fact]
    public async Task Oversized_json_and_redirects_are_rejected()
    {
        foreach (var response in new[] {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 300_000)) },
            new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("https://evil.example/") } },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(Session()).Content, RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://evil.example/") } })
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(response)));
            using var api = new FriendApiClient(http, new(new Uri("https://friends.example/")), new MemorySessionStore());
            await Assert.ThrowsAsync<FriendServiceException>(() => api.LoginAsync(new("a@example.com", "secret"), false, default));
            Assert.Null(api.Session);
        }
    }

    [Fact]
    public async Task Request_timeout_is_bounded_and_does_not_create_session()
    {
        using var http = new HttpClient(new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Json(Session()); }));
        using var api = new FriendApiClient(http, new(new Uri("https://friends.example/")) { RequestTimeout = TimeSpan.FromMilliseconds(40) }, new MemorySessionStore());
        var error = await Assert.ThrowsAsync<FriendServiceException>(() => api.LoginAsync(new("a@example.com", "secret"), false, default));
        Assert.Equal("timeout", error.Code);
        Assert.Null(api.Session);
    }

    [Fact]
    public async Task Pagination_and_actions_use_fixed_routes_and_escape_ids()
    {
        var seen = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            seen.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            return Task.FromResult(request.RequestUri.AbsolutePath.EndsWith("login") ? Json(Session()) :
                request.Method == HttpMethod.Get ? Json(new FriendPage<FriendProfile>([], 2, 41)) : new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        using var api = new FriendApiClient(http, new(new Uri("https://friends.example/")), new MemorySessionStore());
        await api.LoginAsync(new("a@example.com", "secret"), false, default);
        var page = await api.GetFriendsAsync(2, default);
        Assert.Equal(2, page.Page);
        await api.ResolveRequestAsync("abc", "accept", default);
        await api.BlockAsync("bob", default);
        Assert.Contains("GET /api/friends?page=2", seen);
        Assert.Contains("POST /api/friend-requests/abc/accept", seen);
        Assert.Contains("PUT /api/blocks/bob", seen);
        await Assert.ThrowsAsync<ArgumentException>(() => api.ResolveRequestAsync("abc", "../../logout", default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetFriendsAsync(-1, default));
    }

    [Fact]
    public async Task Unauthorized_headers_invalidate_session_without_waiting_for_an_untrusted_error_body()
    {
        var store = new MemorySessionStore();
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("login") ? Json(Session()) :
            new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StreamContent(new NeverReadableStream()) })));
        using var api = new FriendApiClient(http, new(new Uri("https://friends.example/")) { RequestTimeout = TimeSpan.FromMilliseconds(50) }, store);
        await api.LoginAsync(new("a@example.com", "secret"), true, default);
        var error = await Assert.ThrowsAsync<FriendServiceException>(() => api.GetMeAsync(default));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Null(api.Session);
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task Restoring_credentials_cannot_silently_change_the_remembered_account()
    {
        var store = new MemorySessionStore { Value = Session() };
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(Session() with { Me = new("bob", "Bob", "0123456789AC") }))));
        using var api = new FriendApiClient(http, new(new Uri("https://friends.example/")), store);
        var error = await Assert.ThrowsAsync<FriendServiceException>(() => api.RestoreAsync(default));
        Assert.Equal("invalid_response", error.Code);
        Assert.Null(api.Session);
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task Mutation_waiting_for_refresh_cannot_run_as_the_next_account()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutations = 0;
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("login"))
            {
                var body = await request.Content!.ReadAsStringAsync();
                return body.Contains("bob@example.com") ? Json(Session("bob-access") with { Me = new("bob", "Bob", "0123456789AC") }) :
                    Json(Session(expiry: DateTimeOffset.UtcNow.AddSeconds(10)));
            }
            if (path.EndsWith("refresh")) { refreshStarted.TrySetResult(); return await refreshReply.Task; }
            if (request.Method == HttpMethod.Delete) { mutations++; return new(HttpStatusCode.NoContent); }
            return Json(Alice);
        }));
        using var api = new FriendApiClient(http, new(new Uri("https://friends.example/")), new MemorySessionStore());
        await api.LoginAsync(new("a@example.com", "secret"), false, default);
        var pendingRead = api.GetMeAsync(default);
        await refreshStarted.Task;
        var pendingRemove = api.RemoveFriendAsync("charlie", default);
        await api.LoginAsync(new("bob@example.com", "secret"), false, default);
        refreshReply.SetResult(Json(Session("fresh-alice")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRead);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRemove);
        Assert.Equal(0, mutations);
        Assert.Equal("bob", api.Session!.Me.UserId);
    }

    private sealed class NeverReadableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    internal static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    internal sealed class MemorySessionStore : IFriendSessionStore
    {
        public FriendSession? Value { get; set; }
        public Task<FriendSession?> ReadAsync(CancellationToken token) => Task.FromResult(Value);
        public Task WriteAsync(FriendSession session, CancellationToken token) { Value = session; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken token) { Value = null; return Task.CompletedTask; }
    }
}
