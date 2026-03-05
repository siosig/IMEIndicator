using System;
using System.Threading;
using System.Threading.Tasks;

namespace IMEIndicatorClock.Services;

/// <summary>
/// マウスカーソル位置を追跡（PeriodicTimerによるバックグラウンドポーリング）
/// CompositionTarget.Rendering を廃止し、UIスレッドを解放
/// </summary>
public class MouseTracker : IDisposable
{
    public event Action<int, int>? MouseMoved;
    private NativeMethods.POINT _lastPosition;
    private CancellationTokenSource? _cts;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _ = TrackMouseAsync(_cts.Token);
    }

    private async Task TrackMouseAsync(CancellationToken token)
    {
        // 16ms間隔（約60fps相当）でバックグラウンドポーリング
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (NativeMethods.GetCursorPos(out var currentPos))
                {
                    if (currentPos.X != _lastPosition.X || currentPos.Y != _lastPosition.Y)
                    {
                        _lastPosition = currentPos;
                        MouseMoved?.Invoke(currentPos.X, currentPos.Y);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常な停止
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();
}
