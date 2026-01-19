using System.Runtime.InteropServices;

namespace IMEIndicatorClock.Services;

/// <summary>
/// 韓国語IME検出ロジック
/// </summary>
/// <remarks>
/// 韓国語IME 한글/영문 状態検出の問題点 (2026-01-17 テスト結果)
///
/// 【問題1: APIが信頼できない】
/// - ImmGetConversionStatus, WM_IME_CONTROL 等のAPIは他プロセスで不正確
/// - 多くのアプリで常にTrue(한글)を返す（実際はOFFでも）
///
/// 【問題2: ウィンドウごとの状態保存/復元も信頼できない】
/// - 各アプリはウィンドウ切り替え時にIME状態をリセットすることがある
///
/// 【結論】
/// ピクセル判定 + キーボードフック のハイブリッド方式で対応
/// </remarks>
public static class IMEDetector_Korean
{
    private static uint _lastKoreanConvMode = 0;

    /// <summary>
    /// 韓国語IMEの変換モード（한글/영문）を取得
    /// </summary>
    public static (bool isHangul, bool success) GetKoreanIMEModeEx(IntPtr hwndForeground)
    {
        // 方法1: ImmGetConversionStatus を優先
        IntPtr hIMC = NativeMethods.ImmGetContext(hwndForeground);
        if (hIMC != IntPtr.Zero)
        {
            if (NativeMethods.ImmGetConversionStatus(hIMC, out uint convMode, out _))
            {
                NativeMethods.ImmReleaseContext(hwndForeground, hIMC);
                if (convMode > 0 && convMode <= 0x0003)
                {
                    bool isHangul = (convMode & NativeMethods.IME_CMODE_NATIVE) != 0;
                    if (_lastKoreanConvMode != convMode)
                    {
                        DbgLog.Log(5, $"韓国語IME変換モード(IMC): convMode=0x{convMode:X4}, 한글={isHangul}");
                        _lastKoreanConvMode = convMode;
                    }
                    return (isHangul, true);
                }
                DbgLog.Log(6, $"韓国語IME変換モード(IMC): convMode=0x{convMode:X4} (異常値または0)");
            }
            else
            {
                NativeMethods.ImmReleaseContext(hwndForeground, hIMC);
            }
        }

        // 方法2: WM_IME_CONTROL + IMC_GETCONVERSIONMODE
        IntPtr imeWnd = NativeMethods.ImmGetDefaultIMEWnd(hwndForeground);
        if (imeWnd != IntPtr.Zero)
        {
            IntPtr result = NativeMethods.SendMessageTimeout(
                imeWnd,
                (uint)NativeMethods.WM_IME_CONTROL,
                (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE,
                IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG,
                100,
                out IntPtr convMode);

            if (result != IntPtr.Zero)
            {
                uint convModeValue = (uint)convMode;
                if (convModeValue > 0 && convModeValue <= 0x0003)
                {
                    bool isHangul = (convModeValue & NativeMethods.IME_CMODE_NATIVE) != 0;
                    if (_lastKoreanConvMode != convModeValue)
                    {
                        DbgLog.Log(5, $"韓国語IME変換モード(WM): convMode=0x{convModeValue:X4}, 한글={isHangul}");
                        _lastKoreanConvMode = convModeValue;
                    }
                    return (isHangul, true);
                }
                DbgLog.Log(6, $"韓国語IME変換モード(WM): convMode=0x{convModeValue:X4} (異常値)");
            }
        }

        // 方法3: AttachThreadInput
        var attachResult = GetKoreanIMEModeWithAttach(hwndForeground);
        if (attachResult.success)
        {
            return attachResult;
        }

        // 方法4: TSF
        var tsfResult = GetKoreanIMEModeViaTSF();
        if (tsfResult.success)
        {
            return tsfResult;
        }

        // 方法5: コンポジション文字列チェック
        var compResult = CheckKoreanComposition(hwndForeground);
        if (compResult.success)
        {
            return compResult;
        }

        DbgLog.Log(6, "韓国語IME変換モード取得失敗（全方法）");
        return (false, false);
    }

    /// <summary>
    /// AttachThreadInput を使用して韓国語IME変換モードを取得
    /// </summary>
    private static (bool isHangul, bool success) GetKoreanIMEModeWithAttach(IntPtr hwndForeground)
    {
        const int VK_HANGUL = 0x15;
        uint currentThreadId = NativeMethods.GetCurrentThreadId();
        uint targetThreadId = NativeMethods.GetWindowThreadProcessId(hwndForeground, out _);

        if (currentThreadId == targetThreadId)
        {
            return (false, false);
        }

        bool attached = false;
        try
        {
            attached = NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, true);
            if (!attached)
            {
                DbgLog.Log(6, "AttachThreadInput 失敗");
                return (false, false);
            }

            IntPtr hwndFocus = NativeMethods.GetFocus();
            IntPtr targetHwnd = hwndFocus != IntPtr.Zero ? hwndFocus : hwndForeground;

            IntPtr hIMC = NativeMethods.ImmGetContext(targetHwnd);
            if (hIMC != IntPtr.Zero)
            {
                if (NativeMethods.ImmGetConversionStatus(hIMC, out uint convMode, out _))
                {
                    NativeMethods.ImmReleaseContext(targetHwnd, hIMC);
                    if (convMode != 0)
                    {
                        bool isHangul = (convMode & NativeMethods.IME_CMODE_NATIVE) != 0;
                        DbgLog.Log(5, $"韓国語IME変換モード(Attach): convMode=0x{convMode:X4}, 한글={isHangul}");
                        return (isHangul, true);
                    }
                    DbgLog.Log(6, $"韓国語IME変換モード(Attach): convMode=0x0000 (不明)");
                }
                else
                {
                    NativeMethods.ImmReleaseContext(targetHwnd, hIMC);
                }
            }

            byte[] keyState = new byte[256];
            if (NativeMethods.GetKeyboardState(keyState))
            {
                bool isHangulToggled = (keyState[VK_HANGUL] & 0x01) != 0;
                DbgLog.Log(6, $"韓国語IMEキーボード状態(Attach): VK_HANGUL=0x{keyState[VK_HANGUL]:X2}");
                if (keyState[VK_HANGUL] != 0)
                {
                    return (isHangulToggled, true);
                }
            }
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }

        return (false, false);
    }

