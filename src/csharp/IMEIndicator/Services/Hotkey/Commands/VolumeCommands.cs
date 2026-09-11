// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

using IMEIndicator.Interop;
using IMEIndicator.Services.Hotkey;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// 音量制御コマンド（Core Audio の IAudioEndpointVolume のみを使用。WinMM Mixer API は廃止）。
/// 移植元: src/cpp/services/hotkey/commands/VolumeCommands.h / .cpp。
///
/// 対応する内部コマンド ID（CommandExecutor.cpp の「音量コマンド」セクション、
/// specs/014-port-to-csharp/contracts/internal-command-catalog.md「音量（7）」）:
///   13/14 = AdjustVolume(±0.05)、15/16 = 表示名は「Wave 音量 ±」だが実装は 13/14 と全く同じ
///           AdjustVolume（専用の Wave ミキサー API は存在せず、マスター音量の Core Audio API を共用。
///           表示名と実態が乖離している既知のケースであり、現行 C++ 版どおり是正しない）、
///   17 = ToggleMute、58 = 表示名は「Wave ミュート」だが実装は 17 と全く同じ ToggleMute、
///   78 = ExecuteVolumeCommand(param)。
/// ID → メソッドの振り分け（dispatch）自体は行わない（後続タスクの CommandExecutor.cs が担う）。
/// </summary>
public static partial class VolumeCommands
{
    // IAudioEndpointVolume の pguidEventContext（音量変更の発生元を識別する GUID）。
    // VolumeCommands.cpp は常に nullptr を渡す（発生元を区別しない）ため、C# 版でも
    // NULL ポインタ（0）固定とする。Guid.Empty ではなく「ポインタが無い」ことが本来の意味であり、
    // 両者は意味が異なる（後者は「全ゼロの GUID という有効な文脈」を渡したことになってしまう）。
    // nint は const にできないため static readonly を使う（NativeTypes.cs の HWND_BOTTOM 等と同じ理由）。
    private static readonly nint NoEventContext = 0;

    // CLSCTX_ALL（combaseapi.h）= CLSCTX_INPROC_SERVER(0x1) | CLSCTX_INPROC_HANDLER(0x2)
    //  | CLSCTX_LOCAL_SERVER(0x4) | CLSCTX_REMOTE_SERVER(0x10)
    private const uint ClsctxAll = 0x17;

    // COINIT_APARTMENTTHREADED（objbase.h）。
    private const uint CoinitApartmentThreaded = 0x2;

    // ComWrappers の実装。型ごとに使い回してよい（公式チュートリアルの static ComWrappers パターン。
    // https://learn.microsoft.com/dotnet/standard/native-interop/tutorial-comwrappers#com-activation-with-comwrappers ）。
    private static readonly StrategyBasedComWrappers ComWrapperStrategy = new();

