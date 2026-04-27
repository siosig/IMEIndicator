#include "SettingsDialog.h"

#include "../app/AppConstants.h"
#include "../win32/UnicodeUtil.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <commctrl.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cwchar>
#include <string>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "comctl32.lib")

namespace imeindicator::views {

namespace {

constexpr wchar_t kClassName[] = L"IMEIndicator_SettingsDialog";
constexpr wchar_t kRuleEditClassName[] = L"IMEIndicator_RuleEditDialog";

// メイン側コントロール ID
enum CtrlId : int {
    ID_VISIBLE = 1001,
    ID_SIZE,
    ID_OPACITY,
    ID_OFFSETX,
    ID_OFFSETY,
    ID_IMEON,
    ID_IMEOFF,
    ID_LOGLEVEL,
    ID_RULES_LIST,
    ID_RULE_ADD,
    ID_RULE_EDIT,
    ID_RULE_DELETE,
    ID_POLLING,
    ID_OK = 2001,
    ID_CANCEL,
    ID_APPLY,
};

// 編集サブダイアログ側コントロール ID
enum EditId : int {
    EDIT_NAME = 3001,
    EDIT_PRIORITY,
    EDIT_ENABLED,
    EDIT_ECORE,
    EDIT_BACKOFF,
    EDIT_OK,
    EDIT_CANCEL,
};

constexpr int kRowH = 28;
constexpr int kLabelW = 150;
constexpr int kCtrlW = 240;
constexpr int kPad = 10;

HFONT createUiFont()
{
    NONCLIENTMETRICSW ncm{};
    ncm.cbSize = sizeof(ncm);
    if (::SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(ncm), &ncm, 0)) {
        return ::CreateFontIndirectW(&ncm.lfMessageFont);
    }
    return reinterpret_cast<HFONT>(::GetStockObject(DEFAULT_GUI_FONT));
}

void applyFontRecursive(HWND hwnd, HFONT font)
{
    ::EnumChildWindows(hwnd, [](HWND child, LPARAM lp) -> BOOL {
        ::SendMessageW(child, WM_SETFONT,
                       reinterpret_cast<WPARAM>(reinterpret_cast<HFONT>(lp)),
                       TRUE);
        return TRUE;
    }, reinterpret_cast<LPARAM>(font));
}

HWND addLabel(HWND parent, HINSTANCE hInst, int x, int y, int w, const wchar_t* text)
{
    return ::CreateWindowExW(0, L"STATIC", text,
                              WS_CHILD | WS_VISIBLE | SS_LEFT,
                              x, y + 4, w, 20, parent, nullptr, hInst, nullptr);
}

HWND addEdit(HWND parent, HINSTANCE hInst, int x, int y, int w, int h, int id,
             DWORD extraStyle = 0)
{
    return ::CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"",
                              WS_CHILD | WS_VISIBLE | WS_TABSTOP | extraStyle,
                              x, y, w, h, parent,
                              reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                              hInst, nullptr);
}

HWND addCheck(HWND parent, HINSTANCE hInst, int x, int y, int w,
              int id, const wchar_t* text)
{
    return ::CreateWindowExW(0, L"BUTTON", text,
                              WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_AUTOCHECKBOX,
                              x, y, w, 22, parent,
                              reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                              hInst, nullptr);
}

HWND addCombo(HWND parent, HINSTANCE hInst, int x, int y, int w, int id)
{
    return ::CreateWindowExW(0, L"COMBOBOX", L"",
                              WS_CHILD | WS_VISIBLE | WS_TABSTOP |
                              CBS_DROPDOWNLIST | WS_VSCROLL,
                              x, y, w, 200, parent,
                              reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                              hInst, nullptr);
}

HWND addButton(HWND parent, HINSTANCE hInst, int x, int y, int w,
               int id, const wchar_t* text, bool def = false)
{
    DWORD style = WS_CHILD | WS_VISIBLE | WS_TABSTOP;
    style |= def ? BS_DEFPUSHBUTTON : BS_PUSHBUTTON;
    return ::CreateWindowExW(0, L"BUTTON", text, style,
                              x, y, w, 28, parent,
                              reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                              hInst, nullptr);
}

