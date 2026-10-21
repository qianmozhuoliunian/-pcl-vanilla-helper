using System.ComponentModel;
using System.Runtime.CompilerServices;
using PclVersionBridge.Core.Catalog;
using PclVersionBridge.Core.Install;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Presentation;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IVersionCatalog _catalog;
    private readonly IInstallService _installer;
    private CancellationTokenSource? _installCancellation;
    private string _directoryPath;
    private bool _directoryConfirmed;
    private VersionChoice? _selectedVersion;
    private bool _isBusy;
    private string _status = "请选择游戏目录，再选择版本。";
    private string _error = "";
    private double _progressPercent;
    private string _searchText = "";

    public MainViewModel(IVersionCatalog catalog, IInstallService installer,
        string? suggestedDirectory = null)
    {
        _catalog = catalog;
        _installer = installer;
        _directoryPath = suggestedDirectory ?? "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<VersionChoice> Versions { get; private set; } = [];
    public IReadOnlyList<VersionChoice> FilteredVersions => string.IsNullOrWhiteSpace(_searchText)
        ? Versions
        : Versions.Where(version => version.Id.Contains(_searchText.Trim(),
            StringComparison.OrdinalIgnoreCase)).ToArray();
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            Notify();
            Notify(nameof(FilteredVersions));
            SelectedVersion = FilteredVersions.FirstOrDefault();
        }
    }
    public string DirectoryPath
    {
        get => _directoryPath;
        set
        {
            if (_directoryPath == value) return;
            _directoryPath = value;
            _directoryConfirmed = false;
            Notify();
            Notify(nameof(DirectoryConfirmed));
            Notify(nameof(CanInstall));
        }
    }
    public bool DirectoryConfirmed => _directoryConfirmed;
    public VersionChoice? SelectedVersion
    {
        get => _selectedVersion;
        set { _selectedVersion = value; Notify(); Notify(nameof(CanInstall)); }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; Notify(); Notify(nameof(CanInstall)); }
    }
    public bool IsInstalling => _installCancellation is not null;
    public bool CanInstall => DirectoryConfirmed && SelectedVersion is not null && !IsBusy;
    public string Status
    {
        get => _status;
        private set { _status = value; Notify(); }
    }
    public string Error
    {
        get => _error;
        private set { _error = value; Notify(); }
    }
    public double ProgressPercent
    {
        get => _progressPercent;
        private set { _progressPercent = value; Notify(); }
    }

    public void ConfirmDirectory()
    {
        _directoryPath = GamePathValidator.ValidateRoot(_directoryPath);
        _directoryConfirmed = true;
        Notify(nameof(DirectoryPath));
        Notify(nameof(DirectoryConfirmed));
        Notify(nameof(CanInstall));
    }

    public async Task LoadVersionsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        Status = "正在获取正式版列表…";
        try
        {
            Versions = await _catalog.ListReleasesAsync(CancellationToken.None);
            Notify(nameof(Versions));
            Notify(nameof(FilteredVersions));
            SelectedVersion = FilteredVersions.FirstOrDefault();
            Status = "请选择要安装的原版版本。";
        }
        catch (Exception ex) when (ex is InstallException or HttpRequestException)
        {
            Error = "获取版本列表失败。请检查网络后重试。";
            Status = "版本列表暂不可用。";
        }
        finally { IsBusy = false; }
    }

    public async Task InstallAsync()
    {
        if (!CanInstall) throw new InstallException("请先确认游戏目录并选择版本。");
        var version = SelectedVersion!;
        Error = "";
        IsBusy = true;
        Status = "正在准备安装…";
        ProgressPercent = 0;
        _installCancellation = new CancellationTokenSource();
        Notify(nameof(IsInstalling));
        var context = SynchronizationContext.Current;
        var progress = new CallbackProgress(value =>
        {
            void Update()
            {
                if (!IsBusy) return;
                Status = value.CurrentFile is null ? value.Stage : $"{value.Stage}：{value.CurrentFile}";
                ProgressPercent = value.Total > 0
                    ? Math.Clamp(100.0 * value.Completed / value.Total, 0, 100) : 0;
            }
            if (context is null || SynchronizationContext.Current == context) Update();
            else context.Send(_ => Update(), null);
        });
        try
        {
            await _installer.InstallAsync(version, DirectoryPath, progress, _installCancellation.Token);
            Status = $"安装完成。回到 PCL 选择 {version.Id}。";
            ProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            Status = "安装已取消。";
        }
        catch (Exception ex) when (ex is InstallException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Error = ex.Message;
            Status = "安装失败，可检查提示后重试。";
        }
        finally
        {
            _installCancellation.Dispose();
            _installCancellation = null;
            Notify(nameof(IsInstalling));
            IsBusy = false;
        }
    }

    public void Cancel()
    {
        if (_installCancellation is null) return;
        Status = "正在取消…";
        _installCancellation.Cancel();
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class CallbackProgress(Action<InstallProgress> callback) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => callback(value);
    }
}
