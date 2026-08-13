using System.Windows;

namespace CleanMaster;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.LoadDiskInfo();
            }
        };
    }

    /// <summary>
    /// 方案 C 滚轮优化: 展开项内部 ScrollViewer 滚动到边界时,
    /// 将滚轮事件传递给父级 ScrollViewer, 实现无缝衔接。
    /// </summary>
    private void ExpandedItem_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ScrollViewer sv) return;
        // 向下滚到底 or 向上滚到顶 → 冒泡给父级
        bool atTop = sv.VerticalOffset <= 0;
        bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight;
        if ((e.Delta < 0 && atBottom) || (e.Delta > 0 && atTop))
        {
            e.Handled = false; // 让事件冒泡到外层
            return;
        }
        // 否则自己处理
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    /// <summary>
    /// 分类列表主滚动区域不需要特殊处理, 但如果内层事件冒泡上来直接由 ScrollViewer 默认处理。
    /// </summary>
    private void CleanPage_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        // 默认行为已满足需求 — 外层 ScrollViewer 自己滚动
        // 仅当事件从内层冒泡上来(非 Handled)时才到这里
    }
}
