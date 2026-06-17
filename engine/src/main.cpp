#include "correction_lut.h"
#include "ipc_server.h"
#include "openvr_manager.h"
#include "profile.h"
#include "shared_types.h"
#include "osc_sender.h"

#include <openvr.h>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <thread>
#include <atomic>
#include <mutex>
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <cstdlib>

static constexpr const char* kSteamVrAppKey = "com.oinarisama.inarikontroller.engine";

static OpenVRManager g_vr;
static IpcServer g_ipc;
static CorrectionLUT g_lutLeft;
static CorrectionLUT g_lutRight;
static std::atomic<bool> g_correctionEnabled{false};
static Profile g_currentProfile;
static std::mutex g_profileMutex;
static std::filesystem::path g_profileDir;
static OscSender g_oscSender;
static bool g_oscEnabled = false;
static std::atomic<bool> g_running{true};

static std::filesystem::path getLocalAppData() {
    PWSTR path = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, NULL, &path))) {
        std::filesystem::path result = path;
        CoTaskMemFree(path);
        return result;
    }
    wchar_t buf[MAX_PATH];
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", buf, MAX_PATH)) return std::filesystem::path(buf);
    return std::filesystem::temp_directory_path();
}

static void initProfileDir() {
    g_profileDir = getLocalAppData() / "InariKontroller" / "Profiles";
    std::filesystem::create_directories(g_profileDir);
}

static bool loadLatestProfile() {
    std::filesystem::path latestPath;
    std::filesystem::file_time_type latestTime{};

    std::error_code ec;
    if (!std::filesystem::exists(g_profileDir, ec)) return false;

    for (const auto& entry : std::filesystem::directory_iterator(g_profileDir, ec)) {
        if (ec) break;
        if (!entry.is_regular_file(ec) || entry.path().extension() != ".json") continue;
        auto writeTime = entry.last_write_time(ec);
        if (ec) continue;
        if (latestPath.empty() || writeTime > latestTime) {
            latestPath = entry.path();
            latestTime = writeTime;
        }
    }

    if (latestPath.empty()) return false;

    Profile profile;
    if (!profile.load(latestPath.string())) return false;

    std::lock_guard<std::mutex> lock(g_profileMutex);
    g_currentProfile = profile;
    g_lutLeft = profile.left_lut;
    g_lutRight = profile.right_lut;
    return true;
}

static void writeAutostartLog(const std::string& line) {
    auto dir = getLocalAppData() / "InariKontroller";
    std::filesystem::create_directories(dir);
    std::ofstream log(dir / "autostart.log", std::ios::app);
    if (log) log << line << "\n";
}

static std::filesystem::path getExecutablePath() {
    wchar_t buffer[MAX_PATH] = {};
    DWORD len = GetModuleFileNameW(nullptr, buffer, MAX_PATH);
    if (len == 0 || len >= MAX_PATH) return {};
    return std::filesystem::path(buffer);
}

static std::string jsonEscape(const std::string& text) {
    std::string out;
    out.reserve(text.size());
    for (char ch : text) {
        switch (ch) {
        case '\\': out += "\\\\"; break;
        case '"': out += "\\\""; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default: out += ch; break;
        }
    }
    return out;
}

static void resetOscOutputs() {
    if (g_oscEnabled) {
        g_oscSender.resetVrChatInputs();
    }
}

static std::filesystem::path writeRuntimeManifest() {
    auto exePath = getExecutablePath();
    if (exePath.empty()) return {};

    auto manifestPath = exePath.parent_path() / "InariKontroller_engine.vrmanifest";
    std::ofstream f(manifestPath);
    if (!f) return {};

    f << "{\n";
    f << "  \"source\": \"builtin\",\n";
    f << "  \"applications\": [\n";
    f << "    {\n";
    f << "      \"app_key\": \"" << kSteamVrAppKey << "\",\n";
    f << "      \"launch_type\": \"binary\",\n";
    f << "      \"binary_path_windows\": \"InariKontrollerEngine.exe\",\n";
    f << "      \"arguments\": \"--vrchat-osc --enable-correction\",\n";
    f << "      \"is_dashboard_overlay\": true,\n";
    f << "      \"strings\": {\n";
    f << "        \"en_us\": {\n";
    f << "          \"name\": \"Inari-Kontroller Correction Engine\",\n";
    f << "          \"description\": \"Quest controller stick diagnostics and VRChat OSC correction engine\"\n";
    f << "        }\n";
    f << "      }\n";
    f << "    }\n";
    f << "  ]\n";
    f << "}\n";
    return manifestPath;
}