void setEditDouble(HWND h, double v, int decimals = 1)
{
    wchar_t buf[64];
    if (decimals <= 0) {
        ::swprintf_s(buf, L"%lld", static_cast<long long>(std::llround(v)));
    } else {
        ::swprintf_s(buf, L"%.*f", decimals, v);
    }
    ::SetWindowTextW(h, buf);
}

double getEditDouble(HWND h, double fallback)
{
    wchar_t buf[64];
    ::GetWindowTextW(h, buf, ARRAYSIZE(buf));
    wchar_t* end = nullptr;
    double v = ::wcstod(buf, &end);
    if (end == buf) return fallback;
    return v;
}

int getEditInt(HWND h, int fallback)
{
    wchar_t buf[64];
    ::GetWindowTextW(h, buf, ARRAYSIZE(buf));
    wchar_t* end = nullptr;
    long v = ::wcstol(buf, &end, 10);
    if (end == buf) return fallback;
    return static_cast<int>(v);
}

std::wstring getEditText(HWND h)
{
    int len = ::GetWindowTextLengthW(h);
    std::wstring s(len, L'\0');
    if (len > 0) {
        ::GetWindowTextW(h, s.data(), len + 1);
    }
    return s;
}

const wchar_t* priorityLabel(models::PriorityLevel p) noexcept
{
    switch (p) {
        case models::PriorityLevel::Idle:        return L"Idle";
        case models::PriorityLevel::BelowNormal: return L"BelowNormal";
        case models::PriorityLevel::Normal:      return L"Normal";
        case models::PriorityLevel::AboveNormal: return L"AboveNormal";
        case models::PriorityLevel::High:        return L"High";
        case models::PriorityLevel::Realtime:    return L"Realtime";
    }
    return L"Normal";
}

} // namespace

// =====================================================================
// ルール編集サブダイアログ（モーダル風、メッセージループは内部で回す）
// =====================================================================

namespace {

struct RuleEditContext {
    models::ProcessPriorityRule rule;
    bool ok{false};
    bool closed{false};
    HWND hName{nullptr};
    HWND hPriority{nullptr};
    HWND hEnabled{nullptr};
    HWND hECore{nullptr};
    HWND hBackoff{nullptr};
    HWND hOk{nullptr};
    HWND hCancel{nullptr};
};

LRESULT CALLBACK ruleEditWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_NCCREATE) {
        auto* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        ::SetWindowLongPtrW(hwnd, GWLP_USERDATA,
                            reinterpret_cast<LONG_PTR>(cs->lpCreateParams));
        return ::DefWindowProcW(hwnd, msg, wp, lp);
    }
    auto* ctx = reinterpret_cast<RuleEditContext*>(
        ::GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (!ctx) return ::DefWindowProcW(hwnd, msg, wp, lp);

    auto closeWith = [&](bool ok) {
        if (ok) {
            ctx->rule.processName = getEditText(ctx->hName);
            LRESULT pi = ::SendMessageW(ctx->hPriority, CB_GETCURSEL, 0, 0);
            if (pi >= 0 && pi <= static_cast<LRESULT>(models::PriorityLevel::Realtime)) {
                ctx->rule.targetPriority = static_cast<models::PriorityLevel>(pi);
            }
            ctx->rule.isEnabled =
                (::SendMessageW(ctx->hEnabled, BM_GETCHECK, 0, 0) == BST_CHECKED);
            ctx->rule.useECoreOnly =
                (::SendMessageW(ctx->hECore, BM_GETCHECK, 0, 0) == BST_CHECKED);
            ctx->rule.maxBackoffExponent =
                std::clamp(getEditInt(ctx->hBackoff, ctx->rule.maxBackoffExponent), 0, 10);
        }
        ctx->ok = ok;
        ctx->closed = true;
        ::PostMessageW(hwnd, WM_NULL, 0, 0);
    };

    switch (msg) {
        case WM_COMMAND:
            switch (LOWORD(wp)) {
                case EDIT_OK:     closeWith(true);  return 0;
                case EDIT_CANCEL: closeWith(false); return 0;
            }
            break;
        case WM_CLOSE:
            closeWith(false);
            return 0;
    }
    return ::DefWindowProcW(hwnd, msg, wp, lp);
}

} // namespace

