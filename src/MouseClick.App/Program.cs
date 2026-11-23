// 文件：Program.cs
// 描述：Windows 桌面程序入口，配置屏幕坐标感知与原生控件主题。
// 版本：v1.1
// 日期：2026/11/23
// 历史：v1.1 2026/11/23 在创建窗体前初始化深色控件。
// v1.0 2026/11/15 新增应用入口。
namespace MouseClick.App;

internal static class Program
{
    // 2026/11/15 新增：在创建窗口前启用逐显示器 DPI 感知。
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        // 2026/11/23 新增：将窗口和原生控件的外观与石墨色客户端协调。
        Ui.EnableNativeDarkMode();
        Application.Run(new MainForm());
    }
}
