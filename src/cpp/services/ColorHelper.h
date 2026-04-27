#pragma once

#include <string>
#include <string_view>

#include <d2d1.h>

namespace imeindicator::services {

// 色のパース・変換ヘルパー（既存 [ColorHelper.cs] の C++ 版）。
// 内部表現は Direct2D の D2D1_COLOR_F（0.0〜1.0 の float ARGB）。
// 既存 C# 版と同じ HEX 文字列フォーマットをサポートする：
//   #RGB  / #RGBA   （短縮形、各桁を 2 回繰り返したのと等価）
//   #RRGGBB / #AARRGGBB（標準形、A は ALPHA）
struct ColorHelper {
    // HEX → D2D1_COLOR_F。空文字・不正入力は灰色 (0.5, 0.5, 0.5, 1.0) を返す。
    static D2D1_COLOR_F parseColor(std::string_view hex) noexcept;

    // D2D1_COLOR_F → "#AARRGGBB"（大文字）
    static std::string toArgbHex(const D2D1_COLOR_F& color);

    // D2D1_COLOR_F → "#RRGGBB"（大文字、alpha は破棄）
    static std::string toRgbHex(const D2D1_COLOR_F& color);

    // 有効な HEX カラー文字列か（先頭 # 任意、3/4/6/8 桁の hex）
    static bool isValidHexColor(std::string_view hex) noexcept;
};

} // namespace imeindicator::services
