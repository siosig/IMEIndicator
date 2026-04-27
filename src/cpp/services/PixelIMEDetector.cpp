#include "PixelIMEDetector.h"

#include "../win32/ComUtil.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <oleauto.h>
#include <UIAutomation.h>

#include <cmath>
#include <cstring>

#pragma comment(lib, "uiautomationcore.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "oleaut32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "user32.lib")

namespace imeindicator::services {

namespace {

// 既存 C# 版と同じキャッシュ間隔。
constexpr std::chrono::milliseconds kIndicatorSearchInterval{60'000};
constexpr std::chrono::milliseconds kPixelCheckInterval{200};

bool rectIsEmpty(const RECT& rc) noexcept
{
    return (rc.right - rc.left) <= 0 || (rc.bottom - rc.top) <= 0;
}

// UI Automation で Shell_TrayWnd 配下の「入力インジケーター」or「Input indicator」を検索し、
// 見つかれば BoundingRectangle を返す。
RECT findInputIndicatorByUIA() noexcept
{
    RECT empty{};

    // FindWindow で Shell_TrayWnd を取得。CoInitializeEx は呼び出し元（UI スレッド）で済んでいる前提。
    HWND tray = ::FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!tray) return empty;

    Microsoft::WRL::ComPtr<IUIAutomation> uia;
    HRESULT hr = ::CoCreateInstance(
        __uuidof(CUIAutomation),
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&uia));
    if (FAILED(hr) || !uia) return empty;

    Microsoft::WRL::ComPtr<IUIAutomationElement> trayElement;
    hr = uia->ElementFromHandle(tray, &trayElement);
    if (FAILED(hr) || !trayElement) return empty;

    // Name == "入力インジケーター" OR "Input indicator"
    auto makeNameCondition = [&](const wchar_t* name)
        -> Microsoft::WRL::ComPtr<IUIAutomationCondition> {
        VARIANT v{};
        v.vt = VT_BSTR;
        v.bstrVal = ::SysAllocString(name);
        Microsoft::WRL::ComPtr<IUIAutomationCondition> c;
        uia->CreatePropertyCondition(UIA_NamePropertyId, v, &c);
        ::VariantClear(&v);
        return c;
    };

    auto condJa = makeNameCondition(L"入力インジケーター");
    auto condEn = makeNameCondition(L"Input indicator");
    if (!condJa || !condEn) return empty;

    Microsoft::WRL::ComPtr<IUIAutomationCondition> orCond;
    hr = uia->CreateOrCondition(condJa.Get(), condEn.Get(), &orCond);
    if (FAILED(hr) || !orCond) return empty;

    Microsoft::WRL::ComPtr<IUIAutomationElement> indicator;
    hr = trayElement->FindFirst(TreeScope_Descendants, orCond.Get(), &indicator);
    if (FAILED(hr) || !indicator) return empty;

    RECT rc{};
    hr = indicator->get_CurrentBoundingRectangle(&rc);
    if (FAILED(hr)) return empty;
    return rc;
}

constexpr UINT kSrcCopy = SRCCOPY;
constexpr UINT kDibRgbColors = DIB_RGB_COLORS;

} // namespace

PixelIMEDetector& PixelIMEDetector::instance()
{
    static PixelIMEDetector s;
    return s;
}

PixelIMEDetector::~PixelIMEDetector()
{
    releaseGdiResources();
}

void PixelIMEDetector::clearCache()
{
    std::lock_guard lock(stateMutex_);
    hasCachedRect_ = false;
    cachedIndicatorRect_ = {};
    lastIndicatorSearch_ = {};
    lastPixelResult_.reset();
    lastPixelCheck_ = {};
}

std::optional<bool> PixelIMEDetector::detectIMEState(models::LanguageType language)
{
    if (language != models::LanguageType::Japanese) return std::nullopt;

    {
        std::lock_guard lock(stateMutex_);
        const auto now = std::chrono::steady_clock::now();
        if (lastPixelResult_.has_value() &&
            (now - lastPixelCheck_) < kPixelCheckInterval) {
            return lastPixelResult_;
        }
    }

    auto t = detectIMEStateTimed(language);
    {
        std::lock_guard lock(stateMutex_);
        if (t.isOn.has_value()) {
            lastPixelResult_ = t.isOn;
            lastPixelCheck_ = std::chrono::steady_clock::now();
        }
    }
    return t.isOn;
}

PixelIMEDetector::Timing PixelIMEDetector::detectIMEStateTimed(models::LanguageType language)
{
    Timing out{};
    if (language != models::LanguageType::Japanese) return out;

    using clock = std::chrono::steady_clock;
    const auto t0 = clock::now();

    RECT rect = findIndicatorRect();
    const auto t1 = clock::now();
    out.getRectMs = std::chrono::duration<double, std::milli>(t1 - t0).count();

    if (rectIsEmpty(rect) ||
        (rect.right - rect.left) < 5 ||
        (rect.bottom - rect.top) < 5) {
        out.totalMs = std::chrono::duration<double, std::milli>(clock::now() - t0).count();
        return out;
    }

    const bool isOn = analyzeIndicatorWithBitBlt(rect, language);
    const auto t2 = clock::now();
    out.analyzeMs = std::chrono::duration<double, std::milli>(t2 - t1).count();
    out.totalMs = std::chrono::duration<double, std::milli>(t2 - t0).count();
    out.isOn = isOn;
    return out;
}

