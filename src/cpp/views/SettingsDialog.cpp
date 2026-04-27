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

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "comctl32.lib")

namespace imeindicator::views {

namespace {

constexpr wchar_t kClassName[] = L"IMEIndicator_SettingsDialog";

// コントロール ID
enum CtrlId : int {
    ID_SIZE = 1001,
    ID_OPACITY,
    ID_OFFSETX,
    ID_OFFSETY,
    ID_IMEON,
    ID_IMEOFF,
    ID_VISIBLE,
    ID_LOGLEVEL,
    ID_OK = 2001,
    ID_CANCEL,
    ID_APPLY,
};

constexpr int kRowH = 28;
constexpr int kLabelW = 150;
constexpr int kCtrlW = 200;
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

HWND addEdit(HWND parent, HINSTANCE hInst, int x, int y, int w, int id,
             DWORD extraStyle = 0)
{
    return ::CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"",
                              WS_CHILD | WS_VISIBLE | WS_TABSTOP | extraStyle,
                              x, y, w, 22, parent, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                              hInst, nullptr);
}

HWND addCheck(HWND parent, HINSTANCE hInst, int x, int y, int w,
              int id, const wchar_t* text)
{
    return ::CreateWindowExW(0, L"BUTTON", text,
                              WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_AUTOCHECKBOX,
                              x, y, w, 22, parent, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                              hInst, nullptr);
}

HWND addCombo(HWND parent, HINSTANCE hInst, int x, int y, int w, int id)
{
    return ::CreateWindowExW(0, L"COMBOBOX", L"",
                              WS_CHILD | WS_VISIBLE | WS_TABSTOP | CBS_DROPDOWNLIST | WS_VSCROLL,
                              x, y, w, 200, parent, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                              hInst, nullptr);
}

HWND addButton(HWND parent, HINSTANCE hInst, int x, int y, int w,
               int id, const wchar_t* text, bool def = false)
{
    DWORD style = WS_CHILD | WS_VISIBLE | WS_TABSTOP;
    style |= def ? BS_DEFPUSHBUTTON : BS_PUSHBUTTON;
    return ::CreateWindowExW(0, L"BUTTON", text, style,
                              x, y, w, 28, parent, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
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

std::wstring getEditText(HWND h)
{
    int len = ::GetWindowTextLengthW(h);
    std::wstring s(len, L'\0');
    if (len > 0) {
        ::GetWindowTextW(h, s.data(), len + 1);
    }
    return s;
}

} // namespace

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

    INITCOMMONCONTROLSEX icc{sizeof(icc), ICC_STANDARD_CLASSES | ICC_WIN95_CLASSES};
    ::InitCommonControlsEx(&icc);

    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = &SettingsDialog::wndProcStatic;
    wc.hInstance = hInstance;
    wc.hCursor = ::LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_BTNFACE + 1);
    wc.lpszClassName = kClassName;
    ::RegisterClassExW(&wc);  // 重複登録は無視

    constexpr int kWindowW = kPad * 3 + kLabelW + kCtrlW;
    constexpr int kRows = 8;
    constexpr int kButtonsH = 40;
    constexpr int kRulesH = 60;
    const int kWindowH = kPad * 2 + kRows * kRowH + kRulesH + kButtonsH + 30;

    // タスクバー直上配置（既存 C# 版の改善踏襲）
    RECT work{};
    ::SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    const int x = (work.right - kWindowW) / 2;
    const int y = work.bottom - kWindowH - 10;

    hwnd_ = ::CreateWindowExW(
        0, kClassName, L"IME Indicator 設定",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU,
        x, y, kWindowW, kWindowH,
        nullptr, nullptr, hInstance, this);
    if (!hwnd_) return;

    createControls(hwnd_, hInstance);
    loadFromSettings();

    HFONT font = createUiFont();
    applyFontRecursive(hwnd_, font);

    ::ShowWindow(hwnd_, SW_SHOWNORMAL);
    ::SetForegroundWindow(hwnd_);
    ::SetFocus(hSize_);

    // モーダル風メッセージループ
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
    auto row = [&](const wchar_t* lbl, HWND& target, int id, bool isCheck = false,
                    bool isCombo = false) {
        addLabel(parent, hInst, kPad, y, kLabelW, lbl);
        if (isCheck) {
            target = addCheck(parent, hInst, kPad + kLabelW, y, kCtrlW, id, L"");
        } else if (isCombo) {
            target = addCombo(parent, hInst, kPad + kLabelW, y, kCtrlW, id);
        } else {
            target = addEdit(parent, hInst, kPad + kLabelW, y, kCtrlW, id);
        }
        y += kRowH;
    };

    row(L"インジケーター表示", hVisible_, ID_VISIBLE, true);
    row(L"サイズ (20-100)", hSize_, ID_SIZE);
    row(L"不透明度 (0.10-1.00)", hOpacity_, ID_OPACITY);
    row(L"オフセット X", hOffsetX_, ID_OFFSETX);
    row(L"オフセット Y", hOffsetY_, ID_OFFSETY);
    row(L"IME ON テキスト", hImeOn_, ID_IMEON);
    row(L"IME OFF テキスト", hImeOff_, ID_IMEOFF);
    row(L"ログレベル", hLogLevel_, ID_LOGLEVEL, false, true);

    constexpr std::array<const wchar_t*, 6> kLevels = {
        L"trace", L"debug", L"info", L"warn", L"error", L"critical"};
    for (auto s : kLevels) {
        ::SendMessageW(hLogLevel_, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(s));
    }

    // プロセス優先度ルールの注意書き
    addLabel(parent, hInst, kPad, y, kLabelW + kCtrlW,
             L"プロセス優先度ルールは settings.json を手動編集してください。");
    y += 20;
    addLabel(parent, hInst, kPad, y, kLabelW + kCtrlW,
             L"%APPDATA%\\IMEIndicator\\settings.json");
    y += 30;

    // ボタン
    const int btnW = 80;
    const int btnSpacing = 8;
    const int totalBtnW = btnW * 3 + btnSpacing * 2;
    int bx = kPad + kLabelW + kCtrlW - totalBtnW;
    hOk_     = addButton(parent, hInst, bx, y, btnW, ID_OK,     L"OK", true);  bx += btnW + btnSpacing;
    hCancel_ = addButton(parent, hInst, bx, y, btnW, ID_CANCEL, L"キャンセル"); bx += btnW + btnSpacing;
    hApply_  = addButton(parent, hInst, bx, y, btnW, ID_APPLY,  L"適用");
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
    out.clamp();
    return true;
}

void SettingsDialog::onApply()
{
    models::AppSettings next{};
    if (!readControlsToSettings(next)) return;
    mgr_.setSettings(next);
    mgr_.save();
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
                case ID_OK:     onOk();     return 0;
                case ID_CANCEL: onCancel(); return 0;
                case ID_APPLY:  onApply();  return 0;
            }
            break;
        case WM_CLOSE:
            onCancel();
            return 0;
        case WM_DESTROY:
            return 0;
    }
    return ::DefWindowProcW(hwnd_, msg, wp, lp);
}

} // namespace imeindicator::views
