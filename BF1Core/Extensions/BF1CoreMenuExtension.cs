using Frosty.Core;
using LevelEditorPlugin.BF1Core.Native;
using System.Windows;

namespace LevelEditorPlugin.BF1Core.Extensions;

public class BF1CoreMenuExtension : MenuExtension
{
    public override string TopLevelMenuName => "地图";
    public override string MenuItemName => "战地1地图编辑器";

    private MainWindow mainWindow = null;

    public BF1CoreMenuExtension()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (mainWindow == null)
            {
                mainWindow = new MainWindow();
                mainWindow.Hide();
            }

            HotKey.AddKey(Keys.F1);
            HotKey.AddKey(Keys.F5);
            HotKey.Run();
        });
    }

    public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
    {
        // 首次创建
        if (mainWindow == null)
            goto RE_CREATE;

        // 窗口隐藏
        if (mainWindow.Visibility == Visibility.Hidden)
        {
            mainWindow.Show();
            return;
        }

        // 窗口最小化
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
            return;
        }

        // 窗口不在最前面（为false代表窗口已被关闭）
        if (mainWindow.IsVisible)
        {
            mainWindow.Topmost = true;
            mainWindow.Topmost = false;
            return;
        }

        // 窗口已被关闭，重新创建
        mainWindow.Close();
        mainWindow = null;

    RE_CREATE:
        mainWindow = new MainWindow();
        mainWindow.Show();
    });
}
