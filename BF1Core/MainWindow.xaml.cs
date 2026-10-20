using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Screens;
using System;
using System.ComponentModel;
using System.Windows;

namespace LevelEditorPlugin.BF1Core;

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 当前窗口的实例，用于操作此窗口
    /// </summary>
    public static MainWindow WinInstance { get; private set; } = null;

    /// <summary>
    /// 构造方法
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        WinInstance = this;
    }

    /// <summary>
    /// 窗口加载事件
    /// </summary>
    private void Window_Main_Loaded(object sender, RoutedEventArgs e)
    {
    }

    /// <summary>
    /// 窗口渲染完成事件
    /// </summary>
    private void Window_Main_ContentRendered(object sender, EventArgs e)
    {
    }

    /// <summary>
    /// 窗口关闭事件
    /// </summary>
    private void Window_Main_Closing(object sender, CancelEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
        e.Cancel = true;
    }

    /// <summary>
    /// 更新选中的实体
    /// </summary>
    public static void UpdateSelectedEntity(Entity entity, LevelEditorScreen screen)
    {
        if (WinInstance == null)
            return;

        WinInstance.TabItem_MeshView.UpdateSelectedEntity(entity, screen);
    }

    /// <summary>
    /// 更新实体Transform信息
    /// </summary>
    public static void UpdateEntityTransform()
    {
        if (WinInstance == null)
            return;

        WinInstance.TabItem_MeshView.UpdateUI();
    }
}
