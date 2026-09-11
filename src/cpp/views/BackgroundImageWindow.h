#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::views {

// 画面右上 IME ON 背景画像ウィンドウ（013-ime-corner-image）。
// 契約: specs/013-ime-corner-image/contracts/background-image-window-contract.md
//
// 概要:
//   IME ON 時にプライマリモニター作業領域の右上へ、同梱 PNG（IDR_BACKGROUND_IMAGE_PNG）を
//   最背面・クリック透過で表示する専用ウィンドウ。表示する/しないの判断は持たず、
//   App::applyBackgroundImageVisibility が show()/hide() を呼んで制御する。
//
// 描画方式（research.md R-1）:
//   WS_EX_LAYERED の WS_POPUP ウィンドウへ、premultiplied 32bpp BGRA top-down DIB を
//   UpdateLayeredWindow(ULW_ALPHA) で 1 回転送するだけ。転送後は DWM 側がサーフェス内容を
//   保持するため、以降のタイマー・毎フレーム描画は一切不要（アイドル負荷ゼロ、research.md R-9）。
//   静止画 1 枚のため、既存 MouseCursorIndicatorWindow の Direct2D + DirectComposition +
//   スワップチェーン構成は過剰と判断し採用しない。
//
// 最背面固定（research.md R-2）:
//   show()/relayout() の都度 SetWindowPos(HWND_BOTTOM) を明示するのに加え、
//   WM_WINDOWPOSCHANGING で任意の Z 順変更要求を HWND_BOTTOM に書き換えて二重に保証する。
//   WS_EX_TOPMOST は使わない（最前面と最背面は無関係の概念で、TOPMOST は目的に反する）。
//
// COM 前提:
//   relayout() が呼ぶ WicImageLoader::decodePngScaled は WIC (COM) を使うため、
//   呼び出しスレッド（UI スレッド）で CoInitializeEx 済みであること。
//   main.cpp の win32::ComApartment（STA、wWinMain 冒頭）が初期化済みであることを前提とする
//   （PixelIMEDetector / WicImageLoader.h ヘッダコメントと同じ前提）。
class BackgroundImageWindow {
public:
    BackgroundImageWindow();
    ~BackgroundImageWindow();

    BackgroundImageWindow(const BackgroundImageWindow&) = delete;
    BackgroundImageWindow& operator=(const BackgroundImageWindow&) = delete;

    // ウィンドウクラス登録 + 非表示ウィンドウ作成 + 初回 relayout()。
    // 失敗時 false（クラス登録・ウィンドウ作成失敗のみ。呼び出し側は背景画像表示なしで続行する）。
    bool initialize(HINSTANCE hInstance);

    // 非アクティブで表示し最背面へ配置する。既に表示中なら何もしない。
    void show() noexcept;

    // SW_HIDE で隠す。既に非表示なら何もしない。
    void hide() noexcept;

    bool isShown() const noexcept { return shown_; }

    // プライマリモニター/DPI を再取得して表示矩形を再計算する。
    // 物理サイズが変わったときのみ再デコード + UpdateLayeredWindow、変わらなければ
    // 位置のみ反映する（非表示中は位置・サイズ更新のみで Z 順・アクティブ化には触れない）。
    void relayout();

    HWND hwnd() const noexcept { return hwnd_; }

private:
    static LRESULT CALLBACK wndProcStatic(HWND, UINT, WPARAM, LPARAM);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);

    // show() と自己復帰（kMsgRestoreBottom）の共通処理:
    // ShowWindow(SW_SHOWNOACTIVATE) + SetWindowPos(HWND_BOTTOM, rect_, SWP_NOACTIVATE)。
    void showAndPinToBottom() noexcept;

    // WIC デコード → top-down 32bpp DIB 作成 → UpdateLayeredWindow(ULW_ALPHA)。
    // 成功時のみ bitmapWidth_/bitmapHeight_ を更新する（失敗時は次回 relayout() で再試行させるため）。
    bool updateLayeredBitmap(int width, int height);

    // Win+D 等でシェルに最小化された（WM_SIZE/SIZE_MINIMIZED）ときの自己復帰用メッセージ。
    // research.md R-2。WM_APP 系はアプリ内でのみ意味を持つプライベートメッセージ。
    static constexpr UINT kMsgRestoreBottom = WM_APP + 1;

    // === 状態 ===
    HINSTANCE hInstance_{nullptr};
    HWND hwnd_{nullptr};
    ATOM windowClass_{0};
    bool shown_{false};

    RECT rect_{};          // 直近の BackgroundImageLayout::compute() 結果（物理 px、仮想スクリーン座標）
    int bitmapWidth_{0};   // 直近に UpdateLayeredWindow へ反映できた DIB の幅（物理 px）。0 は未反映
    int bitmapHeight_{0};  // 同・高さ
};

} // namespace imeindicator::views
