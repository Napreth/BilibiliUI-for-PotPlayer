#define WINVER 0x0A00
#define _WIN32_WINNT 0x0A00
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <wchar.h>
#include <string.h>
/* The native translation extension calls this small, versioned result channel.
   It never performs translation, selects an engine, or reads account settings. */
#include <stdint.h>
#include <tlhelp32.h>
#define TRANS_CLIENT L"BilibiliTranslation.Client"
#define TRANS_TOKEN L"BilibiliTranslation.Token"
#define TRANS_PACKET 0x42545231u
static BOOL CALLBACK TranslationWindow(HWND window, LPARAM value)
{
    DWORD pid; WCHAR name[64];
    GetWindowThreadProcessId(window, &pid);
    if (pid == GetCurrentProcessId() && GetClassNameW(window, name, 64) && !wcsncmp(name, L"PotPlayer", 9)) {
        *(HWND *)value = window; return FALSE;
    }
    return TRUE;
}
static HWND TranslationOwner(void)
{
    HWND owner = NULL; EnumWindows(TranslationWindow, (LPARAM)&owner);
    /* Script workers need not be attached to the UI thread's desktop. Locate
       only windows of this process, without changing any thread's desktop. */
    if (!owner) {
        HANDLE threads = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
        if (threads != INVALID_HANDLE_VALUE) {
            THREADENTRY32 entry = {sizeof(entry)};
            if (Thread32First(threads, &entry)) do {
                if (entry.th32OwnerProcessID == GetCurrentProcessId()) EnumThreadWindows(entry.th32ThreadID, TranslationWindow, (LPARAM)&owner);
            } while (!owner && Thread32Next(threads, &entry));
            CloseHandle(threads);
        }
    }
    return owner;
}
__declspec(dllexport) uint64_t bili_translation_context(int reserved)
{
    HMODULE pinned;
    /* PotPlayer may execute callbacks on workers. Never unload their bridge
       between calls; the operating system releases the pin at process exit. */
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, (LPCWSTR)bili_translation_context, &pinned);
    HWND owner = TranslationOwner();
    SetPropW(owner, L"BilibiliTranslation.ContextCalls", (HANDLE)((UINT_PTR)GetPropW(owner, L"BilibiliTranslation.ContextCalls") + 1));
    HWND client = (HWND)GetPropW(owner, TRANS_CLIENT);
    return IsWindow(client) ? (uint64_t)(UINT_PTR)GetPropW(owner, TRANS_TOKEN) : 0;
}
__declspec(dllexport) int bili_translation_result(uint64_t token, const char *original, const char *translated, const char *metadata, uint32_t sizes, uint32_t metaSize)
{
    uint32_t originalSize = sizes & 0xffff, translatedSize = sizes >> 16;
    if (!token || !originalSize || originalSize > 8192 || !translatedSize || translatedSize > 32768 || metaSize > 4096 || !original || !translated || !metadata) return 0;
    HWND owner = TranslationOwner(), client = (HWND)GetPropW(owner, TRANS_CLIENT);
    SetPropW(owner, L"BilibiliTranslation.ResultCalls", (HANDLE)((UINT_PTR)GetPropW(owner, L"BilibiliTranslation.ResultCalls") + 1));
    if (!IsWindow(client) || (uint64_t)(UINT_PTR)GetPropW(owner, TRANS_TOKEN) != token) return 0;
    uint32_t total = 32 + originalSize + translatedSize + metaSize;
    BYTE *packet = (BYTE *)HeapAlloc(GetProcessHeap(), 0, total);
    if (!packet) return 0;
    uint32_t header[8] = {TRANS_PACKET, 1, (uint32_t)token, (uint32_t)(token >> 32), GetCurrentProcessId(), originalSize, translatedSize, metaSize};
    memcpy(packet, header, 32);
    SIZE_T read;
    int valid = ReadProcessMemory(GetCurrentProcess(), original, packet + 32, originalSize, &read) && read == originalSize;
    valid = valid && ReadProcessMemory(GetCurrentProcess(), translated, packet + 32 + originalSize, translatedSize, &read) && read == translatedSize;
    valid = valid && ReadProcessMemory(GetCurrentProcess(), metadata, packet + 32 + originalSize + translatedSize, metaSize, &read) && read == metaSize;
    DWORD_PTR result = 0;
    if (valid) {
        COPYDATASTRUCT data = {(ULONG_PTR)TRANS_PACKET, total, packet};
        valid = SendMessageTimeoutW(client, WM_COPYDATA, (WPARAM)owner, (LPARAM)&data, SMTO_ABORTIFHUNG | SMTO_BLOCK, 250, &result) != 0 && result == 1;
    }
    HeapFree(GetProcessHeap(), 0, packet); return valid;
}
__declspec(dllexport) int bili_translation_ready(uint64_t token)
{
    HWND owner = TranslationOwner();
    if (!token || (uint64_t)(UINT_PTR)GetPropW(owner, TRANS_TOKEN) != token) return 0;
    SetPropW(owner, L"BilibiliTranslation.Ready", (HANDLE)(UINT_PTR)token); return 1;
}
