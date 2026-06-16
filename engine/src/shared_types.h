#pragma once
#include <cstdint>
#include <string>

// IPC越しに共有するデータ構造
struct StickRaw {
    float x = 0.0f;
    float y = 0.0f;
};

struct StickCorrected {
    float x = 0.0f;
    float y = 0.0f;
};

struct ControllerState {
    StickRaw     raw;
    StickCorrected corrected;
    bool         touched = false;
};

struct EngineState {
    ControllerState left;
    ControllerState right;
    bool correction_enabled = false;
    std::string engine_status; // "disconnected", "connected", "error"
    int64_t timestamp_ms = 0;
};

enum class Side { Left, Right };
