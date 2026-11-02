using System.Collections.Concurrent;
using System.Diagnostics;
using SmoothScroll.Native;

namespace SmoothScroll.Core;

/// <summary>
/// Чёрный список процессов. Поток хука вызывает только IsBlockedFast —
/// исключительно быстрые операции (WindowFromPoint и чтение кэша), ни одной
/// блокирующей. Имя процесса для нового окна уточняет поток анимации:
/// пока окно не опознано, прокрутка остаётся нативной.
/// </summary>
public sealed class ProcessBlacklist
{
    private readonly Settings _settings;
    private readonly ConcurrentDictionary<IntPtr, bool> _cache = new();
    private readonly ConcurrentDictionary<IntPtr, byte> _pending = new();
    private readonly ConcurrentQueue<IntPtr> _resolveQueue = new();

    public ProcessBlacklist(Settings settings) => _settings = settings;

    /// <summary>Только для потока хука: быстро, без блокировок и без обращений к процессам.</summary>
    internal bool IsBlockedFast(NativeMethods.POINT pt)
    {
        IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;
        if (_cache.TryGetValue(hwnd, out bool blocked)) return blocked;
        if (_pending.TryAdd(hwnd, 0)) _resolveQueue.Enqueue(hwnd);
        return false; // пока не опознано — оставляем нативную прокрутку
    }

    /// <summary>Вызывается из потока анимации: разбирает очередь новых окон.</summary>
    public void ResolvePending()
    {
        while (_resolveQueue.TryDequeue(out IntPtr hwnd))
        {
            _pending.TryRemove(hwnd, out _);
            _cache[hwnd] = IsBlacklisted(hwnd);
        }
        if (_cache.Count > 1024) _cache.Clear(); // окна закрываются — периодически чистим
    }

    private bool IsBlacklisted(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0)
            {
                using var p = Process.GetProcessById((int)pid);
                return _settings.Blacklist.Contains(Settings.NormalizeProcessName(p.ProcessName));
            }
        }
        catch
        {
            // процесс мог завершиться — считаем окно не из чёрного списка
        }
        return false;
    }
}
