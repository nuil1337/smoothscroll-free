using System.Collections.Concurrent;
using System.Diagnostics;
using SmoothScroll.Native;

namespace SmoothScroll.Core;

/// <summary>
/// Движок плавной прокрутки.
///
/// Потоковая модель — принципиальный момент производительности:
///  • поток хука ТОЛЬКО кладёт щелчок в lock-free очередь и будит поток анимации.
///    Любая блокировка в потоке хука (даже микросекундная) приводит к системному
///    таймауту хука и лагам всей мыши — поэтому её там нет вообще;
///  • отдельный поток анимации (высокий приоритет) разбирает очередь, считает
///    кадры по высокоточному waitable-таймеру и вызывает SendInput вне блокировок;
///  • медленные операции (запрос имени процесса для чёрного списка) выполняются
///    только в потоке анимации, никогда — в потоке хука.
/// </summary>
public sealed class SmoothScroller : IDisposable
{
    private const int FrameIntervalMs = 8; // ~125 Гц
    private const double MaxFrameMs = 50;  // предел «догоняющего» кадра после ступора

    private readonly record struct WheelImpulse(short Delta, bool Horizontal);

    private readonly Settings _settings;
    private readonly MouseWheelHook _hook = new();
    private readonly ProcessBlacklist _blacklist;
    private readonly ConcurrentQueue<WheelImpulse> _impulses = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly AxisState _vertical = new();
    private readonly AxisState _horizontal = new();
    private readonly NativeMethods.INPUT[] _inputBuf = new NativeMethods.INPUT[1];

    private Thread? _animThread;
    private IntPtr _frameTimer;
    private IntPtr[] _waits = Array.Empty<IntPtr>();
    private bool _coarseTimerFallback; // используется timeBeginPeriod(1)
    private bool _animating;           // трогает только поток анимации
    private volatile bool _running;
    private bool _disposed;

    public SmoothScroller(Settings settings)
    {
        _settings = settings;
        _blacklist = new ProcessBlacklist(settings);
        _hook.Gate = OnWheel;
    }

    public void Start()
    {
        _hook.Start();
        _running = true;
        _animThread = new Thread(AnimationLoop)
        {
            IsBackground = true,
            Name = "SmoothScroll.Animator",
            Priority = ThreadPriority.AboveNormal
        };
        _animThread.Start();
    }

    /// <summary>Поток хука: обязан быть мгновенным и неблокирующим.</summary>
    private bool OnWheel(short delta, bool horizontal, NativeMethods.POINT pt)
    {
        var s = _settings;
        if (_disposed || !s.Enabled) return false;
        if (horizontal && !s.SmoothHorizontal) return false;
        if (s.Blacklist.Count > 0 && _blacklist.IsBlockedFast(pt)) return false;

        _impulses.Enqueue(new WheelImpulse(delta, horizontal));
        _wake.Set();
        return true;
    }

