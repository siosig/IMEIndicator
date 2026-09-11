// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;
using IMEIndicator.Interop;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// メディア（CD・音楽再生コントロール）系の内部コマンド実装。
/// 移植元: src/cpp/services/hotkey/commands/MediaCommands.h / .cpp（HotkeyP 由来、コマンド 0・1・39〜42・100・120）。
/// CD トレイの開閉は winmm.dll の mciSendStringW（MCI）、メディアキー送出は user32.dll の SendInput を使う。
/// </summary>
/// <remarks>
/// ID → メソッドの dispatch は本クラスの責務外（後続タスクの CommandExecutor.cs が担当する）。
/// C++ 版の <c>executeMediaCommand(cmdId, param)</c> のような ID による switch はここには実装しない。
///
/// 注意: ID 62 はカタログ上「実行経路を持たない 23 種」の 1 つ（CommandCatalog.cs の
/// ExcludedIds 参照）。CommandExecutor.cpp の呼び出し元 switch には ID 62 も列挙されているが、
/// MediaCommands.cpp の executeMediaCommand 内の switch には case 62 が存在せず default
/// （常に MediaError::ApiCallFailed）にしか到達しない実質無効な ID のため、本クラスには
/// 対応するメソッドを設けない。
/// </remarks>
public static class MediaCommands
{
    /// <summary>
    /// CD トレイを開く（イジェクト）。ID 0「CD トレイを開く」・ID 100「CD 読み込み速度」
    /// （⚠ internal-command-catalog.md 記載のとおり表示名と実装が食い違うが、現行 C++ 版と同じく
    /// ID 100 も本メソッドを呼ぶ）。移植元: MediaCommands.cpp の ejectCD。
    /// </summary>
    /// <param name="param">対象ドライブ文字（例: "D" や "D:"）。空文字なら既定の CD ドライブを使う。</param>
    /// <returns>成功時は null。MCI 呼び出しが失敗した場合は <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? EjectCD(string param)
    {
        string openCommand = BuildOpenCommand(param);
        NativeMethods.mciSendStringW(openCommand, 0, 0, 0);
        int error = NativeMethods.mciSendStringW("set cdDrive door open", 0, 0, 0);
        NativeMethods.mciSendStringW("close cdDrive", 0, 0, 0);

        return error != 0 ? ExecuteError.ApiCallFailed : null;
    }

    /// <summary>
    /// CD トレイを閉じる。ID 1「CD トレイを閉じる」。移植元: MediaCommands.cpp の closeCD。
    /// </summary>
    /// <param name="param">対象ドライブ文字（例: "D" や "D:"）。空文字なら既定の CD ドライブを使う。</param>
    /// <returns>成功時は null。MCI 呼び出しが失敗した場合は <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? CloseCD(string param)
    {
        string openCommand = BuildOpenCommand(param);
        NativeMethods.mciSendStringW(openCommand, 0, 0, 0);
        int error = NativeMethods.mciSendStringW("set cdDrive door closed", 0, 0, 0);
        NativeMethods.mciSendStringW("close cdDrive", 0, 0, 0);

        return error != 0 ? ExecuteError.ApiCallFailed : null;
    }

    /// <summary>
    /// 指定した仮想キーコードのメディアキーを、SendInput でキーダウン→キーアップの 1 セット送出する。
    /// 移植元: MediaCommands.cpp の sendMediaKey(WORD vk)。<see cref="NativeConstants"/> の
    /// VK_MEDIA_* 定数と組み合わせて使う汎用プリミティブで、下記の個別メソッド（
    /// <see cref="PlayPauseCd"/> 等）はすべてこれの薄いラッパー。
    /// </summary>
    /// <param name="vk">送出する仮想キーコード（例: <see cref="NativeConstants.VK_MEDIA_PLAY_PAUSE"/>）。</param>
    /// <returns>成功時は null。SendInput が失敗した場合は <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? SendMediaKey(int vk)
    {
        ushort virtualKey = (ushort)vk;
        INPUT[] inputs = new INPUT[2];
        inputs[0] = new INPUT
        {
            Type = NativeConstants.INPUT_KEYBOARD,
            U = new InputUnion { Ki = new KEYBDINPUT { Vk = virtualKey } },
        };
        inputs[1] = new INPUT
        {
            Type = NativeConstants.INPUT_KEYBOARD,
            U = new InputUnion { Ki = new KEYBDINPUT { Vk = virtualKey, Flags = NativeConstants.KEYEVENTF_KEYUP } },
        };

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == 0 ? ExecuteError.ApiCallFailed : null;
    }

    /// <summary>
    /// ID 39「メディア: 再生（CD）」。VK_MEDIA_PLAY_PAUSE を送出する
    /// （挙動は ID 120 の <see cref="PlayPause"/> と同一。経緯は <see cref="PlayPause"/> のコメント参照）。
    /// </summary>
    public static ExecuteError? PlayPauseCd() => SendMediaKey(NativeConstants.VK_MEDIA_PLAY_PAUSE);

    /// <summary>ID 40「メディア: 次のトラック」。VK_MEDIA_NEXT_TRACK を送出する。</summary>
    public static ExecuteError? NextTrack() => SendMediaKey(NativeConstants.VK_MEDIA_NEXT_TRACK);

    /// <summary>ID 41「メディア: 停止」。VK_MEDIA_STOP を送出する。</summary>
    public static ExecuteError? Stop() => SendMediaKey(NativeConstants.VK_MEDIA_STOP);

    /// <summary>ID 42「メディア: 前のトラック」。VK_MEDIA_PREV_TRACK を送出する。</summary>
    public static ExecuteError? PrevTrack() => SendMediaKey(NativeConstants.VK_MEDIA_PREV_TRACK);

    /// <summary>
    /// ID 120「メディア: 再生/一時停止」。VK_MEDIA_PLAY_PAUSE を送出する。
    /// 経緯（MediaCommands.cpp の executeMediaCommand 内コメントより）: HotkeyP のオリジナル ID 体系では
    /// ID 39 は CD 専用、ID 120 は汎用メディアプレイヤー向けとして区別されていたが、送出するキーは
    /// どちらも VK_MEDIA_PLAY_PAUSE で実装上は同一。本クラスでもその経緯どおり、
    /// <see cref="PlayPauseCd"/> と挙動が重複したまま両方のメソッドを維持する。
    /// </summary>
    public static ExecuteError? PlayPause() => SendMediaKey(NativeConstants.VK_MEDIA_PLAY_PAUSE);

    // ejectCD/closeCD 共通: MCI の open コマンド文字列を組み立てる。
    // 移植元ロジック（MediaCommands.cpp）: ドライブ文字が指定されていればそれを使い
    // （末尾が ':' でなければ付与する。この判定は driveLetter の最後の 1 文字のみを見る
    // 元実装のままで、"D:\" のような末尾が ':' 以外の入力を厳密に検証はしない）、
    // 空ならエイリアス "cdaudio" を使って既定の CD デバイスを開かせる。
    private static string BuildOpenCommand(string driveLetter)
    {
        string openCommand = "open ";
        if (!string.IsNullOrEmpty(driveLetter))
        {
            openCommand += driveLetter;
            if (openCommand[^1] != ':')
            {
                openCommand += ':';
            }
        }
        else
        {
            openCommand += "cdaudio";
        }

        return openCommand + " type cdaudio alias cdDrive";
    }
}
