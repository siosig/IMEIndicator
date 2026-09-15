// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;

namespace IMEIndicator.Services;

/// <summary>
/// 左右の Ctrl キー（VK_LCONTROL / VK_RCONTROL）と左右を区別しない汎用コード（VK_CONTROL）の
/// 押下状態を追跡し、「どれか 1 つでも押されている」（IsDown）の真偽が変わったときだけ true を返す、
/// アロケーションしない純粋な状態機械。永続化はしない
/// （specs/018-draggable-background-image/data-model.md §1「ControlKeyTracker」）。
/// 低レベルキーボードフック KeyboardHook（WH_KEYBOARD_LL）のコールバックから、キーダウン・
/// キーアップのたびに直接呼ばれる想定である。低レベルフックは登録したスレッドのコンテキストで
/// 呼ばれ、処理に時間がかかると Windows 7 以降は通知なくフックが外されるため（research.md R-2
/// 「フック内の処理を最小にする」）、内部状態は bool フィールドのみで持ち、
/// new・ボックス化・例外送出を行わない。
/// </summary>
public sealed class ControlKeyTracker
{
    // 左 Ctrl・右 Ctrl・左右を区別しない汎用コードを別々に持つ。IsDown はこの 3 つの論理和。
    private bool _left;
    private bool _right;
    private bool _generic;

    /// <summary>左 Ctrl・右 Ctrl・汎用コードのいずれかが押されていれば true。</summary>
    public bool IsDown => _left || _right || _generic;

    /// <summary>
    /// キーの押下/解放を通知する。IsDown が変化した場合のみ true を返す。
    /// オートリピート（同じキーの押下通知が連続する場合）や、一度も押されていないキーの解放通知では
    /// 状態が変わらないため false を返す。VK_LCONTROL / VK_RCONTROL / VK_CONTROL 以外の仮想キーコードを
    /// 渡した場合は、状態を変えずに常に false を返す。
    /// フック内から直接呼ばれる想定のため、アロケーション・例外を発生させない
    /// （research.md R-2「フック内の処理を最小にする」）。
    /// </summary>
    public bool OnKey(uint vkCode, bool isKeyUp)
    {
        bool before = IsDown;
        switch (vkCode)
        {
            case NativeConstants.VK_LCONTROL: _left = !isKeyUp; break;
            case NativeConstants.VK_RCONTROL: _right = !isKeyUp; break;
            case NativeConstants.VK_CONTROL: _generic = !isKeyUp; break;
            default: return false;
        }
        return before != IsDown;
    }

    /// <summary>左右・汎用のすべてのキーを解放済みの状態に戻す。</summary>
    public void Reset()
    {
        _left = false;
        _right = false;
        _generic = false;
    }
}
