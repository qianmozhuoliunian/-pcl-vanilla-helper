using System.Configuration;
using System.Data;
using System.Windows;

namespace PclVersionBridge.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==2 && e.Args[0]=="--friends-preview")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try {await FriendPreviewLauncher.RunAsync(e.Args[1]);}
            catch(Exception) {MessageBox.Show("本机好友测试套件未能启动。请完整解压包含 client 和 server 的测试包，检查端口是否被占用，再重试。","本机测试启动失败",MessageBoxButton.OK,MessageBoxImage.Information);}
            Shutdown();return;
        }
        MainWindow=new MainWindow();MainWindow.Show();
    }
}

