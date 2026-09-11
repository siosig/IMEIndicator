#include "services/WicImageLoader.h"

#include <gtest/gtest.h>

// CoInitializeEx / CoUninitialize（WIN32_LEAN_AND_MEAN では <windows.h> から ole2.h が外れるため明示）
#include <objbase.h>

#include <cstddef>
#include <filesystem>
#include <format>
#include <fstream>
#include <string>
#include <vector>

namespace fs = std::filesystem;
using namespace imeindicator;

namespace {

// ============================================================
// テスト画像（src/cpp/resources/ime-on-background.png, 512x512 RGBA, 四隅透過）の解決
// ============================================================
// CMake（tests/cpp/CMakeLists.txt）がワイド文字列リテラルとして
//   IMEINDICATOR_RESOURCES_DIR=L"<repo>/src/cpp/resources"
// を定義する想定。未定義時（IDE 直接ビルド等）は、このソースファイルからの相対パスと
// カレントディレクトリからの相対パスを順に試し、どこにも無ければ GTEST_SKIP する。
constexpr const wchar_t* kBackgroundPngName = L"ime-on-background.png";

std::vector<fs::path> candidateResourceDirs()
{
    std::vector<fs::path> dirs;
#ifdef IMEINDICATOR_RESOURCES_DIR
    dirs.emplace_back(IMEINDICATOR_RESOURCES_DIR);
#endif
    // tests/cpp/unit/WicImageLoaderTests.cpp → <repo>/src/cpp/resources
    dirs.emplace_back(fs::path(__FILE__).parent_path() / L"../../../src/cpp/resources");
    // カレントディレクトリ基準のフォールバック
    dirs.emplace_back(L"../../src/cpp/resources");
    return dirs;
}

std::vector<std::byte> readAllBytes(const fs::path& p)
{
    std::error_code ec;
    const auto size = fs::file_size(p, ec);
    if (ec || size == 0) return {};

    std::ifstream in(p, std::ios::binary);
    if (!in) return {};

    std::vector<std::byte> bytes(static_cast<size_t>(size));
    in.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    if (!in) return {};
    return bytes;
}

// 見つからなければ空ベクタ（呼び出し側で GTEST_SKIP）。
std::vector<std::byte> loadBackgroundPng()
{
    for (const auto& dir : candidateResourceDirs()) {
        const fs::path p = dir / kBackgroundPngName;
        std::error_code ec;
        if (fs::is_regular_file(p, ec)) {
            return readAllBytes(p);
        }
    }
    return {};
}

// premultiplied BGRA（top-down、stride = width * 4）の (x, y) 画素の各チャネル。
struct Pixel { int b{}; int g{}; int r{}; int a{}; };

Pixel pixelAt(const services::DecodedImage& img, int x, int y)
{
    const size_t offset =
        (static_cast<size_t>(y) * static_cast<size_t>(img.width) + static_cast<size_t>(x)) * 4;
    return Pixel{
        std::to_integer<int>(img.pbgra[offset + 0]),
        std::to_integer<int>(img.pbgra[offset + 1]),
        std::to_integer<int>(img.pbgra[offset + 2]),
        std::to_integer<int>(img.pbgra[offset + 3]),
    };
}

std::string hrText(HRESULT hr)
{
    return std::format("0x{:08X}", static_cast<unsigned long>(hr));
}

// WIC は COM コンポーネントなので、テストスレッドで COM を初期化しておく。
class WicImageLoaderFixture : public ::testing::Test {
protected:
    void SetUp() override
    {
        // S_FALSE（同スレッドで既に初期化済み）も成功扱い。
        // RPC_E_CHANGED_MODE（別アパートメントモデルで初期化済み）でも WIC は使えるが、
        // その場合は自分が初期化したわけではないので TearDown で CoUninitialize しない。
        initHr_ = ::CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        ASSERT_TRUE(SUCCEEDED(initHr_) || initHr_ == RPC_E_CHANGED_MODE)
            << "CoInitializeEx failed: " << hrText(initHr_);
    }

    void TearDown() override
    {
        if (SUCCEEDED(initHr_)) {
            ::CoUninitialize();
        }
    }

private:
    HRESULT initHr_{E_FAIL};
};

} // namespace

// ============================================================
// decodePngScaled — 同梱画像を 128px へ縮小
// ============================================================

TEST_F(WicImageLoaderFixture, DecodePngScaled_BackgroundImage128_DimensionsAndBufferSize)
{
    const auto png = loadBackgroundPng();
    if (png.empty()) GTEST_SKIP() << "ime-on-background.png not found";

    const auto result = services::WicImageLoader::decodePngScaled(png, 128, 128);
    ASSERT_TRUE(result.has_value()) << "hr=" << hrText(result.error());

    EXPECT_EQ(result->width, 128);
    EXPECT_EQ(result->height, 128);
    EXPECT_EQ(result->pbgra.size(), static_cast<size_t>(128 * 128 * 4));
    EXPECT_EQ(result->stride(), static_cast<size_t>(128 * 4));
}

TEST_F(WicImageLoaderFixture, DecodePngScaled_BackgroundImage128_CornersTransparentCenterOpaque)
{
    const auto png = loadBackgroundPng();
    if (png.empty()) GTEST_SKIP() << "ime-on-background.png not found";

    const auto result = services::WicImageLoader::decodePngScaled(png, 128, 128);
    ASSERT_TRUE(result.has_value()) << "hr=" << hrText(result.error());
    ASSERT_EQ(result->pbgra.size(), static_cast<size_t>(128 * 128 * 4));

    // 角丸バッジなので四隅は完全透過、中央は完全不透明。
    EXPECT_EQ(pixelAt(*result, 0, 0).a, 0);
    EXPECT_EQ(pixelAt(*result, 127, 0).a, 0);
    EXPECT_EQ(pixelAt(*result, 0, 127).a, 0);
    EXPECT_EQ(pixelAt(*result, 127, 127).a, 0);
    EXPECT_EQ(pixelAt(*result, 64, 64).a, 255);
}

