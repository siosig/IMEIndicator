#pragma once

#include "../models/ProcessPriorityRule.h"
#include "../models/hotkey/HotKeyEntry.h"
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
    // 各ルールが管理者権限不足で制御不能か否かを判定するコールバック。
    // 戻り値は workingRules_ と同サイズの bool 配列（true = 制御不能 = 薄ピンク表示）。
    using AccessibilityProbeCallback =
        std::function<std::vector<bool>(const std::vector<models::ProcessPriorityRule>&)>;

    explicit SettingsDialog(services::SettingsManager& mgr);
    ~SettingsDialog();

    SettingsDialog(const SettingsDialog&) = delete;
    SettingsDialog& operator=(const SettingsDialog&) = delete;

    void setAppliedCallback(AppliedCallback cb) { appliedCallback_ = std::move(cb); }
    void setAccessDeniedCountCallback(AccessDeniedCountCallback cb)
    {
        accessDeniedCountCallback_ = std::move(cb);
    }
    void setAccessibilityProbeCallback(AccessibilityProbeCallback cb)
    {
        accessibilityProbeCallback_ = std::move(cb);
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

    // === ホットキー編集系（Phase 3 / US1 / 010-hotkeyp-merge） ===
    void refreshHotkeyListView();
    int  selectedHotkeyIndex() const;
    void onAddHotkey();
    void onEditHotkey();
    void onDeleteHotkey();

    // ホットキー編集サブダイアログ（モーダル）。OK で true。
    static bool showHotkeyEditDialog(HWND owner, HINSTANCE hInstance,
                                     models::hotkey::HotKeyEntry& entry);

    services::SettingsManager& mgr_;
    AppliedCallback appliedCallback_;
    AccessDeniedCountCallback accessDeniedCountCallback_;
    AccessibilityProbeCallback accessibilityProbeCallback_;

    // ルール編集の作業コピー（OK/適用で settings へ反映）
    std::vector<models::ProcessPriorityRule> workingRules_;
    // workingRules_ と同サイズ。true = 管理者権限不足で制御不能（薄ピンク表示）。
    std::vector<bool> ruleAccessBlocked_;

    // ホットキー編集の作業コピー（OK/適用で settings.hotkeySettings へ反映）
    std::vector<models::hotkey::HotKeyEntry> workingHotkeys_;

    HWND hwnd_{nullptr};
    HINSTANCE hInstance_{nullptr};

    // インジケーター設定コントロール
    HWND hVisible_{nullptr};
    // 013-ime-corner-image: 「インジケーター表示」と同じ行の右隣に並ぶ
    // 「背景画像表示」チェックボックス（backgroundImage.isVisible / FR-001）
    HWND hBackgroundImage_{nullptr};
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

    // ホットキー（010-hotkeyp-merge / Phase 3）
    HWND hHotkeyList_{nullptr};
    HWND hAddHotkey_{nullptr};
    HWND hEditHotkey_{nullptr};
    HWND hDeleteHotkey_{nullptr};

    // ボタン
    HWND hOk_{nullptr};
    HWND hCancel_{nullptr};
    HWND hApply_{nullptr};

    bool dialogClosed_{false};
};

} // namespace imeindicator::views