    /// <summary>
    /// 音量を相対的に変更する（delta: -1.0〜+1.0、加算後は 0.0〜1.0 にクランプ）。
    /// ID 13/14/15/16 が呼ぶ（delta には ±0.05 を渡す想定）。
    /// 移植元: VolumeCommands.cpp の adjustVolume()。
    /// </summary>
    public static ExecuteError? AdjustVolume(float delta)
    {
        IAudioEndpointVolume? volume = GetEndpointVolume();
        if (volume is null)
        {
            return ExecuteError.ApiCallFailed;
        }

        int hr = volume.GetMasterVolumeLevelScalar(out float current);
        if (hr < 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        float newLevel = Math.Clamp(current + delta, 0.0f, 1.0f);
        hr = volume.SetMasterVolumeLevelScalar(newLevel, NoEventContext);
        return hr < 0 ? ExecuteError.ApiCallFailed : null;
    }

    /// <summary>
    /// ミュート状態を切り替える。ID 17/58 が呼ぶ。
    /// 移植元: VolumeCommands.cpp の toggleMute()。
    /// </summary>
    public static ExecuteError? ToggleMute()
    {
        IAudioEndpointVolume? volume = GetEndpointVolume();
        if (volume is null)
        {
            return ExecuteError.ApiCallFailed;
        }

        int hr = volume.GetMute(out bool muted);
        if (hr < 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        hr = volume.SetMute(!muted, NoEventContext);
        return hr < 0 ? ExecuteError.ApiCallFailed : null;
    }

    /// <summary>
    /// パラメータ文字列から音量コマンドを実行する。ID 78 が呼ぶ。
    /// param 形式: "M"（ミュート切替） / "V+NN"（NN% 上げる） / "V-NN"（NN% 下げる） /
    /// "VNN"（絶対値 NN% に設定、0〜100 目安。クランプは 0.0〜1.0 換算で行う）。
    /// 移植元: VolumeCommands.cpp の executeVolumeCommand()。
    /// </summary>
    public static ExecuteError? ExecuteVolumeCommand(string param)
    {
        if (string.IsNullOrEmpty(param))
        {
            return ExecuteError.ApiCallFailed;
        }

        // ミュート切替。
        if (param == "M")
        {
            return ToggleMute();
        }

        // 音量コマンド（"V" で始まる）。
        if (param.Length < 2 || param[0] != 'V')
        {
            return ExecuteError.ApiCallFailed;
        }

        char sign = param[1];
        if (sign is '+' or '-')
        {
            // 相対変更: "V+5" / "V-3"。
            if (param.Length < 3 || !TryParseLeadingInt(param.AsSpan(2), out int pct))
            {
                return ExecuteError.ApiCallFailed;
            }

            float delta = pct / 100.0f;
            return AdjustVolume(sign == '+' ? delta : -delta);
        }

        // 絶対設定: "V50" → 50%。
        if (!TryParseLeadingInt(param.AsSpan(1), out int absolutePct))
        {
            return ExecuteError.ApiCallFailed;
        }

        float level = Math.Clamp(absolutePct / 100.0f, 0.0f, 1.0f);
        return SetVolume(level);
    }

    /// <summary>
    /// 音量を絶対値で設定する（level: 0.0〜1.0）。executeVolumeCommand の "VNN" 分岐専用。
    /// 移植元: VolumeCommands.cpp の setVolume()。
    /// </summary>
    private static ExecuteError? SetVolume(float level)
    {
        IAudioEndpointVolume? volume = GetEndpointVolume();
        if (volume is null)
        {
            return ExecuteError.ApiCallFailed;
        }

        float clamped = Math.Clamp(level, 0.0f, 1.0f);
        int hr = volume.SetMasterVolumeLevelScalar(clamped, NoEventContext);
        return hr < 0 ? ExecuteError.ApiCallFailed : null;
    }

    /// <summary>
    /// C++ 版の std::stoi と同じ意味論で「先頭の整数」を解析する。符号（+/-）に続く連続した数字を
    /// 読み取り、末尾に数字以外の文字が続いていても無視する（std::stoi も同様に末尾の非数字を無視する）。
    /// 先頭に 1 桁も数字が無い場合、または int の範囲を超える場合のみ失敗を返す
    /// （std::stoi が invalid_argument / out_of_range を投げ、呼び出し元が catch する場合に相当）。
    /// 実機の音量 API を呼ばない純粋関数のため public にして単体テスト対象にする
    /// （TextCommands.ParseMacroToInputs と同じ理由。InternalsVisibleTo は本プロジェクトで未設定）。
    /// </summary>
    public static bool TryParseLeadingInt(ReadOnlySpan<char> s, out int value)
    {
        int i = 0;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            i++;
        }

        int digitsStart = i;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            i++;
        }

        if (i == digitsStart)
        {
            value = 0;
            return false;
        }

        return int.TryParse(s[..i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    // --- 内部: Core Audio へのアクセス ---

    /// <summary>
    /// 既定の再生エンドポイントの IAudioEndpointVolume を取得する。失敗時は null。
    /// 移植元: VolumeCommands.cpp 無名名前空間の getEndpointVolume()
    /// （IMMDeviceEnumerator → IMMDevice → IAudioEndpointVolume の順に辿る）。
    ///
    /// 取得した COM オブジェクトは明示的に解放しない。CreateObjectFlags.None で作成した
    /// ComWrappers ラッパーは GC のファイナライズ時に Release される
    /// （https://learn.microsoft.com/dotnet/standard/native-interop/tutorial-comwrappers
    ///   「COM Activation with ComWrappers」の公式パターンに準拠。IAudioEndpointVolume 等は
    ///   他プロセスの排他資源を握り続けるものではないため、GC 任せの解放で FR 上問題ない）。
    /// <c>Marshal.ReleaseComObject</c> は使わない
    /// （comwrappers-source-generation ドキュメントの「Marshal APIs」節: 組み込み COM 相互運用の
    ///   オブジェクトテーブルを前提とする一部の Marshal API はソース生成 COM と非互換であり、
    ///   ReleaseComObject もその一つ）。
    /// </summary>
    private static IAudioEndpointVolume? GetEndpointVolume()
    {
        try
        {
            bool comInitializedHere = EnsureComInitialized();
            try
            {
                int hr = CoCreateInstance(
                    CoreAudioClsids.MMDeviceEnumerator,
                    0,
                    ClsctxAll,
                    typeof(IMMDeviceEnumerator).GUID,
                    out nint enumeratorPtr);
                if (hr < 0 || enumeratorPtr == 0)
                {
                    return null;
                }

                var enumerator = (IMMDeviceEnumerator)ComWrapperStrategy.GetOrCreateObjectForComInstance(enumeratorPtr, CreateObjectFlags.None);

                hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out nint devicePtr);
                if (hr < 0 || devicePtr == 0)
                {
                    return null;
                }

                var device = (IMMDevice)ComWrapperStrategy.GetOrCreateObjectForComInstance(devicePtr, CreateObjectFlags.None);

                hr = device.Activate(typeof(IAudioEndpointVolume).GUID, ClsctxAll, 0, out nint volumePtr);
                if (hr < 0 || volumePtr == 0)
                {
                    return null;
                }

                return (IAudioEndpointVolume)ComWrapperStrategy.GetOrCreateObjectForComInstance(volumePtr, CreateObjectFlags.None);
            }
            finally
            {
                if (comInitializedHere)
                {
                    CoUninitialize();
                }
            }
        }
        catch (Exception)
        {
            // C++ 版は noexcept で例外を一切外へ出さない設計。ホットキー実行経路
            // （低レベルフックに連なる同期呼び出し）から想定外の例外を伝播させないため、
            // COM 相互運用層で発生し得る例外（不正なキャスト等）もここで確実に失敗扱いへ変換する。
            return null;
        }
    }

    /// <summary>
    /// 呼び出しスレッドの COM を初期化する（未初期化の場合のみ実際に初期化する）。
    /// ホットキー実行は通常 UI スレッド（Program.cs の [STAThread] 上で Application.Run が内部的に
    /// OleInitialize 済み）から呼ばれるため通常は不要だが、単体テスト等 UI スレッド外からの
    /// 呼び出しに備えた防御的初期化。
    /// 戻り値: このメソッドが新規に初期化した場合のみ true（対で CoUninitialize を呼ぶ必要がある）。
    /// CoInitializeEx は S_OK（新規初期化）/ S_FALSE（既に同じアパートメントモデルで初期化済み。
    /// 参照カウントの対称性のため Uninitialize は必要）のいずれも SUCCEEDED であり true を返す。
    /// RPC_E_CHANGED_MODE（別のアパートメントモデルで初期化済み）は FAILED 扱いとなり false を返す
    /// （COM 自体は利用可能だが、この呼び出しでは参照を取得していないため Uninitialize してはならない）。
    /// </summary>
    private static bool EnsureComInitialized()
    {
        int hr = CoInitializeEx(0, CoinitApartmentThreaded);
        return hr >= 0;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();
}
