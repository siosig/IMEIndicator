#pragma once

#include "../models/ProcessPriorityRule.h"
#include "../services/SettingsManager.h"

#include <functional>
#include <vector>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::views {

// 設定ダイアログ。CreateWindowEx で動的にコントロールを配置する。
// インジケーター設定 + プロセス優先度ルール編集（ListView + 追加/編集/削除）+
// 管理者権限ステータス + ポーリング間隔を編集できる。
class SettingsDialog {
public:
    using AppliedCallback = std::function<void(const models::AppSettings&)>;
    // 管理者権限不足カウンタを取得（spec T095：常時表示判定材料）
    using AccessDeniedCountCallback = std::function<int()>;

    explicit SettingsDialog(services::SettingsManager& mgr);
    ~SettingsDialog();

    SettingsDialog(const SettingsDialog&) = delete;
    SettingsDialog& operator=(const SettingsDialog&) = delete;

    void setAppliedCallback(AppliedCallback cb) { appliedCallback_ = std::move(cb); }
    void setAccessDeniedCountCallback(AccessDeniedCountCallback cb)
    {
        accessDeniedCountCallback_ = std::move(cb);
    }

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

    // === ルール編集系 ===
    void refreshRuleListView();
    int  selectedRuleIndex() const;
    void onAddRule();
    void onEditRule();
    void onDeleteRule();
    void updateAdminStatusLabel();

    // ルール編集サブダイアログ（モーダル）。OK で true。
    static bool showRuleEditDialog(HWND owner, HINSTANCE hInstance,
                                   models::ProcessPriorityRule& rule);

    services::SettingsManager& mgr_;
    AppliedCallback appliedCallback_;
    AccessDeniedCountCallback accessDeniedCountCallback_;

    // ルール編集の作業コピー（OK/適用で settings へ反映）
    std::vector<models::ProcessPriorityRule> workingRules_;

    HWND hwnd_{nullptr};
    HINSTANCE hInstance_{nullptr};

    // インジケーター設定コントロール
    HWND hVisible_{nullptr};
    HWND hSize_{nullptr};
    HWND hOpacity_{nullptr};
    HWND hOffsetX_{nullptr};
    HWND hOffsetY_{nullptr};
    HWND hImeOn_{nullptr};
    HWND hImeOff_{nullptr};
    HWND hLogLevel_{nullptr};

    // プロセス優先度ルール
    HWND hRulesList_{nullptr};
    HWND hAddRule_{nullptr};
    HWND hEditRule_{nullptr};
    HWND hDeleteRule_{nullptr};
    HWND hPollingInterval_{nullptr};
    HWND hAdminStatus_{nullptr};

    // ボタン
    HWND hOk_{nullptr};
    HWND hCancel_{nullptr};
    HWND hApply_{nullptr};

    bool dialogClosed_{false};
};

} // namespace imeindicator::views
