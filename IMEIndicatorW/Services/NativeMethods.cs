using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace IMEIndicatorClock.Services;

// TSF (Text Services Framework) COM インターフェース
[ComImport, Guid("aa80e801-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfThreadMgr
{
    void Activate(out uint clientId);
    void Deactivate();
    void CreateDocumentMgr(out IntPtr docMgr);
    void EnumDocumentMgrs(out IntPtr enumDocMgrs);
    void GetFocus(out IntPtr docMgr);
    void SetFocus(IntPtr docMgr);
    void AssociateFocus(IntPtr hwnd, IntPtr newDocMgr, out IntPtr prevDocMgr);
    void IsThreadFocus([MarshalAs(UnmanagedType.Bool)] out bool isFocus);
    void GetFunctionProvider(ref Guid clsid, out IntPtr funcProv);
    void EnumFunctionProviders(out IntPtr enumProviders);
    void GetGlobalCompartment(out ITfCompartmentMgr compartmentMgr);
}

[ComImport, Guid("7dcf57ac-18ad-438b-824d-979bffb74b7c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfCompartmentMgr
{
    void GetCompartment(ref Guid guid, out ITfCompartment compartment);
    void ClearCompartment(uint tid, ref Guid guid);
    void EnumCompartments(out IntPtr enumGuid);
}

[ComImport, Guid("bb08f7a9-607a-4384-8623-056892b64371"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfCompartment
{
    void SetValue(uint tid, ref object varValue);
    void GetValue(out object varValue);
}

// TSF ITfInputProcessorProfiles インターフェース
[ComImport, Guid("1f02b6c5-7842-4ee6-8a0b-9a24183a95ca"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfInputProcessorProfiles
{
    void Register(ref Guid rclsid);
    void Unregister(ref Guid rclsid);
    void AddLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile,
        [MarshalAs(UnmanagedType.LPWStr)] string pchDesc, uint cchDesc,
        [MarshalAs(UnmanagedType.LPWStr)] string pchIconFile, uint cchFile,
        uint uIconIndex);
    void RemoveLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile);
    void EnumInputProcessorInfo(out IntPtr ppEnum);
    void GetDefaultLanguageProfile(ushort langid, ref Guid catid, out Guid pclsid, out Guid pguidProfile);
    void SetDefaultLanguageProfile(ushort langid, ref Guid rclsid, ref Guid guidProfiles);
    void ActivateLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfiles);
    void GetActiveLanguageProfile(ref Guid rclsid, out ushort plangid, out Guid pguidProfile);
    void GetLanguageProfileDescription(ref Guid rclsid, ushort langid, ref Guid guidProfile, out IntPtr pbstrProfile);
    void GetCurrentLanguage(out ushort plangid);
    void ChangeCurrentLanguage(ushort langid);
    void GetLanguageList(out IntPtr ppLangId, out uint pulCount);
    void EnumLanguageProfiles(ushort langid, out IntPtr ppEnum);
    void EnableLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile, [MarshalAs(UnmanagedType.Bool)] bool fEnable);
    void IsEnabledLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile, [MarshalAs(UnmanagedType.Bool)] out bool pfEnable);
    void EnableLanguageProfileByDefault(ref Guid rclsid, ushort langid, ref Guid guidProfile, [MarshalAs(UnmanagedType.Bool)] bool fEnable);
    void SubstituteKeyboardLayout(ref Guid rclsid, ushort langid, ref Guid guidProfile, IntPtr hKL);
}

// TSF GUIDs
internal static class TsfGuids
{
    // GUID_COMPARTMENT_KEYBOARD_INPUTMODE_CONVERSION
    public static readonly Guid GUID_COMPARTMENT_KEYBOARD_INPUTMODE_CONVERSION =
        new Guid("ccf05dd8-4a87-11d7-a6e2-00065b84435c");

    // GUID_COMPARTMENT_KEYBOARD_OPENCLOSE
    public static readonly Guid GUID_COMPARTMENT_KEYBOARD_OPENCLOSE =
        new Guid("58273aad-01bb-4164-95c6-755ba0b5162d");

    // GUID_COMPARTMENT_KEYBOARD_INPUTMODE_SENTENCE (韓国語IME用)
    public static readonly Guid GUID_COMPARTMENT_KEYBOARD_INPUTMODE_SENTENCE =
        new Guid("ccf05dd9-4a87-11d7-a6e2-00065b84435c");

    // CLSID_TF_ThreadMgr
    public static readonly Guid CLSID_TF_ThreadMgr =
        new Guid("529a9e6b-6587-4f23-ab9e-9c7d683e3c50");

    // CLSID_TF_InputProcessorProfiles
    public static readonly Guid CLSID_TF_InputProcessorProfiles =
        new Guid("33c53a50-f456-4884-b049-85fd643ecfed");

    // GUID_TFCAT_TIP_KEYBOARD
    public static readonly Guid GUID_TFCAT_TIP_KEYBOARD =
        new Guid("34745c63-b2f0-4784-8b67-5e12c8701a31");
}

/// <summary>
/// Windows API P/Invoke定義
/// </summary>
internal static partial class NativeMethods
{
    // User32.dll
    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetWindowTextInternal(IntPtr hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetClassNameInternal(IntPtr hWnd, char* lpClassName, int nMaxCount);

    public static unsafe int GetWindowText(IntPtr hWnd, Span<char> buffer)
    {
        fixed (char* ptr = buffer)
        {
            return GetWindowTextInternal(hWnd, ptr, buffer.Length);
        }
    }

    public static unsafe int GetClassName(IntPtr hWnd, Span<char> buffer)
    {
        fixed (char* ptr = buffer)
        {
            return GetClassNameInternal(hWnd, ptr, buffer.Length);
        }
    }

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetFocus();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetKeyboardState([In, Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 256)] byte[] lpKeyState);

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int nVirtKey);

    // Kernel32.dll
    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    // Ole32.dll
    // Note: COM相互運用のため DllImport を維持
    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetWindow(IntPtr hWnd, int uCmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // EnumWindows用デリゲートとAPI
    // Note: デリゲートコールバックを使用するため DllImport を維持
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    // GetWindow フラグ
    public const int GW_OWNER = 4;

    // Imm32.dll
    [LibraryImport("imm32.dll")]
    public static partial IntPtr ImmGetContext(IntPtr hWnd);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmGetOpenStatus(IntPtr hIMC);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmGetConversionStatus(IntPtr hIMC, out uint lpfdwConversion, out uint lpfdwSentence);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [LibraryImport("imm32.dll")]
    public static partial IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [LibraryImport("imm32.dll", EntryPoint = "ImmGetCompositionStringW")]
    public static partial int ImmGetCompositionString(IntPtr hIMC, uint dwIndex, IntPtr lpBuf, int dwBufLen);

    // ImmGetCompositionString のインデックス
    public const uint GCS_COMPSTR = 0x0008;

    // 構造体
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    // 言語ID定数
    public const int LANG_JAPANESE = 0x0411;
    public const int LANG_KOREAN = 0x0412;
    public const int LANG_CHINESE_SIMPLIFIED = 0x0804;
    public const int LANG_CHINESE_TRADITIONAL = 0x0404;
    public const int LANG_CHINESE_TRADITIONAL_HK = 0x0C04;
    public const int LANG_ENGLISH_US = 0x0409;
    public const int LANG_ENGLISH_UK = 0x0809;
    public const int LANG_THAI = 0x041E;
    public const int LANG_VIETNAMESE = 0x042A;
    public const int LANG_ARABIC = 0x0401;
    public const int LANG_ARABIC_EGYPT = 0x0C01;
    public const int LANG_ARABIC_UAE = 0x3801;
    public const int LANG_HEBREW = 0x040D;
    public const int LANG_HINDI = 0x0439;
    public const int LANG_BENGALI_IN = 0x0445;
    public const int LANG_BENGALI_BD = 0x0845;
    public const int LANG_TAMIL = 0x0449;
    public const int LANG_TELUGU = 0x044A;
    public const int LANG_NEPALI = 0x0461;
    public const int LANG_SINHALA = 0x045B;
    public const int LANG_MYANMAR = 0x0455;
    public const int LANG_KHMER = 0x0453;
    public const int LANG_LAO = 0x0454;
    public const int LANG_MONGOLIAN = 0x0450;
    public const int LANG_MONGOLIAN_CN = 0x0850;
    public const int LANG_PERSIAN = 0x0429;
    public const int LANG_UKRAINIAN = 0x0422;
    public const int LANG_RUSSIAN = 0x0419;
    public const int LANG_GREEK = 0x0408;

    // 言語IDプライマリコード（サブ言語に関係なく判定用）
    public const int LANG_PRIMARY_KOREAN = 0x12;
    public const int LANG_PRIMARY_ARABIC = 0x01;
    public const int LANG_PRIMARY_BENGALI = 0x45;
    public const int LANG_PRIMARY_MONGOLIAN = 0x50;

    // IME変換モード
    public const uint IME_CMODE_ALPHANUMERIC = 0x0000;
    public const uint IME_CMODE_NATIVE = 0x0001;
    public const uint IME_CMODE_KATAKANA = 0x0002;
    public const uint IME_CMODE_FULLSHAPE = 0x0008;
    public const uint IME_CMODE_ROMAN = 0x0010;

    // WM_IME_CONTROL メッセージ
    public const int WM_IME_CONTROL = 0x0283;
    public const int IMC_GETOPENSTATUS = 0x0005;
    public const int IMC_GETCONVERSIONMODE = 0x0001;

    // SendMessageTimeout フラグ
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    public static partial IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    // ヘルパーメソッド
    public static string GetWindowTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "(null)";
        Span<char> buffer = stackalloc char[256];
        int len = GetWindowText(hWnd, buffer);
        var title = len > 0 ? new string(buffer[..len]) : "";
        return string.IsNullOrEmpty(title) ? "(無題)" : title;
    }

    public static string GetWindowClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "(null)";
        Span<char> buffer = stackalloc char[256];
        int len = GetClassName(hWnd, buffer);
        return len > 0 ? new string(buffer[..len]) : "";
    }

    public static string GetProcessName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "(null)";
        _ = GetWindowThreadProcessId(hWnd, out uint processId);
        try
        {
            var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return $"PID:{processId}";
        }
    }
}