TEST_F(WicImageLoaderFixture, DecodePngScaled_BackgroundImage128_IsPremultiplied)
{
    const auto png = loadBackgroundPng();
    if (png.empty()) GTEST_SKIP() << "ime-on-background.png not found";

    const auto result = services::WicImageLoader::decodePngScaled(png, 128, 128);
    ASSERT_TRUE(result.has_value()) << "hr=" << hrText(result.error());
    ASSERT_EQ(result->pbgra.size(), static_cast<size_t>(128 * 128 * 4));

    // premultiplied alpha（UpdateLayeredWindow の AC_SRC_ALPHA 要件）なら、全画素で B,G,R <= A。
    // 透過画素（A=0）は色成分も 0 になる。
    for (int y = 0; y < result->height; ++y) {
        for (int x = 0; x < result->width; ++x) {
            const Pixel p = pixelAt(*result, x, y);
            ASSERT_LE(p.b, p.a) << "at (" << x << ", " << y << ")";
            ASSERT_LE(p.g, p.a) << "at (" << x << ", " << y << ")";
            ASSERT_LE(p.r, p.a) << "at (" << x << ", " << y << ")";
        }
    }
}

TEST_F(WicImageLoaderFixture, DecodePngScaled_NonSquareUpscale_ProducesRequestedSize)
{
    const auto png = loadBackgroundPng();
    if (png.empty()) GTEST_SKIP() << "ime-on-background.png not found";

    // 拡大・非正方形でも stride = width * 4 で要求寸法どおりのバッファになる。
    const auto result = services::WicImageLoader::decodePngScaled(png, 600, 300);
    ASSERT_TRUE(result.has_value()) << "hr=" << hrText(result.error());

    EXPECT_EQ(result->width, 600);
    EXPECT_EQ(result->height, 300);
    EXPECT_EQ(result->pbgra.size(), static_cast<size_t>(600 * 300 * 4));
    EXPECT_EQ(pixelAt(*result, 300, 150).a, 255);
}

// ============================================================
// decodePngScaled — 引数・入力の異常系
// ============================================================

TEST_F(WicImageLoaderFixture, DecodePngScaled_EmptyInput_ReturnsInvalidArg)
{
    const auto result = services::WicImageLoader::decodePngScaled({}, 128, 128);
    ASSERT_FALSE(result.has_value());
    EXPECT_EQ(result.error(), E_INVALIDARG);
}

TEST_F(WicImageLoaderFixture, DecodePngScaled_CorruptBytes_Fails)
{
    constexpr char kNotPng[] = "not a png";
    std::vector<std::byte> bytes;
    for (const char c : kNotPng) {
        bytes.push_back(static_cast<std::byte>(c));
    }

    const auto result = services::WicImageLoader::decodePngScaled(bytes, 128, 128);
    EXPECT_FALSE(result.has_value());
}

TEST_F(WicImageLoaderFixture, DecodePngScaled_ZeroWidth_ReturnsInvalidArg)
{
    // 引数チェックはデコードより先に行われるので、中身は PNG でなくてよい。
    const std::vector<std::byte> bytes(16, std::byte{0x00});

    const auto result = services::WicImageLoader::decodePngScaled(bytes, 0, 128);
    ASSERT_FALSE(result.has_value());
    EXPECT_EQ(result.error(), E_INVALIDARG);
}

TEST_F(WicImageLoaderFixture, DecodePngScaled_NegativeHeight_ReturnsInvalidArg)
{
    const std::vector<std::byte> bytes(16, std::byte{0x00});

    const auto result = services::WicImageLoader::decodePngScaled(bytes, 128, -1);
    ASSERT_FALSE(result.has_value());
    EXPECT_EQ(result.error(), E_INVALIDARG);
}

// ============================================================
// lockRcData
// ============================================================

TEST(WicImageLoaderTests, LockRcData_MissingResourceId_Fails)
{
    // テスト EXE には ID 9999 の RT_RCDATA は存在しない → FindResourceW 失敗 → GetLastError() が返る。
    const auto result =
        services::WicImageLoader::lockRcData(::GetModuleHandleW(nullptr), 9999);
    ASSERT_FALSE(result.has_value());
    EXPECT_NE(result.error(), static_cast<DWORD>(ERROR_SUCCESS));
}

TEST(WicImageLoaderTests, LockRcData_OutOfRangeId_ReturnsInvalidParameter)
{
    // MAKEINTRESOURCE で表現できない ID（0 以下 / 0xFFFF 超）は API を呼ばずに拒否する。
    const auto zero = services::WicImageLoader::lockRcData(::GetModuleHandleW(nullptr), 0);
    ASSERT_FALSE(zero.has_value());
    EXPECT_EQ(zero.error(), static_cast<DWORD>(ERROR_INVALID_PARAMETER));

    const auto tooLarge = services::WicImageLoader::lockRcData(::GetModuleHandleW(nullptr), 0x10000);
    ASSERT_FALSE(tooLarge.has_value());
    EXPECT_EQ(tooLarge.error(), static_cast<DWORD>(ERROR_INVALID_PARAMETER));
}
