# ---- 依存パッケージ ----
find_package(GTest CONFIG REQUIRED)
find_package(nlohmann_json CONFIG REQUIRED)
find_package(spdlog CONFIG REQUIRED)
find_package(wil CONFIG REQUIRED)

include(GoogleTest)

# ---- IMEIndicatorTests ----
add_executable(IMEIndicatorTests)

target_sources(IMEIndicatorTests PRIVATE
    # Phase 2 ユニットテスト
    unit/LoggerTests.cpp
    unit/SettingsManagerTests.cpp
    unit/ProcessPriorityRuleTests.cpp
    unit/PowerModeBackupTests.cpp
    unit/JsonSchemaCompatTests.cpp

    # 被テストコード（IMEIndicator 本体と同じソースを直接組み込む。
    # 静的ライブラリにせずに重複ビルドする方針：MSVC の WIN32 アプリと
    # コンソール TEST EXE で /SUBSYSTEM が異なるため、本体ライブラリ化はせず）
    ${CMAKE_SOURCE_DIR}/src/cpp/win32/UnicodeUtil.cpp
    ${CMAKE_SOURCE_DIR}/src/cpp/win32/HwndUtil.cpp
    ${CMAKE_SOURCE_DIR}/src/cpp/win32/NativeConstants.cpp
    ${CMAKE_SOURCE_DIR}/src/cpp/models/PowerMode.cpp
    ${CMAKE_SOURCE_DIR}/src/cpp/models/ProcessPriorityRule.cpp
    ${CMAKE_SOURCE_DIR}/src/cpp/models/AppSettings.cpp
    ${CMAKE_SOURCE_DIR}/src/cpp/services/Logger.cpp
    ${CMAKE_SOURCE_DIR}/src/cpp/services/SettingsManager.cpp
    ${CMAKE_SOURCE_DIR}/src/cpp/services/PowerModeBackup.cpp
)

target_include_directories(IMEIndicatorTests PRIVATE
    ${CMAKE_SOURCE_DIR}/src/cpp
    ${CMAKE_CURRENT_SOURCE_DIR}
)

target_link_libraries(IMEIndicatorTests PRIVATE
    GTest::gtest
    GTest::gmock
    GTest::gtest_main
    nlohmann_json::nlohmann_json
    spdlog::spdlog
    WIL::WIL
)

target_link_libraries(IMEIndicatorTests PRIVATE
    kernel32
    user32
    ole32
    oleaut32
    shell32
    shcore
    powrprof
    psapi
)

# CTest 用にテストを自動登録
# WORKING_DIRECTORY を EXE のあるディレクトリに固定することで、テストコード側の
# `fs::current_path() / "fixtures"` が POST_BUILD でコピーされる fixtures/ を
# 確実に解決できるようにする（解決失敗時は GTEST_SKIP となるため無視されない）。
gtest_discover_tests(IMEIndicatorTests
    DISCOVERY_TIMEOUT 30
    PROPERTIES TIMEOUT 60
    WORKING_DIRECTORY $<TARGET_FILE_DIR:IMEIndicatorTests>
)

# fixtures をビルドディレクトリへコピー
add_custom_command(TARGET IMEIndicatorTests POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        ${CMAKE_CURRENT_SOURCE_DIR}/fixtures
        $<TARGET_FILE_DIR:IMEIndicatorTests>/fixtures
)
