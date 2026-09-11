// IMEIndicator リソース ID 定義。
// rc.exe（app.rc）と C++（views/BackgroundImageWindow.cpp 等）の両方から include する。
// rc.exe のプリプロセッサは pragma once を扱えないため、インクルードガードを使う。
//
// 注意: このファイルは UTF-8（BOM なし）。rc.exe は既定でシステム ANSI コードページで
// 読むため、日本語コメントがあるとガードのプリプロセッサ指令を取りこぼす。
// src/cpp/CMakeLists.txt で app.rc に /c65001 を指定して回避している。
#ifndef IMEINDICATOR_RESOURCE_IDS_H
#define IMEINDICATOR_RESOURCE_IDS_H

// 013-ime-corner-image: 画面右上 IME ON 背景画像。
// RT_RCDATA として ime-on-background.png（512x512 RGBA PNG、四隅は透過）を埋め込む。
// 読み出しは services/WicImageLoader::lockRcData(hInstance, IDR_BACKGROUND_IMAGE_PNG)。
#define IDR_BACKGROUND_IMAGE_PNG 101

#endif // IMEINDICATOR_RESOURCE_IDS_H
