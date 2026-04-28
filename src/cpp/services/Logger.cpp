#include "Logger.h"

#include "../app/AppConstants.h"
#include "../win32/UnicodeUtil.h"

#include <array>
#include <atomic>
#include <chrono>
#include <mutex>

#include <spdlog/async.h>
#include <spdlog/sinks/rotating_file_sink.h>
#ifdef _DEBUG
#  include <spdlog/sinks/msvc_sink.h>
#endif
#include <spdlog/sinks/sink.h>

namespace imeindicator::services {
namespace {

constexpr size_t kMaxLogSizeBytes = 5 * 1024 * 1024;  // 5MB
constexpr size_t kMaxLogFiles     = 5;                // 5 世代
constexpr size_t kAsyncQueueSize  = 8192;
constexpr size_t kAsyncWorkerCount = 1;

std::atomic_bool g_initialized{false};
std::mutex g_initMutex;

// 全カテゴリ名（contracts/log-file-contract.md §ロガー）
constexpr std::array<std::string_view, 12> kLoggerNames = {
    app::AppConstants::LoggerApp,
    app::AppConstants::LoggerIme,
    app::AppConstants::LoggerPixel,
    app::AppConstants::LoggerPriority,
    app::AppConstants::LoggerPower,
    app::AppConstants::LoggerDisplay,
    app::AppConstants::LoggerTray,
    app::AppConstants::LoggerSettings,
    // 010-hotkeyp-merge で追加されたカテゴリ
    app::AppConstants::LoggerHotkey,
    app::AppConstants::LoggerHook,
    app::AppConstants::LoggerCommand,
    app::AppConstants::LoggerMacro,
};

constexpr const char* kLogPattern = "[%Y-%m-%d %H:%M:%S.%e] [%^%l%$] [%t] [%n] %v";

} // namespace

spdlog::level::level_enum Logger::toSpdlogLevel(models::LogLevel level) noexcept
{
    switch (level) {
        case models::LogLevel::Trace:    return spdlog::level::trace;
        case models::LogLevel::Debug:    return spdlog::level::debug;
        case models::LogLevel::Info:     return spdlog::level::info;
        case models::LogLevel::Warn:     return spdlog::level::warn;
        case models::LogLevel::Error:    return spdlog::level::err;
        case models::LogLevel::Critical: return spdlog::level::critical;
    }
    return spdlog::level::warn;
}

bool Logger::isInitialized() noexcept
{
    return g_initialized.load(std::memory_order_acquire);
}

void Logger::init(const std::filesystem::path& logsDirectory,
                  models::LogLevel initialLevel,
                  bool enableMsvcSinkInDebug)
{
    std::lock_guard lk(g_initMutex);
    if (g_initialized.load(std::memory_order_relaxed)) {
        return;
    }

    std::error_code ec;
    std::filesystem::create_directories(logsDirectory, ec);

    const auto logFilePath = logsDirectory /
        std::filesystem::path(app::AppConstants::LogFileName);

    // 非同期ログ用スレッドプール
    spdlog::init_thread_pool(kAsyncQueueSize, kAsyncWorkerCount);

    // spdlog の filename_t は既定で std::string（vcpkg 標準ビルド）。
    // パスを UTF-8 化して渡す。本番環境のユーザー名が ASCII のみであれば問題ないが、
    // 非 ASCII ユーザー名対応が必要な場合は vcpkg で spdlog[wchar-filenames] を有効化する。
    const std::string logFileStr = win32::wideToUtf8(logFilePath.wstring());

    // ローテーション + (Debug 時) msvc_sink
    auto rotating = std::make_shared<spdlog::sinks::rotating_file_sink_mt>(
        logFileStr, kMaxLogSizeBytes, kMaxLogFiles);

    std::vector<spdlog::sink_ptr> sinks;
    sinks.push_back(rotating);
#ifdef _DEBUG
    if (enableMsvcSinkInDebug) {
        sinks.push_back(std::make_shared<spdlog::sinks::msvc_sink_mt>());
    }
#else
    (void)enableMsvcSinkInDebug;
#endif

    const auto level = toSpdlogLevel(initialLevel);
    for (const auto name : kLoggerNames) {
        const std::string strName(name);
        auto logger = std::make_shared<spdlog::async_logger>(
            strName, sinks.begin(), sinks.end(),
            spdlog::thread_pool(),
            spdlog::async_overflow_policy::overrun_oldest);
        logger->set_pattern(kLogPattern);
        logger->set_level(level);
        // warn 以上は即時 flush して、クラッシュ・強制終了でもエラーログを失わない。
        // info/debug/trace は flush_every(3s) でまとめて書き出す。
        logger->flush_on(spdlog::level::warn);
        spdlog::register_logger(logger);
    }

    spdlog::flush_every(std::chrono::seconds(3));
    spdlog::set_level(level);

    g_initialized.store(true, std::memory_order_release);
}

void Logger::setGlobalLevel(models::LogLevel level)
{
    if (!g_initialized.load(std::memory_order_acquire)) return;

    const auto target = toSpdlogLevel(level);
    spdlog::set_level(target);
    spdlog::apply_all([target](std::shared_ptr<spdlog::logger> l) {
        l->set_level(target);
    });
}

void Logger::shutdown()
{
    if (!g_initialized.load(std::memory_order_acquire)) return;

    spdlog::shutdown();
    g_initialized.store(false, std::memory_order_release);
}

} // namespace imeindicator::services