static int configureSteamVrAutostart(bool enable) {
    auto manifestPath = writeRuntimeManifest();
    if (manifestPath.empty()) {
        writeAutostartLog("failed: could not write runtime manifest");
        return 1;
    }
    writeAutostartLog(std::string(enable ? "install" : "uninstall") + " manifest=" + manifestPath.string());

    vr::EVRInitError initError = vr::VRInitError_None;
    vr::VR_Init(&initError, vr::VRApplication_Utility);
    if (initError != vr::VRInitError_None) {
        writeAutostartLog(std::string("failed: VR_Init ") + vr::VR_GetVRInitErrorAsEnglishDescription(initError));
        return 1;
    }

    auto* apps = vr::VRApplications();
    if (!apps) {
        writeAutostartLog("failed: VRApplications returned null");
        vr::VR_Shutdown();
        return 1;
    }

    if (enable) {
        apps->AddApplicationManifest(manifestPath.string().c_str(), false);
        apps->SetApplicationAutoLaunch(kSteamVrAppKey, true);
        writeAutostartLog("ok: autostart enabled");
    } else {
        apps->SetApplicationAutoLaunch(kSteamVrAppKey, false);
        apps->RemoveApplicationManifest(manifestPath.string().c_str());
        writeAutostartLog("ok: autostart disabled");
    }

    vr::VR_Shutdown();
    return 0;
}

static int querySteamVrAutostart() {
    vr::EVRInitError initError = vr::VRInitError_None;
    vr::VR_Init(&initError, vr::VRApplication_Utility);
    if (initError != vr::VRInitError_None) {
        writeAutostartLog(std::string("status failed: VR_Init ") + vr::VR_GetVRInitErrorAsEnglishDescription(initError));
        return 1;
    }
    auto* apps = vr::VRApplications();
    if (!apps) {
        writeAutostartLog("status failed: VRApplications returned null");
        vr::VR_Shutdown();
        return 1;
    }
    bool enabled = apps->GetApplicationAutoLaunch(kSteamVrAppKey);
    vr::VR_Shutdown();
    return enabled ? 0 : 2;
}

static int handleCommandLineModes() {
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv) return -1;

    int result = -1;
    for (int i = 1; i < argc; ++i) {
        std::wstring arg = argv[i];
        if (arg == L"--install-autostart") {
            result = configureSteamVrAutostart(true);
            break;
        }
        if (arg == L"--uninstall-autostart") {
            result = configureSteamVrAutostart(false);
            break;
        }
        if (arg == L"--autostart-status") {
            result = querySteamVrAutostart();
            break;
        }
    }

    LocalFree(argv);
    return result;
}

static bool commandLineHasFlag(const std::wstring& flag) {
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv) return false;
    bool found = false;
    for (int i = 1; i < argc; ++i) {
        if (argv[i] == flag) {
            found = true;
            break;
        }
    }
    LocalFree(argv);
    return found;
}

