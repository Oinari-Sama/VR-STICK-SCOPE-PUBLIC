#include "openvr_manager.h"

#include <openvr.h>
#include <cmath>
#include <cstdlib>
#include <fstream>
#include <string>

static bool isDebugLogEnabled() {
    static bool enabled = [] {
        const char* value = std::getenv("InariKontroller_OPENVR_DEBUG");
        return value && value[0] != '\0' && value[0] != '0';
    }();
    return enabled;
}

static void writeDebugLog(const std::string& line) {
    if (!isDebugLogEnabled()) return;
    std::ofstream log("InariKontroller_openvr_debug.log", std::ios::app);
    if (log) log << line << "\n";
}

static std::string getDeviceString(vr::IVRSystem* system, uint32_t deviceIndex, vr::ETrackedDeviceProperty prop) {
    char value[256] = {};
    vr::ETrackedPropertyError err = vr::TrackedProp_Success;
    system->GetStringTrackedDeviceProperty(deviceIndex, prop, value, sizeof(value), &err);
    if (err != vr::TrackedProp_Success) return {};
    return value;
}

OpenVRManager::OpenVRManager() = default;

OpenVRManager::~OpenVRManager() {
    disconnect();
}

bool OpenVRManager::connect() {
    if (connected_) return true;

    vr::EVRInitError err = vr::VRInitError_None;
    vrSystem_ = vr::VR_Init(&err, vr::VRApplication_Background);
    if (err != vr::VRInitError_None) {
        vrSystem_ = nullptr;
        return false;
    }

    connected_ = true;
    refreshControllerIndices();
    return true;
}

void OpenVRManager::disconnect() {
    if (!connected_) return;
    vr::VR_Shutdown();
    vrSystem_ = nullptr;
    connected_ = false;
    leftIndex_ = vr::k_unTrackedDeviceIndexInvalid;
    rightIndex_ = vr::k_unTrackedDeviceIndexInvalid;
    leftAxisIndex_ = -1;
    rightAxisIndex_ = -1;
}

void OpenVRManager::refreshControllerIndices() {
    if (!vrSystem_) return;

    writeDebugLog("--- refreshControllerIndices ---");
    for (uint32_t i = 0; i < vr::k_unMaxTrackedDeviceCount; ++i) {
        if (!vrSystem_->IsTrackedDeviceConnected(i)) continue;
        vr::ETrackedPropertyError err = vr::TrackedProp_Success;
        int32_t roleHint = vrSystem_->GetInt32TrackedDeviceProperty(i, vr::Prop_ControllerRoleHint_Int32, &err);
        if (err != vr::TrackedProp_Success) roleHint = -1;

        writeDebugLog(
            "device=" + std::to_string(i) +
            " class=" + std::to_string(static_cast<int>(vrSystem_->GetTrackedDeviceClass(i))) +
            " activeRole=" + std::to_string(static_cast<int>(vrSystem_->GetControllerRoleForTrackedDeviceIndex(i))) +
            " roleHint=" + std::to_string(roleHint) +
            " manufacturer=" + getDeviceString(vrSystem_, i, vr::Prop_ManufacturerName_String) +
            " model=" + getDeviceString(vrSystem_, i, vr::Prop_ModelNumber_String) +
            " serial=" + getDeviceString(vrSystem_, i, vr::Prop_SerialNumber_String));
    }

    leftIndex_ = findPhysicalController(vr::TrackedControllerRole_LeftHand);
    rightIndex_ = findPhysicalController(vr::TrackedControllerRole_RightHand);
    leftAxisIndex_ = -1;
    rightAxisIndex_ = -1;
    writeDebugLog("selected left=" + std::to_string(leftIndex_) + " right=" + std::to_string(rightIndex_));
}

bool OpenVRManager::isInariKontrollerDevice(uint32_t deviceIndex) const {
    if (!vrSystem_ || deviceIndex == vr::k_unTrackedDeviceIndexInvalid) return false;

    char value[128] = {};
    vr::ETrackedPropertyError err = vr::TrackedProp_Success;
    vrSystem_->GetStringTrackedDeviceProperty(
        deviceIndex,
        vr::Prop_ManufacturerName_String,
        value,
        sizeof(value),
        &err);
    if (err == vr::TrackedProp_Success && std::string(value).find("InariKontroller") != std::string::npos) {
        return true;
    }

    value[0] = '\0';
    vrSystem_->GetStringTrackedDeviceProperty(
        deviceIndex,
        vr::Prop_ModelNumber_String,
        value,
        sizeof(value),
        &err);
    return err == vr::TrackedProp_Success && std::string(value).find("InariKontroller") != std::string::npos;
}

