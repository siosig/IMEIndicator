#pragma once

#include <cstddef>
#include <expected>
#include <span>
#include <vector>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

// WIC でデコード・拡縮済みの画像（013-ime-corner-image data-model.md §4）。
//   - premultiplied BGRA（GUID_WICPixelFormat32bppPBGRA）、top-down、stride = width * 4
//   - UpdateLayeredWindow(ULW_ALPHA) + BLENDFUNCTION{AC_SRC_ALPHA} が要求する形式そのもの
//     https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-blendfunction
//   - BackgroundImageWindow は 32bpp top-down DIB セクションへ転写した後は保持しない
struct DecodedImage {
    int width{};
    int height{};
    std::vector<std::byte> pbgra;

    // 1 行のバイト数（= width * 4）。DIB 転写時の行コピーに使う。
    size_t stride() const noexcept { return static_cast<size_t>(width) * 4; }
};

// PNG バイト列 → premultiplied BGRA への WIC デコード + 拡縮、および RT_RCDATA リソースの取得。
// 例外は投げない（失敗は HRESULT / Win32 エラーコードで返す）。
//
// COM 前提: decodePngScaled の呼び出しスレッドは CoInitializeEx 済みであること。
// 本体では main.cpp の win32::ComApartment（STA）が UI スレッドで初期化済み（PixelIMEDetector と同じ前提）。
// テストでは fixture の SetUp/TearDown で初期化・解放する。
struct WicImageLoader {
    // PNG バイト列を WIC でデコードし、premultiplied BGRA へ変換してから
    // targetWidth × targetHeight へ縮小/拡大する（変換 → 拡縮の順。半透明エッジのフリンジ防止）。
    // 補間は WICBitmapInterpolationModeHighQualityCubic、失敗時は Fant で再試行。
    // 失敗時は HRESULT（png が空、targetWidth/targetHeight <= 0 は E_INVALIDARG）。
    static std::expected<DecodedImage, HRESULT>
    decodePngScaled(std::span<const std::byte> png, int targetWidth, int targetHeight) noexcept;

    // 実行ファイル（hInst）の RT_RCDATA リソースをロックしてバイト列を返す
    // （FindResourceW / LoadResource / LockResource / SizeofResource）。
    // 返す span はモジュールがアンロードされるまで有効（Win32 では FreeResource 不要）。
    // 失敗時は GetLastError() の値（ID が 1〜0xFFFF の範囲外なら ERROR_INVALID_PARAMETER）。
    static std::expected<std::span<const std::byte>, DWORD>
    lockRcData(HINSTANCE hInst, int resourceId) noexcept;
};

} // namespace imeindicator::services