static bool parseLutJson(const std::string& json, CorrectionLUT& lut) {
    auto pos = json.find("\"strength\"");
    if (pos != std::string::npos) {
        pos = json.find(':', pos);
        if (pos != std::string::npos) {
            try {
                lut.setStrength(std::stof(json.substr(pos + 1)));
            } catch (...) {
            }
        }
    }

    auto arrPos = json.find("\"entries\"");
    if (arrPos == std::string::npos) return false;
    arrPos = json.find('[', arrPos);
    if (arrPos == std::string::npos) return false;

    int binIdx = 0;
    size_t cursor = arrPos + 1;
    auto readFloat = [&](const std::string& obj, const std::string& key, float& out) {
        auto kpos = obj.find('"' + key + '"');
        if (kpos == std::string::npos) return;
        kpos = obj.find(':', kpos);
        if (kpos == std::string::npos) return;
        try {
            out = std::stof(obj.substr(kpos + 1));
        } catch (...) {
        }
    };

    while (binIdx < CorrectionLUT::BINS && cursor < json.size()) {
        auto objStart = json.find('{', cursor);
        auto objEnd = json.find('}', objStart);
        if (objStart == std::string::npos || objEnd == std::string::npos) break;
        std::string obj = json.substr(objStart, objEnd - objStart + 1);
        LutEntry entry;
        readFloat(obj, "rs", entry.radius_scale);
        readFloat(obj, "ao", entry.angle_offset);
        readFloat(obj, "xc", entry.x_cross);
        readFloat(obj, "yc", entry.y_cross);
        lut.setEntry(binIdx++, entry);
        cursor = objEnd + 1;
    }

    return binIdx > 0;
}

static std::string stateToJson(const EngineState& s) {
    auto ts = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count();

    std::ostringstream ss;
    ss << std::fixed << std::setprecision(6);
    ss << "{\"type\":\"state\",";
    ss << "\"ts\":" << ts << ",";
    ss << "\"left\":{";
    ss << "\"rx\":" << s.left.raw.x << ",";
    ss << "\"ry\":" << s.left.raw.y << ",";
    ss << "\"cx\":" << s.left.corrected.x << ",";
    ss << "\"cy\":" << s.left.corrected.y << ",";
    ss << "\"touched\":" << (s.left.touched ? "true" : "false") << "},";
    ss << "\"right\":{";
    ss << "\"rx\":" << s.right.raw.x << ",";
    ss << "\"ry\":" << s.right.raw.y << ",";
    ss << "\"cx\":" << s.right.corrected.x << ",";
    ss << "\"cy\":" << s.right.corrected.y << ",";
    ss << "\"touched\":" << (s.right.touched ? "true" : "false") << "},";
    ss << "\"correction_enabled\":" << (g_correctionEnabled ? "true" : "false") << ",";
    ss << "\"status\":\"" << s.engine_status << "\"";
    ss << "}";
    return ss.str();
}

static std::string findJsonString(const std::string& json, const std::string& key) {
    auto pos = json.find('"' + key + '"');
    if (pos == std::string::npos) return {};
    pos = json.find('"', json.find(':', pos) + 1);
    if (pos == std::string::npos) return {};
    auto end = json.find('"', pos + 1);
    if (end == std::string::npos) return {};
    return json.substr(pos + 1, end - pos - 1);
}

static void handleCommand(const std::string& json) {
    std::string type = findJsonString(json, "type");

    if (type == "set_correction") {
        auto pos = json.find("\"enabled\"");
        if (pos != std::string::npos) {
            pos = json.find(':', pos);
            if (pos != std::string::npos) {
                ++pos;
                while (pos < json.size() && (json[pos] == ' ' || json[pos] == '\t')) ++pos;
                g_correctionEnabled = (json.substr(pos, 4) == "true");
            }
        }
    } else if (type == "update_lut") {
        std::string side = findJsonString(json, "side");
        auto lutKey = json.find("\"lut\"");
        if (lutKey == std::string::npos) return;

        auto objStart = json.find('{', lutKey + 5);
        if (objStart == std::string::npos) return;

        int depth = 0;
        size_t objEnd = objStart;
        for (size_t i = objStart; i < json.size(); ++i) {
            if (json[i] == '{') ++depth;
            else if (json[i] == '}' && --depth == 0) {
                objEnd = i;
                break;
            }
        }

        std::string lutJson = json.substr(objStart, objEnd - objStart + 1);
        std::lock_guard<std::mutex> lock(g_profileMutex);
        if (side == "left") {
            g_lutLeft.fromJson(lutJson);
            g_currentProfile.left_lut = g_lutLeft;
        } else if (side == "right") {
            g_lutRight.fromJson(lutJson);
            g_currentProfile.right_lut = g_lutRight;
        } else {
            return;
        }

        auto savePath = g_profileDir / (g_currentProfile.id + ".json");
        g_currentProfile.save(savePath.string());
    } else if (type == "load_profile") {
        std::string pid = findJsonString(json, "profile_id");
        if (!pid.empty()) {
            Profile p;
            if (p.load((g_profileDir / (pid + ".json")).string())) {
                std::lock_guard<std::mutex> lock(g_profileMutex);
                g_currentProfile = p;
                g_lutLeft = p.left_lut;
                g_lutRight = p.right_lut;
            }
        }
    } else if (type == "shutdown") {
        resetOscOutputs();
        g_running = false;
    }
}

