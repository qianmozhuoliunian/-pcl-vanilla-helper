using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

internal static class ThemeControlChecks
{
    internal static void Run()
    {
        var panel=new StackPanel {Margin=new Thickness(24)};
        var input=new TextBox {Text="控件检查：文本输入",Margin=new Thickness(0,0,0,12)};
        var password=new PasswordBox {Password="SyntheticFixture!2026",Margin=new Thickness(0,0,0,12)};
        var combo=new ComboBox {ItemsSource=new[]{"原版正式版","第二个选项","第三个选项"},SelectedIndex=0,Margin=new Thickness(0,0,0,12)};
        var button=new Button {Content="继续",Style=(Style)Application.Current.FindResource("PrimaryButton"),Margin=new Thickness(0,0,0,12)};
        var disabled=new Button {Content="暂不可用",IsEnabled=false,Margin=new Thickness(0,0,0,12)};
        var progress=new ProgressBar {Minimum=0,Maximum=100,Value=50,Height=6,Margin=new Thickness(0,0,0,12)};
        foreach(var control in new UIElement[]{input,password,combo,button,disabled,progress})panel.Children.Add(control);
        var host=new Window {Content=panel,Width=500,Height=410,Left=-3000,Top=0,ShowInTaskbar=false,ShowActivated=false,Background=(Brush)Application.Current.FindResource("AppBackground"),FontFamily=(FontFamily)Application.Current.FindResource("AppFont")};
        try
        {
            host.Show();Pump();
            var inputPeer=(IValueProvider)new TextBoxAutomationPeer(input).GetPattern(PatternInterface.Value)!;
            inputPeer.SetValue("由辅助功能接口写入的测试文字");
            Check(input.Text.Contains("测试文字") && input.Template.FindName("PART_ContentHost",input) is ScrollViewer sv && sv.ActualWidth>0,"Text input template lost editable content");
            var passwordPeer=new PasswordBoxAutomationPeer(password);
            Check(passwordPeer.IsPassword(),"Password control lost its protected automation role");
            // Native PasswordBox supports setting a value through UIA, while denying reads.
            if(passwordPeer.GetPattern(PatternInterface.Value) is IValueProvider passwordValue)
            {
                var unreadable=false;
                try {unreadable=passwordValue.Value!=password.Password;}
                catch(InvalidOperationException) {unreadable=true;}
                Check(unreadable,"Password was readable through automation");
            }
            var source=PresentationSource.FromVisual(combo)!;
            void Key(Key key)=>combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,key){RoutedEvent=Keyboard.KeyDownEvent});
            Key(System.Windows.Input.Key.F4);Pump();Check(combo.IsDropDownOpen,"Keyboard could not open dropdown");
            var popup=(Popup)combo.Template.FindName("PART_Popup",combo)!;
            Check(popup.IsOpen && popup.Child is FrameworkElement child && child.ActualWidth>=combo.ActualWidth-2,"Dropdown did not open at usable width");
            Key(System.Windows.Input.Key.Down);Key(System.Windows.Input.Key.Enter);Pump();
            Check(combo.SelectedIndex==1 && !combo.IsDropDownOpen,"Keyboard dropdown selection did not commit");
            var clicks=0;button.Click+=(_,_)=>clicks++;
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();Pump();Check(clicks==1,"Native button invocation lost its action");
            var indicator=(FrameworkElement)progress.Template.FindName("PART_Indicator",progress)!;
            var track=(FrameworkElement)progress.Template.FindName("PART_Track",progress)!;
            Check(Math.Abs(indicator.ActualWidth-track.ActualWidth/2)<2,"Progress indicator no longer represents its value");
            foreach(var key in new[]{"TextPrimary","TextSecondary"})Check(Contrast(Color(key),Color("Surface"))>=4.5,"Text palette contrast failed: "+key);
            Check(Contrast(Colors.White,Color("Primary"))>=4.5,"Primary button text contrast failed");
            var preview=new Border {Background=(Brush)Application.Current.FindResource("AppBackground")};
            host.Content=null;preview.Child=panel;DesktopPolishChecks.Render(preview,"native-controls",450,380);preview.Child=null;host.Content=panel;
            Console.WriteLine("PASS native editable/password semantics, keyboard dropdown open/select/close, button invocation, progress fraction and text contrast");
        }
        finally {combo.IsDropDownOpen=false;password.Clear();host.Close();}
    }
    private static Color Color(string key)=>((SolidColorBrush)Application.Current.FindResource(key)).Color;
    private static double Contrast(Color a,Color b)
    {
        double L(Color c){double F(byte value){var v=value/255d;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);}
        var x=L(a);var y=L(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
