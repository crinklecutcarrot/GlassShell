#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shellapi.h>
#include <stdint.h>

// Explorer's WM_COPYDATA payload is private implementation detail. This layout
// matches the x64 Windows 11 notification-area transport and is intentionally
// isolated in this replaceable DLL.
#pragma pack(push, 4)
typedef struct TrayIconData {
    uint32_t size, owner, uid, flags, callback, icon;
    wchar_t tooltip[128];
    uint32_t state, stateMask;
    wchar_t info[256];
    uint32_t version;
    wchar_t infoTitle[64];
    uint32_t infoFlags;
    GUID guid;
    uint32_t balloonIcon;
} TrayIconData;
typedef struct ShellTrayMessage {
    int32_t magic;
    uint32_t operation;
    TrayIconData icon;
    uint32_t messageVersion;
} ShellTrayMessage;
typedef struct GlassTrayEvent {
    uint32_t operation, uid, callback, version, flags, visible;
    uintptr_t owner, icon;
    GUID guid;
    wchar_t tooltip[128];
} GlassTrayEvent;
#pragma pack(pop)

static const ULONG_PTR GLASS_TRAY_COPYDATA = 0x52544C47; /* "GLTR" */

__declspec(dllexport) LRESULT CALLBACK CallWndProc(int code, WPARAM wParam, LPARAM lParam) {
    if (code >= 0 && lParam) {
        const CWPSTRUCT* message = (const CWPSTRUCT*)lParam;
        wchar_t className[64] = {0};
        GetClassNameW(message->hwnd, className, 64);
        if (message->message == WM_COPYDATA && wcscmp(className, L"Shell_TrayWnd") == 0) {
            const COPYDATASTRUCT* copy = (const COPYDATASTRUCT*)message->lParam;
            if (copy && copy->dwData == 1 && copy->lpData && copy->cbData >= sizeof(ShellTrayMessage)) {
                const ShellTrayMessage* tray = (const ShellTrayMessage*)copy->lpData;
                if (tray->operation == NIM_ADD || tray->operation == NIM_MODIFY ||
                    tray->operation == NIM_DELETE || tray->operation == NIM_SETVERSION) {
                    HWND target = FindWindowW(NULL, L"GlassShell · Status");
                    if (target) {
                        GlassTrayEvent event = {0};
                        event.operation = tray->operation;
                        event.uid = tray->icon.uid;
                        event.callback = tray->icon.callback;
                        event.version = tray->icon.version;
                        event.flags = tray->icon.flags;
                        event.visible = (tray->icon.state & NIS_HIDDEN) == 0;
                        event.owner = (uintptr_t)tray->icon.owner;
                        event.icon = (uintptr_t)tray->icon.icon;
                        event.guid = tray->icon.guid;
                        lstrcpynW(event.tooltip, tray->icon.tooltip, 128);
                        COPYDATASTRUCT outgoing = {0};
                        DWORD_PTR ignored = 0;
                        outgoing.dwData = GLASS_TRAY_COPYDATA;
                        outgoing.cbData = sizeof(event);
                        outgoing.lpData = &event;
                        SendMessageTimeoutW(target, WM_COPYDATA, 0, (LPARAM)&outgoing,
                            SMTO_ABORTIFHUNG | SMTO_BLOCK, 100, &ignored);
                    }
                }
            }
        }
    }
    return CallNextHookEx(NULL, code, wParam, lParam);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    (void)instance; (void)reason; (void)reserved; return TRUE;
}