int WINAPI WinMain(HINSTANCE, HINSTANCE, LPSTR, int) {
    int commandModeResult = handleCommandLineModes();
    if (commandModeResult >= 0) return commandModeResult;

    if (commandLineHasFlag(L"--enable-correction")) {
        g_correctionEnabled = true;
    }
    if (const char* env_correction = std::getenv("InariKontroller_CORRECTION_ENABLE")) {
        if (std::string(env_correction) == "1") g_correctionEnabled = true;
    }

    bool enableOsc = commandLineHasFlag(L"--vrchat-osc");
    if (const char* env_osc = std::getenv("InariKontroller_OSC_ENABLE")) {
        if (std::string(env_osc) == "1") {
            enableOsc = true;
        }
    }

    if (enableOsc) {
            std::string osc_host = "127.0.0.1";
            int osc_port = 9000;
            if (const char* env_host = std::getenv("InariKontroller_OSC_HOST")) {
                osc_host = env_host;
            }
            if (const char* env_port = std::getenv("InariKontroller_OSC_PORT")) {
                try {
                    osc_port = std::stoi(env_port);
                } catch (...) {}
            }
            g_oscEnabled = g_oscSender.init(osc_host, osc_port);
    }

    initProfileDir();
    g_ipc.start(handleCommand);
    {
        std::lock_guard<std::mutex> lock(g_profileMutex);
        g_currentProfile = Profile::createNew("Default");
    }
    loadLatestProfile();

    bool vrConnected = false;
    auto lastRetry = std::chrono::steady_clock::now() - std::chrono::seconds(5);
    const auto retryInterval = std::chrono::seconds(5);

    while (g_running) {
        if (!vrConnected) {
            auto now = std::chrono::steady_clock::now();
            if (now - lastRetry >= retryInterval) {
                vrConnected = g_vr.connect();
                lastRetry = now;
            }
        }

        EngineState state;
        state.engine_status = g_vr.getStatusString();

        if (vrConnected) {
            if (!g_vr.poll(state)) {
                g_vr.disconnect();
                vrConnected = false;
                state.engine_status = "disconnected";
            } else {
                CorrectionLUT leftLut;
                CorrectionLUT rightLut;
                {
                    std::lock_guard<std::mutex> lock(g_profileMutex);
                    leftLut = g_lutLeft;
                    rightLut = g_lutRight;
                }
                leftLut.apply(state.left.raw.x, state.left.raw.y, state.left.corrected.x, state.left.corrected.y);
                rightLut.apply(state.right.raw.x, state.right.raw.y, state.right.corrected.x, state.right.corrected.y);
                if (!g_correctionEnabled) {
                    state.left.corrected = {state.left.raw.x, state.left.raw.y};
                    state.right.corrected = {state.right.raw.x, state.right.raw.y};
                }
                if (g_oscEnabled) {
                    g_oscSender.sendFloat("/input/Horizontal", state.left.corrected.x);
                    g_oscSender.sendFloat("/input/Vertical", state.left.corrected.y);
                    g_oscSender.sendFloat("/input/LookHorizontal", state.right.corrected.x);
                }
            }
        }

        if (g_ipc.isClientConnected()) {
            g_ipc.broadcast(stateToJson(state));
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(16));
    }

    resetOscOutputs();
    g_ipc.stop();
    g_vr.disconnect();
    return 0;
}

int main() {
    return WinMain(GetModuleHandleW(nullptr), nullptr, GetCommandLineA(), SW_HIDE);
}