bool SettingsDialog::showRuleEditDialog(HWND owner, HINSTANCE hInstance,
                                        models::ProcessPriorityRule& rule)
{
    static bool s_classRegistered = false;
    if (!s_classRegistered) {
        WNDCLASSEXW wc{};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = &ruleEditWndProc;
        wc.hInstance = hInstance;
        wc.hCursor = ::LoadCursorW(nullptr, IDC_ARROW);
        wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_BTNFACE + 1);
        wc.lpszClassName = kRuleEditClassName;
        ::RegisterClassExW(&wc);
        s_classRegistered = true;
    }

    RuleEditContext ctx{};
    ctx.rule = rule;

    constexpr int kW = 360;
    constexpr int kH = 280;
    RECT rcOwner{};
    if (owner) ::GetWindowRect(owner, &rcOwner);
    int x = (rcOwner.left + rcOwner.right - kW) / 2;
    int y = (rcOwner.top + rcOwner.bottom - kH) / 2;
    if (x < 0) x = 100;
    if (y < 0) y = 100;

    HWND hwnd = ::CreateWindowExW(
        WS_EX_DLGMODALFRAME, kRuleEditClassName, L"プロセス優先度ルール",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU,
        x, y, kW, kH, owner, nullptr, hInstance, &ctx);
    if (!hwnd) return false;

    int yy = kPad;
    addLabel(hwnd, hInstance, kPad, yy, 100, L"プロセス名");
    ctx.hName = addEdit(hwnd, hInstance, kPad + 110, yy, 220, 22, EDIT_NAME);
    ::SetWindowTextW(ctx.hName, ctx.rule.processName.c_str());
    yy += kRowH;

    addLabel(hwnd, hInstance, kPad, yy, 100, L"優先度");
    ctx.hPriority = addCombo(hwnd, hInstance, kPad + 110, yy, 220, EDIT_PRIORITY);
    constexpr std::array<models::PriorityLevel, 6> kAll = {
        models::PriorityLevel::Idle, models::PriorityLevel::BelowNormal,
        models::PriorityLevel::Normal, models::PriorityLevel::AboveNormal,
        models::PriorityLevel::High, models::PriorityLevel::Realtime,
    };
    for (auto p : kAll) {
        ::SendMessageW(ctx.hPriority, CB_ADDSTRING, 0,
                       reinterpret_cast<LPARAM>(priorityLabel(p)));
    }
    ::SendMessageW(ctx.hPriority, CB_SETCURSEL,
                   static_cast<WPARAM>(ctx.rule.targetPriority), 0);
    yy += kRowH;

    ctx.hEnabled = addCheck(hwnd, hInstance, kPad + 110, yy, 220,
                            EDIT_ENABLED, L"有効");
    ::SendMessageW(ctx.hEnabled, BM_SETCHECK,
                   ctx.rule.isEnabled ? BST_CHECKED : BST_UNCHECKED, 0);
    yy += kRowH;

    ctx.hECore = addCheck(hwnd, hInstance, kPad + 110, yy, 220,
                          EDIT_ECORE, L"E-Core 限定（ハイブリッド CPU のみ）");
    ::SendMessageW(ctx.hECore, BM_SETCHECK,
                   ctx.rule.useECoreOnly ? BST_CHECKED : BST_UNCHECKED, 0);
    yy += kRowH;

    addLabel(hwnd, hInstance, kPad, yy, 100, L"最大バックオフ指数");
    ctx.hBackoff = addEdit(hwnd, hInstance, kPad + 110, yy, 80, 22, EDIT_BACKOFF);
    {
        wchar_t b[16];
        ::swprintf_s(b, L"%d", std::clamp(ctx.rule.maxBackoffExponent, 0, 10));
        ::SetWindowTextW(ctx.hBackoff, b);
    }
    yy += kRowH + 10;

    constexpr int btnW = 90;
    constexpr int btnGap = 10;
    int bx = kW - 30 - btnW * 2 - btnGap;
    ctx.hOk     = addButton(hwnd, hInstance, bx, yy, btnW, EDIT_OK,     L"OK", true);
    bx += btnW + btnGap;
    ctx.hCancel = addButton(hwnd, hInstance, bx, yy, btnW, EDIT_CANCEL, L"キャンセル");

    HFONT font = createUiFont();
    applyFontRecursive(hwnd, font);

    ::ShowWindow(hwnd, SW_SHOWNORMAL);
    if (owner) ::EnableWindow(owner, FALSE);
    ::SetFocus(ctx.hName);

    MSG msg{};
    while (!ctx.closed && ::GetMessageW(&msg, nullptr, 0, 0) > 0) {
        if (!::IsDialogMessageW(hwnd, &msg)) {
            ::TranslateMessage(&msg);
            ::DispatchMessageW(&msg);
        }
    }
    if (msg.message == WM_QUIT) {
        ::PostQuitMessage(static_cast<int>(msg.wParam));
    }

    if (owner) ::EnableWindow(owner, TRUE);
    if (font) ::DeleteObject(font);
    ::DestroyWindow(hwnd);

    if (ctx.ok) rule = ctx.rule;
    return ctx.ok;
}

