#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <optional>
#include <string>

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

} // namespace imeindicator::win32
