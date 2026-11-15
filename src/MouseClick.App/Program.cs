// 文件：Program.cs
// 描述：Windows 桌面程序入口，启用物理像素坐标感知。
// 版本：v1.0
// 日期：2026/11/15
// 历史：v1.0 2026/11/15 新增应用入口。
namespace MouseClick.App;

internal static class Program
{
    // 2026/11/15 新增：在创建窗口前启用逐显示器 DPI 感知。
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
