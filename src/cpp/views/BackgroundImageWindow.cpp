#include "BackgroundImageWindow.h"

#include "../app/AppConstants.h"
#include "../resources/resource_ids.h"
#include "../services/BackgroundImageLayout.h"
#include "../services/DisplayHelper.h"
#include "../services/WicImageLoader.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <wil/resource.h>

#include <spdlog/spdlog.h>

#include <cstdint>
#include <cstring>

// DIB セクション作成・選択に使う GDI 関数群（CreateCompatibleDC/CreateDIBSection/SelectObject）。
// CMakeLists.txt では既に gdi32 をリンクしているが、本ファイルが直接依存する箇所を明示する。
#pragma comment(lib, "gdi32.lib")

#define BIW_LOG_ERROR(...) \
    do { if (auto _log = spdlog::get(std::string(imeindicator::app::AppConstants::LoggerDisplay))) _log->error(__VA_ARGS__); } while(0)
#define BIW_LOG_WARN(...) \
    do { if (auto _log = spdlog::get(std::string(imeindicator::app::AppConstants::LoggerDisplay))) _log->warn(__VA_ARGS__); } while(0)
#define BIW_LOG_DEBUG(...) \
    do { if (auto _log = spdlog::get(std::string(imeindicator::app::AppConstants::LoggerDisplay))) _log->debug(__VA_ARGS__); } while(0)