// =====================================================================
// SettingsDialog 本体
// =====================================================================

SettingsDialog::SettingsDialog(services::SettingsManager& mgr)
    : mgr_(mgr)
{
}

SettingsDialog::~SettingsDialog()
{
    if (hwnd_) ::DestroyWindow(hwnd_);
}

void SettingsDialog::show(HINSTANCE hInstance)
{
    if (hwnd_) {
        ::SetForegroundWindow(hwnd_);
        return;
    }
    hInstance_ = hInstance;

    INITCOMMONCONTROLSEX icc{sizeof(icc),
        ICC_STANDARD_CLASSES | ICC_WIN95_CLASSES | ICC_LISTVIEW_CLASSES};
    ::InitCommonControlsEx(&icc);

    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = &SettingsDialog::wndProcStatic;
    wc.hInstance = hInstance;
    wc.hCursor = ::LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_BTNFACE + 1);
    wc.lpszClassName = kClassName;
    ::RegisterClassExW(&wc);

    constexpr int kIndicatorRows = 8;
    constexpr int kRulesH = 200;
    constexpr int kRuleButtonsH = 36;
    constexpr int kPollingH = kRowH;
    constexpr int kAdminH = 22;
    constexpr int kButtonsH = 40;

    constexpr int kWindowW = kPad * 3 + kLabelW + kCtrlW;
    const int kWindowH = kPad * 2 + kIndicatorRows * kRowH + 14
                         + kRulesH + kRuleButtonsH + kPollingH + kAdminH
                         + kButtonsH + 30;

    RECT work{};
    ::SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    const int x = (work.right - kWindowW) / 2;
    const int y = std::max<long>(work.top + 10, work.bottom - kWindowH - 10);

    hwnd_ = ::CreateWindowExW(
        0, kClassName, L"IME Indicator 設定",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU,
        x, y, kWindowW, kWindowH,
        nullptr, nullptr, hInstance, this);
    if (!hwnd_) return;

    workingRules_ = mgr_.settings().processPriorityRules;

    createControls(hwnd_, hInstance);
    loadFromSettings();
    refreshRuleListView();
    updateAdminStatusLabel();

    HFONT font = createUiFont();
    applyFontRecursive(hwnd_, font);

    ::ShowWindow(hwnd_, SW_SHOWNORMAL);
    ::SetForegroundWindow(hwnd_);
    ::SetFocus(hSize_);

    dialogClosed_ = false;
    MSG msg{};
    while (!dialogClosed_ && ::GetMessageW(&msg, nullptr, 0, 0) > 0) {
        if (!::IsDialogMessageW(hwnd_, &msg)) {
            ::TranslateMessage(&msg);
            ::DispatchMessageW(&msg);
        }
    }
    if (msg.message == WM_QUIT) {
        ::PostQuitMessage(static_cast<int>(msg.wParam));
    }

    if (font) ::DeleteObject(font);
    if (hwnd_) {
        ::DestroyWindow(hwnd_);
        hwnd_ = nullptr;
    }
}