    private void AnimationLoop()
    {
        // Высокоточный таймер кадров (Windows 10 1803+); при недоступности —
        // обычный таймер плюс timeBeginPeriod(1).
        _frameTimer = NativeMethods.CreateWaitableTimerEx(IntPtr.Zero, IntPtr.Zero,
            NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
            NativeMethods.TIMER_MODIFY_STATE | NativeMethods.SYNCHRONIZE);
        bool hasTimer = _frameTimer != IntPtr.Zero;
        if (!hasTimer)
        {
            _frameTimer = NativeMethods.CreateWaitableTimerEx(IntPtr.Zero, IntPtr.Zero, 0,
                NativeMethods.TIMER_MODIFY_STATE | NativeMethods.SYNCHRONIZE);
            hasTimer = _frameTimer != IntPtr.Zero;
            if (hasTimer) _coarseTimerFallback = true;
        }

        if (hasTimer)
            _waits = new[] { _frameTimer, _wake.SafeWaitHandle.DangerousGetHandle() };

        long frameStamp = Stopwatch.GetTimestamp();

        while (_running)
        {
            _blacklist.ResolvePending(); // медленные запросы — здесь, не в потоке хука

            bool freshStart = false;
            while (_impulses.TryDequeue(out var imp))
            {
                (imp.Horizontal ? _horizontal : _vertical).AddImpulse(imp.Delta, _settings);
                freshStart |= !_animating;
            }

            if (freshStart)
            {
                // Начало новой анимации: отсчёт кадров заново, иначе первый кадр
                // «догонит» простой и дёрнет прокрутку скачком.
                frameStamp = Stopwatch.GetTimestamp();
            }

            double dtMs = ElapsedMsSince(ref frameStamp);
            bool busy = StepAxis(_vertical, dtMs, horizontal: false);
            busy |= StepAxis(_horizontal, dtMs, horizontal: true);
            _animating = busy || !_impulses.IsEmpty;

            if (_animating)
            {
                if (hasTimer)
                {
                    long due = -FrameIntervalMs * 10_000; // относительное время, единицы 100 нс
                    NativeMethods.SetWaitableTimer(_frameTimer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
                    if (NativeMethods.WaitForMultipleObjects(2, _waits, false, uint.MaxValue)
                        == NativeMethods.WAIT_FAILED)
                        Thread.Sleep(FrameIntervalMs);
                }
                else
                {
                    _wake.WaitOne(FrameIntervalMs);
                }
            }
            else if (hasTimer)
            {
                // Простой: ждём следующий щелчок. Таймер не взведён, так что
                // ожидание прервёт только событие _wake.
                if (NativeMethods.WaitForMultipleObjects(2, _waits, false, uint.MaxValue)
                    == NativeMethods.WAIT_FAILED)
                    Thread.Sleep(FrameIntervalMs);
            }
            else
            {
                _wake.WaitOne(Timeout.Infinite);
            }
        }
    }

    private bool StepAxis(AxisState axis, double dtMs, bool horizontal)
    {
        if (!axis.Active) return false;
        var s = _settings;

        int due = axis.ComputeDueUnits(dtMs, s);
        if (due != 0)
        {
            SendWheel(due, horizontal);
            axis.MarkSent(due);
        }

        if (axis.IsFinished(s))
        {
            axis.Reset();
            return false;
        }
        return true;
    }

    /// <summary>Инжектирует шаг прокрутки, помеченный как «наш» — хук его пропустит.</summary>
    private void SendWheel(int units, bool horizontal)
    {
        _inputBuf[0] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dwFlags = horizontal
                        ? NativeMethods.MOUSEEVENTF_HWHEEL
                        : NativeMethods.MOUSEEVENTF_WHEEL,
                    mouseData = unchecked((uint)units),
                    dwExtraInfo = NativeMethods.InjectedMarker
                }
            }
        };
        NativeMethods.SendInput(1, _inputBuf, NativeMethods.INPUT.Size);
    }

    private static double ElapsedMsSince(ref long stamp)
    {
        long now = Stopwatch.GetTimestamp();
        double ms = (now - stamp) * 1000.0 / Stopwatch.Frequency;
        stamp = now;
        return Math.Clamp(ms, 0.1, MaxFrameMs);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        _wake.Set();
        try { _animThread?.Join(1500); } catch { /* поток мог не успеть стартовать */ }
        _hook.Dispose();
        if (_frameTimer != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_frameTimer);
            _frameTimer = IntPtr.Zero;
        }
        if (_coarseTimerFallback) NativeMethods.timeEndPeriod(1);
        _wake.Dispose();
    }

    /// <summary>
    /// Состояние одной оси. Доступается только из потока анимации — синхронизация не нужна.
    /// Единица измерения — «единицы колеса» (один щелчок = ±120). В кадре отправляются
    /// только целые единицы, дробные копятся и не теряются.
    /// </summary>
    private sealed class AxisState
    {
        private const double RapidTickMs = 350; // окно серии щелчков для ускорения

        private double _remaining;  // сколько единиц ещё отправить (со знаком)
        private double _target;     // суммарная цель всплеска (режимы с таймлайном)
        private double _carry;      // дробный остаток (экспоненциальный режим)
        private double _startMs;    // начало всплеска
        private double _lastTickMs; // время последнего щелчка
        private int _consecutive;   // щелчков подряд в одном направлении
        private bool _active;

        public bool Active => _active;

        public void AddImpulse(short delta, Settings s)
        {
            double nowMs = NowMs();
            double sign = Math.Sign(delta);

            if (_active && Math.Sign(_remaining) == sign && nowMs - _lastTickMs < RapidTickMs)
                _consecutive = Math.Min(_consecutive + 1, 10);
            else
                _consecutive = 1;
            _lastTickMs = nowMs;

            // Ускорение растёт с каждым быстрым щелчком серии (полный разгон за ~5 щелчков).
            double accel = 1.0;
            if (s.Acceleration > 1.001)
                accel = Math.Min(s.Acceleration, 1.0 + (s.Acceleration - 1.0) * (_consecutive - 1) / 4.0);

            double units = delta * s.StepMultiplier * accel;

            if (!_active)
            {
                _active = true;
                _carry = 0;
                _target = 0;
                _startMs = nowMs;
                if (Math.Sign(_remaining) != 0 && Math.Sign(_remaining) != sign)
                    _remaining = 0; // резкая смена направления — начинаем с чистого листа
            }
            else if (s.Easing != EasingMode.Exponential)
            {
                // Ребейз таймлайна: добавленный щелчок не должен вызывать скачок —
                // прошедшая часть кривой сохраняется, скорость просто растёт.
                double t = Progress(nowMs - _startMs, s.AnimationMs);
                _startMs = nowMs - t * s.AnimationMs;
            }

            _target += units;
            _remaining += units;
        }

        /// <summary>Сколько целых единиц пора отправить на этом кадре.</summary>
        public int ComputeDueUnits(double dtMs, Settings s)
        {
            double nowMs = NowMs();

            if (s.Easing == EasingMode.Exponential)
            {
                // Экспоненциальное затухание: за кадр отправляем долю остатка.
                // tau = AnimationMs/4 — движение растянуто во времени: резкий
                // старт сглажен, полный выбег ~1.2×AnimationMs (важно для
                // системных приложений, где списки «щёлкают» целыми строками —
                // при резкой кривой все строки укладываются в первые мгновения
                // и прокрутка выглядит как обычная).
                double tau = Math.Max(40, s.AnimationMs) / 4.0;
                _carry += _remaining * (1.0 - Math.Exp(-dtMs / tau));
                int due = (int)Math.Truncate(_carry);
                _carry -= due;
                return due;
            }

            // Режимы с таймлайном: позиция на кривой от начала всплеска.
            double tPrev = Progress(nowMs - dtMs - _startMs, s.AnimationMs);
            double tNow = Progress(nowMs - _startMs, s.AnimationMs);
            return (int)Math.Truncate(_target * (Ease(tNow, s) - Ease(tPrev, s)));
        }

        public void MarkSent(int units) => _remaining -= units;

        public bool IsFinished(Settings s)
        {
            if (Math.Abs(_remaining) >= 1) return false;
            double nowMs = NowMs();
            return s.Easing == EasingMode.Exponential
                ? nowMs - _lastTickMs > 250 // ждём возможного продолжения серии
                : nowMs - _startMs >= s.AnimationMs;
        }

        public void Reset()
        {
            _active = false;
            _target = 0;
            _carry = 0;
            _consecutive = 0;
            // дробный «хвост» меньше единицы остаётся в _remaining и не мешает
        }

        private static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

        private static double Progress(double elapsedMs, int durationMs)
            => Math.Clamp(elapsedMs / Math.Max(1, durationMs), 0.0, 1.0);

        private static double Ease(double t, Settings s) => s.Easing switch
        {
            EasingMode.Linear => t,
            EasingMode.EaseOut => 1.0 - Math.Pow(1.0 - t, 3),
            _ => t // экспоненциальный режим таймлайн не использует
        };
    }
}