    /// <summary>
    /// TSF を使用して韓国語IME変換モードを取得
    /// </summary>
    private static (bool isHangul, bool success) GetKoreanIMEModeViaTSF()
    {
        ITfThreadMgr? threadMgr = null;
        ITfInputProcessorProfiles? profiles = null;
        try
        {
            var clsid = TsfGuids.CLSID_TF_ThreadMgr;
            var iid = typeof(ITfThreadMgr).GUID;

            int hr = NativeMethods.CoCreateInstance(
                ref clsid,
                IntPtr.Zero,
                1,
                ref iid,
                out var obj);

            if (hr != 0 || obj == null)
            {
                DbgLog.Log(6, $"TSF: CoCreateInstance(ThreadMgr) 失敗 (hr=0x{hr:X8})");
            }
            else
            {
                threadMgr = (ITfThreadMgr)obj;

                threadMgr.GetGlobalCompartment(out var compartmentMgr);
                if (compartmentMgr != null)
                {
                    var guidsToTry = new[]
                    {
                        (TsfGuids.GUID_COMPARTMENT_KEYBOARD_INPUTMODE_CONVERSION, "CONVERSION"),
                        (TsfGuids.GUID_COMPARTMENT_KEYBOARD_INPUTMODE_SENTENCE, "SENTENCE"),
                        (TsfGuids.GUID_COMPARTMENT_KEYBOARD_OPENCLOSE, "OPENCLOSE")
                    };

                    foreach (var (guid, name) in guidsToTry)
                    {
                        var result = TryGetConversionModeFromCompartmentGuid(compartmentMgr, guid, name);
                        if (result.success)
                        {
                            return result;
                        }
                    }
                }
            }

            var profilesClsid = TsfGuids.CLSID_TF_InputProcessorProfiles;
            var profilesIid = typeof(ITfInputProcessorProfiles).GUID;

            hr = NativeMethods.CoCreateInstance(
                ref profilesClsid,
                IntPtr.Zero,
                1,
                ref profilesIid,
                out obj);

            if (hr == 0 && obj != null)
            {
                profiles = (ITfInputProcessorProfiles)obj;
                profiles.GetCurrentLanguage(out ushort langid);
                DbgLog.Log(6, $"TSF: 現在の言語=0x{langid:X4}");

                if ((langid & 0xFF) == NativeMethods.LANG_PRIMARY_KOREAN)
                {
                    var keyboardGuid = TsfGuids.GUID_TFCAT_TIP_KEYBOARD;
                    try
                    {
                        profiles.GetActiveLanguageProfile(ref keyboardGuid, out ushort activeLangId, out Guid profileGuid);
                        DbgLog.Log(6, $"TSF: アクティブプロファイル langid=0x{activeLangId:X4}");
                    }
                    catch
                    {
                        DbgLog.Log(6, "TSF: GetActiveLanguageProfile 失敗");
                    }
                }
            }

            DbgLog.Log(6, "TSF: 変換モード取得失敗");
        }
        catch (Exception ex)
        {
            DbgLog.Log(6, $"TSF: 例外 - {ex.Message}");
        }
        finally
        {
            if (threadMgr != null) Marshal.ReleaseComObject(threadMgr);
            if (profiles != null) Marshal.ReleaseComObject(profiles);
        }

        return (false, false);
    }

