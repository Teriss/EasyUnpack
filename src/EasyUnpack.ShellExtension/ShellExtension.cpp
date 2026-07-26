#include <windows.h>
#include <shobjidl_core.h>
#include <shellapi.h>
#include <shlwapi.h>
#include <sddl.h>
#include <wrl.h>
#include <wrl/implements.h>

#include <atomic>
#include <string>
#include <thread>
#include <vector>

using namespace Microsoft::WRL;

namespace
{
    constexpr wchar_t kCommandTitle[] = L"\u4F7F\u7528 EasyUnpack \u81EA\u52A8\u89E3\u538B";
    constexpr wchar_t kCommandClsid[] = L"{A7B99305-3DA8-4EAB-965E-72070CDBA1A8}";
    std::atomic_ulong g_activeWorkers = 0;
    HINSTANCE g_instance = nullptr;

    std::wstring GetModuleDirectory()
    {
        wchar_t path[MAX_PATH]{};
        const auto length = GetModuleFileNameW(g_instance, path, ARRAYSIZE(path));
        if (length == 0 || length == ARRAYSIZE(path)) return {};

        std::wstring result(path, length);
        const auto separator = result.find_last_of(L'\\');
        return separator == std::wstring::npos ? std::wstring{} : result.substr(0, separator);
    }

    std::wstring CreatePipeName()
    {
        GUID guid{};
        if (CoCreateGuid(&guid) != S_OK) return {};
        wchar_t guidText[40]{};
        if (StringFromGUID2(guid, guidText, ARRAYSIZE(guidText)) == 0) return {};

        std::wstring name = L"EasyUnpack.Selection.";
        name += guidText;
        return name;
    }

    bool WriteAll(HANDLE pipe, const void* buffer, DWORD bytes)
    {
        const auto* current = static_cast<const BYTE*>(buffer);
        while (bytes > 0)
        {
            DWORD written = 0;
            if (!WriteFile(pipe, current, bytes, &written, nullptr) || written == 0) return false;
            current += written;
            bytes -= written;
        }
        return true;
    }

    void SendSelectionToApplication(std::vector<std::wstring> paths)
    {
        const auto decrement = [] { g_activeWorkers.fetch_sub(1); };

        const auto pipeName = CreatePipeName();
        const auto moduleDirectory = GetModuleDirectory();
        if (pipeName.empty() || moduleDirectory.empty())
        {
            decrement();
            return;
        }

        const std::wstring fullPipeName = L"\\\\.\\pipe\\" + pipeName;
        PSECURITY_DESCRIPTOR securityDescriptor = nullptr;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:P(A;;GA;;;OW)", SDDL_REVISION_1, &securityDescriptor, nullptr))
        {
            decrement();
            return;
        }
        SECURITY_ATTRIBUTES securityAttributes{};
        securityAttributes.nLength = sizeof(securityAttributes);
        securityAttributes.lpSecurityDescriptor = securityDescriptor;
        HANDLE pipe = CreateNamedPipeW(
            fullPipeName.c_str(), PIPE_ACCESS_OUTBOUND, PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1, 0, 64 * 1024, 0, &securityAttributes);
        LocalFree(securityDescriptor);
        if (pipe == INVALID_HANDLE_VALUE)
        {
            decrement();
            return;
        }

        const std::wstring applicationPath = moduleDirectory + L"\\EasyUnpack.App.exe";
        const std::wstring parameters = L"--pipe \"" + pipeName + L"\"";
        const auto started = reinterpret_cast<INT_PTR>(ShellExecuteW(nullptr, L"open", applicationPath.c_str(), parameters.c_str(), moduleDirectory.c_str(), SW_SHOWNORMAL)) > 32;
        if (!started)
        {
            CloseHandle(pipe);
            decrement();
            return;
        }

        const BOOL connected = ConnectNamedPipe(pipe, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED;
        if (connected)
        {
            const auto count = static_cast<DWORD>(paths.size());
            if (WriteAll(pipe, &count, sizeof(count)))
            {
                for (const auto& path : paths)
                {
                    const auto length = static_cast<DWORD>(path.size());
                    if (!WriteAll(pipe, &length, sizeof(length)) || !WriteAll(pipe, path.data(), length * sizeof(wchar_t))) break;
                }
            }
        }

        FlushFileBuffers(pipe);
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
        decrement();
    }
}

class __declspec(uuid("A7B99305-3DA8-4EAB-965E-72070CDBA1A8")) EasyUnpackCommand final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) override { return SHStrDupW(kCommandTitle, title); }
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override { *icon = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* toolTip) override { *toolTip = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCanonicalName(GUID* canonicalName) override { return CLSIDFromString(kCommandClsid, canonicalName); }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = ECF_DEFAULT; return S_OK; }
    IFACEMETHODIMP GetState(IShellItemArray* selection, BOOL, EXPCMDSTATE* state) override
    {
        *state = selection == nullptr ? ECS_HIDDEN : ECS_ENABLED;
        return S_OK;
    }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override { *commands = nullptr; return E_NOTIMPL; }

    IFACEMETHODIMP Invoke(IShellItemArray* selection, IBindCtx*) override
    {
        if (selection == nullptr) return E_INVALIDARG;

        DWORD count = 0;
        if (FAILED(selection->GetCount(&count)) || count == 0) return E_INVALIDARG;

        std::vector<std::wstring> paths;
        paths.reserve(count);
        for (DWORD index = 0; index < count; ++index)
        {
            ComPtr<IShellItem> item;
            if (FAILED(selection->GetItemAt(index, &item))) continue;

            PWSTR path = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path != nullptr)
            {
                paths.emplace_back(path);
                CoTaskMemFree(path);
            }
        }

        if (!paths.empty())
        {
            g_activeWorkers.fetch_add(1);
            try
            {
                std::thread(SendSelectionToApplication, std::move(paths)).detach();
            }
            catch (...)
            {
                g_activeWorkers.fetch_sub(1);
                return E_FAIL;
            }
        }
        return S_OK;
    }
};

CoCreatableClass(EasyUnpackCommand)
CoCreatableClassWrlCreatorMapInclude(EasyUnpackCommand)

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) g_instance = instance;
    return TRUE;
}

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID classId, REFIID interfaceId, LPVOID* object)
{
    return Module<InProc>::GetModule().GetClassObject(classId, interfaceId, object);
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return g_activeWorkers.load() == 0 && Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}
