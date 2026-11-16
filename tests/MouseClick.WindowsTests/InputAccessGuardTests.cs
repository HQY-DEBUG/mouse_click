// 文件：InputAccessGuardTests.cs
// 描述：验证只读完整性查询、权限阻止规则及无目标窗口路径，不发送桌面输入。
// 版本：v1.0
// 日期：2026/11/16
// 历史：
// v1.0 2026/11/16 新增完整性级别零值、同级、更高权限和 UIAccess 例外回归。

using MouseClick.App.Interop;

namespace MouseClick.WindowsTests;

// 2026/11/16 新增：只调用真实令牌查询及权限比较，隐藏窗口仅用于定位本进程，不依赖任何游戏窗口。
internal static class InputAccessGuardTests
{
    internal static int Run()
    {
        (string Name, Action Check)[] cases =
        [
            ("中等权限拒绝较高权限窗口并提示处理办法", CheckHigherIntegrity),
            ("零级别不能误作缺少权限信息", CheckZeroIntegrity),
            ("同级与更低级别允许继续预检", CheckEqualOrLowerIntegrity),
            ("UIAccess 允许跨完整性级别预检", CheckUiAccess),
            ("真实本进程窗口权限查询不发送输入", CheckCurrentProcess),
            ("没有接收窗口时不查询未知进程", CheckNoWindow)
        ];
        int failures = 0;
        foreach (var item in cases)
        {
            try
            {
                item.Check();
                Console.WriteLine("通过：" + item.Name + "（只读查询，无输入注入）。");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"失败：{item.Name} — {exception.GetType().Name}: {exception.Message}");
            }
        }
        Console.WriteLine($"输入权限预检回归：{cases.Length - failures}/{cases.Length} 项通过。");
        return failures;
    }

    // 2026/11/16 新增：独立指定中等与高完整性 RID，捕获高权限分支未阻止和缺少可执行处理说明。
    private static void CheckHigherIntegrity()
    {
        InvalidOperationException exception = ExpectBlocked(8192, 12288, false);
        Check(exception.Message.Contains("以管理员身份运行", StringComparison.Ordinal) &&
            exception.Message.Contains("普通权限", StringComparison.Ordinal) &&
            exception.Message.Contains("退出连点器", StringComparison.Ordinal),
            "权限冲突应说明先退出后手动以管理员身份运行，或将目标改为普通权限。");
    }

    // 2026/11/16 新增：RID 为零是实际低完整性值，不能绕过更高权限比较或被当作未知直接拒绝同级。
    private static void CheckZeroIntegrity()
    {
        ExpectBlocked(0, 8192, false);
        InputAccessGuard.EnsureIntegrityAllowsInput(0, 0, false, "测试窗口");
        InputAccessGuard.EnsureIntegrityAllowsInput(8192, 0, false, "测试窗口");
    }

    // 2026/11/16 新增：同级和较低级别不应触发越权阻止，避免预检使普通桌面目标不可用。
    private static void CheckEqualOrLowerIntegrity()
    {
        InputAccessGuard.EnsureIntegrityAllowsInput(8192, 8192, false, "测试窗口");
        InputAccessGuard.EnsureIntegrityAllowsInput(12288, 8192, false, "测试窗口");
        InputAccessGuard.EnsureIntegrityAllowsInput(12288, 12288, false, "测试窗口");
    }

    // 2026/11/16 新增：UIAccess 是当前进程的明确例外，防止仅比较 RID 后错误拦截辅助输入进程。
    private static void CheckUiAccess()
    {
        InputAccessGuard.EnsureIntegrityAllowsInput(8192, 12288, true, "测试窗口");
        InputAccessGuard.EnsureIntegrityAllowsInput(0, 12288, true, "测试窗口");
    }

    // 2026/11/16 新增：读取真实主令牌并沿窗口 PID 查询同一进程，窗口不显示、不注册热键也不注入鼠标。
    private static void CheckCurrentProcess()
    {
        uint currentIntegrity = InputAccessGuard.ReadProcessIntegrity((uint)Environment.ProcessId);
        using var window = new Form { ShowInTaskbar = false };
        InputAccessGuard.EnsureWindowCanReceive(window.Handle, currentIntegrity, false, "本进程隐藏窗口");
        Check(!window.Visible, "只读权限回归不得显示窗口。");
    }

    // 2026/11/16 新增：窗口句柄为空时没有接收进程，预检应正常返回，不能尝试打开 PID 0。
    private static void CheckNoWindow() =>
        InputAccessGuard.EnsureWindowCanReceive(IntPtr.Zero, 8192, false, "无目标窗口");

    // 2026/11/16 新增：必须观察到真实权限分支抛出异常，不以替身或复制的比较表达式作为判断结果。
    private static InvalidOperationException ExpectBlocked(uint current, uint receiving, bool hasUiAccess)
    {
        try
        {
            InputAccessGuard.EnsureIntegrityAllowsInput(current, receiving, hasUiAccess, "测试窗口");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"完整性级别 {current} → {receiving} 未阻止发送。");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
