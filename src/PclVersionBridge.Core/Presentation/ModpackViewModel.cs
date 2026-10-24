using System.ComponentModel;
using System.Runtime.CompilerServices;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Modpacks;
using PclVersionBridge.Core.Paths;
using PclVersionBridge.Core.Friends;

namespace PclVersionBridge.Core.Presentation;

public sealed class OptionalPackFile(string path)
{
    public string Path { get; } = path;
    public bool Selected { get; set; }
}

public sealed class ModpackViewModel : INotifyPropertyChanged
{
    private readonly IModrinthCatalog _catalog;
    private readonly IModpackContentSource _content;
    private readonly IModpackInstallService _installer;
    private readonly InstallationGate _gate;
    private readonly string _cache;
    private CancellationTokenSource? _cancellation;
    private ModpackProject? _project;
    private ModpackRelease? _release;
    private string _instanceName = "";
    private int _generation;
    private int _criteriaRevision;
    private string _query="", _gameVersion="", _sort="auto";
    private bool _includePrerelease;
    private ModpackSearchRequest? _submittedSearch;
    public ModpackViewModel(IModrinthCatalog catalog, IModpackContentSource content, IModpackInstallService installer,
        InstallationGate gate, string cache)
    {
        _catalog = catalog; _content = content; _installer = installer; _gate = gate; _cache = cache;
        gate.PropertyChanged += (_, _) => Notify(nameof(CanInstall));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Query { get=>_query; set {if(_query==value)return;_query=value;_criteriaRevision++;Notify();NotifySearchState();} }
    public string GameVersion { get=>_gameVersion; set {if(_gameVersion==value)return;_gameVersion=value;_criteriaRevision++;ClearVersions();Notify();NotifySearchState();} }
    public string Sort { get=>_sort; set {if(_sort==value)return;_sort=value;_criteriaRevision++;Notify();NotifySearchState();} }
    public bool IncludePrerelease {get=>_includePrerelease;set{if(_includePrerelease==value)return;_includePrerelease=value;ClearVersions();Notify();}}
    public IReadOnlyList<ModpackProject> Projects { get; private set; } = [];
    public IReadOnlyList<ModpackRelease> Releases { get; private set; } = [];
    public IReadOnlyList<OptionalPackFile> OptionalFiles { get; private set; } = [];
    public ModpackProject? SelectedProject
    {
        get => _project;
        set { if (_project == value) return; _project = value; Invalidate(); Releases = []; _release = null; Notify(); Notify(nameof(Releases)); Notify(nameof(SelectedRelease)); }
    }
    public ModpackRelease? SelectedRelease
    {
        get => _release;
        set { if (_release == value) return; _release = value; Invalidate(); Notify(); }
    }
    public PackPreview? Preview { get; private set; }
    public string PreviewSummary => Preview is null ? "先搜索并检查包，或导入本地 .mrpack 文件。" :
        $"{Preview.Plan.Name} · {Preview.Plan.VersionId}\nMinecraft {Preview.Plan.MinecraftVersion} · Fabric {Preview.Plan.FabricVersion}\n{Preview.Plan.Files.Count(f => !f.Optional)} 个必需文件 · {Preview.Plan.Overrides.Count} 个覆盖文件 · {(Preview.Plan.Files.Sum(f => f.Size) / 1048576.0):F1} MiB 内容下载（另需游戏依赖）";
    public string InstanceName { get => _instanceName; set { _instanceName = value; Notify(); Notify(nameof(CanInstall)); } }
    public string DirectoryPath { get; private set; } = "";
    public bool DirectoryConfirmed { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsInstalling { get; private set; }
    public bool CanInstall => Preview is not null && DirectoryConfirmed && !IsBusy && !_gate.IsBusy && !string.IsNullOrWhiteSpace(InstanceName);
    public string Status { get; private set; } = "首版支持 Modrinth / 本地 mrpack，限 Fabric 与原版正式版。";
    public string Error { get; private set; } = "";
    public double ProgressPercent { get; private set; }
    public int Offset { get; private set; }
    public int Total { get; private set; }
    public string PageSummary => $"{Offset + (Projects.Count > 0 ? 1 : 0)}–{Offset + Projects.Count} / {Total}";
    public bool HasPendingSearch => _submittedSearch is not null && CurrentSearch(0)!=(_submittedSearch with {Offset=0});
    public bool CanPreviousPage => !IsBusy && !HasPendingSearch && _submittedSearch is not null && Offset>0;
    public bool CanNextPage => !IsBusy && !HasPendingSearch && _submittedSearch is not null && Offset+20<Total && Offset<10000;
    public string SearchSummary => HasPendingSearch ? "搜索条件已修改，点击搜索应用。" : _submittedSearch is null
        ? "可按名称搜索；仅显示 Modrinth 的 Fabric 整合包。"
        : $"Modrinth · {(_submittedSearch.GameVersion is null ? "全部 MC 版本" : "MC "+_submittedSearch.GameVersion)} · {(_submittedSearch.Sort=="downloads"?"按下载量":_submittedSearch.Sort=="updated"?"最近更新":"按相关度")} · {Total} 个结果";
    public void SetDirectory(string path, bool confirmed)
    {
        if (path == DirectoryPath && confirmed == DirectoryConfirmed) return;
        DirectoryPath = path; DirectoryConfirmed = confirmed; Invalidate(); Notify(nameof(CanInstall));
    }
    private ModpackSearchRequest CurrentSearch(int offset)=>new(Query.Trim(),string.IsNullOrWhiteSpace(GameVersion)?null:GameVersion.Trim(),offset,
        Sort=="auto" ? (string.IsNullOrWhiteSpace(Query)?"downloads":"relevance") : Sort);
    public Task SearchAsync(int offset = 0) => SubmitSearchAsync(CurrentSearch(offset));
    public Task ChangePageAsync(int offset)
    {
        if(_submittedSearch is null || offset<0 || offset>=Total || offset>10000 || offset%20!=0) return Task.CompletedTask;
        return SubmitSearchAsync(_submittedSearch with {Offset=offset});
    }
    private Task SubmitSearchAsync(ModpackSearchRequest request)=>RunAsync(async token=>
    {
        var revision=_criteriaRevision;
        _submittedSearch=null;SelectedProject=null;ClearVersions();Projects=[];Offset=0;Total=0;
        Notify(nameof(Projects));NotifySearchState();
        var page = await _catalog.SearchAsync(request, token);
        token.ThrowIfCancellationRequested();
        if(revision!=_criteriaRevision){Status="搜索条件已改变，请重新搜索。";return;}
        _submittedSearch=request;
        Projects = page.Hits; Offset = page.Offset; Total = page.Total;
        Notify(nameof(Projects)); NotifySearchState();
        Status = Projects.Count == 0 ? "没有找到支持的整合包，试试其他关键词。" : "选择整合包，然后获取版本。";
    });
    public Task LoadVersionsAsync() => RunAsync(async token =>
    {
        if (SelectedProject is null) throw new InstallException("请先选择整合包。");
        var id = SelectedProject.Id;
        var mc=string.IsNullOrWhiteSpace(GameVersion)?null:GameVersion.Trim();
        var pre=IncludePrerelease;
        var releases = await _catalog.GetVersionsAsync(id, pre, mc, token);
        token.ThrowIfCancellationRequested();
        if (SelectedProject?.Id != id || mc!=(string.IsNullOrWhiteSpace(GameVersion)?null:GameVersion.Trim()) || pre!=IncludePrerelease) return;
        Releases = releases; Notify(nameof(Releases));
        SelectedRelease = null;
        Status = Releases.Count == 0 ? "没有可安装的 Fabric 包版本。" : "选择包版本，再点击下载并检查。";
    });
    public Task PreviewAsync() => RunAsync(async token =>
    {
        var generation = _generation;
        var release = SelectedRelease ?? throw new InstallException("请先选择包版本。");
        Directory.CreateDirectory(_cache);
        var archive = Path.Combine(_cache, "pack-" + Guid.NewGuid().ToString("N") + ".mrpack");
        try
        {
            var preview = await _content.DownloadArchiveAsync(release, archive, token);
            token.ThrowIfCancellationRequested();
            if (generation == _generation) Accept(preview);
            else { File.Delete(archive); Status = "目录或选择已改变，请重新检查整合包。"; }
        }
        catch { if (File.Exists(archive)) File.Delete(archive); throw; }
    });
    public Task PreviewInvitationAsync(PackReference reference, CancellationToken cancellationToken) => RunAsync(async token =>
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,cancellationToken);
        var requestToken=linked.Token;
        SelectedProject=null;ClearVersions();
        var generation=_generation;
        var release=await _catalog.GetPinnedReleaseAsync(reference.ProjectId,reference.VersionId,reference.ArchiveSha512,requestToken);
        requestToken.ThrowIfCancellationRequested();
        if(generation!=_generation){Status="目录或选择已改变，请重新检查邀请。";return;}
        _release=release;Releases=[release];Notify(nameof(Releases));Notify(nameof(SelectedRelease));
        Directory.CreateDirectory(_cache);
        var archive=Path.Combine(_cache,"invite-"+Guid.NewGuid().ToString("N")+".mrpack");
        try
        {
            var preview=await _content.DownloadArchiveAsync(release,archive,requestToken);
            requestToken.ThrowIfCancellationRequested();
            if(!release.GameVersions.Contains(preview.Plan.MinecraftVersion,StringComparer.Ordinal))
                throw new InstallException("归档的 Minecraft 版本与已验证的邀请不一致。");
            if(generation==_generation)Accept(preview);
            else {if(File.Exists(archive))File.Delete(archive);Status="目录或选择已改变，请重新检查邀请。";}
        }
        catch {if(File.Exists(archive))File.Delete(archive);throw;}
    });
    public Task ImportAsync(string path) => RunAsync(async token =>
    {
        var generation = _generation;
        var preview = await _content.ReadAsync(path, token);
        token.ThrowIfCancellationRequested();
        if (generation == _generation) Accept(preview);
        else Status = "目录或选择已改变，请重新导入整合包。";
    });
    private void Accept(PackPreview preview)
    {
        Preview = preview;
        OptionalFiles = preview.Plan.Files.Where(f => f.Optional).Select(f => new OptionalPackFile(f.Path)).ToArray();
        InstanceName = "pack-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Notify(nameof(Preview)); Notify(nameof(PreviewSummary)); Notify(nameof(OptionalFiles)); Notify(nameof(CanInstall));
        Status = "检查完成。确认目录、实例名与可选文件后安装。";
    }
    public async Task InstallAsync()
    {
        if (!CanInstall) throw new InstallException("请先确认目录并检查整合包。");
        using var lease = _gate.Acquire();
        var preview = Preview!; var root = DirectoryPath; var id = InstanceName;
        IsInstalling = true; Notify(nameof(IsInstalling));
        try
        {
            await RunAsync(async token =>
            {
                GamePathValidator.ValidateVersionId(id);
                var optional = OptionalFiles.Where(f => f.Selected).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
                var progress = new Progress<InstallProgress>(value =>
                {
                    if (!IsInstalling) return;
                    Status = value.Stage; ProgressPercent = value.Total > 0 ? Math.Clamp(100.0 * value.Completed / value.Total,0,100) : 0;
                    Notify(nameof(Status)); Notify(nameof(ProgressPercent));
                });
                await _installer.InstallAsync(preview,id,root,optional,progress,token);
                Status = $"安装完成。回到 PCL 刷新版本列表，选择 {id}。"; ProgressPercent = 100;
                Notify(nameof(ProgressPercent));
                Preview = null; Notify(nameof(Preview)); Notify(nameof(PreviewSummary));
            });
        }
        finally { IsInstalling = false; Notify(nameof(IsInstalling)); Notify(nameof(CanInstall)); }
    }
    public void Cancel() => _cancellation?.Cancel();
    public Task<ModpackDetails> GetProjectDetailsAsync(ModpackProject project,CancellationToken token)=>_catalog.GetProjectAsync(project.Id,token);
    private void ClearVersions()
    {
        Releases=[];_release=null;Invalidate();Notify(nameof(Releases));Notify(nameof(SelectedRelease));
    }
    private void NotifySearchState()
    {
        Notify(nameof(PageSummary));Notify(nameof(HasPendingSearch));Notify(nameof(CanPreviousPage));Notify(nameof(CanNextPage));Notify(nameof(SearchSummary));
    }
    private void Invalidate()
    {
        _generation++;
        // Installation captures its input; UI controls are locked while it runs.
        Preview = null; OptionalFiles = []; Notify(nameof(Preview)); Notify(nameof(PreviewSummary)); Notify(nameof(OptionalFiles)); Notify(nameof(CanInstall));
    }
    private async Task RunAsync(Func<CancellationToken,Task> action)
    {
        if (IsBusy) return;
        IsBusy = true; Error = ""; Status = "正在处理…";
        Notify(nameof(IsBusy)); Notify(nameof(Error)); Notify(nameof(Status)); Notify(nameof(CanInstall));
        NotifySearchState();
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { Status = "操作已取消，可以重试。"; }
        catch (InstallException ex) { Error = ex.Message; Status = "未能完成，请检查提示后重试。"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        { Error = "来源数据、网络或文件暂时不可用。请检查网络、文件和磁盘空间后重试。"; Status = "操作未完成。"; }
        finally
        {
            _cancellation = null; IsBusy = false;
            Notify(nameof(IsBusy)); Notify(nameof(Error)); Notify(nameof(Status)); Notify(nameof(CanInstall));
            NotifySearchState();
        }
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this,new(name));
}
