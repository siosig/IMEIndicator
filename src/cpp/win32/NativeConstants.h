#pragma once

// IME / TSF / 電源モード周りのネイティブ定数を集約する。
// COM GUID は extern 宣言だけ行い、定義は NativeConstants.cpp に置く

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <guiddef.h>

namespace imeindicator::win32 {

// ---- Win32 メッセージ ID（プロセス内・contracts/ipc-contract.md §4） ----
constexpr UINT WM_APP_POWER_TOGGLE         = WM_APP + 0;
constexpr UINT WM_APP_IME_STATE_CHANGED    = WM_APP + 1;
constexpr UINT WM_APP_CURSOR_POSITION      = WM_APP + 2;
constexpr UINT WM_APP_TRAY_NOTIFY          = WM_APP + 3;
constexpr UINT WM_APP_PRIORITY_RULE_RESULT = WM_APP + 4;

// ---- 電源モード（Power Overlay Scheme）GUID ----
// 既存 C# 版 PowerModeService.cs の値と一致させる。
// {961cc777-2547-4f9d-8174-7d86181b8a7a} = 最適な電力効率
extern const GUID GUID_POWER_OVERLAY_BEST_POWER_EFFICIENCY;
// バランスは GUID_NULL（Guid.Empty）
extern const GUID GUID_POWER_OVERLAY_BALANCED;
// {ded574b5-45a0-4f42-8737-46345c09c238} = 最適なパフォーマンス
extern const GUID GUID_POWER_OVERLAY_BEST_PERFORMANCE;

// ---- TSF（Text Services Framework）でよく使う GUID ----
// MSCTF の <msctf.h> ヘッダ依存を避けるため、必要な定数は実装側で明示的に extern 化する。
// 例: GUID_COMPARTMENT_KEYBOARD_OPENCLOSE は <msctf.h> から取得（NativeConstants.cpp で再定義しない）。

// ---- IME 変換モード（IMM32） ----
// IME_CMODE_* / IME_SMODE_* は <imm.h> で定義済みなので追加定義しない。

} // namespace imeindicator::win32

// ---- Power Overlay API は LoadLibrary/GetProcAddress で動的解決する ----
// PowerSetActiveOverlayScheme / PowerGetActualOverlayScheme は powrprof.dll が
// エクスポートしている undocumented API である。Microsoft 公式ドキュメントには
// 記載されておらず（Win32 API リファレンスには PowerSetActiveScheme のみ掲載）、
// Windows SDK の powrprof.lib にもインポートライブラリが含まれないため、静的リンクでは
// 未解決シンボルになる。C# 版が [DllImport("powrprof.dll")] で動的解決しているのと
// 同じ方針を取り、本クラス経由で利用する。
// 参考:
//   - https://stackoverflow.com/questions/61869347/control-windows-10s-power-mode-programmatically
//   - https://strontic.github.io/xcyclopedia/library/powrprof.dll-C0D9CE03397FD7307F5ED742AB845723.html
//     （powrprof.dll の Power*OverlayScheme エクスポート一覧）
namespace imeindicator::win32 {

using PFN_PowerSetActiveOverlayScheme = DWORD(WINAPI*)(GUID);
using PFN_PowerGetActualOverlayScheme = DWORD(WINAPI*)(GUID*);

struct PowerOverlayApi {
    PFN_PowerSetActiveOverlayScheme setActive{nullptr};
    PFN_PowerGetActualOverlayScheme getActual{nullptr};

    // 一度だけ powrprof.dll をロードしてエントリポイントを解決する。
    // 失敗時はメンバが nullptr のままなので、呼び出し側で null チェックする。
    static const PowerOverlayApi& instance() noexcept;

    bool ready() const noexcept { return setActive && getActual; }
};

} // namespace imeindicator::win32