uint32_t OpenVRManager::findPhysicalController(int role) const {
    if (!vrSystem_) return vr::k_unTrackedDeviceIndexInvalid;

    for (uint32_t i = 0; i < vr::k_unMaxTrackedDeviceCount; ++i) {
        if (!vrSystem_->IsTrackedDeviceConnected(i)) continue;
        if (vrSystem_->GetTrackedDeviceClass(i) != vr::TrackedDeviceClass_Controller) continue;
        if (static_cast<int>(vrSystem_->GetControllerRoleForTrackedDeviceIndex(i)) != role) continue;
        if (isInariKontrollerDevice(i)) continue;
        return i;
    }

    for (uint32_t i = 0; i < vr::k_unMaxTrackedDeviceCount; ++i) {
        if (!vrSystem_->IsTrackedDeviceConnected(i)) continue;
        if (vrSystem_->GetTrackedDeviceClass(i) != vr::TrackedDeviceClass_Controller) continue;
        if (isInariKontrollerDevice(i)) continue;

        vr::ETrackedPropertyError err = vr::TrackedProp_Success;
        int32_t roleHint = vrSystem_->GetInt32TrackedDeviceProperty(i, vr::Prop_ControllerRoleHint_Int32, &err);
        if (err == vr::TrackedProp_Success && roleHint == role) return i;
    }

    uint32_t fallback = vrSystem_->GetTrackedDeviceIndexForControllerRole(static_cast<vr::ETrackedControllerRole>(role));
    if (isInariKontrollerDevice(fallback)) return vr::k_unTrackedDeviceIndexInvalid;
    return fallback;
}

int OpenVRManager::selectStickAxis(uint32_t deviceIndex, int previousAxis) const {
    if (!vrSystem_ || deviceIndex == vr::k_unTrackedDeviceIndexInvalid) return -1;

    vr::VRControllerState_t state{};
    if (!vrSystem_->GetControllerState(deviceIndex, &state, sizeof(state))) return previousAxis;

    int bestAxis = previousAxis;
    float bestMagnitude = 0.0f;
    for (int i = 0; i < 5; ++i) {
        float magnitude = std::fabs(state.rAxis[i].x) + std::fabs(state.rAxis[i].y);
        if (magnitude > bestMagnitude) {
            bestMagnitude = magnitude;
            bestAxis = i;
        }
    }

    if (bestMagnitude > 0.02f) return bestAxis;
    return previousAxis >= 0 ? previousAxis : 0;
}

void OpenVRManager::readStick(uint32_t deviceIndex, int& axisIndex, float& x, float& y, bool& touched) {
    static int debugReads = 0;

    x = 0.0f;
    y = 0.0f;
    touched = false;
    if (!vrSystem_ || deviceIndex == vr::k_unTrackedDeviceIndexInvalid) return;

    vr::VRControllerState_t state{};
    bool ok = vrSystem_->GetControllerState(deviceIndex, &state, sizeof(state));
    if (!ok) {
        if (debugReads < 80) {
            writeDebugLog("GetControllerState failed device=" + std::to_string(deviceIndex));
            ++debugReads;
        }
        return;
    }

    axisIndex = selectStickAxis(deviceIndex, axisIndex);
    if (axisIndex < 0 || axisIndex >= 5) axisIndex = 0;

    x = state.rAxis[axisIndex].x;
    y = state.rAxis[axisIndex].y;
    float magnitude = std::fabs(x) + std::fabs(y);
    touched = magnitude > 0.02f ||
        (state.ulButtonTouched & vr::ButtonMaskFromId(static_cast<vr::EVRButtonId>(vr::k_EButton_Axis0 + axisIndex))) != 0;

    if (debugReads < 80) {
        std::string line = "state device=" + std::to_string(deviceIndex) +
            " selectedAxis=" + std::to_string(axisIndex) +
            " touched=" + std::to_string(touched ? 1 : 0);
        for (int i = 0; i < 5; ++i) {
            line += " a" + std::to_string(i) + "=(" +
                std::to_string(state.rAxis[i].x) + "," +
                std::to_string(state.rAxis[i].y) + ")";
        }
        writeDebugLog(line);
        ++debugReads;
    }
}

bool OpenVRManager::poll(EngineState& out) {
    if (!vrSystem_ || !connected_) return false;

    static int refreshCounter = 0;
    if (++refreshCounter >= 300) {
        refreshCounter = 0;
        refreshControllerIndices();
    }

    vr::VREvent_t event{};
    while (vrSystem_->PollNextEvent(&event, sizeof(event))) {
        if (event.eventType == vr::VREvent_TrackedDeviceActivated ||
            event.eventType == vr::VREvent_TrackedDeviceDeactivated) {
            refreshControllerIndices();
        }
    }

    float lx = 0.0f, ly = 0.0f, rx = 0.0f, ry = 0.0f;
    bool lTouched = false;
    bool rTouched = false;
    readStick(leftIndex_, leftAxisIndex_, lx, ly, lTouched);
    readStick(rightIndex_, rightAxisIndex_, rx, ry, rTouched);

    out.left.raw = {lx, ly};
    out.right.raw = {rx, ry};
    out.left.touched = lTouched;
    out.right.touched = rTouched;
    return true;
}

std::string OpenVRManager::getStatusString() const {
    if (!connected_) return "disconnected";
    bool lOk = leftIndex_ != vr::k_unTrackedDeviceIndexInvalid;
    bool rOk = rightIndex_ != vr::k_unTrackedDeviceIndexInvalid;
    if (lOk && rOk) return "connected";
    if (lOk) return "left_only";
    if (rOk) return "right_only";
    return "no_controllers";
}
