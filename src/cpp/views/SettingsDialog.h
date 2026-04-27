#pragma once

#include "../services/SettingsManager.h"

#include <functional>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::views {

// 簡易設定ダイアログ。CreateWindowEx で動的にコントロールを配置するモーダル風実装。
// インジケーター設定（サイズ・不透明度・オフセット・IME ON/OFF テキスト・ログレベル・
// 表示切替）を編集できる。プロセス優先度ルールの編集は本 MVP では非対応で、
// settings.json の手動編集案内のみ表示する。
class SettingsDialog {
public:
    using AppliedCallback = std::function<void(const models::AppSettings&)>;

    explicit SettingsDialog(services::SettingsManager& mgr);
    ~SettingsDialog();

    SettingsDialog(const SettingsDialog&) = delete;
    SettingsDialog& operator=(const SettingsDialog&) = delete;

    // 適用時のコールバック（ホットリロード用）
    void setAppliedCallback(AppliedCallback cb) { appliedCallback_ = std::move(cb); }

    // ダイアログを開く（モードレスだが内部で独自メッセージループを回してモーダル風挙動）
    void show(HINSTANCE hInstance);

    bool isOpen() const noexcept { return hwnd_ != nullptr; }

private:
    static LRESULT CALLBACK wndProcStatic(HWND, UINT, WPARAM, LPARAM);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);

    void createControls(HWND parent, HINSTANCE hInstance);
    void loadFromSettings();
    bool readControlsToSettings(models::AppSettings& out) const;
    void onApply();
    void onOk();
    void onCancel();

    services::SettingsManager& mgr_;
    AppliedCallback appliedCallback_;

    HWND hwnd_{nullptr};
    HWND hSize_{nullptr};
    HWND hOpacity_{nullptr};
    HWND hOffsetX_{nullptr};
    HWND hOffsetY_{nullptr};
    HWND hImeOn_{nullptr};
    HWND hImeOff_{nullptr};
    HWND hVisible_{nullptr};
    HWND hLogLevel_{nullptr};
    HWND hRulesInfo_{nullptr};
    HWND hOk_{nullptr};
    HWND hCancel_{nullptr};
    HWND hApply_{nullptr};

    bool dialogClosed_{false};
};

} // namespace imeindicator::views
