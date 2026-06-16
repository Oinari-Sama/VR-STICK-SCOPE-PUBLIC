#pragma once

#include "correction_lut.h"
#include <string>

struct Profile {
    std::string id;
    std::string name;
    std::string created_at;
    std::string notes;
    std::string steamvr_version;
    std::string vd_version;

    CorrectionLUT left_lut;
    CorrectionLUT right_lut;

    bool needsRediagnosis = false;

    bool load(const std::string& path);
    bool save(const std::string& path) const;

    static Profile createNew(const std::string& name);

private:
    static std::string generateUUID();
    static std::string nowISO8601();
};
