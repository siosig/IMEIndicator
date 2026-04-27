#include "MouseCursorIndicatorWindow.h"

#include "../app/AppConstants.h"
#include "../win32/UnicodeUtil.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <d2d1.h>
#include <d2d1_1.h>
#include <d3d11.h>
#include <dcomp.h>
#include <dwrite.h>
#include <dxgi1_2.h>
#include <shellscalingapi.h>

#include <spdlog/spdlog.h>

#include <algorithm>
#include <cmath>

#define IND_LOG_DEBUG(...) \
    do { if (auto _log = spdlog::get(std::string(imeindicator::app::AppConstants::LoggerApp))) _log->debug(__VA_ARGS__); } while(0)
#define IND_LOG_ERROR(...) \
    do { if (auto _log = spdlog::get(std::string(imeindicator::app::AppConstants::LoggerApp))) _log->error(__VA_ARGS__); } while(0)

#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dcomp.lib")
#pragma comment(lib, "dwrite.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "shcore.lib")

namespace imeindicator::views {

namespace {

constexpr wchar_t kClassName[] = L"IMEIndicator_MouseCursorIndicatorWindow";
constexpr float  kFontSizeRatioToWindow = 0.55f;  // ウィンドウサイズに対するフォントサイズ比率

// ガンマ補正された色を 0.7 倍にして "GlowColor" を作る（C# 版の見た目と近似）。
D2D1_COLOR_F deriveGlowColor(const D2D1_COLOR_F& base) noexcept
{
    return {base.r * 0.7f, base.g * 0.7f, base.b * 0.7f, base.a};
}

UINT getDpiForHwnd(HWND hwnd) noexcept
{
    if (auto fn = ::GetDpiForWindow) {
        UINT dpi = fn(hwnd);
        if (dpi != 0) return dpi;
    }
    return 96;
}

} // namespace

MouseCursorIndicatorWindow::MouseCursorIndicatorWindow() = default;

MouseCursorIndicatorWindow::~MouseCursorIndicatorWindow()
{
    releaseDeviceResources();
    if (hwnd_) {
        ::DestroyWindow(hwnd_);
        hwnd_ = nullptr;
    }
    if (windowClass_ && hInstance_) {
        ::UnregisterClassW(kClassName, hInstance_);
        windowClass_ = 0;
    }
}

bool MouseCursorIndicatorWindow::initialize(HINSTANCE hInstance)
{
    hInstance_ = hInstance;
    IND_LOG_DEBUG("MCI: initialize start");

    // ---- ウィンドウクラス登録 ----
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = &MouseCursorIndicatorWindow::wndProcStatic;
    wc.hInstance = hInstance;
    wc.lpszClassName = kClassName;
    wc.hCursor = nullptr;
    windowClass_ = ::RegisterClassExW(&wc);
    if (!windowClass_) return false;

    // ---- ウィンドウ作成 ----
    // WS_EX_NOREDIRECTIONBITMAP は DirectComposition によるフルアルファ合成のため必須。
    // 参考: https://learn.microsoft.com/windows/win32/directcomp/basic-concepts
    constexpr DWORD exStyle =
        WS_EX_NOREDIRECTIONBITMAP |
        WS_EX_TRANSPARENT |
        WS_EX_TOOLWINDOW |
        WS_EX_NOACTIVATE |
        WS_EX_TOPMOST;
    constexpr DWORD style = WS_POPUP;

    hwnd_ = ::CreateWindowExW(
        exStyle,
        kClassName,
        L"IMEIndicator",
        style,
        0, 0, 64, 64,
        nullptr, nullptr,
        hInstance,
        this);
    if (!hwnd_) return false;

    currentDpi_ = getDpiForHwnd(hwnd_);
    IND_LOG_DEBUG("MCI: hwnd={}, dpi={}", reinterpret_cast<uintptr_t>(hwnd_), currentDpi_);

    if (!createDeviceIndependentResources()) {
        IND_LOG_ERROR("MCI: createDeviceIndependentResources FAILED");
        return false;
    }
    IND_LOG_DEBUG("MCI: device-independent resources OK");
    if (!createDeviceResources()) {
        IND_LOG_ERROR("MCI: createDeviceResources FAILED");
        return false;
    }
    IND_LOG_DEBUG("MCI: device resources OK");
    int ps = physicalSize();
    if (!ensureSwapChainSize(ps)) {
        IND_LOG_ERROR("MCI: ensureSwapChainSize({}) FAILED", ps);
        return false;
    }
    IND_LOG_DEBUG("MCI: swapchain ready (size={})", ps);
    return true;
}

bool MouseCursorIndicatorWindow::createDeviceIndependentResources()
{
    HRESULT hr = ::D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED,
        __uuidof(ID2D1Factory1),
        reinterpret_cast<void**>(d2dFactory_.GetAddressOf()));
    if (FAILED(hr)) return false;

