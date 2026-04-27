#pragma once

#include "../models/MouseCursorIndicatorSettings.h"

#include <string>
#include <wrl/client.h>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <d2d1_1.h>
#include <d3d11.h>
#include <dcomp.h>
#include <dwrite.h>
#include <dxgi1_2.h>

namespace imeindicator::views {

// マウスカーソル追従インジケーターウィンドウ。
// 既存 [MouseCursorIndicatorWindow.xaml] の見た目を Direct2D + DirectComposition +
// DirectWrite で再現する。
//
// レイヤード合成の仕組み:
//   - WS_EX_NOREDIRECTIONBITMAP（GDI のリダイレクションバッファを無効化）
//   - IDXGISwapChain1 + IDCompositionVisual で DWM のフルアルファ合成を利用
//   - ID2D1HwndRenderTarget は WS_EX_LAYERED と相性が悪いため非採用
//
// XAML の描画要素マッピング:
//   1) 外側楕円 RadialGradient（中心=背景色, 周辺=透明）
//   2) 内側楕円 LinearGradient（左上=背景色, 右下=グロー色, マージン 3px）
//   3) 白いボーダー楕円（線幅 1px, 不透明度 0.3, マージン 3px）
//   4) テキスト影（黒, 不透明度 0.25, オフセット +0.5px,+0.5px）
//   5) テキスト本体（白）
class MouseCursorIndicatorWindow {
public:
    MouseCursorIndicatorWindow();
    ~MouseCursorIndicatorWindow();

    MouseCursorIndicatorWindow(const MouseCursorIndicatorWindow&) = delete;
    MouseCursorIndicatorWindow& operator=(const MouseCursorIndicatorWindow&) = delete;

    // ウィンドウクラス登録 + ウィンドウ作成 + Direct2D/DComposition リソース初期化。
    // 失敗時 false。
    bool initialize(HINSTANCE hInstance);

    // インジケーターサイズ・不透明度・オフセット等を反映。
    void updateSettings(const models::MouseCursorIndicatorSettings& s);

    // 表示文字列（"あ" / "A" など、1〜8 文字）。
    void updateText(std::wstring text);

    // インジケーター色（XAML BackgroundColor 相当）。GlowColor は内部で派生計算。
    void updateColor(D2D1_COLOR_F color);

    // マウス位置に追従して移動。x,y はカーソル座標（仮想スクリーン座標）。
    void updatePosition(int x, int y);

    void show() noexcept;
    void hide() noexcept;

    HWND hwnd() const noexcept { return hwnd_; }

private:
    static LRESULT CALLBACK wndProcStatic(HWND, UINT, WPARAM, LPARAM);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);

    bool createDeviceIndependentResources();
    bool createDeviceResources();
    void releaseDeviceResources();
    bool ensureSwapChainSize(int physicalSize);

    void render();
    void onDpiChanged(UINT newDpi);

    // 物理サイズ計算（論理サイズ × DPI スケール）。
    int physicalSize() const noexcept;

    // === 状態 ===
    HINSTANCE hInstance_{nullptr};
    HWND hwnd_{nullptr};
    ATOM windowClass_{0};
    UINT currentDpi_{96};

    models::MouseCursorIndicatorSettings settings_{};
    std::wstring displayText_{L"あ"};
    D2D1_COLOR_F backgroundColor_{0.231f, 0.510f, 0.965f, 1.0f}; // #3B82F6
    D2D1_COLOR_F glowColor_{0.231f, 0.510f, 0.965f, 1.0f};

    int currentSwapChainSize_{0};
    bool deviceResourcesValid_{false};

    // === Direct2D / DXGI / DComposition / DirectWrite ===
    Microsoft::WRL::ComPtr<ID2D1Factory1> d2dFactory_;
    Microsoft::WRL::ComPtr<IDWriteFactory> dwriteFactory_;
    Microsoft::WRL::ComPtr<IDWriteTextFormat> textFormat_;

    Microsoft::WRL::ComPtr<ID3D11Device> d3dDevice_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> d3dContext_;
    Microsoft::WRL::ComPtr<IDXGIDevice> dxgiDevice_;
    Microsoft::WRL::ComPtr<IDXGIFactory2> dxgiFactory_;

    Microsoft::WRL::ComPtr<ID2D1Device> d2dDevice_;
    Microsoft::WRL::ComPtr<ID2D1DeviceContext> d2dContext_;
    Microsoft::WRL::ComPtr<IDXGISwapChain1> swapChain_;
    Microsoft::WRL::ComPtr<ID2D1Bitmap1> targetBitmap_;

    Microsoft::WRL::ComPtr<IDCompositionDevice> dcompDevice_;
    Microsoft::WRL::ComPtr<IDCompositionTarget> dcompTarget_;
    Microsoft::WRL::ComPtr<IDCompositionVisual> dcompVisual_;
};

} // namespace imeindicator::views
