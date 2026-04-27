#pragma once

#include "../models/MonitorInfo.h"

#include <vector>

namespace imeindicator::services {

// マルチディスプレイ関連のヘルパー（既存 [DisplayHelper.cs] と同じ API セマンティクス）。
// 内部で EnumDisplayMonitors + GetMonitorInfoW + GetDpiForMonitor を呼ぶ。
// 呼び出しごとに列挙し直すため、頻繁な呼び出しは避ける（必要に応じて呼び出し側でキャッシュ）。
struct DisplayHelper {
    // 全モニター情報を取得（プライマリも含む）。
    // EnumDisplayMonitors の列挙順をそのまま保持する。
    static std::vector<models::MonitorInfo> getAllMonitors();

    // モニター数。
    static size_t getScreenCount();

    // 指定 index のモニター矩形（仮想スクリーン座標、左上原点）。
    // index 範囲外なら全 0 を返す。
    struct ScreenBounds { LONG left{}; LONG top{}; LONG width{}; LONG height{}; };
    static ScreenBounds getScreenBounds(int index);

    // 矩形 (x,y,width,height) の中心が属するモニターインデックス。
    // どのモニターにも含まれない場合は最近傍を返す。モニターが 1 つも無い場合は -1。
    static int getDisplayIndexFromPosition(double x, double y, double width, double height);

    // 有効なディスプレイインデックスか。
    static bool isValidDisplayIndex(int displayIndex);

    // 座標 (x,y) が任意のモニター矩形に含まれるか。
    static bool isPositionOnAnyDisplay(double x, double y);

    // プライマリモニターのワーク領域（タスクバー除外、物理ピクセル）。
    // プライマリが見つからない場合は最初のモニターを返す。
    struct WorkArea { LONG left{}; LONG top{}; LONG right{}; LONG bottom{}; };
    static WorkArea getPrimaryWorkArea();

    // 座標がディスプレイ外ならフォールバック位置を返す。
    // useTopRight=false ならワーク領域左上、true なら右上に配置。
    struct ValidPosition { double x{}; double y{}; int displayIndex{}; };
    static ValidPosition getValidPosition(double x, double y, double width, double height,
                                          int preferredDisplayIndex, bool useTopRight = false);
};

} // namespace imeindicator::services