void SettingsDialog::createControls(HWND parent, HINSTANCE hInst)
{
    int y = kPad;
    auto row = [&](const wchar_t* lbl, HWND& target, int id,
                   bool isCheck = false, bool isCombo = false) {
        addLabel(parent, hInst, kPad, y, kLabelW, lbl);
        if (isCheck) {
            target = addCheck(parent, hInst, kPad + kLabelW, y, kCtrlW, id, L"");
        } else if (isCombo) {
            target = addCombo(parent, hInst, kPad + kLabelW, y, kCtrlW, id);
        } else {
            target = addEdit(parent, hInst, kPad + kLabelW, y, kCtrlW, 22, id);
        }
        y += kRowH;
    };

    row(L"インジケーター表示",       hVisible_,  ID_VISIBLE,  true);
    row(L"サイズ (20-100)",           hSize_,     ID_SIZE);
    row(L"不透明度 (0.10-1.00)",      hOpacity_,  ID_OPACITY);
    row(L"オフセット X",              hOffsetX_,  ID_OFFSETX);
    row(L"オフセット Y",              hOffsetY_,  ID_OFFSETY);
    row(L"IME ON テキスト",           hImeOn_,    ID_IMEON);
    row(L"IME OFF テキスト",          hImeOff_,   ID_IMEOFF);
    row(L"ログレベル",                hLogLevel_, ID_LOGLEVEL, false, true);
    constexpr std::array<const wchar_t*, 6> kLevels = {
        L"trace", L"debug", L"info", L"warn", L"error", L"critical"};
    for (auto s : kLevels) {
        ::SendMessageW(hLogLevel_, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(s));
    }

    // セパレーター + ヘッダ
    addLabel(parent, hInst, kPad, y, kLabelW + kCtrlW,
             L"プロセス優先度ルール（最大 30 件）");
    y += 22;

    // ListView
    constexpr int kListH = 200;
    constexpr int kListW = kLabelW + kCtrlW;
    hRulesList_ = ::CreateWindowExW(
        WS_EX_CLIENTEDGE, WC_LISTVIEWW, L"",
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | LVS_REPORT | LVS_SHOWSELALWAYS |
            LVS_SINGLESEL,
        kPad, y, kListW, kListH, parent,
        reinterpret_cast<HMENU>(static_cast<INT_PTR>(ID_RULES_LIST)),
        hInst, nullptr);
    ListView_SetExtendedListViewStyle(hRulesList_,
        LVS_EX_FULLROWSELECT | LVS_EX_GRIDLINES);

    LVCOLUMNW col{};
    col.mask = LVCF_TEXT | LVCF_WIDTH;
    col.pszText = const_cast<wchar_t*>(L"プロセス名");  col.cx = 140;
    ListView_InsertColumn(hRulesList_, 0, &col);
    col.pszText = const_cast<wchar_t*>(L"優先度");      col.cx = 100;
    ListView_InsertColumn(hRulesList_, 1, &col);
    col.pszText = const_cast<wchar_t*>(L"E-Core");      col.cx = 60;
    ListView_InsertColumn(hRulesList_, 2, &col);
    col.pszText = const_cast<wchar_t*>(L"有効");        col.cx = 60;
    ListView_InsertColumn(hRulesList_, 3, &col);

    y += kListH + 6;

    // ルール操作ボタン
    constexpr int btnW = 90;
    constexpr int btnGap = 6;
    int bx = kPad;
    hAddRule_    = addButton(parent, hInst, bx, y, btnW, ID_RULE_ADD,    L"追加");
    bx += btnW + btnGap;
    hEditRule_   = addButton(parent, hInst, bx, y, btnW, ID_RULE_EDIT,   L"編集");
    bx += btnW + btnGap;
    hDeleteRule_ = addButton(parent, hInst, bx, y, btnW, ID_RULE_DELETE, L"削除");
    constexpr int kRuleButtonsH = 36;  // show() の高さ計算と整合
    y += kRuleButtonsH;

    // ポーリング間隔
    addLabel(parent, hInst, kPad, y, kLabelW, L"ポーリング間隔（秒、1-1800）");
    hPollingInterval_ = addEdit(parent, hInst, kPad + kLabelW, y, 100, 22, ID_POLLING);
    y += kRowH;

    // 管理者権限ステータス（spec T095）
    hAdminStatus_ = ::CreateWindowExW(
        0, L"STATIC", L"",
        WS_CHILD | WS_VISIBLE | SS_LEFT,
        kPad, y, kLabelW + kCtrlW, 22, parent, nullptr, hInst, nullptr);
    y += 22 + 6;

    // OK / キャンセル / 適用
    constexpr int finalBtnW = 90;
    const int totalW = finalBtnW * 3 + btnGap * 2;
    bx = kPad + kLabelW + kCtrlW - totalW;
    hOk_     = addButton(parent, hInst, bx, y, finalBtnW, ID_OK,     L"OK", true);
    bx += finalBtnW + btnGap;
    hCancel_ = addButton(parent, hInst, bx, y, finalBtnW, ID_CANCEL, L"キャンセル");
    bx += finalBtnW + btnGap;
    hApply_  = addButton(parent, hInst, bx, y, finalBtnW, ID_APPLY,  L"適用");
}

