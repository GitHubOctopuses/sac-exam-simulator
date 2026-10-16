using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SacExam.Services;

namespace SacExam;

public partial class MainWindow : Window
{
    public UserControl Current { get; private set; }
    private bool _reallyClosing;

    public MainWindow()
    {
        InitializeComponent();
        Icon = Ui.AppIcon;
        Loaded += (_, _) => Navigate(new MainMenuPage(this));
        Closing += OnClosing;
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_reallyClosing) return;
        if (Current is ExamPage ep && !ep.IsFinished)
        {
            e.Cancel = true;
            bool ok = SacDialog.Confirm(this, "提示",
                "考试尚未交卷，作答与剩余时间已自动保存。退出后可在主页“恢复未完成考试”。确定退出？",
                okText: "确定退出");
            if (ok)
            {
                ep.DetachTimer();
                _reallyClosing = true;
                Application.Current.Shutdown();
            }
        }
    }

    public void Navigate(UserControl page)
    {
        Current = page;
        Host.Content = page;
    }

}
