# ---- 依存パッケージ ----
find_package(nlohmann_json CONFIG REQUIRED)
find_package(spdlog CONFIG REQUIRED)
find_package(wil CONFIG REQUIRED)

# ---- IMEIndicator (本体) ----
add_executable(IMEIndicator WIN32)

target_sources(IMEIndicator PRIVATE
    # エントリ・アプリ骨格
    app/main.cpp
    app/App.cpp
    app/EntryPoint_PowerToggle.cpp

    # win32 ヘルパー
    win32/NativeConstants.cpp
    win32/UnicodeUtil.cpp
    win32/HwndUtil.cpp

    # モデル
    models/PowerMode.cpp
    models/ProcessPriorityRule.cpp
    models/AppSettings.cpp

    # モデル: HotkeyP マージ (010-hotkeyp-merge)
    models/hotkey/HotKeyEntry.cpp
    models/hotkey/HotkeyCategory.cpp
    models/hotkey/HotkeyGlobalOptions.cpp
    models/hotkey/HotkeySettings.cpp

    # サービス（Phase 2 範囲）
    services/Logger.cpp
    services/SettingsManager.cpp
    services/PowerModeBackup.cpp

    # サービス（Phase 3 / US1 描画支援）
    services/ColorHelper.cpp
    services/PowerModeService.cpp
    services/DisplayHelper.cpp

    # サービス（Phase 3 / US1 入力監視）
    services/MouseTracker.cpp
    services/KeyboardHook.cpp
    services/WinEventHook.cpp

    # サービス（Phase 3 / US1 IME 検出）
    services/IMEDetector.cpp
    services/PixelIMEDetector.cpp
    services/IMEMonitor.cpp

    # サービス（Phase 5 / US3 プロセス優先度・電源モード制御）
    services/ECoreCpuInfo.cpp
    services/ProcessPriorityService.cpp
    services/RuleRuntimeState.cpp
    services/ProcessPriorityMonitor.cpp

    # ビュー（Phase 3 / US1 描画ウィンドウ）
    views/MouseCursorIndicatorWindow.cpp

    # ビュー（Phase 3 / US1 トレイアイコン）
    views/TrayIcon.cpp

    # ビュー（Phase 4 / US2 設定ダイアログ）
    views/SettingsDialog.cpp

    # リソース
    resources/app.rc
)

target_include_directories(IMEIndicator PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/resources
)

# Visual Studio Generator のリソースコンパイラ (rc.exe) に
# version.h を見つけさせる。
# CMake 4.x + VS Generator では COMPILE_FLAGS の文字列パースが不安定なため、
# COMPILE_OPTIONS のリスト形式で渡し、また target_compile_definitions で
# rc.exe にバージョンマクロを直接注入する二重対策を採る。
set_source_files_properties(resources/app.rc PROPERTIES
    COMPILE_OPTIONS "/I${CMAKE_CURRENT_SOURCE_DIR}/resources")

# rc.exe が version.h の解決に失敗しても動くよう、CMake の project(VERSION) を
# 直接マクロ定義として渡す。version.h と一致するように維持すること（FR-009）。
target_compile_definitions(IMEIndicator PRIVATE
    IMEINDICATOR_VERSION_MAJOR=${PROJECT_VERSION_MAJOR}
    IMEINDICATOR_VERSION_MINOR=${PROJECT_VERSION_MINOR}
    IMEINDICATOR_VERSION_PATCH=${PROJECT_VERSION_PATCH}
    IMEINDICATOR_VERSION_BUILD=0
)

target_link_libraries(IMEIndicator PRIVATE
    nlohmann_json::nlohmann_json
    spdlog::spdlog
    WIL::WIL
)

# MSBuild 既定の <GenerateManifest>true</GenerateManifest> による自動マニフェスト生成は
# app.rc の RT_MANIFEST 埋め込み（PerMonitorV2 + comctl32 v6 を含む完全制御版）と衝突し、
# `CVT1100: 重複するリソースです type:MANIFEST` を引き起こす。リンカ側の自動マニフェスト
# 出力を OFF にしてリソース埋め込み一本化する。
# 参考: https://learn.microsoft.com/cpp/build/reference/manifest-create-side-by-side-assembly-manifest
target_link_options(IMEIndicator PRIVATE "/MANIFEST:NO")

# Win32 / Direct2D / DirectComposition / TSF / IME / Power 関連
target_link_libraries(IMEIndicator PRIVATE
    kernel32
    user32
    ole32
    oleaut32
    shell32
    shcore       # SetProcessDpiAwareness
    comctl32
    imm32
    powrprof
    gdi32
    psapi
    # Direct2D 系（Phase 3 で実装が乗る）
    d2d1
    dwrite
    dcomp
    # UI Automation（Phase 3 / PixelIMEDetector）
    uiautomationcore
)

set_target_properties(IMEIndicator PROPERTIES
    OUTPUT_NAME "IMEIndicator"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)
