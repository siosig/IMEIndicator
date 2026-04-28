cmake_minimum_required(VERSION 3.27)

# vcpkg toolchain（manifest mode）はプリセット側で指定するが、
# 直接 cmake を呼ぶケースに備えてここでも探索する。
if(NOT DEFINED CMAKE_TOOLCHAIN_FILE AND EXISTS "${CMAKE_SOURCE_DIR}/external/vcpkg/scripts/buildsystems/vcpkg.cmake")
    set(CMAKE_TOOLCHAIN_FILE "${CMAKE_SOURCE_DIR}/external/vcpkg/scripts/buildsystems/vcpkg.cmake"
        CACHE STRING "vcpkg toolchain")
endif()

project(IMEIndicator
    VERSION 1.4.0
    DESCRIPTION "IME Indicator with HotkeyP merge (C++ port + global hotkeys)"
    LANGUAGES CXX
)

# ---- 言語規格 ----
# C++23: HotkeyP モダン化版コード（services/hotkey/）が std::expected を多用しているため必須。
# MSVC v143（VS 2022 17.4+ / VS 2026 18 Community）で <expected> がサポートされる。
# 既存 009-port-to-cpp のコードは C++20 互換だが C++23 でも問題なくビルドできる（後方互換）。
set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# ---- MSVC ランタイム: スタティック /MT (SC-003 単一EXE 5MB以下) ----
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
    CACHE STRING "Static CRT" FORCE)

# ---- ターゲット OS / Win11 SDK ----
add_compile_definitions(
    _WIN32_WINNT=0x0A00      # Windows 10/11
    WINVER=0x0A00
    UNICODE _UNICODE          # Win32 API はワイド文字版固定
    NOMINMAX                  # std::min/std::max を Win32 マクロから守る
    WIN32_LEAN_AND_MEAN
)

# ---- 共通コンパイラ/リンカフラグ ----
# Release 最適化方針:
#   /O2 /Oi /Gy   : 標準的な速度優先最適化と関数 COMDAT。
#   /Ob3          : /O2 既定（Ob2）を超える積極的インライン展開（VS 2019 16.4+）。
#   /Gw           : グローバル const データを COMDAT 化し、未参照削除と ICF を効かせる。
#   /GL → /LTCG   : リンク時コード生成（プログラム全体最適化）。
#   /arch:AVX2    : 配布対象 CPU（Intel 13600k / Ryzen 7840U / 8845HS）が全て AVX2
#                   対応のため、SIMD 自動ベクトル化を最大化する。
#                   AVX2 非対応 CPU では起動時に Illegal instruction でクラッシュする。
#   /OPT:REF /OPT:ICF /INCREMENTAL:NO : 未参照シンボル削除・同一 COMDAT 折りたたみ。
if(MSVC)
    add_compile_options(
        /utf-8
        /W4
        /permissive-
        /Zc:__cplusplus
        /Zc:preprocessor
        /MP                    # 並列ビルド
        $<$<CONFIG:Release>:/O2>
        $<$<CONFIG:Release>:/Ob3>           # 積極的インライン展開（Ob2 を上書き）
        $<$<CONFIG:Release>:/Oi>
        $<$<CONFIG:Release>:/GL>            # LTCG
        $<$<CONFIG:Release>:/Gy>            # 関数COMDATで /OPT:REF を効かせる
        $<$<CONFIG:Release>:/Gw>            # グローバルデータCOMDATで /OPT:ICF を効かせる
        $<$<CONFIG:Release>:/arch:AVX2>     # AVX2 命令セット (要 CPU 対応)
        $<$<CONFIG:Debug>:/Od>
        $<$<CONFIG:Debug>:/Zi>
    )
    add_link_options(
        $<$<CONFIG:Release>:/LTCG>
        $<$<CONFIG:Release>:/OPT:REF>
        $<$<CONFIG:Release>:/OPT:ICF>
        $<$<CONFIG:Release>:/INCREMENTAL:NO>
    )
endif()

# ---- 出力ディレクトリ ----
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

# ---- テスト有効化 ----
enable_testing()
include(CTest)

# ---- サブディレクトリ ----
add_subdirectory(src/cpp)
add_subdirectory(tests/cpp)
