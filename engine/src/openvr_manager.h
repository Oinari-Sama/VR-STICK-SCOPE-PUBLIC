#pragma once

#include "shared_types.h"
#include <cstdint>
#include <string>

namespace vr {
class IVRSystem;
}

class OpenVRManager {
public:
    OpenVRManager();
    ~OpenVRManager();

    bool connect();
    void disconnect();
    bool isConnected() const { return connected_; }

    bool poll(EngineState& state);
    void refreshControllerIndices();
    std::string getStatusString() const;

private:
    vr::IVRSystem* vrSystem_ = nullptr;
    bool connected_ = false;
    uint32_t leftIndex_ = 0xFFFFFFFF;
    uint32_t rightIndex_ = 0xFFFFFFFF;
    int leftAxisIndex_ = -1;
    int rightAxisIndex_ = -1;

    bool isVRStickScopeDevice(uint32_t deviceIndex) const;
    uint32_t findPhysicalController(int role) const;
    int selectStickAxis(uint32_t deviceIndex, int previousAxis) const;
    void readStick(uint32_t deviceIndex, int& axisIndex, float& x, float& y, bool& touched);
};
