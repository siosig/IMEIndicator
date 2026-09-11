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

    # サービス: HotkeyP コア (010-hotkeyp-merge)
    services/hotkey/HookEngine.cpp
    services/hotkey/HotkeyManager.cpp
    services/hotkey/InputRouter.cpp
    services/hotkey/CommandExecutor.cpp
    services/hotkey/HotkeyService.cpp

    # サービス: HotkeyP コマンド (010-hotkeyp-merge)
    services/hotkey/commands/DisplayCommands.cpp
    services/hotkey/commands/MediaCommands.cpp
    services/hotkey/commands/MouseCommands.cpp
    services/hotkey/commands/PowerCommands.cpp
    services/hotkey/commands/ProcessCommands.cpp
    services/hotkey/commands/SystemCommands.cpp
    services/hotkey/commands/TextCommands.cpp
    services/hotkey/commands/VolumeCommands.cpp
    services/hotkey/commands/WindowCommands.cpp
    services/hotkey/commands/ImeIndicatorCommands.cpp

    # サービス: HotkeyP プラットフォーム (010-hotkeyp-merge)
    services/hotkey/platform/Accessibility.cpp
    services/hotkey/platform/VirtualDesktop.cpp

    # サービス（Phase 2 範囲）
    services/Logger.cpp
    services/SettingsManager.cpp
    services/PowerModeBackup.cpp

    # サービス（Phase 3 / US1 描画支援）
    services/ColorHelper.cpp
    services/PowerModeService.cpp
    services/DisplayHelper.cpp

    # サービス（013-ime-corner-image 背景画像: 幾何計算・WIC デコード）
    services/BackgroundImageLayout.cpp
    services/WicImageLoader.cpp

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

    # ビュー（013-ime-corner-image: 画面右上 IME ON 背景画像ウィンドウ）
    views/BackgroundImageWindow.cpp

    # リソース
    resources/app.rc
)

target_include_directories(IMEIndicator PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/resources
)

# Visual Studio Generator のリソースコンパイラ (rc.exe) の設定。
#
# /I  : version.h / resource_ids.h を見つけさせる。
#       CMake 4.x + VS Generator では COMPILE_FLAGS の文字列パースが不安定なため、
#       COMPILE_OPTIONS のリスト形式で渡す。
# /c65001 : **必須**。rc.exe は既定でシステム ANSI コードページ（日本語環境では CP932）で
#       ソースを読むため、UTF-8 の日本語コメントを含むヘッダ（version.h / resource_ids.h）が
#       文字化けする。CP932 では 2 バイト文字のリードバイトが直後の改行を飲み込むため、
#       コメント行の次の行にあるプリプロセッサ指令が失われ、
#       `fatal error RC1020: unexpected '#endif'` や #define の取りこぼしが発生する。
#       コードページを UTF-8 (65001) に指定することで正しく解釈される。
#       参考: https://learn.microsoft.com/windows/win32/menurc/using-rc-the-rc-command-line
set_source_files_properties(resources/app.rc PROPERTIES
    COMPILE_OPTIONS "/I${CMAKE_CURRENT_SOURCE_DIR}/resources;/c65001")

# 旧来ここで IMEINDICATOR_VERSION_* を target_compile_definitions で二重定義していたが、
# 上記 /c65001 により rc.exe が version.h を正しく解釈できるようになったため撤去した
# （013-ime-corner-image）。二重定義は version.h の値と乖離しやすく、実際に
# version.h が 1.4.0.9 の時点でも BUILD=7 が埋め込まれる不整合を招いていた。
# バージョンの正は src/cpp/resources/version.h のみとする（FR-009）。

target_link_libraries(IMEIndicator PRIVATE
    nlohmann_json::nlohmann_json
    spdlog::spdlog
    WIL::WIL
)

