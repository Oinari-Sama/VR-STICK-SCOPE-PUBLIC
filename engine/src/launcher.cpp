#include <windows.h>

#include <filesystem>
#include <string>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    wchar_t modulePath[MAX_PATH]{};
    DWORD length = GetModuleFileNameW(nullptr, modulePath, MAX_PATH);
    if (length == 0 || length == MAX_PATH) {
        MessageBoxW(nullptr, L"Could not find launcher path.", L"VR Stick Scope", MB_OK | MB_ICONERROR);
        return 1;
    }

    std::filesystem::path root(modulePath);
    root.remove_filename();

    const std::filesystem::path appDir = root / L"app";
    const std::filesystem::path guiExe = appDir / L"DiagnosticGUI.exe";

    if (!std::filesystem::exists(guiExe)) {
        MessageBoxW(nullptr, L"app\\DiagnosticGUI.exe was not found. Please extract the whole ZIP before running.", L"VR Stick Scope", MB_OK | MB_ICONERROR);
        return 1;
    }

    HINSTANCE result = ShellExecuteW(
        nullptr,
        L"open",
        guiExe.c_str(),
        nullptr,
        appDir.c_str(),
        SW_SHOWNORMAL);

    if (reinterpret_cast<intptr_t>(result) <= 32) {
        MessageBoxW(nullptr, L"Failed to start app\\DiagnosticGUI.exe.", L"VR Stick Scope", MB_OK | MB_ICONERROR);
        return 1;
    }

    return 0;
}