void SettingsDialog::loadFromSettings()
{
    const auto& s = mgr_.settings();
    ::SendMessageW(hVisible_, BM_SETCHECK,
                   s.mouseCursorIndicator.isVisible ? BST_CHECKED : BST_UNCHECKED, 0);
    setEditDouble(hSize_,    s.mouseCursorIndicator.size,    0);
    setEditDouble(hOpacity_, s.mouseCursorIndicator.opacity, 2);
    setEditDouble(hOffsetX_, s.mouseCursorIndicator.offsetX, 0);
    setEditDouble(hOffsetY_, s.mouseCursorIndicator.offsetY, 0);
    ::SetWindowTextW(hImeOn_,  s.imeOnText.c_str());
    ::SetWindowTextW(hImeOff_, s.imeOffText.c_str());
    ::SendMessageW(hLogLevel_, CB_SETCURSEL,
                   static_cast<WPARAM>(s.logLevel), 0);

    wchar_t buf[16];
    ::swprintf_s(buf, L"%d", std::clamp(s.pollingIntervalSeconds, 1, 1800));
    ::SetWindowTextW(hPollingInterval_, buf);
}

void SettingsDialog::refreshRuleListView()
{
    ListView_DeleteAllItems(hRulesList_);
    for (size_t i = 0; i < workingRules_.size(); ++i) {
        const auto& r = workingRules_[i];

        LVITEMW item{};
        item.mask = LVIF_TEXT;
        item.iItem = static_cast<int>(i);
        item.iSubItem = 0;
        std::wstring name = r.processName;
        item.pszText = name.empty() ? const_cast<wchar_t*>(L"")
                                     : name.data();
        ListView_InsertItem(hRulesList_, &item);

        ListView_SetItemText(hRulesList_, static_cast<int>(i), 1,
            const_cast<wchar_t*>(priorityLabel(r.targetPriority)));
        ListView_SetItemText(hRulesList_, static_cast<int>(i), 2,
            const_cast<wchar_t*>(r.useECoreOnly ? L"○" : L""));
        ListView_SetItemText(hRulesList_, static_cast<int>(i), 3,
            const_cast<wchar_t*>(r.isEnabled ? L"○" : L""));
    }
}

int SettingsDialog::selectedRuleIndex() const
{
    return ListView_GetNextItem(hRulesList_, -1, LVNI_SELECTED);
}

void SettingsDialog::onAddRule()
{
    if (workingRules_.size() >= 30) {
        ::MessageBoxW(hwnd_,
            L"プロセス優先度ルールは最大 30 件までです。",
            L"IME Indicator",
            MB_OK | MB_ICONINFORMATION);
        return;
    }
    models::ProcessPriorityRule rule{};
    rule.targetPriority = models::PriorityLevel::Normal;
    rule.maxBackoffExponent = 6;
    rule.isEnabled = true;
    if (showRuleEditDialog(hwnd_, hInstance_, rule)) {
        if (rule.isValid()) {
            workingRules_.push_back(std::move(rule));
            refreshRuleListView();
            ListView_SetItemState(hRulesList_,
                static_cast<int>(workingRules_.size() - 1),
                LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED);
        }
    }
}

void SettingsDialog::onEditRule()
{
    int idx = selectedRuleIndex();
    if (idx < 0 || static_cast<size_t>(idx) >= workingRules_.size()) return;
    auto rule = workingRules_[idx];
    if (showRuleEditDialog(hwnd_, hInstance_, rule)) {
        if (rule.isValid()) {
            workingRules_[idx] = std::move(rule);
            refreshRuleListView();
            ListView_SetItemState(hRulesList_, idx,
                LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED);
        }
    }
}

void SettingsDialog::onDeleteRule()
{
    int idx = selectedRuleIndex();
    if (idx < 0 || static_cast<size_t>(idx) >= workingRules_.size()) return;
    workingRules_.erase(workingRules_.begin() + idx);
    refreshRuleListView();
}

