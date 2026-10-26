/* GetDpiForWindow is part of the supported Windows 10 API surface. */
#define WINVER 0x0A00
#define _WIN32_WINNT 0x0A00
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <wchar.h>
#include <stdlib.h>
#define TRANS_TOKEN L"BilibiliTranslation.Token"

/* Decide geometry on the player's UI thread, before a native child is shown.
   Automatic fullscreen suppression preserves the last explicit sidebar choice. */
#define MSG_LAYOUT (WM_APP + 0x317)
#define MSG_SMALL (WM_APP + 0x318)
#define MSG_REFLOW (WM_APP + 0x319)
/* Reserve the unused window-centering skin command for the small-window button.
   PotPlayer resolves named CmdIDs; arbitrary numeric skin commands are ignored. */
#define CMD_SMALL 10377
#define PROP(name) L"BilibiliLayout." name
enum { VIDEO = 1, HEADER, SIDEBAR, SPLITTER };
static LRESULT CALLBACK ChildProc(HWND, UINT, WPARAM, LPARAM);
static void RestoreWindow(HWND child)
{
    WNDPROC original = (WNDPROC)GetPropW(child, PROP(L"ChildOriginal"));
    if (original && IsWindow(child)) SetWindowLongPtrW(child, GWLP_WNDPROC, (LONG_PTR)original);
    RemovePropW(child, PROP(L"ChildOriginal")); RemovePropW(child, PROP(L"Owner")); RemovePropW(child, PROP(L"Role"));
}
static int Value(HWND h, LPCWSTR key) { return (int)(INT_PTR)GetPropW(h, key); }
static void Put(HWND h, LPCWSTR key, int n) { SetPropW(h, key, (HANDLE)(INT_PTR)n); }
static void Count(HWND h, LPCWSTR key) { Put(h, key, Value(h, key) + 1); }
static int Requested(HWND h) { return Value(h, Value(h, PROP(L"Full")) ? PROP(L"FullIntent") : PROP(L"Intent")); }
static int Fullscreen(HWND h)
{
    RECT r; MONITORINFO m = {sizeof(m)};
    GetWindowRect(h, &r); GetMonitorInfoW(MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST), &m);
    return EqualRect(&r, &m.rcMonitor);
}
static void Policy(HWND h)
{
    RECT r; GetClientRect(h, &r);
    int dpi = GetDpiForWindow(h), full = Fullscreen(h);
    int small = !full && (r.right * 96 < 800 * dpi || r.bottom * 96 < 480 * dpi);
    int allowed = full || (!small && r.right * 96 >= 1024 * dpi && r.bottom * 96 >= 540 * dpi);
    if (full && !Value(h, PROP(L"Full"))) Put(h, PROP(L"FullIntent"), 0);
    Put(h, PROP(L"Small"), small); Put(h, PROP(L"Full"), full);
    Put(h, PROP(L"Border"), full ? 0 : MulDiv(3, dpi, 96));
    Put(h, PROP(L"Top"), small || full ? Value(h, PROP(L"Border")) : MulDiv(34, dpi, 96));
    Put(h, PROP(L"AllowSidebar"), allowed);
    Put(h, PROP(L"Open"), allowed && Requested(h) && Value(h, PROP(L"SidebarReady")));
}
static void ToggleSidebar(HWND h)
{
    Policy(h);
    if (!Value(h, PROP(L"AllowSidebar")) || Value(h, PROP(L"Sizing"))) return;
    int requested = !Requested(h);
    if (Value(h, PROP(L"Full"))) Put(h, PROP(L"FullIntent"), requested);
    Put(h, PROP(L"Intent"), requested);
}
static RECT Area(HWND h, int role)
{
    RECT r; GetClientRect(h, &r);
    int border = Value(h, PROP(L"Border")), width = Value(h, PROP(L"SidebarWidth"));
    if (!width) width = MulDiv(314, GetDpiForWindow(h), 96);
    r.bottom -= border; r.top = Value(h, PROP(L"Top"));
    if (role == VIDEO) { r.left = border; r.right -= border + (Value(h, PROP(L"Open")) ? width : 0); }
    else if (role == SIDEBAR) { r.right -= border; r.left = r.right - width; }
    else if (role == SPLITTER) { r.right -= border + width; r.left = r.right; }
    return r;
}
static void AttachChild(HWND h, HWND child, int role)
{
    if (!IsWindow(child) || GetPropW(child, PROP(L"ChildOriginal"))) return;
    SetPropW(child, PROP(L"Owner"), h); Put(child, PROP(L"Role"), role);
    SetPropW(child, PROP(L"ChildOriginal"), (HANDLE)SetWindowLongPtrW(child, GWLP_WNDPROC, (LONG_PTR)ChildProc));
}
static BOOL CALLBACK FindChildren(HWND child, LPARAM data)
{
    HWND h = (HWND)data;
    if (GetParent(child) != h) return TRUE;
    WCHAR name[80]; GetClassNameW(child, name, 80);
    if (wcsncmp(name, L"AfxWnd", 6)) return TRUE;
    RECT r; GetWindowRect(child, &r);
    POINT p = {r.left, r.top}; ScreenToClient(h, &p);
    int dpi = GetDpiForWindow(h), width = r.right - r.left, height = r.bottom - r.top;
    if (child == (HWND)GetPropW(h, PROP(L"Video"))) AttachChild(h, child, VIDEO);
    else if (p.y <= MulDiv(4, dpi, 96) && abs(height - MulDiv(31, dpi, 96)) <= 2 && width > 100) {
        SetPropW(h, PROP(L"Header"), child); AttachChild(h, child, HEADER);
    }
    return TRUE;
}
static BOOL CALLBACK FindSplitter(HWND child, LPARAM data)
{
    HWND h = (HWND)data, pane = (HWND)GetPropW(h, PROP(L"Sidebar"));
    if (GetParent(child) != h || child == pane) return TRUE;
    WCHAR name[80]; GetClassNameW(child, name, 80);
    if (wcsncmp(name, L"AfxWnd", 6)) return TRUE;
    RECT r, side; GetWindowRect(child, &r); GetWindowRect(pane, &side);
    if (r.right == side.left && r.top == side.top && r.bottom == side.bottom && r.right - r.left == MulDiv(3, GetDpiForWindow(h), 96)) {
        SetPropW(h, PROP(L"Splitter"), child); AttachChild(h, child, SPLITTER); return FALSE;
    }
    return TRUE;
}
static BOOL CALLBACK FindPlaylist(HWND child, LPARAM data)
{
    /* The playlist is the native dialog control, not whichever large rectangle
       happens to be on the right. Its parent may exist while its content is hidden. */
    if (GetDlgCtrlID(child) != 3230) return TRUE;
    WCHAR name[80]; GetClassNameW(child, name, 80);
    if (wcsncmp(name, L"Afx:", 4)) return TRUE;
    HWND h = (HWND)data, pane = child;
    while (GetParent(pane) && GetParent(pane) != h) pane = GetParent(pane);
    if (GetParent(pane) != h || pane == (HWND)GetPropW(h, PROP(L"Video"))) return TRUE;
    SetPropW(h, PROP(L"Playlist"), child); SetPropW(h, PROP(L"Sidebar"), pane);
    RECT r; GetWindowRect(pane, &r);
    if (r.right - r.left > 100) Put(h, PROP(L"SidebarWidth"), r.right - r.left);
    AttachChild(h, pane, SIDEBAR);
    EnumChildWindows(h, FindSplitter, (LPARAM)h);
    return FALSE;
}
static void NativeCommand(HWND h, int command)
{
    WNDPROC original = (WNDPROC)GetPropW(h, PROP(L"Original"));
    if (!original) return;
    RECT bounds; GetWindowRect(h, &bounds);
    Put(h, PROP(L"FrozenWidth"), bounds.right - bounds.left); Put(h, PROP(L"FrozenHeight"), bounds.bottom - bounds.top);
    Put(h, PROP(L"Freeze"), 1);
    /* Native playlist show commands center the fullscreen pointer. Constrain
       that synchronous call at its existing position, then restore the caller's
       clipping rectangle; never move the pointer back after a visible jump. */
    RECT clip; POINT pointer;
    int retainPointer = command == 20048 && Fullscreen(h) && GetClipCursor(&clip) && GetCursorPos(&pointer);
    if (retainPointer) {
        RECT point = {pointer.x, pointer.y, pointer.x + 1, pointer.y + 1};
        retainPointer = ClipCursor(&point);
    }
    HWND focus = GetFocus(); Count(h, PROP(L"NativeDepth"));
    CallWindowProcW(original, h, WM_COMMAND, command, 0);
    Put(h, PROP(L"NativeDepth"), Value(h, PROP(L"NativeDepth")) - 1);
    if (IsWindow(focus)) SetFocus(focus);
    if (retainPointer) ClipCursor(&clip);
    RemovePropW(h, PROP(L"Freeze"));
}
static void PrepareSidebar(HWND h)
{
    /* Native panes can be recreated after a file or display-mode change. Bind
       their replacements even while suppressed, so they cannot escape hiding. */
    EnumChildWindows(h, FindPlaylist, (LPARAM)h);
    int ready = IsWindow((HWND)GetPropW(h, PROP(L"Sidebar")));
    if (!ready && Requested(h) && Value(h, PROP(L"AllowSidebar"))) {
        /* Open the attached pane without selecting a tab. Hidden playlist
           controls are normal while the browser tab is selected. */
        NativeCommand(h, 20048);
        EnumChildWindows(h, FindPlaylist, (LPARAM)h);
        ready = IsWindow((HWND)GetPropW(h, PROP(L"Sidebar")));
        Count(h, PROP(L"SidebarInitializedCount"));
    }
    Put(h, PROP(L"SidebarReady"), ready);
}
static void Place(HWND h, LPCWSTR key, int role, int show)
{
    HWND child = (HWND)GetPropW(h, key);
    if (!IsWindow(child)) return;
    if (role == VIDEO && !IsWindowVisible(child)) return;
    if (role == HEADER) { if (!!IsWindowVisible(child) != show) ShowWindow(child, show ? SW_SHOWNA : SW_HIDE); return; }
    RECT r = Area(h, role), old; GetWindowRect(child, &old);
    POINT p = {old.left, old.top}; ScreenToClient(h, &p);
    if (p.x != r.left || p.y != r.top || old.right - old.left != r.right - r.left || old.bottom - old.top != r.bottom - r.top || !!IsWindowVisible(child) != show)
        SetWindowPos(child, role == VIDEO ? HWND_BOTTOM : HWND_TOP, r.left, r.top, max(1, r.right - r.left), max(1, r.bottom - r.top), SWP_NOACTIVATE | (show ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
}
static void Apply(HWND h)
{
    /* COM calls in native playback queries can pump sent messages. Never resize
       a renderer reentrantly while PotPlayer is using or replacing its graph. */
    if (Value(h, PROP(L"NativeDepth"))) { Put(h, PROP(L"LayoutPending"), 1); return; }
    if (GetPropW(h, PROP(L"Applying"))) return;
    Put(h, PROP(L"Applying"), 1); Policy(h);
    /* Identify skin roles once while its rectangles are stable. During a resize
       the old right border can temporarily look like the playlist splitter. */
    if (!Value(h, PROP(L"Identified"))) { EnumChildWindows(h, FindChildren, (LPARAM)h); Put(h, PROP(L"Identified"), 1); }
    HWND video = (HWND)GetPropW(h, PROP(L"Video")), previous = (HWND)GetPropW(h, PROP(L"VideoHooked"));
    if (previous != video) { RestoreWindow(previous); SetPropW(h, PROP(L"VideoHooked"), video); }
    AttachChild(h, video, VIDEO);
    PrepareSidebar(h); Policy(h);
    int open = Value(h, PROP(L"Open"));
    int changed = !Value(h, PROP(L"NativeSynced")) || Value(h, PROP(L"NativeOpen")) != open;
    if (changed) {
        /* Showing/hiding the HWND alone leaves the native renderer's reserved
           sidebar area and cached pixels intact, especially in fullscreen. */
        Put(h, PROP(L"NativeSynced"), 1); Put(h, PROP(L"NativeOpen"), open);
        NativeCommand(h, open ? 20048 : 20049);
    }
    Place(h, PROP(L"Header"), HEADER, !Value(h, PROP(L"Small")) && !Value(h, PROP(L"Full")));
    Place(h, PROP(L"Sidebar"), SIDEBAR, Value(h, PROP(L"Open")));
    /* Its skin samples cached video pixels, producing a colored vertical strip.
       The video meets the playlist directly; no splitter surface is exposed. */
    Place(h, PROP(L"Splitter"), SPLITTER, 0);
    Place(h, PROP(L"Video"), VIDEO, 1);
    RECT viewport = Area(h, VIDEO);
    int geometryChanged = (HWND)GetPropW(h, PROP(L"LastVideo")) != video ||
        Value(h, PROP(L"ViewX")) != viewport.left || Value(h, PROP(L"ViewY")) != viewport.top ||
        Value(h, PROP(L"ViewWidth")) != viewport.right - viewport.left || Value(h, PROP(L"ViewHeight")) != viewport.bottom - viewport.top;
    SetPropW(h, PROP(L"LastVideo"), video);
    Put(h, PROP(L"ViewX"), viewport.left); Put(h, PROP(L"ViewY"), viewport.top);
    Put(h, PROP(L"ViewWidth"), viewport.right - viewport.left); Put(h, PROP(L"ViewHeight"), viewport.bottom - viewport.top);
    if (changed || geometryChanged) Put(h, PROP(L"NeedsReflow"), 1);
    if (Value(h, PROP(L"NeedsReflow")) && !Value(h, PROP(L"Sizing")) && !Value(h, PROP(L"ReflowQueued"))) {
        Put(h, PROP(L"ReflowQueued"), 1); PostMessageW(h, MSG_REFLOW, 0, 0);
    }
    RemovePropW(h, PROP(L"Applying"));
}
static LRESULT CALLBACK ChildProc(HWND child, UINT msg, WPARAM wp, LPARAM lp)
{
    WNDPROC original = (WNDPROC)GetPropW(child, PROP(L"ChildOriginal"));
    HWND h = (HWND)GetPropW(child, PROP(L"Owner")); int role = Value(child, PROP(L"Role"));
    if (!original) return DefWindowProcW(child, msg, wp, lp);
    if (msg == WM_WINDOWPOSCHANGING) {
        WINDOWPOS *p = (WINDOWPOS *)lp;
        /* A renderer is reparented/replaced during fullscreen and file changes.
           Let PotPlayer retire it instead of forcing a stale surface visible. */
        if (role == VIDEO && (child != (HWND)GetPropW(h, PROP(L"Video")) || (p->flags & SWP_HIDEWINDOW) ||
            (Value(h, PROP(L"NativeDepth")) && !Value(h, PROP(L"NativeLayout")))))
            return CallWindowProcW(original, child, msg, wp, lp);
        int show = role == VIDEO || (role == HEADER ? !Value(h, PROP(L"Small")) && !Value(h, PROP(L"Full")) : role == SIDEBAR && Value(h, PROP(L"Open")));
        if (role != VIDEO) p->flags = (p->flags & ~(SWP_SHOWWINDOW | SWP_HIDEWINDOW)) | (show ? SWP_SHOWWINDOW : SWP_HIDEWINDOW);
        if (role != HEADER) {
            RECT r = Area(h, role);
            if (p->x != r.left || p->y != r.top || p->cx != r.right - r.left || p->cy != r.bottom - r.top) Count(h, PROP(L"ChildConstrainedCount"));
            p->x = r.left; p->y = r.top; p->cx = max(1, r.right - r.left); p->cy = max(1, r.bottom - r.top);
            p->flags &= ~(SWP_NOMOVE | SWP_NOSIZE);
            if (role == VIDEO) { p->hwndInsertAfter = HWND_BOTTOM; p->flags &= ~SWP_NOZORDER; p->flags |= SWP_NOCOPYBITS; }
        }
    }
    return CallWindowProcW(original, child, msg, wp, lp);
}
static BOOL CALLBACK RestoreChild(HWND child, LPARAM unused)
{
    RestoreWindow(child);
    return TRUE;
}
static void ConstrainAspect(HWND h, WPARAM edge, RECT *r)
{
    int ratio = Value(h, PROP(L"Ratio")); if (!ratio || !Value(h, PROP(L"Small"))) return;
    RECT client, outer; GetClientRect(h, &client); GetWindowRect(h, &outer);
    int border = Value(h, PROP(L"Border"));
    int dx = outer.right - outer.left - client.right + 2 * border, dy = outer.bottom - outer.top - client.bottom + 2 * border;
    int width = max(1, r->right - r->left - dx), height = max(1, r->bottom - r->top - dy);
    if (edge == WMSZ_TOP || edge == WMSZ_BOTTOM) width = MulDiv(height, ratio, 100000); else height = MulDiv(width, 100000, ratio);
    if (edge == WMSZ_LEFT || edge == WMSZ_TOPLEFT || edge == WMSZ_BOTTOMLEFT) r->left = r->right - width - dx; else r->right = r->left + width + dx;
    if (edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT) r->top = r->bottom - height - dy; else r->bottom = r->top + height + dy;
}
static void ConstrainOuterPosition(HWND h, WINDOWPOS *p)
{
    if (Value(h, PROP(L"Freeze"))) {
        if (!(p->flags & SWP_NOSIZE)) { p->cx = Value(h, PROP(L"FrozenWidth")); p->cy = Value(h, PROP(L"FrozenHeight")); }
        p->flags |= SWP_NOMOVE;
    }
    else if (Value(h, PROP(L"Restoring")) && !(p->flags & SWP_NOSIZE)) {
        /* Native attach commands adjust PotPlayer's cached normal width while
           fullscreen. Keep the actual pre-fullscreen outer bounds. */
        p->x = Value(h, PROP(L"RestoreX")); p->y = Value(h, PROP(L"RestoreY"));
        p->cx = Value(h, PROP(L"RestoreWidth")); p->cy = Value(h, PROP(L"RestoreHeight"));
        p->flags &= ~SWP_NOMOVE;
    }
}
static LRESULT CALLBACK LayoutProc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    WNDPROC original = (WNDPROC)GetPropW(h, PROP(L"Original"));
    if (!original) return DefWindowProcW(h, msg, wp, lp);
    if (msg == WM_COMMAND && !HIWORD(wp) && ((LOWORD(wp) >= 11114 && LOWORD(wp) <= 11149) || (LOWORD(wp) >= 12000 && LOWORD(wp) <= 12400))) {
        RemovePropW(h, TRANS_TOKEN);
        Put(h, L"BilibiliTranslation.Settings", Value(h, L"BilibiliTranslation.Settings") + 1);
    }
    if (msg == MSG_LAYOUT && wp == 2) {
        EnumChildWindows(h, RestoreChild, 0); RemovePropW(h, PROP(L"Identified")); RemovePropW(h, PROP(L"NativeSynced")); RemovePropW(h, PROP(L"NeedsReflow")); RemovePropW(h, PROP(L"ReflowQueued")); SetWindowLongPtrW(h, GWLP_WNDPROC, (LONG_PTR)original); RemovePropW(h, PROP(L"Original")); return 1;
    }
    if (msg == MSG_REFLOW) {
        RemovePropW(h, PROP(L"ReflowQueued"));
        if (!Value(h, PROP(L"NeedsReflow"))) return 0;
        if (Value(h, PROP(L"NativeDepth")) || Value(h, PROP(L"Applying")) || Value(h, PROP(L"Sizing"))) { Put(h, PROP(L"LayoutPending"), 1); return 0; }
        RemovePropW(h, PROP(L"NeedsReflow"));
        /* Native attach commands leave the fullscreen render destination at
           its old size. Wait until they finish, then use the normal size path
           with child constraints active to scale the picture into the viewport. */
        RECT client, bounds; GetClientRect(h, &client); GetWindowRect(h, &bounds);
        Put(h, PROP(L"FrozenWidth"), bounds.right - bounds.left); Put(h, PROP(L"FrozenHeight"), bounds.bottom - bounds.top);
        Put(h, PROP(L"Freeze"), 1);
        SendMessageW(h, WM_SIZE, SIZE_RESTORED, MAKELPARAM(client.right, client.bottom));
        RemovePropW(h, PROP(L"Freeze"));
        Count(h, PROP(L"ReflowCount"));
        RedrawWindow(h, NULL, NULL, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN);
        return 0;
    }
    if (msg == MSG_LAYOUT) {
        /* Explicit bridge input must not be mistaken for native auto-layout
           commands delivered while the player is handling another message. */
        if (wp == 1) ToggleSidebar(h);
        Apply(h); return 1;
    }
    if (msg == WM_COMMAND && LOWORD(wp) == CMD_SMALL) { PostMessageW((HWND)GetPropW(h, PROP(L"Callback")), MSG_SMALL, 0, 0); return 0; }
    int fullscreenCommand = msg == WM_COMMAND && LOWORD(wp) == 10013;
    if (fullscreenCommand) {
        if (!Fullscreen(h)) {
            RECT bounds; GetWindowRect(h, &bounds);
            Put(h, PROP(L"RestoreX"), bounds.left); Put(h, PROP(L"RestoreY"), bounds.top);
            Put(h, PROP(L"RestoreWidth"), bounds.right - bounds.left); Put(h, PROP(L"RestoreHeight"), bounds.bottom - bounds.top);
            Put(h, PROP(L"RestoreValid"), 1);
        }
        else if (Value(h, PROP(L"RestoreValid"))) Put(h, PROP(L"Restoring"), 1);
    }
    if (msg == WM_WINDOWPOSCHANGING) ConstrainOuterPosition(h, (WINDOWPOS *)lp);
    if (msg == WM_COMMAND && LOWORD(wp) == 10011) {
        /* Native size/edge-hover requests cannot change intent. Do not call the
           original fullscreen command: it also warps the pointer. */
        if (Value(h, PROP(L"NativeDepth")) || Value(h, PROP(L"NativeLayout")) || Value(h, PROP(L"Sizing")) || Value(h, PROP(L"Applying"))) return 0;
        ToggleSidebar(h); Apply(h); return 0;
    }
    if (msg == WM_ENTERSIZEMOVE) Put(h, PROP(L"Sizing"), 1);
    if (msg == WM_EXITSIZEMOVE) RemovePropW(h, PROP(L"Sizing"));
    if (msg == WM_SIZING && Value(h, PROP(L"Ratio")) && Value(h, PROP(L"Small"))) {
        ConstrainAspect(h, wp, (RECT *)lp); Count(h, PROP(L"AspectResizeCount")); return TRUE;
    }
    int layout = msg == WM_SIZE || msg == WM_WINDOWPOSCHANGED || msg == WM_TIMER || msg == WM_MOUSEMOVE;
    if (layout) {
        Count(h, PROP(L"NativeLayout")); Policy(h);
        if (Value(h, PROP(L"Open")) != Value(h, PROP(L"NativeOpen"))) Put(h, PROP(L"LayoutPending"), 1);
    }
    Count(h, PROP(L"NativeDepth"));
    LRESULT result = CallWindowProcW(original, h, msg, wp, lp);
    Put(h, PROP(L"NativeDepth"), Value(h, PROP(L"NativeDepth")) - 1);
    /* The native handler can revise WINDOWPOS, including maximized bounds. */
    if (msg == WM_WINDOWPOSCHANGING) ConstrainOuterPosition(h, (WINDOWPOS *)lp);
    if (fullscreenCommand) RemovePropW(h, PROP(L"Restoring"));
    if (layout) Put(h, PROP(L"NativeLayout"), Value(h, PROP(L"NativeLayout")) - 1);
    if (msg == WM_SIZE || msg == WM_WINDOWPOSCHANGED || msg == WM_EXITSIZEMOVE || Value(h, PROP(L"LayoutPending"))) { RemovePropW(h, PROP(L"LayoutPending")); Apply(h); }
    return result;
}
__declspec(dllexport) LRESULT CALLBACK LayoutHook(int code, WPARAM wp, LPARAM lp)
{
    if (code >= 0) {
        CWPSTRUCT *call = (CWPSTRUCT *)lp;
        if (call->message == MSG_LAYOUT && GetPropW(call->hwnd, PROP(L"Attach")) && !GetPropW(call->hwnd, PROP(L"Original"))) {
            HMODULE module; GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, (LPCWSTR)LayoutHook, &module);
            SetPropW(call->hwnd, PROP(L"Original"), (HANDLE)SetWindowLongPtrW(call->hwnd, GWLP_WNDPROC, (LONG_PTR)LayoutProc));
        }
    }
    return CallNextHookEx(0, code, wp, lp);
}
