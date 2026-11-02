using System.Runtime.InteropServices;
using SmoothScroll.Native;

namespace SmoothScroll.Core;

/// <summary>
/// Решение по событию колеса: true — поглотить событие (движок сам анимирует прокрутку),
/// false — пропустить дальше (нативное поведение).
/// Вызывается в потоке хука и должен отрабатывать быстро.
/// </summary>
internal delegate bool WheelGate(short delta, bool horizontal, NativeMethods.POINT pt);

/// <summary>
/// Низкоуровневый хук мыши (WH_MOUSE_LL) в отдельном потоке со своим циклом сообщений.
/// Перехватывает классические «щелчки» колеса; мелкие дельты (высокоточные тачпады)
/// и события, инжектированные другими программами, проходят без изменений.
/// </summary>
public sealed class MouseWheelHook : IDisposable
{
    // Минимальная дельта, которую считаем «щелчком» колеса (стандарт — 120).
    private const short DetentThreshold = 100;

    private IntPtr _hook = IntPtr.Zero;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new(false);
    private NativeMethods.HookProc? _proc; // держим делегат живым, пока жив хук
    private volatile bool _disposed;

    internal WheelGate? Gate { get; set; }
    public int FailError { get; private set; }

    /// <summary>Устанавливает хук. Бросает исключение, если не удалось.</summary>
    public void Start()
    {
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "SmoothScroll.MouseHook",
            Priority = ThreadPriority.AboveNormal
        };
        thread.Start();
        _ready.Wait(5000);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Не удалось установить перехват мыши (код ошибки {FailError}).");
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _proc = HookProc;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            FailError = Marshal.GetLastWin32Error();
        _ready.Set();
        if (_hook == IntPtr.Zero)
            return;

        // Цикл сообщений необходим: без него хук низкого уровня не получает события.
        while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_disposed)
        {
            int msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_MOUSEWHEEL || msg == NativeMethods.WM_MOUSEHWHEEL)
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                bool injected = (info.flags & NativeMethods.LLMHF_INJECTED) != 0
                                || info.dwExtraInfo == NativeMethods.InjectedMarker;
                if (!injected)
                {
                    short delta = (short)((info.mouseData >> 16) & 0xFFFF);
                    var gate = Gate;
                    if (gate != null && Math.Abs(delta) >= DetentThreshold
                        && gate(delta, msg == NativeMethods.WM_MOUSEHWHEEL, info.pt))
                    {
                        return new IntPtr(1); // поглощаем исходное событие
                    }
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        if (_threadId != 0)
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _ready.Dispose();
    }
}
