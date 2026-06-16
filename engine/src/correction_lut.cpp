#include "correction_lut.h"

#include <cmath>
#include <sstream>
#include <string>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

CorrectionLUT::CorrectionLUT() = default;

int CorrectionLUT::angleIndex(float x, float y) {
    if (x == 0.0f && y == 0.0f) return 0;
    float deg = std::atan2(y, x) * (180.0f / static_cast<float>(M_PI));
    int idx = static_cast<int>(std::round(deg)) % BINS;
    if (idx < 0) idx += BINS;
    return idx;
}

void CorrectionLUT::apply(float rx, float ry, float& cx, float& cy) const {
    if (strength_ <= 0.0f) {
        cx = rx;
        cy = ry;
        return;
    }

    float r = std::sqrt(rx * rx + ry * ry);
    if (r < 1e-6f) {
        cx = rx;
        cy = ry;
        return;
    }

    const LutEntry& e = entries_[angleIndex(rx, ry)];
    float theta = std::atan2(ry, rx);
    float corrAngleRad = e.angle_offset * static_cast<float>(M_PI) / 180.0f;
    float corrTheta = theta + corrAngleRad;
    float corrR = r * e.radius_scale;

    float bx = corrR * std::cos(corrTheta);
    float by = corrR * std::sin(corrTheta);
    bx -= e.x_cross * ry;
    by -= e.y_cross * rx;

    cx = rx + (bx - rx) * strength_;
    cy = ry + (by - ry) * strength_;

    auto clamp = [](float v) {
        return v < -1.0f ? -1.0f : (v > 1.0f ? 1.0f : v);
    };
    cx = clamp(cx);
    cy = clamp(cy);
}

void CorrectionLUT::setEntry(int deg, const LutEntry& e) {
    if (deg < 0 || deg >= BINS) return;
    entries_[deg] = e;
}

const LutEntry& CorrectionLUT::getEntry(int deg) const {
    return entries_[deg >= 0 && deg < BINS ? deg : 0];
}

void CorrectionLUT::smooth(int window) {
    auto copy = entries_;
    int half = window / 2;
    for (int i = 0; i < BINS; ++i) {
        float rs = 0.0f, ao = 0.0f, xc = 0.0f, yc = 0.0f, w = 0.0f;
        for (int d = -half; d <= half; ++d) {
            int j = (i + d + BINS) % BINS;
            float weight = 1.0f - std::abs(d) / static_cast<float>(half + 1);
            rs += copy[j].radius_scale * weight;
            ao += copy[j].angle_offset * weight;
            xc += copy[j].x_cross * weight;
            yc += copy[j].y_cross * weight;
            w += weight;
        }
        entries_[i] = {rs / w, ao / w, xc / w, yc / w};
    }
}

std::string CorrectionLUT::toJson() const {
    std::ostringstream ss;
    ss << "{\"strength\":" << strength_ << ",\"entries\":[";
    for (int i = 0; i < BINS; ++i) {
        const auto& e = entries_[i];
        ss << "{\"rs\":" << e.radius_scale
           << ",\"ao\":" << e.angle_offset
           << ",\"xc\":" << e.x_cross
           << ",\"yc\":" << e.y_cross << "}";
        if (i + 1 < BINS) ss << ",";
    }
    ss << "]}";
    return ss.str();
}

bool CorrectionLUT::fromJson(const std::string& json) {
    auto readFloat = [](const std::string& text, const std::string& key, float& out) {
        auto pos = text.find('"' + key + '"');
        if (pos == std::string::npos) return false;
        pos = text.find(':', pos);
        if (pos == std::string::npos) return false;
        try {
            out = std::stof(text.substr(pos + 1));
            return true;
        } catch (...) {
            return false;
        }
    };

    auto pos = json.find("\"strength\"");
    if (pos != std::string::npos) {
        pos = json.find(':', pos);
        if (pos != std::string::npos) {
            try {
                strength_ = std::stof(json.substr(pos + 1));
            } catch (...) {
            }
        }
    }

    auto arrPos = json.find("\"entries\"");
    if (arrPos == std::string::npos) return true;
    arrPos = json.find('[', arrPos);
    if (arrPos == std::string::npos) return false;

    int binIdx = 0;
    size_t cursor = arrPos + 1;
    while (binIdx < BINS && cursor < json.size()) {
        auto objStart = json.find('{', cursor);
        if (objStart == std::string::npos) break;

        int depth = 0;
        size_t objEnd = std::string::npos;
        for (size_t i = objStart; i < json.size(); ++i) {
            if (json[i] == '{') ++depth;
            else if (json[i] == '}' && --depth == 0) {
                objEnd = i;
                break;
            }
        }
        if (objEnd == std::string::npos) break;

        std::string obj = json.substr(objStart, objEnd - objStart + 1);
        LutEntry entry;
        readFloat(obj, "rs", entry.radius_scale);
        readFloat(obj, "ao", entry.angle_offset);
        readFloat(obj, "xc", entry.x_cross);
        readFloat(obj, "yc", entry.y_cross);
        setEntry(binIdx++, entry);
        cursor = objEnd + 1;
    }

    return binIdx > 0;
}