void SettingsDialog::updateAdminStatusLabel()
{
    if (!hAdminStatus_) return;
    int count = 0;
    if (accessDeniedCountCallback_) count = accessDeniedCountCallback_();
    if (count > 0) {
        wchar_t buf[160];
        ::swprintf_s(buf,
            L"⚠ 一部プロセスに管理者権限が必要です（拒否回数: %d）",
            count);
        ::SetWindowTextW(hAdminStatus_, buf);
    } else {
        ::SetWindowTextW(hAdminStatus_, L"");
    }
}

bool SettingsDialog::readControlsToSettings(models::AppSettings& out) const
{
    out = mgr_.settings();
    out.mouseCursorIndicator.isVisible =
        (::SendMessageW(hVisible_, BM_GETCHECK, 0, 0) == BST_CHECKED);
    out.mouseCursorIndicator.size    = getEditDouble(hSize_,    out.mouseCursorIndicator.size);
    out.mouseCursorIndicator.opacity = getEditDouble(hOpacity_, out.mouseCursorIndicator.opacity);
    out.mouseCursorIndicator.offsetX = getEditDouble(hOffsetX_, out.mouseCursorIndicator.offsetX);
    out.mouseCursorIndicator.offsetY = getEditDouble(hOffsetY_, out.mouseCursorIndicator.offsetY);

    auto on  = getEditText(hImeOn_);
    auto off = getEditText(hImeOff_);
    if (!on.empty())  out.imeOnText  = on;
    if (!off.empty()) out.imeOffText = off;

    LRESULT idx = ::SendMessageW(hLogLevel_, CB_GETCURSEL, 0, 0);
    if (idx >= 0 && idx <= static_cast<LRESULT>(models::LogLevel::Critical)) {
        out.logLevel = static_cast<models::LogLevel>(idx);
    }

    out.processPriorityRules = workingRules_;
    out.pollingIntervalSeconds = getEditInt(hPollingInterval_, out.pollingIntervalSeconds);

    out.clamp();
    return true;
}

void SettingsDialog::onApply()
{
    models::AppSettings next{};
    if (!readControlsToSettings(next)) return;
    mgr_.setSettings(next);
    mgr_.save();
    workingRules_ = mgr_.settings().processPriorityRules;
    refreshRuleListView();
    updateAdminStatusLabel();
    if (appliedCallback_) appliedCallback_(next);
}

void SettingsDialog::onOk()
{
    onApply();
    dialogClosed_ = true;
    ::PostMessageW(hwnd_, WM_NULL, 0, 0);
}

void SettingsDialog::onCancel()
{
    dialogClosed_ = true;
    ::PostMessageW(hwnd_, WM_NULL, 0, 0);
}

LRESULT CALLBACK SettingsDialog::wndProcStatic(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_NCCREATE) {
        auto* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        ::SetWindowLongPtrW(hwnd, GWLP_USERDATA,
                            reinterpret_cast<LONG_PTR>(cs->lpCreateParams));
        return ::DefWindowProcW(hwnd, msg, wp, lp);
    }
    auto* self = reinterpret_cast<SettingsDialog*>(
        ::GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (self) return self->handleMessage(msg, wp, lp);
    return ::DefWindowProcW(hwnd, msg, wp, lp);
}

LRESULT SettingsDialog::handleMessage(UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
        case WM_COMMAND:
            switch (LOWORD(wp)) {
                case ID_OK:          onOk();         return 0;
                case ID_CANCEL:      onCancel();     return 0;
                case ID_APPLY:       onApply();      return 0;
                case ID_RULE_ADD:    onAddRule();    return 0;
                case ID_RULE_EDIT:   onEditRule();   return 0;
                case ID_RULE_DELETE: onDeleteRule(); return 0;
            }
            break;
        case WM_NOTIFY: {
            auto* nm = reinterpret_cast<NMHDR*>(lp);
            if (nm && nm->hwndFrom == hRulesList_ && nm->code == NM_DBLCLK) {
                onEditRule();
                return 0;
            }
            break;
        }
        case WM_CLOSE:
            onCancel();
            return 0;
    }
    return ::DefWindowProcW(hwnd_, msg, wp, lp);
}

} // namespace imeindicator::views