namespace imeindicator::views {

namespace {

constexpr wchar_t kClassName[] = L"IMEIndicator_BackgroundImageWindow";

} // namespace

BackgroundImageWindow::BackgroundImageWindow() = default;

BackgroundImageWindow::~BackgroundImageWindow()
{
    // レイヤードサーフェスの内容（DIB）はメンバとして保持していない。
    // updateLayeredBitmap() は UpdateLayeredWindow 呼び出しの都度、一時的な
    // HDC/HBITMAP を RAII（wil::unique_hdc/unique_hbitmap）で作成・破棄する。
    // 転送後のピクセル内容は DWM 側のサーフェスが保持する（非表示→表示で
    // 再描画不要なのはこのため。research.md R-9）ので、C++ 側で GDI オブジェクトを
    // 持ち越す必要がなく、DestroyWindow がサーフェスごと解放する。
    if (hwnd_) {
        ::DestroyWindow(hwnd_);
        hwnd_ = nullptr;
    }
    if (windowClass_ && hInstance_) {
        ::UnregisterClassW(kClassName, hInstance_);
        windowClass_ = 0;
    }
}

bool BackgroundImageWindow::initialize(HINSTANCE hInstance)
{
    hInstance_ = hInstance;
    BIW_LOG_DEBUG("BackgroundImageWindow: initialize start");

    // ---- ウィンドウクラス登録 ----
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = &BackgroundImageWindow::wndProcStatic;
    wc.hInstance = hInstance;
    wc.lpszClassName = kClassName;
    wc.hCursor = nullptr;
    windowClass_ = ::RegisterClassExW(&wc);
    if (!windowClass_) {
        BIW_LOG_ERROR("BackgroundImageWindow: RegisterClassExW failed, GetLastError={}", ::GetLastError());
        return false;
    }

    // ---- ウィンドウ作成 ----
    // 契約書 §ウィンドウ属性: WS_POPUP（オーナーなし、WS_VISIBLE なし）。
    // 拡張スタイルは WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE のみ。
    // WS_EX_TOPMOST / WS_EX_NOREDIRECTIONBITMAP / WS_EX_APPWINDOW は含めない。
    //   - WS_EX_LAYERED    : UpdateLayeredWindow によるフルアルファ合成に必須
    //   - WS_EX_TRANSPARENT: レイヤードウィンドウと組み合わせるとヒットテストを完全に素通りさせる
    //                        （クリック透過。デスクトップアイコン等の操作を妨げない）
    //   - WS_EX_TOOLWINDOW : タスクバー・Alt+Tab に出さない
    //   - WS_EX_NOACTIVATE : 表示・クリックでフォアグラウンドを奪わない
    // https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles
    // https://learn.microsoft.com/windows/win32/winmsg/window-features#layered-windows
    constexpr DWORD exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
    constexpr DWORD style = WS_POPUP;

    // 初期サイズは 1x1 のプレースホルダ。直後の relayout() が実サイズで
    // UpdateLayeredWindow（表示中でなければ SetWindowPos）を行う。
    hwnd_ = ::CreateWindowExW(
        exStyle, kClassName, L"", style,
        0, 0, 1, 1,
        nullptr, nullptr, hInstance, this);
    if (!hwnd_) {
        BIW_LOG_ERROR("BackgroundImageWindow: CreateWindowExW failed, GetLastError={}", ::GetLastError());
        return false;
    }

    relayout();
    BIW_LOG_DEBUG("BackgroundImageWindow: initialize OK hwnd={:#x}",
                  reinterpret_cast<uintptr_t>(hwnd_));
    return true;
}

void BackgroundImageWindow::show() noexcept
{
    if (!hwnd_ || shown_) return;
    shown_ = true;
    showAndPinToBottom();
}

void BackgroundImageWindow::hide() noexcept
{
    if (!hwnd_ || !shown_) return;
    shown_ = false;
    BIW_LOG_DEBUG("BackgroundImageWindow: hide()");
    ::ShowWindow(hwnd_, SW_HIDE);
}

void BackgroundImageWindow::showAndPinToBottom() noexcept
{
    // 契約書 §Z 順・入力の保証: ShowWindow(SW_SHOWNOACTIVATE) の後に
    // SetWindowPos(HWND_BOTTOM, ..., SWP_NOACTIVATE)。
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos
    const int width = rect_.right - rect_.left;
    const int height = rect_.bottom - rect_.top;
    BIW_LOG_DEBUG("BackgroundImageWindow: showAndPinToBottom pos=({},{}) size={}x{}",
                  rect_.left, rect_.top, width, height);
    ::ShowWindow(hwnd_, SW_SHOWNOACTIVATE);
    if (!::SetWindowPos(hwnd_, HWND_BOTTOM, rect_.left, rect_.top, width, height, SWP_NOACTIVATE)) {
        BIW_LOG_WARN("BackgroundImageWindow: SetWindowPos(HWND_BOTTOM) failed, GetLastError={}",
                     ::GetLastError());
    }
}

void BackgroundImageWindow::relayout()
{
    // research.md R-5: 対象モニターは常にプライマリ。DisplayHelper::getPrimaryMonitor() は
    // 呼び出しごとに EnumDisplayMonitors を列挙し直すため、再配置トリガー時のみ呼ぶ。
    auto monitor = services::DisplayHelper::getPrimaryMonitor();
    if (!monitor) {
        // 契約書 §エラーハンドリング: モニター取得失敗時は前回の矩形を維持し警告ログ。
        // 次のトリガー（WM_DISPLAYCHANGE 等）で再試行される。
        BIW_LOG_WARN("BackgroundImageWindow: getPrimaryMonitor() failed, keep previous rect");
        return;
    }

    rect_ = services::BackgroundImageLayout::compute(
        monitor->workRect, monitor->dpiX,
        app::AppConstants::BackgroundImageLogicalSize,
        app::AppConstants::BackgroundImageLogicalMargin);

    const int width = rect_.right - rect_.left;
    const int height = rect_.bottom - rect_.top;
    BIW_LOG_DEBUG("BackgroundImageWindow: relayout rect=({},{},{},{}) shown={}",
                  rect_.left, rect_.top, rect_.right, rect_.bottom, shown_);

    if (width != bitmapWidth_ || height != bitmapHeight_) {
        // 契約書 §描画コンテンツ: 再デコードは物理サイズが変わったときのみ。
        // 表示中でなくても UpdateLayeredWindow は呼んでよい（次の show() のために先行準備する）。
        // 失敗時は warn ログ済み。既存のレイヤード内容（あれば）を維持し、bitmapWidth_/Height_ は
        // 更新しないため次回 relayout() で再試行される。
        updateLayeredBitmap(width, height);
    }

    if (shown_) {
        // research.md R-2: HWND_BOTTOM を都度明示し、WM_WINDOWPOSCHANGING の書き換えと二重化する。
        if (!::SetWindowPos(hwnd_, HWND_BOTTOM, rect_.left, rect_.top, width, height, SWP_NOACTIVATE)) {
            BIW_LOG_WARN("BackgroundImageWindow: relayout SetWindowPos(HWND_BOTTOM) failed, GetLastError={}",
                         ::GetLastError());
        }
    } else {
        // 契約書 §幾何: 非表示中は位置・サイズ更新のみ（Z 順・アクティブ化には触れない）。
        ::SetWindowPos(hwnd_, nullptr, rect_.left, rect_.top, width, height,
                       SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);
    }
}

bool BackgroundImageWindow::updateLayeredBitmap(int width, int height)
{
    if (width <= 0 || height <= 0) return false;

    // 1. リソース（RT_RCDATA）から PNG バイト列を取得。
    auto rcData = services::WicImageLoader::lockRcData(hInstance_, IDR_BACKGROUND_IMAGE_PNG);
    if (!rcData) {
        BIW_LOG_WARN("BackgroundImageWindow: lockRcData failed, GetLastError={}", rcData.error());
        return false;
    }

    // 2. WIC で premultiplied BGRA へ変換 + 物理サイズへ拡縮（変換 → 拡縮の順。research.md R-4）。
    auto decoded = services::WicImageLoader::decodePngScaled(*rcData, width, height);
    if (!decoded) {
        BIW_LOG_WARN("BackgroundImageWindow: decodePngScaled failed, hr=0x{:08x}",
                     static_cast<uint32_t>(decoded.error()));
        return false;
    }

    // 3. top-down 32bpp DIB セクションを作成し、premultiplied BGRA をそのまま書き込む。
    //    biHeight を負値にすると上下反転なしの top-down になる。
    //    https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-createdibsection
    BITMAPINFO bmi{};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = width;
    bmi.bmiHeader.biHeight = -height;
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    // メモリ DC と DIB は本関数内だけの一時オブジェクト（wil RAII で確実に解放）。
    // UpdateLayeredWindow はピクセルデータをサーフェスへコピーするため、呼び出し後は
    // これらの GDI オブジェクトを保持し続ける必要がない（クラスコメント参照）。
    wil::unique_hdc hdcMem(::CreateCompatibleDC(nullptr));
    if (!hdcMem) {
        BIW_LOG_WARN("BackgroundImageWindow: CreateCompatibleDC failed, GetLastError={}", ::GetLastError());
        return false;
    }

    void* bits = nullptr;
    wil::unique_hbitmap dib(::CreateDIBSection(hdcMem.get(), &bmi, DIB_RGB_COLORS, &bits, nullptr, 0));
    if (!dib || !bits) {
        BIW_LOG_WARN("BackgroundImageWindow: CreateDIBSection failed, GetLastError={}", ::GetLastError());
        return false;
    }
    std::memcpy(bits, decoded->pbgra.data(), decoded->pbgra.size());

    // SelectObject の戻り値（生成直後の既定 1x1 モノクロビットマップ）を保持しておき、
    // UpdateLayeredWindow の後に選択し戻してから DIB を破棄する
    // （選択中のビットマップを削除しない、という GDI の作法に従う）。
    // https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-createcompatibledc
    HBITMAP oldBitmap = static_cast<HBITMAP>(::SelectObject(hdcMem.get(), dib.get()));

    // 4. UpdateLayeredWindow(ULW_ALPHA) でサーフェスへ 1 回だけ転送。
    //    BLENDFUNCTION{AC_SRC_OVER,0,255,AC_SRC_ALPHA} は premultiplied BGRA を要求する
    //    （WicImageLoader が既に premultiplied 変換済み）。
    //    https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-updatelayeredwindow
    //    https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-blendfunction
    POINT ptDst{rect_.left, rect_.top};
    POINT ptSrc{0, 0};
    SIZE size{width, height};
    BLENDFUNCTION blend{AC_SRC_OVER, 0, 255, AC_SRC_ALPHA};

    const BOOL ok = ::UpdateLayeredWindow(hwnd_, nullptr, &ptDst, &size, hdcMem.get(), &ptSrc,
                                          0, &blend, ULW_ALPHA);
    const DWORD updateErr = ok ? ERROR_SUCCESS : ::GetLastError();

    // 選択解除（破棄前に既定ビットマップへ戻す）。dib / hdcMem は関数を抜けるときに
    // wil RAII が DeleteObject / DeleteDC する。
    ::SelectObject(hdcMem.get(), oldBitmap);

    if (!ok) {
        BIW_LOG_WARN("BackgroundImageWindow: UpdateLayeredWindow failed, GetLastError={}", updateErr);
        return false;
    }

    bitmapWidth_ = width;
    bitmapHeight_ = height;
    BIW_LOG_DEBUG("BackgroundImageWindow: updateLayeredBitmap OK {}x{}", width, height);
    return true;
}

LRESULT CALLBACK BackgroundImageWindow::wndProcStatic(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_NCCREATE) {
        auto* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        auto* self = static_cast<BackgroundImageWindow*>(cs->lpCreateParams);
        ::SetWindowLongPtrW(hwnd, GWLP_USERDATA,
                            reinterpret_cast<LONG_PTR>(self));
        // CreateWindowExW が戻る前（initialize() の代入前）にも WM_CREATE / WM_SIZE 等が届く。
        // ここで hwnd_ を確定させておかないと、handleMessage の既定処理が
        // DefWindowProcW(nullptr, ...) を呼ぶことになるため、この時点で設定する。
        if (self) self->hwnd_ = hwnd;
        return ::DefWindowProcW(hwnd, msg, wp, lp);
    }
    auto* self = reinterpret_cast<BackgroundImageWindow*>(
        ::GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (self) return self->handleMessage(msg, wp, lp);
    return ::DefWindowProcW(hwnd, msg, wp, lp);
}

LRESULT BackgroundImageWindow::handleMessage(UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
        case WM_WINDOWPOSCHANGING: {
            // research.md R-2: 明示的な SWP_NOZORDER を伴わない Z 順変更要求はすべて
            // 最背面へ書き換える（WINDOWPOS を直接変更するのは公式に認められた挙動）。
            // https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-windowpos
            // https://learn.microsoft.com/windows/win32/winmsg/wm-windowposchanging
            auto* windowPos = reinterpret_cast<WINDOWPOS*>(lp);
            if (windowPos && !(windowPos->flags & SWP_NOZORDER)) {
                windowPos->hwndInsertAfter = HWND_BOTTOM;
            }
            return 0;
        }
        case WM_DISPLAYCHANGE:
            // 解像度・プライマリモニター構成の変更。
            relayout();
            return 0;
        case WM_DPICHANGED:
            // PerMonitorV2 の DPI 変更。lp が指す提案矩形は使わず、DisplayHelper 経由で
            // 自前計算する（research.md R-5）。
            // https://learn.microsoft.com/windows/win32/hidpi/wm-dpichanged
            relayout();
            return 0;
        case WM_SETTINGCHANGE:
            // タスクバーの位置・サイズ変更（作業領域の変化）のみを対象にする。
            if (wp == SPI_SETWORKAREA) {
                relayout();
            }
            return 0;
        case WM_SIZE:
            // Win+D 等でシェルが最小化してきた場合、表示すべき状態なら自己復帰する（research.md R-2）。
            // ここで直接 ShowWindow/SetWindowPos せず PostMessage するのは、WM_SIZE 処理中の
            // 再入（ウィンドウ状態を変更しながら同じメッセージを処理し続けること）を避けるため。
            if (wp == SIZE_MINIMIZED && shown_) {
                ::PostMessageW(hwnd_, kMsgRestoreBottom, 0, 0);
            }
            return 0;
        case kMsgRestoreBottom:
            showAndPinToBottom();
            return 0;
        case WM_MOUSEACTIVATE:
            // WS_EX_TRANSPARENT + WS_EX_NOACTIVATE で通常は発生しないが、保険として明示する。
            return MA_NOACTIVATE;
        case WM_DESTROY:
            // このウィンドウの破棄はアプリ終了を意味しないため PostQuitMessage は呼ばない。
            return 0;
    }
    return ::DefWindowProcW(hwnd_, msg, wp, lp);
}

} // namespace imeindicator::views
