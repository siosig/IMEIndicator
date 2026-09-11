// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;
// System.Threading と System.Windows.Forms は両方とも暗黙 global using（GlobalUsings.g.cs）で
// 有効になっており、どちらにも Timer 型があるため素の "Timer" は CS0104（あいまい参照）になる。
// LogTests.cs の "using Log = IMEIndicator.Services.Log;" と同じ理由で、using-alias により
// 明示的に System.Windows.Forms.Timer を指す。
using FormsTimer = System.Windows.Forms.Timer;

namespace IMEIndicator.Services;

/// <summary>
/// マウスカーソル位置を一定間隔で監視し、移動時にイベントを発火する。
/// 移植元: src/cpp/services/MouseTracker.h / .cpp の class MouseTracker。
/// </summary>
/// <remarks>
/// C++ 版は HWND_MESSAGE の隠しウィンドウに対する SetTimer(既定 16ms) + WM_TIMER と
/// GetCursorPos の差分検出で実装されている（WPF 版 MouseTracker.cs の CompositionTarget.Rendering
/// の代替）。C# 版は同じ Win32 メッセージポンプ上で動作する <see cref="System.Windows.Forms.Timer"/>
/// （内部実装も隠しウィンドウ + WM_TIMER）を使い、GetCursorPos の P/Invoke（<see cref="NativeMethods"/>、
/// 既存 NativeMethods.User32.cs で定義済み）による差分検出方式をそのまま踏襲する。C++ 版と同じく、
/// このクラス自体は UI スレッド（メッセージループを持つスレッド）から使うことを前提とする。
/// </remarks>
public sealed class MouseTracker : IDisposable
{
    /// <summary>既定のティック間隔（ミリ秒 ≒ 60Hz）。移植元 MouseTracker::start() の既定値と同じ。</summary>
    public const int DefaultIntervalMs = 16;

    private readonly FormsTimer _timer;
    private POINT _lastPos;
    private bool _disposed;

    public MouseTracker()
    {
        _timer = new FormsTimer { Interval = DefaultIntervalMs };
        _timer.Tick += OnTick;
    }

    /// <summary>マウスカーソル位置が変化するたびに発火する（スクリーン座標 x, y）。</summary>
    public event Action<int, int>? PositionChanged;

    /// <summary>監視中かどうか（移植元 isRunning() に対応）。</summary>
    public bool IsRunning => _timer.Enabled;

    /// <summary>
    /// 監視を開始する。<paramref name="intervalMs"/> 省略時は既定の 16ms
    /// （移植元 <c>start(unsigned intervalMs = 16)</c> と同じ既定値）。
    /// 既に開始済みの場合は何もしない（二重 Start を無害化する。移植元の
    /// "if (timerWindow_) return true;" と同じ早期リターン方針）。呼び出し側は、
    /// インジケーター非表示中は必ず Stop を呼んでおく責務を負うが、このメソッド自体は
    /// 誤って複数回呼ばれても安全に動作する。
    /// </summary>
    public void Start(int intervalMs = DefaultIntervalMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_timer.Enabled)
        {
            return;
        }

        _timer.Interval = Math.Max(1, intervalMs);
        _timer.Start();

        // 差分検出ベースのため、これが無いとユーザーがマウスを動かすまで表示側が
        // 初期値 (0,0) のまま固定されてしまう（移植元 start() と同じ理由の初回即時発火）。
        NativeMethods.GetCursorPos(out _lastPos);
        PositionChanged?.Invoke(_lastPos.X, _lastPos.Y);
    }

    /// <summary>監視を停止する（移植元 stop() に対応）。停止済みの場合は何もしない。</summary>
    public void Stop()
    {
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out POINT current))
        {
            return;
        }

        if (current.X != _lastPos.X || current.Y != _lastPos.Y)
        {
            _lastPos = current;
            PositionChanged?.Invoke(current.X, current.Y);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer.Dispose();
    }
}
