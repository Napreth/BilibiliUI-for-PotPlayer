#define UNICODE
#define _UNICODE
#include <windows.h>
#include <wchar.h>
#include <stdint.h>
#include <stdio.h>
static HINSTANCE instance;
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID reserved)
{if(reason==DLL_PROCESS_ATTACH){instance=module;DisableThreadLibraryCalls(module);}return TRUE;}
__declspec(dllexport) int native_test_trace(const char* text)
{
    char data[4096];unsigned n=0;SIZE_T read;
    while(n<4094){if(!ReadProcessMemory(GetCurrentProcess(),text+n,data+n,1,&read)||read!=1)return 0;if(!data[n])break;n++;}data[n++]='\r';data[n++]='\n';
    WCHAR path[MAX_PATH];if(!GetModuleFileNameW(instance,path,MAX_PATH))return 0;WCHAR* tail=wcsrchr(path,L'\\');if(!tail)return 0;wcscpy(tail+1,L"native-test-trace.txt");
    HANDLE file=CreateFileW(path,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,NULL,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,NULL);if(file==INVALID_HANDLE_VALUE)return 0;DWORD written;WriteFile(file,data,n,&written,NULL);
    if(n>=4 && data[0]=='C' && data[1]=='A' && data[2]=='L' && data[3]=='L') {
        HMODULE bridge=GetModuleHandleW(L"BilibiliTranslation.dll");
        uint64_t (*context)(int)=bridge?(void*)GetProcAddress(bridge,"bili_translation_context"):NULL;
        char debug[180];int bytes=snprintf(debug,sizeof(debug),"BRIDGE %p PROC %p CONTEXT %llu PID %lu\r\n",(void*)bridge,(void*)context,(unsigned long long)(context?context(0):0),(unsigned long)GetCurrentProcessId());
        if(bytes>0)WriteFile(file,debug,bytes,&written,NULL);
    }
    CloseHandle(file);return 1;
}
