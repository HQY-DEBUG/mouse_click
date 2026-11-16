// 文件：InputAccessGuard.cs
// 描述：发送输入前只读检查当前进程、前台窗口和目标像素窗口的完整性权限。
// 版本：v1.0
// 日期：2026/11/16
// 历史：
// v1.0 2026/11/16 新增完整性级别与 UIAccess 预检，阻止已知权限不匹配时继续发送。

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MouseClick.Core;

namespace MouseClick.App.Interop;

// 2026/11/16 新增：WindowsClickOutput 在每次输入前调用；只查询令牌，不提升权限、不发送输入、不跨点击缓存进程信息。
internal static class InputAccessGuard
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int TokenUiAccess = 26;
    private const int ErrorInsufficientBuffer = 122;

    // 2026/11/16 新增：跟随鼠标模式读取当前像素，同时检查前台权限，覆盖目标像素属于自身但前台权限较高的情况。
    internal static void EnsureCanSend(ClickPoint? target)
    {
        using SafeAccessTokenHandle currentToken = OpenQueryToken((uint)Environment.ProcessId);
        uint currentIntegrity = ReadIntegrity(currentToken);
        bool hasUiAccess = ReadUiAccess(currentToken);
        EnsureWindowCanReceive(GetForegroundWindow(), currentIntegrity, hasUiAccess, "前台窗口");

        NativePoint point;
        if (target is not null)
            point = new NativePoint { X = target.X, Y = target.Y };
        else if (!GetCursorPos(out point))
            throw NativeError("读取当前鼠标位置", Marshal.GetLastPInvokeError());
        EnsureWindowCanReceive(WindowFromPoint(point), currentIntegrity, hasUiAccess, "目标位置的窗口");
    }

    // 2026/11/16 新增：无窗口表示没有可比较的接收进程；已有窗口失效或令牌查询失败时明确传播错误。
    internal static void EnsureWindowCanReceive(IntPtr window, uint currentIntegrity, bool hasUiAccess, string description)
    {
        if (window == IntPtr.Zero) return;
        if (GetWindowThreadProcessId(window, out uint processId) == 0)
            throw NativeError($"读取{description}所属进程", Marshal.GetLastPInvokeError());
        uint receivingIntegrity = ReadProcessIntegrity(processId);
        EnsureIntegrityAllowsInput(currentIntegrity, receivingIntegrity, hasUiAccess, description);
    }

    // 2026/11/16 新增：数值零仍是有效的完整性级别；只有当前 UIAccess 可豁免向更高完整性窗口的限制。
    internal static void EnsureIntegrityAllowsInput(uint currentIntegrity, uint receivingIntegrity,
        bool hasUiAccess, string description)
    {
        if (!hasUiAccess && receivingIntegrity > currentIntegrity)
            throw new InvalidOperationException(
                $"{description}的权限高于连点器，Windows 可能阻止鼠标输入。" +
                "请先退出连点器，再右键选择“以管理员身份运行”；或将目标程序改为普通权限后重新启动。");
    }

    // 2026/11/16 新增：每次重新打开指定进程并读取主令牌，避免缓存 PID 后因退出重用而误判权限。
    internal static uint ReadProcessIntegrity(uint processId)
    {
        using SafeAccessTokenHandle token = OpenQueryToken(processId);
        return ReadIntegrity(token);
    }

    // 2026/11/16 新增：仅申请进程有限查询和 TOKEN_QUERY，SafeHandle 在成功与失败路径均负责释放内核句柄。
    private static SafeAccessTokenHandle OpenQueryToken(uint processId)
    {
        using SafeProcessHandle process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
            throw NativeError($"打开进程 {processId} 的权限查询句柄", Marshal.GetLastPInvokeError());
        if (!OpenProcessToken(process, TokenQuery, out SafeAccessTokenHandle token))
        {
            int error = Marshal.GetLastPInvokeError();
            token.Dispose();
            throw NativeError($"读取进程 {processId} 的访问令牌", error);
        }
        return token;
    }

    // 2026/11/16 新增：按系统返回长度分配完整性标签缓冲区，校验 SID 后读取最后一个子授权 RID，最后始终释放缓冲区。
    private static uint ReadIntegrity(SafeAccessTokenHandle token)
    {
        bool sized = GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out int length);
        int error = Marshal.GetLastPInvokeError();
        if (!sized && error != ErrorInsufficientBuffer)
            throw NativeError("查询进程完整性标签长度", error);
        if (length < Marshal.SizeOf<TokenMandatoryLabel>())
            throw new InvalidDataException("进程完整性标签长度无效，已停止发送鼠标输入。");

        IntPtr buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out int returned))
                throw NativeError("读取进程完整性标签", Marshal.GetLastPInvokeError());
            if (returned < Marshal.SizeOf<TokenMandatoryLabel>())
                throw new InvalidDataException("进程完整性标签不完整，已停止发送鼠标输入。");
            TokenMandatoryLabel label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            if (label.Sid == IntPtr.Zero || !IsValidSid(label.Sid))
                throw new InvalidDataException("进程完整性 SID 无效，已停止发送鼠标输入。");
            byte count = Marshal.ReadByte(GetSidSubAuthorityCount(label.Sid));
            if (count == 0)
                throw new InvalidDataException("进程完整性 SID 缺少级别，已停止发送鼠标输入。");
            return unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(label.Sid, (uint)count - 1)));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // 2026/11/16 新增：UIAccess 来自当前进程令牌中的 DWORD，读取失败不能当作默认权限继续执行。
    private static bool ReadUiAccess(SafeAccessTokenHandle token)
    {
        if (!GetTokenInformationValue(token, TokenUiAccess, out uint value, sizeof(uint), out int returned))
            throw NativeError("读取连点器 UIAccess 权限", Marshal.GetLastPInvokeError());
        if (returned != sizeof(uint))
            throw new InvalidDataException("连点器 UIAccess 权限长度无效，已停止发送鼠标输入。");
        return value != 0;
    }

    // 2026/11/16 新增：保留 Win32 错误码与实际系统说明，让调用方显示查询失败的具体原因。
    private static Win32Exception NativeError(string operation, int error) => new(error,
        $"无法{operation}（Windows 错误 {error}：{new Win32Exception(error).Message}），已停止发送鼠标输入。");

    // 2026/11/16 新增：TOKEN_MANDATORY_LABEL 首字段为 SID_AND_ATTRIBUTES，顺序布局保持原生指针对齐。
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    // 2026/11/16 新增：只导入公开的窗口定位和令牌查询接口，不导入输入注入或权限修改接口。
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        IntPtr information, int length, out int returned);

    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformationValue(SafeAccessTokenHandle token, int informationClass,
        out uint information, int length, out int returned);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsValidSid(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