# HotkeyP マージ (010-hotkeyp-merge) で追加される Win32 ライブラリ
# - propsys: PROPVARIANT（Core Audio エンドポイント名取得）
# - avrt: AvSetMmThreadCharacteristics（メディアスレッド優先度、HotkeyP 由来）
target_link_libraries(IMEIndicator PRIVATE
    propsys
    avrt
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
    # WIC（Windows Imaging Component）: 背景画像 PNG のデコード・縮小（013-ime-corner-image）
    windowscodecs
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

# ---- コード署名（任意・既定オフ） ----
#
# Windows 11 の Smart App Control (SAC) は、署名が無いか信頼できない実行ファイルを
# ブロックする。ローカルビルドはリビルドのたびにハッシュが変わるため、クラウドの
# 評価が付かず繰り返しブロックされる。
#
# 重要（調査結果）:
#   - 自己署名証明書では SAC は解除されない。証明書は Microsoft Trusted Root Program に
#     含まれる CA が発行したものか、Microsoft Trusted Signing である必要がある。
#   - SAC の署名検証は RSA のみ。ECC 証明書は現時点で非対応。
#   参考: https://learn.microsoft.com/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control
#
# 使い方（証明書を用意したあと、構成時に指定する）:
#   証明書ストアの拇印を使う場合:
#     cmake --preset windows-x64-release -DIMEINDICATOR_SIGN=ON \
#           -DIMEINDICATOR_SIGN_THUMBPRINT=<40桁の拇印>
#   PFX ファイルを使う場合:
#     cmake --preset windows-x64-release -DIMEINDICATOR_SIGN=ON \
#           -DIMEINDICATOR_SIGN_PFX=C:/path/to/cert.pfx -DIMEINDICATOR_SIGN_PFX_PASSWORD=<pw>
#   Microsoft Trusted Signing（署名代行 dlib）を使う場合:
#     cmake --preset windows-x64-release -DIMEINDICATOR_SIGN=ON \
#           -DIMEINDICATOR_SIGN_DLIB=<Azure.CodeSigning.Dlib.dll> \
#           -DIMEINDICATOR_SIGN_DLIB_METADATA=<metadata.json>
#
# 既定（OFF）では何も追加しないため、証明書が無い環境でもビルドはそのまま通る。
option(IMEINDICATOR_SIGN "ビルド後に signtool で実行ファイルを署名する" OFF)
set(IMEINDICATOR_SIGN_THUMBPRINT ""  CACHE STRING "署名に使う証明書の拇印（証明書ストア内）")
set(IMEINDICATOR_SIGN_PFX ""         CACHE FILEPATH "署名に使う PFX ファイル")
set(IMEINDICATOR_SIGN_PFX_PASSWORD ""CACHE STRING "PFX のパスワード")
set(IMEINDICATOR_SIGN_DLIB ""        CACHE FILEPATH "Trusted Signing の署名代行 DLL")
set(IMEINDICATOR_SIGN_DLIB_METADATA ""CACHE FILEPATH "Trusted Signing のメタデータ JSON")
# タイムスタンプは必須。付けないと証明書の有効期限切れと同時に署名も無効になる。
set(IMEINDICATOR_SIGN_TIMESTAMP_URL "http://timestamp.digicert.com"
    CACHE STRING "RFC3161 タイムスタンプサーバー")

if(IMEINDICATOR_SIGN)
    # signtool.exe を Windows SDK から探す（最新バージョンを採用）。
    if(NOT IMEINDICATOR_SIGNTOOL)
        file(GLOB _imeindicator_signtool_candidates
             "$ENV{ProgramFiles\(x86\)}/Windows Kits/10/bin/*/x64/signtool.exe")
        if(_imeindicator_signtool_candidates)
            list(SORT _imeindicator_signtool_candidates)
            list(REVERSE _imeindicator_signtool_candidates)
            list(GET _imeindicator_signtool_candidates 0 _imeindicator_signtool)
            set(IMEINDICATOR_SIGNTOOL "${_imeindicator_signtool}"
                CACHE FILEPATH "signtool.exe のパス")
        endif()
    endif()
    if(NOT IMEINDICATOR_SIGNTOOL)
        message(FATAL_ERROR
            "IMEINDICATOR_SIGN=ON ですが signtool.exe が見つかりません。"
            " Windows SDK を入れるか -DIMEINDICATOR_SIGNTOOL=<path> で指定してください。")
    endif()

    # 署名方式を 1 つに決める（拇印 / PFX / Trusted Signing dlib）。
    if(IMEINDICATOR_SIGN_DLIB)
        if(NOT IMEINDICATOR_SIGN_DLIB_METADATA)
            message(FATAL_ERROR "IMEINDICATOR_SIGN_DLIB には IMEINDICATOR_SIGN_DLIB_METADATA が必要です。")
        endif()
        set(_imeindicator_sign_cred
            /dlib "${IMEINDICATOR_SIGN_DLIB}" /dmdf "${IMEINDICATOR_SIGN_DLIB_METADATA}")
    elseif(IMEINDICATOR_SIGN_PFX)
        set(_imeindicator_sign_cred /f "${IMEINDICATOR_SIGN_PFX}")
        if(IMEINDICATOR_SIGN_PFX_PASSWORD)
            list(APPEND _imeindicator_sign_cred /p "${IMEINDICATOR_SIGN_PFX_PASSWORD}")
        endif()
    elseif(IMEINDICATOR_SIGN_THUMBPRINT)
        set(_imeindicator_sign_cred /sha1 "${IMEINDICATOR_SIGN_THUMBPRINT}")
    else()
        message(FATAL_ERROR
            "IMEINDICATOR_SIGN=ON ですが証明書が指定されていません。"
            " IMEINDICATOR_SIGN_THUMBPRINT / IMEINDICATOR_SIGN_PFX /"
            " IMEINDICATOR_SIGN_DLIB のいずれかを指定してください。")
    endif()

    # /fd sha256 /td sha256: SAC が要求するダイジェスト。/tr: RFC3161 タイムスタンプ。
    add_custom_command(TARGET IMEIndicator POST_BUILD
        COMMAND "${IMEINDICATOR_SIGNTOOL}" sign
                ${_imeindicator_sign_cred}
                /fd sha256
                /tr "${IMEINDICATOR_SIGN_TIMESTAMP_URL}" /td sha256
                "$<TARGET_FILE:IMEIndicator>"
        COMMAND "${IMEINDICATOR_SIGNTOOL}" verify /pa "$<TARGET_FILE:IMEIndicator>"
        COMMENT "IMEIndicator.exe を署名・検証しています"
        VERBATIM)
    message(STATUS "コード署名: 有効（signtool = ${IMEINDICATOR_SIGNTOOL}）")
endif()
