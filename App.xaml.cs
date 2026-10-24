using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace OffWorkWidget.WinUI;

public partial class App : Application
{
    private MainWindow? window;
    public App()
    {
        UnhandledException += (_, e) => RecordFailure(e.Exception);
        InitializeComponent();
    }
    internal static void RecordFailure(Exception error)
    {
        try
        {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OffWorkWidget","diagnostics");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder,"startup-errors.log"),DateTime.Now.ToString("O")+Environment.NewLine+error.GetType().FullName+" HResult="+error.HResult.ToString("X8")+Environment.NewLine+(error is Microsoft.UI.Xaml.Markup.XamlParseException ? error.Message+Environment.NewLine : "")+error.StackTrace+Environment.NewLine,new System.Text.UTF8Encoding(true));
        }
        catch { }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { window=new MainWindow();window.Activate(); }
        catch(Exception ex)
        {
            RecordFailure(ex);
            MessageBox(IntPtr.Zero,"启动失败，诊断已记录到 %LOCALAPPDATA%\\OffWorkWidget\\diagnostics\\startup-errors.log。\n错误代码："+ex.HResult.ToString("X8"),"下班倒计时",0x10);
            Exit();
        }
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int MessageBox(IntPtr owner,string text,string title,uint flags);
}