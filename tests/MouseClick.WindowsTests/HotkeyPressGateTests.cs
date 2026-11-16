// 文件：HotkeyPressGateTests.cs
// 描述：纯逻辑验证热键消息和按键状态采样的去重、修饰键边沿及时间戳回绕。
// 版本：v1.0
// 日期：2026/11/16
// 历史：v1.0 2026/11/16 新增无窗口、无输入注入的双通道热键回归。

using MouseClick.App.Interop;

namespace MouseClick.WindowsTests;

// 2026/11/16 新增：默认验收及独立纯逻辑入口均可调用，不读取真实键盘或注册系统热键。
internal static class HotkeyPressGateTests
{
    internal static int Run()
    {
        (string Name, Action Check)[] cases =
        [
            ("采样只在按下边沿触发，释放后可再次触发", SampleEdges),
            ("无效组合按住期间松开修饰键不能凭空触发", ModifierRelease),
            ("原生先到后，同次按住采样不重复", NativeBeforeSample),
            ("连续原生消息不依赖采样看到释放", ConsecutiveNative),
            ("采样先到后拒绝同刻及更早原生消息", SampleBeforeNative),
            ("释放后仍拒绝迟到的已采样消息", LateNativeAfterRelease),
            ("较新的原生消息不受采样按住锁定影响", NewNativeAfterSample),
            ("重新注册重置去重并屏蔽已按住的键", ResetHeld),
            ("uint 回绕后拒绝旧消息并接受新消息", WrapAfterSample),
            ("uint 回绕前的采样允许回绕后的新消息", WrapBeforeSample),
            ("两个快捷键独立去重", IndependentGates)
        ];
        int failures = 0;
        foreach (var item in cases)
        {
            try { item.Check(); Console.WriteLine("通过：" + item.Name + "（纯逻辑，无输入）。"); }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"失败：{item.Name} — {exception.Message}");
            }
        }
        Console.WriteLine($"热键去重回归：{cases.Length - failures}/{cases.Length} 项通过。");
        return failures;
    }

    // 2026/11/16 新增：按键本身决定释放边沿，组合有效性只决定首次按下能否触发。
    private static void SampleEdges()
    {
        var gate = new HotkeyPressGate();
        Check(gate.Sample(true, true, 10), "首次按下应触发。");
        Check(!gate.Sample(true, true, 20), "持续按住不应再次触发。");
        Check(!gate.Sample(false, true, 30), "释放不应触发。");
        Check(gate.Sample(true, true, 40), "释放后新按下应触发。");
    }

    private static void ModifierRelease()
    {
        var gate = new HotkeyPressGate();
        Check(!gate.Sample(true, false, 10), "有额外修饰键或未获注册时不能触发。");
        Check(!gate.Sample(true, true, 20), "松开修饰键或更改许可不能制造按下边沿。");
        gate.Sample(false, false, 30);
        Check(gate.Sample(true, true, 40), "重新按下有效组合应触发。");
    }

    // 2026/11/16 新增：原生事件各自有效，采样只去掉同次按住的重复通知。
    private static void NativeBeforeSample()
    {
        var gate = new HotkeyPressGate();
        Check(gate.Native(10, true), "原生消息应触发。");
        Check(!gate.Sample(true, true, 20), "同次按住的采样不应重复。");
        gate.Sample(false, true, 30);
        Check(gate.Sample(true, true, 40), "下次采样按下仍应触发。");
    }

    private static void ConsecutiveNative()
    {
        var gate = new HotkeyPressGate();
        Check(gate.Native(10, false) && gate.Native(11, false), "采样未见到的同批 down/up 必须各自触发。");
        Check(gate.Native(12, true) && gate.Native(13, true), "不得用按住锁丢弃连续独立原生消息。");
    }

    // 2026/11/16 新增：时间戳来自同一系统毫秒时钟，已采样动作的迟到消息在释放后也不得重复触发。
    private static void SampleBeforeNative()
    {
        var gate = new HotkeyPressGate();
        Check(gate.Sample(true, true, 100), "采样应触发。");
        Check(!gate.Native(99, true) && !gate.Native(100, true), "更早和同刻消息应去重。");
    }

    private static void LateNativeAfterRelease()
    {
        var gate = new HotkeyPressGate();
        gate.Sample(true, true, 100);
        gate.Sample(false, true, 110);
        Check(!gate.Native(100, false), "释放不能撤销迟到消息去重。");
        Check(gate.Native(111, false), "新一次原生按下应触发。");
    }

    private static void NewNativeAfterSample()
    {
        var gate = new HotkeyPressGate();
        gate.Sample(true, true, 100);
        Check(gate.Native(101, true), "时间更新的原生按下必须独立触发。");
        Check(!gate.Sample(true, true, 102), "原生触发后持续按住不能被采样重复。");
    }

    // 2026/11/16 新增：重新注册时以当前按键状态初始化，避免设置切换或停止清理制造新边沿。
    private static void ResetHeld()
    {
        var gate = new HotkeyPressGate();
        gate.Sample(true, true, 100);
        gate.Reset(true);
        Check(!gate.Sample(true, true, 101), "重置时已按住应等释放。");
        Check(gate.Native(10, false), "重置后旧去重时间戳应清空。");
        gate.Reset();
        Check(gate.Sample(true, true, 102), "未按住重置后可接收新边沿。");
    }

    private static void WrapAfterSample()
    {
        var gate = new HotkeyPressGate();
        gate.Sample(true, true, 3);
        Check(!gate.Native(uint.MaxValue - 2, true), "回绕前的旧消息仍应拒绝。");
        Check(gate.Native(4, false), "回绕后的新消息应接受。");
    }

    private static void WrapBeforeSample()
    {
        var gate = new HotkeyPressGate();
        gate.Sample(true, true, uint.MaxValue - 2);
        Check(!gate.Native(uint.MaxValue - 3, true), "回绕前的旧消息应拒绝。");
        Check(gate.Native(3, false), "跨回绕的新消息应接受。");
    }

    private static void IndependentGates()
    {
        var toggle = new HotkeyPressGate();
        var stop = new HotkeyPressGate();
        toggle.Sample(true, true, 100);
        Check(stop.Native(99, false), "开始键的采样不能抑制停止键消息。");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
