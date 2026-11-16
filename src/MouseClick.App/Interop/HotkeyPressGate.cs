// 文件：HotkeyPressGate.cs
// 描述：合并原生热键和按键状态采样，保证按住边沿与迟到消息不重复派发。
// 版本：v1.0
// 日期：2026/11/16
// 历史：v1.0 2026/11/16 新增独立可验证的热键双通道去重。

namespace MouseClick.App.Interop;

// 2026/11/16 新增：仅保存单个快捷键状态，HotkeyWindow 仍负责注册许可和精确组合校验。
internal sealed class HotkeyPressGate
{
    private bool _keyWasDown;
    private uint? _lastSampleTimestamp;

    // 2026/11/16 新增：即使组合无效也记住按下，松开修饰键不能把仍按住的功能键变成新边沿。
    internal bool Sample(bool keyDown, bool eligible, uint timestamp)
    {
        bool pressed = keyDown && !_keyWasDown;
        _keyWasDown = keyDown;
        if (!pressed || !eligible) return false;
        _lastSampleTimestamp = timestamp;
        return true;
    }

    // 2026/11/16 新增：只拒绝已被采样覆盖的迟到消息；连续原生消息独立有效，不依赖采样观察释放。
    internal bool Native(uint timestamp, bool keyDown)
    {
        if (_lastSampleTimestamp is uint sampled && unchecked((int)(timestamp - sampled)) <= 0)
            return false;
        _keyWasDown = keyDown;
        return true;
    }

    // 2026/11/16 新增：注册变更时清空去重历史，当前已按住的键必须先释放才能触发采样。
    internal void Reset(bool keyDown = false)
    {
        _keyWasDown = keyDown;
        _lastSampleTimestamp = null;
    }
}
