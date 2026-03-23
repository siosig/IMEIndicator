using System;
using System.Windows.Media;

namespace IMEIndicator.Services;

/// <summary>
/// マウスカーソル位置を追跡（CompositionTarget.Rendering によるVBlank同期）
/// PeriodicTimer(16ms)を廃止し、DWMのVBlankに同期してカクつきを解消
/// </summary>
public class MouseTracker : IDisposable
{
    public event Action<int, int>? MouseMoved;
    private NativeMethods.POINT _lastPosition;
    private bool _isTracking;

    public void Start()
    {
        if (_isTracking) return;
        _isTracking = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
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

    public void Stop()
    {
        if (!_isTracking) return;
        _isTracking = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    public void Dispose() => Stop();
}