    hr = ::DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED,
        __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(dwriteFactory_.GetAddressOf()));
    if (FAILED(hr)) return false;

    // 既定: Yu Gothic UI ボールド。論理サイズに比例した FontSize は描画時に再生成する。
    hr = dwriteFactory_->CreateTextFormat(
        L"Yu Gothic UI",
        nullptr,
        DWRITE_FONT_WEIGHT_BOLD,
        DWRITE_FONT_STYLE_NORMAL,
        DWRITE_FONT_STRETCH_NORMAL,
        20.0f,
        L"ja-JP",
        textFormat_.GetAddressOf());
    if (FAILED(hr)) return false;
    textFormat_->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
    textFormat_->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);

    return true;
}

bool MouseCursorIndicatorWindow::createDeviceResources()
{
    if (deviceResourcesValid_) return true;
    IND_LOG_DEBUG("MCI: createDeviceResources begin");

    UINT createFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#ifdef _DEBUG
    // デバッグレイヤーは _DEBUG ビルドでも環境次第で利用不可なので、失敗時はフォールバック。
    createFlags |= D3D11_CREATE_DEVICE_DEBUG;
#endif

    constexpr D3D_FEATURE_LEVEL kFeatureLevels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1,
        D3D_FEATURE_LEVEL_10_0,
    };

    HRESULT hr = ::D3D11CreateDevice(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        createFlags,
        kFeatureLevels, ARRAYSIZE(kFeatureLevels),
        D3D11_SDK_VERSION,
        d3dDevice_.GetAddressOf(),
        nullptr,
        d3dContext_.GetAddressOf());
    if (FAILED(hr) && (createFlags & D3D11_CREATE_DEVICE_DEBUG)) {
        // デバッグレイヤーが入ってない環境ではリトライ
        createFlags &= ~D3D11_CREATE_DEVICE_DEBUG;
        hr = ::D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
            createFlags,
            kFeatureLevels, ARRAYSIZE(kFeatureLevels),
            D3D11_SDK_VERSION,
            d3dDevice_.GetAddressOf(),
            nullptr,
            d3dContext_.GetAddressOf());
    }
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: D3D11CreateDevice hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    IND_LOG_DEBUG("MCI: D3D11CreateDevice OK");

    hr = d3dDevice_.As(&dxgiDevice_);
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: As IDXGIDevice hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }

    Microsoft::WRL::ComPtr<IDXGIAdapter> adapter;
    hr = dxgiDevice_->GetAdapter(adapter.GetAddressOf());
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: GetAdapter hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    hr = adapter->GetParent(IID_PPV_ARGS(dxgiFactory_.GetAddressOf()));
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: GetParent IDXGIFactory2 hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    IND_LOG_DEBUG("MCI: DXGI factory OK");

    hr = d2dFactory_->CreateDevice(dxgiDevice_.Get(), d2dDevice_.GetAddressOf());
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: D2D CreateDevice hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    hr = d2dDevice_->CreateDeviceContext(
        D2D1_DEVICE_CONTEXT_OPTIONS_NONE,
        d2dContext_.GetAddressOf());
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: D2D CreateDeviceContext hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    IND_LOG_DEBUG("MCI: D2D device context OK");

    hr = ::DCompositionCreateDevice(
        dxgiDevice_.Get(),
        IID_PPV_ARGS(dcompDevice_.GetAddressOf()));
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: DCompositionCreateDevice hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    hr = dcompDevice_->CreateTargetForHwnd(hwnd_, TRUE,
                                            dcompTarget_.GetAddressOf());
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: CreateTargetForHwnd hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    hr = dcompDevice_->CreateVisual(dcompVisual_.GetAddressOf());
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: CreateVisual hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    IND_LOG_DEBUG("MCI: DComposition OK");

    deviceResourcesValid_ = true;
    return true;
}

void MouseCursorIndicatorWindow::releaseDeviceResources()
{
    targetBitmap_.Reset();
    swapChain_.Reset();
    dcompVisual_.Reset();
    dcompTarget_.Reset();
    dcompDevice_.Reset();
    d2dContext_.Reset();
    d2dDevice_.Reset();
    dxgiFactory_.Reset();
    dxgiDevice_.Reset();
    d3dContext_.Reset();
    d3dDevice_.Reset();
    deviceResourcesValid_ = false;
    currentSwapChainSize_ = 0;
}