    private static (bool isHangul, bool success) TryGetConversionModeFromCompartmentGuid(
        ITfCompartmentMgr compartmentMgr, Guid guid, string name)
    {
        try
        {
            var guidCopy = guid;
            compartmentMgr.GetCompartment(ref guidCopy, out var compartment);
            if (compartment == null)
            {
                DbgLog.Log(6, $"TSF({name}): GetCompartment 失敗");
                return (false, false);
            }

            compartment.GetValue(out var value);
            if (value is int intValue && intValue != 0)
            {
                bool isHangul = (intValue & (int)NativeMethods.IME_CMODE_NATIVE) != 0;
                DbgLog.Log(5, $"TSF({name}): value=0x{intValue:X4}, 한글={isHangul}");
                return (isHangul, true);
            }
            DbgLog.Log(6, $"TSF({name}): value={value ?? "null"} (無効)");
        }
        catch (Exception ex)
        {
            DbgLog.Log(6, $"TSF({name}): 例外 - {ex.Message}");
        }
        return (false, false);
    }

    /// <summary>
    /// コンポジション文字列をチェック
    /// </summary>
    private static (bool isHangul, bool success) CheckKoreanComposition(IntPtr hwndForeground)
    {
        IntPtr hIMC = NativeMethods.ImmGetContext(hwndForeground);
        if (hIMC == IntPtr.Zero)
        {
            return (false, false);
        }

        try
        {
            int compLen = NativeMethods.ImmGetCompositionString(hIMC, NativeMethods.GCS_COMPSTR, IntPtr.Zero, 0);
            if (compLen > 0)
            {
                DbgLog.Log(5, $"韓国語IME: コンポジション文字列あり (長さ={compLen})");
                return (true, true);
            }
            DbgLog.Log(6, "韓国語IME: コンポジション文字列なし（状態不明）");
        }
        finally
        {
            NativeMethods.ImmReleaseContext(hwndForeground, hIMC);
        }

        return (false, false);
    }
}
