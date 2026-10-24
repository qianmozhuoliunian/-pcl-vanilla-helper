using System.Diagnostics;
using System.Windows;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Modpacks;

namespace PclVersionBridge.App;

public partial class ModpackDetailsWindow:Window
{
    private readonly CancellationTokenSource _cancellation=new();
    private readonly ModpackProject _project;
    private readonly Func<CancellationToken,Task<ModpackDetails>> _load;
    public ModpackDetailsWindow(ModpackProject project,Func<CancellationToken,Task<ModpackDetails>> load)
    {
        InitializeComponent();_project=project;_load=load;
        ProjectTitle.Text=project.Title;AuthorText.Text=string.IsNullOrWhiteSpace(project.Author)?"作者信息未提供":"发布者："+project.Author;
        DescriptionText.Text=project.DescriptionText;DownloadsText.Text=project.DownloadsText;
        VersionsText.Text="正在读取项目资料…";LoadStatus.Text="正在从 Modrinth 读取补充资料…";
        ProjectIcon.ProjectId=project.Id;ProjectIcon.SourceUri=project.IconUri;
        Loaded+=async(_,_)=>await LoadAsync();Closed+=(_,_)=>{_cancellation.Cancel();};
    }
    private async Task LoadAsync()
    {
        try
        {
            var detail=await _load(_cancellation.Token);_cancellation.Token.ThrowIfCancellationRequested();
            ProjectTitle.Text=detail.Title;DescriptionText.Text=detail.DescriptionText;
            DownloadsText.Text=detail.Downloads is long count?$"项目下载量：{count:N0}":"下载量未提供";
            VersionsText.Text=detail.GameVersions.Count==0?"项目未提供版本信息。":string.Join("、",detail.GameVersions);
            CategoryText.Text=detail.Categories.Count==0?"分类未提供":"项目分类（原文）："+string.Join("、",detail.Categories);
            ProjectIcon.SourceUri=detail.RawIconUri??detail.IconUri;LoadStatus.Text="简介保留发布者原文，可打开来源页查看完整内容。";
        }
        catch(OperationCanceledException) {if(!_cancellation.IsCancellationRequested)LoadStatus.Text="资料请求超时，可关闭后重新打开。";}
        catch(Exception ex) when(ModpackImageCache.IsImageFailure(ex) || ex is System.Text.Json.JsonException)
        {VersionsText.Text="版本资料暂不可用。";LoadStatus.Text="补充资料暂不可用；当前简介来自搜索返回的项目资料，可关闭后重试。";}
    }
    private void Source_Click(object sender,RoutedEventArgs e)
    {
        try{Process.Start(new ProcessStartInfo(_project.ProjectUrl.AbsoluteUri){UseShellExecute=true});}
        catch {LoadStatus.Text="无法打开浏览器，请稍后重试。";}
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