bool MouseCursorIndicatorWindow::ensureSwapChainSize(int physical)
{
    if (physical < 1) physical = 1;
    if (currentSwapChainSize_ == physical && swapChain_ && targetBitmap_) return true;
    IND_LOG_DEBUG("MCI: ensureSwapChainSize requested={} current={} hasSwap={} hasBitmap={}",
                  physical, currentSwapChainSize_,
                  swapChain_ ? 1 : 0, targetBitmap_ ? 1 : 0);

    // 既存のリソースを解放（ターゲットビットマップは作り直し）
    targetBitmap_.Reset();
    if (d2dContext_) d2dContext_->SetTarget(nullptr);

    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width = physical;
    desc.Height = physical;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    desc.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED;

    if (!swapChain_) {
        HRESULT hr = dxgiFactory_->CreateSwapChainForComposition(
            d3dDevice_.Get(), &desc, nullptr, swapChain_.GetAddressOf());
        if (FAILED(hr)) { IND_LOG_ERROR("MCI: CreateSwapChainForComposition hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
        IND_LOG_DEBUG("MCI: swapchain created");
    } else {
        HRESULT hr = swapChain_->ResizeBuffers(
            desc.BufferCount, desc.Width, desc.Height, desc.Format, 0);
        if (FAILED(hr)) { IND_LOG_ERROR("MCI: ResizeBuffers hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
        IND_LOG_DEBUG("MCI: swapchain resized to {}x{}", desc.Width, desc.Height);
    }

    HRESULT hr = dcompVisual_->SetContent(swapChain_.Get());
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: Visual.SetContent hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    hr = dcompTarget_->SetRoot(dcompVisual_.Get());
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: Target.SetRoot hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }

    Microsoft::WRL::ComPtr<IDXGISurface2> surface;
    hr = swapChain_->GetBuffer(0, IID_PPV_ARGS(surface.GetAddressOf()));
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: GetBuffer hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }

    D2D1_BITMAP_PROPERTIES1 props = D2D1::BitmapProperties1(
        D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
        D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED),
        96.0f, 96.0f);
    hr = d2dContext_->CreateBitmapFromDxgiSurface(surface.Get(), &props, targetBitmap_.GetAddressOf());
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: CreateBitmapFromDxgiSurface hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    d2dContext_->SetTarget(targetBitmap_.Get());

    currentSwapChainSize_ = physical;
    hr = dcompDevice_->Commit();
    if (FAILED(hr)) { IND_LOG_ERROR("MCI: dcomp Commit hr=0x{:08x}", static_cast<uint32_t>(hr)); return false; }
    IND_LOG_DEBUG("MCI: ensureSwapChainSize OK size={}", physical);
    return true;
}

int MouseCursorIndicatorWindow::physicalSize() const noexcept
{
    const double scale = currentDpi_ / 96.0;
    const double phys = settings_.size * scale;
    return static_cast<int>(std::round(phys > 1.0 ? phys : 1.0));
}

void MouseCursorIndicatorWindow::updateSettings(const models::MouseCursorIndicatorSettings& s)
{
    settings_ = s;
    settings_.clamp();
    if (!hwnd_) return;
    int phys = physicalSize();
    ::SetWindowPos(hwnd_, nullptr, 0, 0, phys, phys,
                   SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);
    ensureSwapChainSize(phys);
    render();
}

void MouseCursorIndicatorWindow::updateText(std::wstring text)
{
    if (text.empty()) text = L"あ";
    displayText_ = std::move(text);
    render();
}

void MouseCursorIndicatorWindow::updateColor(D2D1_COLOR_F color)
{
    backgroundColor_ = color;
    glowColor_ = deriveGlowColor(color);
    render();
}

void MouseCursorIndicatorWindow::updatePosition(int x, int y)
{
    if (!hwnd_) return;
    // インジケーターの中心がカーソルに来るよう左上を補正。設定 offsetX/Y も加味する。
    const int phys = physicalSize();
    const int left = x + static_cast<int>(std::round(settings_.offsetX)) - phys / 2;
    const int top  = y + static_cast<int>(std::round(settings_.offsetY)) - phys / 2;
    ::SetWindowPos(hwnd_, nullptr, left, top, 0, 0,
                   SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);
}

void MouseCursorIndicatorWindow::show() noexcept
{
    if (hwnd_) {
        RECT rc{};
        ::GetWindowRect(hwnd_, &rc);
        BOOL wasVisible = ::IsWindowVisible(hwnd_);
        IND_LOG_DEBUG("MCI: show() pos=({},{}) size={}x{} prevVisible={}",
                      rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top,
                      wasVisible ? 1 : 0);
        ::ShowWindow(hwnd_, SW_SHOWNOACTIVATE);
        render();
    }
}

void MouseCursorIndicatorWindow::hide() noexcept
{
    if (hwnd_) {
        IND_LOG_DEBUG("MCI: hide()");
        ::ShowWindow(hwnd_, SW_HIDE);
    }
}

void MouseCursorIndicatorWindow::onDpiChanged(UINT newDpi)
{
    currentDpi_ = newDpi;
    if (!hwnd_) return;
    int phys = physicalSize();
    ::SetWindowPos(hwnd_, nullptr, 0, 0, phys, phys,
                   SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);
    ensureSwapChainSize(phys);
    render();
}

LRESULT CALLBACK MouseCursorIndicatorWindow::wndProcStatic(HWND hwnd, UINT msg,
                                                            WPARAM wp, LPARAM lp)
{
    if (msg == WM_NCCREATE) {
        auto* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        ::SetWindowLongPtrW(hwnd, GWLP_USERDATA,
                            reinterpret_cast<LONG_PTR>(cs->lpCreateParams));
        return ::DefWindowProcW(hwnd, msg, wp, lp);
    }
    auto* self = reinterpret_cast<MouseCursorIndicatorWindow*>(
        ::GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (self) return self->handleMessage(msg, wp, lp);
    return ::DefWindowProcW(hwnd, msg, wp, lp);
}

LRESULT MouseCursorIndicatorWindow::handleMessage(UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
        case WM_DPICHANGED:
            onDpiChanged(LOWORD(wp));
            return 0;
        case WM_PAINT:
            render();
            ::ValidateRect(hwnd_, nullptr);
            return 0;
        case WM_DESTROY:
            return 0;
    }
    return ::DefWindowProcW(hwnd_, msg, wp, lp);
}

void MouseCursorIndicatorWindow::render()
{
    if (!deviceResourcesValid_ || !d2dContext_ || !targetBitmap_) return;

    const int phys = currentSwapChainSize_;
    if (phys < 4) return;
    const float fphys = static_cast<float>(phys);
    const float opacity = static_cast<float>(std::clamp(settings_.opacity, 0.1, 1.0));

    d2dContext_->BeginDraw();
    d2dContext_->SetTransform(D2D1::Matrix3x2F::Identity());
    d2dContext_->Clear(D2D1::ColorF(0, 0, 0, 0));  // 完全透明

    // ---- 1) 外側 RadialGradient（中心=背景色, 周辺=透明） ----
    {
        const D2D1_GRADIENT_STOP stops[] = {
            { 0.0f, {backgroundColor_.r, backgroundColor_.g, backgroundColor_.b, opacity} },
            { 1.0f, {0, 0, 0, 0} },
        };
        Microsoft::WRL::ComPtr<ID2D1GradientStopCollection> coll;
        if (SUCCEEDED(d2dContext_->CreateGradientStopCollection(
                stops, ARRAYSIZE(stops), coll.GetAddressOf()))) {
            const D2D1_POINT_2F center = D2D1::Point2F(fphys / 2.0f, fphys / 2.0f);
            const D2D1_POINT_2F origin = center;
            const float radius = fphys / 2.0f;
            D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES rgp =
                D2D1::RadialGradientBrushProperties(center, origin, radius, radius);
            Microsoft::WRL::ComPtr<ID2D1RadialGradientBrush> brush;
            if (SUCCEEDED(d2dContext_->CreateRadialGradientBrush(
                    rgp, coll.Get(), brush.GetAddressOf()))) {
                D2D1_ELLIPSE outer = D2D1::Ellipse(center, radius, radius);
                d2dContext_->FillEllipse(outer, brush.Get());
            }
        }
    }

    // ---- 2) 内側 LinearGradient（マージン 3px、左上=背景色, 右下=グロー色） ----
    constexpr float kMargin = 3.0f;
    {
        const D2D1_GRADIENT_STOP stops[] = {
            { 0.0f, {backgroundColor_.r, backgroundColor_.g, backgroundColor_.b, opacity} },
            { 1.0f, {glowColor_.r, glowColor_.g, glowColor_.b, opacity} },
        };
        Microsoft::WRL::ComPtr<ID2D1GradientStopCollection> coll;
        if (SUCCEEDED(d2dContext_->CreateGradientStopCollection(
                stops, ARRAYSIZE(stops), coll.GetAddressOf()))) {
            D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES lgp =
                D2D1::LinearGradientBrushProperties(
                    D2D1::Point2F(kMargin, kMargin),
                    D2D1::Point2F(fphys - kMargin, fphys - kMargin));
            Microsoft::WRL::ComPtr<ID2D1LinearGradientBrush> brush;
            if (SUCCEEDED(d2dContext_->CreateLinearGradientBrush(
                    lgp, coll.Get(), brush.GetAddressOf()))) {
                const float radius = (fphys - 2.0f * kMargin) / 2.0f;
                D2D1_ELLIPSE inner = D2D1::Ellipse(
                    D2D1::Point2F(fphys / 2.0f, fphys / 2.0f), radius, radius);
                d2dContext_->FillEllipse(inner, brush.Get());
            }
        }
    }

    // ---- 3) 白いボーダー（線幅 1px、不透明度 0.3） ----
    {
        Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> brush;
        if (SUCCEEDED(d2dContext_->CreateSolidColorBrush(
                D2D1::ColorF(1.0f, 1.0f, 1.0f, 0.3f * opacity),
                brush.GetAddressOf()))) {
            const float radius = (fphys - 2.0f * kMargin) / 2.0f - 0.5f;
            D2D1_ELLIPSE border = D2D1::Ellipse(
                D2D1::Point2F(fphys / 2.0f, fphys / 2.0f), radius, radius);
            d2dContext_->DrawEllipse(border, brush.Get(), 1.0f);
        }
    }

    // ---- 4)+5) テキスト影 + 本体 ----
    if (textFormat_ && !displayText_.empty()) {
        const float fontSize = fphys * kFontSizeRatioToWindow;
        textFormat_->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
        textFormat_->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);

        // フォントサイズは TextFormat 単位なので、TextLayout で個別指定。
        Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
        if (SUCCEEDED(dwriteFactory_->CreateTextLayout(
                displayText_.data(),
                static_cast<UINT32>(displayText_.size()),
                textFormat_.Get(), fphys, fphys, layout.GetAddressOf()))) {
            DWRITE_TEXT_RANGE all{0, static_cast<UINT32>(displayText_.size())};
            layout->SetFontSize(fontSize, all);
            layout->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
            layout->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);

            // 影
            Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> shadowBrush;
            d2dContext_->CreateSolidColorBrush(
                D2D1::ColorF(0, 0, 0, 0.25f * opacity),
                shadowBrush.GetAddressOf());
            if (shadowBrush) {
                d2dContext_->DrawTextLayout(
                    D2D1::Point2F(0.5f, 0.5f), layout.Get(),
                    shadowBrush.Get(), D2D1_DRAW_TEXT_OPTIONS_NONE);
            }

            // 本体
            Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> textBrush;
            d2dContext_->CreateSolidColorBrush(
                D2D1::ColorF(1, 1, 1, opacity),
                textBrush.GetAddressOf());
            if (textBrush) {
                d2dContext_->DrawTextLayout(
                    D2D1::Point2F(0, 0), layout.Get(),
                    textBrush.Get(), D2D1_DRAW_TEXT_OPTIONS_NONE);
            }
        }
    }

    HRESULT hr = d2dContext_->EndDraw();
    if (hr == D2DERR_RECREATE_TARGET) {
        IND_LOG_ERROR("MCI: EndDraw D2DERR_RECREATE_TARGET, recreating");
        releaseDeviceResources();
        if (createDeviceResources()) {
            ensureSwapChainSize(physicalSize());
        }
        return;
    }
    if (FAILED(hr)) {
        IND_LOG_ERROR("MCI: EndDraw hr=0x{:08x}", static_cast<uint32_t>(hr));
    }

    DXGI_PRESENT_PARAMETERS pp{};
    HRESULT phr = swapChain_->Present1(1, 0, &pp);
    if (FAILED(phr)) IND_LOG_ERROR("MCI: Present1 hr=0x{:08x}", static_cast<uint32_t>(phr));
    HRESULT chr = dcompDevice_->Commit();
    if (FAILED(chr)) IND_LOG_ERROR("MCI: render Commit hr=0x{:08x}", static_cast<uint32_t>(chr));
}

} // namespace imeindicator::views
