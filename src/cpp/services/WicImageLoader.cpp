#include "WicImageLoader.h"

#include <objbase.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <cstdint>
#include <limits>
#include <utility>

// WIC API 本体と CLSID_WICImagingFactory / GUID_WICPixelFormat32bppPBGRA の定義。
// CMake 側（src/cpp, tests/cpp）のリンク指定は別タスク（T013）で追加する。
#pragma comment(lib, "windowscodecs.lib")
#pragma comment(lib, "ole32.lib")

namespace imeindicator::services {

namespace {

using Microsoft::WRL::ComPtr;

constexpr UINT kBytesPerPixel = 4; // 32bpp PBGRA

// 指定の補間モードでスケーラーを生成・初期化する。
// IWICBitmapScaler::Initialize は 1 インスタンスにつき 1 回しか呼べない
// （再呼び出しは WINCODEC_ERR_WRONGSTATE）ため、再試行のたびに新しいインスタンスを作る。
// https://learn.microsoft.com/windows/win32/api/wincodec/nf-wincodec-iwicbitmapscaler-initialize
std::expected<ComPtr<IWICBitmapScaler>, HRESULT>
createScaler(IWICImagingFactory& factory, IWICBitmapSource& source,
             UINT width, UINT height, WICBitmapInterpolationMode mode) noexcept
{
    ComPtr<IWICBitmapScaler> scaler;
    HRESULT hr = factory.CreateBitmapScaler(&scaler);
    if (FAILED(hr)) return std::unexpected(hr);

    hr = scaler->Initialize(&source, width, height, mode);
    if (FAILED(hr)) return std::unexpected(hr);

    return scaler;
}

} // namespace

std::expected<DecodedImage, HRESULT>
WicImageLoader::decodePngScaled(std::span<const std::byte> png, int targetWidth, int targetHeight) noexcept
{
    if (png.empty() || targetWidth <= 0 || targetHeight <= 0) {
        return std::unexpected(E_INVALIDARG);
    }
    // IWICStream::InitializeFromMemory のサイズは DWORD、CopyPixels のバッファサイズは UINT。
    // これらに収まらない要求は WIC に渡す前に拒否する。
    if (png.size() > (std::numeric_limits<DWORD>::max)()) {
        return std::unexpected(E_INVALIDARG);
    }
    const UINT width = static_cast<UINT>(targetWidth);
    const UINT height = static_cast<UINT>(targetHeight);
    const std::uint64_t stride = static_cast<std::uint64_t>(width) * kBytesPerPixel;
    const std::uint64_t bufferSize = stride * height;
    if (bufferSize > (std::numeric_limits<UINT>::max)()) {
        return std::unexpected(E_INVALIDARG);
    }

    // 1. ファクトリ。呼び出しスレッドは COM 初期化済みが前提（ヘッダのコメント参照）。
    //    CLSID_WICImagingFactory は _WIN32_WINNT >= Win8 で CLSID_WICImagingFactory2 に解決されるが、
    //    IWICImagingFactory2 は IWICImagingFactory を継承しているので基底インターフェースで受けられる。
    ComPtr<IWICImagingFactory> factory;
    HRESULT hr = ::CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                                    IID_PPV_ARGS(&factory));
    if (FAILED(hr)) return std::unexpected(hr);

    // 2. メモリ上の PNG をストリーム化。InitializeFromMemory はバッファをコピーせず、寿命は呼び出し側が保証する
    //    （この関数の中でしか使わないので png の寿命で足りる）。デコーダーは読み取りしか行わないため、
    //    読み取り専用のリソースメモリ（LockResource の戻り）をそのまま渡せる。
    //    https://learn.microsoft.com/windows/win32/api/wincodec/nf-wincodec-iwicstream-initializefrommemory
    ComPtr<IWICStream> stream;
    hr = factory->CreateStream(&stream);
    if (FAILED(hr)) return std::unexpected(hr);

    // WICInProcPointer は BYTE*（非 const）。std::byte* との相互変換は reinterpret_cast でしか表現できない。
    hr = stream->InitializeFromMemory(
        reinterpret_cast<WICInProcPointer>(const_cast<std::byte*>(png.data())),
        static_cast<DWORD>(png.size()));
    if (FAILED(hr)) return std::unexpected(hr);

    // 3. デコーダー（コンテナ形式はストリーム内容から自動判定。PNG 以外や壊れたデータはここで失敗）→ 先頭フレーム。
    //    https://learn.microsoft.com/windows/win32/api/wincodec/nf-wincodec-iwicimagingfactory-createdecoderfromstream
    ComPtr<IWICBitmapDecoder> decoder;
    hr = factory->CreateDecoderFromStream(stream.Get(), nullptr, WICDecodeMetadataCacheOnDemand, &decoder);
    if (FAILED(hr)) return std::unexpected(hr);

