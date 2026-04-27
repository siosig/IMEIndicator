#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <combaseapi.h>

#include <wrl/client.h>
#include <wil/result.h>
#include <wil/com.h>

namespace imeindicator::win32 {

// ComPtr エイリアス（既存コードでよく使う Microsoft::WRL::ComPtr を再エクスポート）
template <typename T>
using ComPtr = Microsoft::WRL::ComPtr<T>;

// CoInitializeEx / CoUninitialize の RAII。アパートメントスレッドモデル。
// メインスレッドや UI スレッドで保持する想定。
class ComApartment {
public:
    explicit ComApartment(DWORD coinit = COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE)
        : hr_(::CoInitializeEx(nullptr, coinit))
    {
        // S_OK / S_FALSE（既に初期化済み）以外は失敗扱い。
        // RPC_E_CHANGED_MODE は呼び出し元で検出できるよう ok() に委ねる。
    }

    ComApartment(const ComApartment&) = delete;
    ComApartment& operator=(const ComApartment&) = delete;
    ComApartment(ComApartment&&) = delete;
    ComApartment& operator=(ComApartment&&) = delete;

    ~ComApartment()
    {
        if (SUCCEEDED(hr_)) {
            ::CoUninitialize();
        }
    }

    bool ok() const noexcept { return SUCCEEDED(hr_); }
    HRESULT hr() const noexcept { return hr_; }

private:
    HRESULT hr_;
};

// HRESULT 失敗時に std::system_error を投げるヘルパー（wil との橋渡し）
inline void throwIfFailed(HRESULT hr, const char* context = nullptr)
{
    if (FAILED(hr)) {
        if (context) {
            // %hs で narrow 文字列を強制（書式マクロは wide でも narrow でも揃う）
            THROW_HR_MSG(hr, "%hs", context);
        } else {
            THROW_HR(hr);
        }
    }
}

} // namespace imeindicator::win32
