#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <optional>
#include <string>
#include <string_view>

namespace imeindicator::win32 {

// 指定 HWND を所有するプロセスの実行ファイル名（拡張子付き、フルパス無し）を返す。
// 既存 C# 版 NativeMethods.GetProcessName と等価。
// 取得失敗時は空 wstring。短期キャッシュ（process id 単位、TTL 1 秒）で連続呼び出しを軽減する。
std::wstring getProcessNameByHwnd(HWND hwnd);

// 同上だが、PID から直接取得する低レベル版。
std::wstring getProcessNameByPid(DWORD pid);

// ウィンドウタイトル
std::wstring getWindowTitle(HWND hwnd);

// ウィンドウクラス名
std::wstring getWindowClassName(HWND hwnd);

// 指定 HWND を所有するスレッドのキーボードレイアウト HKL を返す
HKL getKeyboardLayoutOfHwnd(HWND hwnd);

// HKL の言語 ID（下位 ワード）が日本語(0x0411)かどうか
bool isJapaneseHkl(HKL hkl);

// ===== HotkeyP マージ (010-hotkeyp-merge) で追加されたヘルパー =====
// HotkeyP の findWindow / findProcess / getForegroundProcessName 等を IMEIndicator の
// 名前空間とユーティリティに合わせて移植したもの。HotkeyService::onHotkeyDetected で
// multInst=false のときに既存ウィンドウを前面化するために利用する。

// フォアグラウンドウィンドウの実行ファイル名（フルパスなし、拡張子付き）を返す。
// 取得失敗時は空 wstring。
std::wstring getForegroundExeName();

// 指定 exe（フルパスまたはファイル名）に一致する起動済みウィンドウを 1 つ返す。
// 同名 exe が複数ある場合は最初に見つかったトップレベルウィンドウを返す。
// 見つからなければ nullptr。HotkeyP の findWindow(exe, pid) 相当。
HWND findWindowByExeName(std::wstring_view exeFullPathOrName);

// 指定ウィンドウを前面化する。最小化状態なら復元してから前面化。
// 戻り値: 前面化に成功すれば true。
bool bringWindowToFront(HWND hwnd) noexcept;

} // namespace imeindicator::win32