RECT PixelIMEDetector::findIndicatorRect()
{
    const auto now = std::chrono::steady_clock::now();
    {
        std::lock_guard lock(stateMutex_);
        if (hasCachedRect_ && (now - lastIndicatorSearch_) < kIndicatorSearchInterval) {
            return cachedIndicatorRect_;
        }
    }
    return searchAndCacheIndicatorRect();
}

RECT PixelIMEDetector::searchAndCacheIndicatorRect()
{
    const auto now = std::chrono::steady_clock::now();
    RECT rc = findInputIndicatorByUIA();
    {
        std::lock_guard lock(stateMutex_);
        lastIndicatorSearch_ = now;
        cachedIndicatorRect_ = rc;
        hasCachedRect_ = !rectIsEmpty(rc);
    }
    return rc;
}

bool PixelIMEDetector::ensureGdiResources(int width, int height)
{
    if (screenDC_ && memDC_ && bitmap_ && gdiWidth_ == width && gdiHeight_ == height) {
        return true;
    }

    releaseGdiResources();

    screenDC_ = ::GetDC(nullptr);
    if (!screenDC_) return false;

    memDC_ = ::CreateCompatibleDC(screenDC_);
    if (!memDC_) {
        releaseGdiResources();
        return false;
    }

    bitmap_ = ::CreateCompatibleBitmap(screenDC_, width, height);
    if (!bitmap_) {
        releaseGdiResources();
        return false;
    }

    oldBitmap_ = ::SelectObject(memDC_, bitmap_);
    gdiWidth_ = width;
    gdiHeight_ = height;
    pixelBuffer_.assign(static_cast<size_t>(width) * height * 4, 0);
    return true;
}

void PixelIMEDetector::releaseGdiResources()
{
    if (memDC_ && oldBitmap_) {
        ::SelectObject(memDC_, oldBitmap_);
        oldBitmap_ = nullptr;
    }
    if (bitmap_) {
        ::DeleteObject(bitmap_);
        bitmap_ = nullptr;
    }
    if (memDC_) {
        ::DeleteDC(memDC_);
        memDC_ = nullptr;
    }
    if (screenDC_) {
        ::ReleaseDC(nullptr, screenDC_);
        screenDC_ = nullptr;
    }
    gdiWidth_ = 0;
    gdiHeight_ = 0;
    pixelBuffer_.clear();
}

bool PixelIMEDetector::analyzeIndicatorWithBitBlt(const RECT& rect, models::LanguageType language)
{
    const int left = rect.left;
    const int top = rect.top;
    const int width = rect.right - rect.left;
    const int height = rect.bottom - rect.top;

    if (!ensureGdiResources(width, height)) return false;

    if (!::BitBlt(memDC_, 0, 0, width, height, screenDC_, left, top, kSrcCopy)) {
        return false;
    }

    // GetDIBits の前にビットマップを DC から外す（API 要件）。
    ::SelectObject(memDC_, oldBitmap_);

    BITMAPINFO bmi{};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = width;
    bmi.bmiHeader.biHeight = height;
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    const int stride = width * 4;
    int rc = ::GetDIBits(screenDC_, bitmap_, 0, static_cast<UINT>(height),
                          pixelBuffer_.data(), &bmi, kDibRgbColors);

    // ビットマップを DC に戻す（次回の BitBlt に備える）。
    oldBitmap_ = ::SelectObject(memDC_, bitmap_);

    if (rc == 0) return false;

    return analyzePixelData(pixelBuffer_, width, height, stride, language);
}

bool PixelIMEDetector::analyzePixelData(const std::vector<uint8_t>& pixels,
                                        int width, int height, int stride,
                                        models::LanguageType /*language*/)
{
    constexpr int kBorderSize = 5;
    long long bgBrightnessSum = 0;
    int bgPixelCount = 0;

    // 背景輝度サンプリング（左端・右端・下端の各 5px、上端 1 行は除外）。
    for (int y = 1; y < height; ++y) {
        for (int x = 0; x < width; ++x) {
            const bool isBorder = (x < kBorderSize) ||
                                  (x >= width - kBorderSize) ||
                                  (y >= height - kBorderSize);
            if (!isBorder) continue;

            const int offset = y * stride + x * 4;
            const uint8_t b = pixels[offset];
            const uint8_t g = pixels[offset + 1];
            const uint8_t r = pixels[offset + 2];
            bgBrightnessSum += (r * 299 + g * 587 + b * 114) / 1000;
            ++bgPixelCount;
        }
    }
    if (bgPixelCount == 0) return false;
    const int bgBrightness = static_cast<int>(bgBrightnessSum / bgPixelCount);

    constexpr int kTopMargin = 3;
    constexpr int kBottomMargin = 8;
    constexpr int kDiffThreshold = 30;

    int textPixels = 0;
    int innerPixels = 0;
    for (int y = kTopMargin; y < height - kBottomMargin; ++y) {
        for (int x = kBorderSize; x < width - kBorderSize; ++x) {
            ++innerPixels;
            const int offset = y * stride + x * 4;
            const uint8_t b = pixels[offset];
            const uint8_t g = pixels[offset + 1];
            const uint8_t r = pixels[offset + 2];
            const int brightness = (r * 299 + g * 587 + b * 114) / 1000;
            if (std::abs(brightness - bgBrightness) > kDiffThreshold) {
                ++textPixels;
            }
        }
    }
    if (innerPixels == 0) return false;

    const double textRatio = static_cast<double>(textPixels) / innerPixels;
    // 既存実測値: 日本語 A=7.1%, あ=11.3% → 閾値 8.5%
    return textRatio > 0.085;
}

} // namespace imeindicator::services
