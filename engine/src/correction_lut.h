#pragma once

#include <array>
#include <string>

struct LutEntry {
    float radius_scale = 1.0f;
    float angle_offset = 0.0f;
    float x_cross = 0.0f;
    float y_cross = 0.0f;
};

class CorrectionLUT {
public:
    static constexpr int BINS = 360;

    CorrectionLUT();

    void apply(float rx, float ry, float& cx, float& cy) const;
    static int angleIndex(float x, float y);

    void setEntry(int deg, const LutEntry& e);
    const LutEntry& getEntry(int deg) const;
    void smooth(int window = 5);

    std::string toJson() const;
    bool fromJson(const std::string& json);

    void setStrength(float s) { strength_ = s; }
    float strength() const { return strength_; }

private:
    std::array<LutEntry, BINS> entries_;
    float strength_ = 1.0f;
};