    ComPtr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, &frame);
    if (FAILED(hr)) return std::unexpected(hr);

    // 4. premultiplied BGRA へ変換。拡縮の「前」に行う（直線 alpha のまま補間すると透明画素の色が滲む）。
    //    https://learn.microsoft.com/windows/win32/api/wincodec/nf-wincodec-iwicformatconverter-initialize
    ComPtr<IWICFormatConverter> converter;
    hr = factory->CreateFormatConverter(&converter);
    if (FAILED(hr)) return std::unexpected(hr);

    hr = converter->Initialize(frame.Get(), GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherTypeNone,
                               nullptr, 0.0, WICBitmapPaletteTypeCustom);
    if (FAILED(hr)) return std::unexpected(hr);

    // 5. 拡縮。ソースと同寸ならスケーラーを挟まない（無駄な再サンプリングを省く）。
    //    HighQualityCubic は Windows 10 以降で利用可能で、2 倍超の縮小でもエイリアシングが出にくい。
    //    失敗時は Fant（従来の高品質縮小）で再試行する。
    //    https://learn.microsoft.com/windows/win32/api/wincodec/ne-wincodec-wicbitmapinterpolationmode
    ComPtr<IWICBitmapSource> source = converter;
    UINT srcWidth = 0;
    UINT srcHeight = 0;
    hr = frame->GetSize(&srcWidth, &srcHeight);
    if (FAILED(hr)) return std::unexpected(hr);

    if (srcWidth != width || srcHeight != height) {
        auto scaler = createScaler(*factory.Get(), *converter.Get(), width, height,
                                   WICBitmapInterpolationModeHighQualityCubic);
        if (!scaler) {
            scaler = createScaler(*factory.Get(), *converter.Get(), width, height,
                                  WICBitmapInterpolationModeFant);
        }
        if (!scaler) return std::unexpected(scaler.error());
        source = std::move(*scaler);
    }

    // 6. 出力バッファへコピー（prc = nullptr で全域、stride = width * 4、top-down）。
    //    https://learn.microsoft.com/windows/win32/api/wincodec/nf-wincodec-iwicbitmapsource-copypixels
    DecodedImage out;
    out.width = targetWidth;
    out.height = targetHeight;
    try {
        out.pbgra.resize(static_cast<size_t>(bufferSize));
    } catch (...) {
        // std::bad_alloc のみ起こり得る（サイズは UINT 範囲に制限済み）。例外は境界の外へ出さない。
        return std::unexpected(E_OUTOFMEMORY);
    }

    hr = source->CopyPixels(nullptr, static_cast<UINT>(stride), static_cast<UINT>(bufferSize),
                            reinterpret_cast<BYTE*>(out.pbgra.data()));
    if (FAILED(hr)) return std::unexpected(hr);

    return out;
}

std::expected<std::span<const std::byte>, DWORD>
WicImageLoader::lockRcData(HINSTANCE hInst, int resourceId) noexcept
{
    // MAKEINTRESOURCE が表現できるのは 16bit の整数 ID のみ。
    if (resourceId <= 0 || resourceId > 0xFFFF) {
        return std::unexpected(static_cast<DWORD>(ERROR_INVALID_PARAMETER));
    }

    // FindResourceW → LoadResource → SizeofResource → LockResource。
    // Win32 ではリソースはモジュールイメージ内に直接マップされており、LoadResource の戻りは解放不要
    // （FreeResource は 16bit 互換の名残で、32bit 以降は何もしない）。
    // https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-findresourcew
    // https://learn.microsoft.com/windows/win32/api/libloaderapi/nf-libloaderapi-loadresource
    // https://learn.microsoft.com/windows/win32/api/libloaderapi/nf-libloaderapi-lockresource
    const HRSRC hRes = ::FindResourceW(hInst, MAKEINTRESOURCEW(resourceId), RT_RCDATA);
    if (!hRes) return std::unexpected(::GetLastError());

    const HGLOBAL hGlobal = ::LoadResource(hInst, hRes);
    if (!hGlobal) return std::unexpected(::GetLastError());

    const DWORD size = ::SizeofResource(hInst, hRes);
    if (size == 0) {
        // 失敗時は 0 が返る。サイズ 0 の正常なリソース（GetLastError() == ERROR_SUCCESS）も
        // 画像としては無効なので失敗扱いにする。
        const DWORD err = ::GetLastError();
        return std::unexpected(err != ERROR_SUCCESS ? err : static_cast<DWORD>(ERROR_INVALID_DATA));
    }

    // LockResource は失敗時に GetLastError を設定する保証がないため固定コードで返す。
    const void* data = ::LockResource(hGlobal);
    if (!data) return std::unexpected(static_cast<DWORD>(ERROR_INVALID_DATA));

    return std::span<const std::byte>(static_cast<const std::byte*>(data), size);
}

} // namespace imeindicator::services
