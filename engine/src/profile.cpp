#include "profile.h"

#include <ctime>
#include <fstream>
#include <iomanip>
#include <random>
#include <sstream>

static std::string extractJsonObject(const std::string& json, const std::string& key) {
    auto keyPos = json.find('"' + key + '"');
    if (keyPos == std::string::npos) return {};
    auto colon = json.find(':', keyPos);
    if (colon == std::string::npos) return {};
    auto objStart = colon + 1;
    while (objStart < json.size() && (json[objStart] == ' ' || json[objStart] == '\t' ||
           json[objStart] == '\r' || json[objStart] == '\n')) {
        ++objStart;
    }
    if (objStart >= json.size() || json[objStart] != '{') return {};

    int depth = 0;
    bool inString = false;
    bool escaped = false;
    for (size_t i = objStart; i < json.size(); ++i) {
        char ch = json[i];
        if (escaped) {
            escaped = false;
            continue;
        }
        if (ch == '\\' && inString) {
            escaped = true;
            continue;
        }
        if (ch == '"') {
            inString = !inString;
            continue;
        }
        if (inString) continue;

        if (ch == '{') ++depth;
        else if (ch == '}' && --depth == 0) {
            return json.substr(objStart, i - objStart + 1);
        }
    }
    return {};
}

Profile Profile::createNew(const std::string& name) {
    Profile p;
    p.id = generateUUID();
    p.name = name;
    p.created_at = nowISO8601();
    return p;
}

bool Profile::save(const std::string& path) const {
    std::ofstream f(path);
    if (!f) return false;

    f << "{\n";
    f << "  \"id\": \"" << id << "\",\n";
    f << "  \"name\": \"" << name << "\",\n";
    f << "  \"created_at\": \"" << created_at << "\",\n";
    f << "  \"notes\": \"" << notes << "\",\n";
    f << "  \"steamvr_version\": \"" << steamvr_version << "\",\n";
    f << "  \"vd_version\": \"" << vd_version << "\",\n";
    f << "  \"needs_rediagnosis\": " << (needsRediagnosis ? "true" : "false") << ",\n";
    f << "  \"left_lut\": " << left_lut.toJson() << ",\n";
    f << "  \"right_lut\": " << right_lut.toJson() << "\n";
    f << "}\n";
    return true;
}

bool Profile::load(const std::string& path) {
    std::ifstream f(path);
    if (!f) return false;
    std::ostringstream ss;
    ss << f.rdbuf();
    std::string json = ss.str();

    auto extractStr = [&](const std::string& key) -> std::string {
        auto pos = json.find('"' + key + '"');
        if (pos == std::string::npos) return {};
        auto colon = json.find(':', pos);
        if (colon == std::string::npos) return {};
        pos = json.find('"', colon + 1);
        if (pos == std::string::npos) return {};
        std::string out;
        bool escaped = false;
        for (size_t i = pos + 1; i < json.size(); ++i) {
            char ch = json[i];
            if (escaped) {
                switch (ch) {
                case 'n': out += '\n'; break;
                case 'r': out += '\r'; break;
                case 't': out += '\t'; break;
                default: out += ch; break;
                }
                escaped = false;
                continue;
            }
            if (ch == '\\') {
                escaped = true;
                continue;
            }
            if (ch == '"') return out;
            out += ch;
        }
        return {};
    };
    auto extractBool = [&](const std::string& key) -> bool {
        auto pos = json.find('"' + key + '"');
        if (pos == std::string::npos) return false;
        pos = json.find(':', pos);
        if (pos == std::string::npos) return false;
        ++pos;
        while (pos < json.size() && (json[pos] == ' ' || json[pos] == '\t' ||
               json[pos] == '\r' || json[pos] == '\n')) {
            ++pos;
        }
        return json.substr(pos, 4) == "true";
    };

    id = extractStr("id");
    name = extractStr("name");
    created_at = extractStr("created_at");
    notes = extractStr("notes");
    steamvr_version = extractStr("steamvr_version");
    vd_version = extractStr("vd_version");
    needsRediagnosis = extractBool("needs_rediagnosis");

    auto leftJson = extractJsonObject(json, "left_lut");
    if (!leftJson.empty()) left_lut.fromJson(leftJson);

    auto rightJson = extractJsonObject(json, "right_lut");
    if (!rightJson.empty()) right_lut.fromJson(rightJson);

    return true;
}

std::string Profile::generateUUID() {
    std::random_device rd;
    std::mt19937_64 gen(rd());
    std::uniform_int_distribution<uint64_t> dis;
    uint64_t a = dis(gen), b = dis(gen);
    a = (a & 0xFFFFFFFFFFFF0FFFULL) | 0x0000000000004000ULL;
    b = (b & 0x3FFFFFFFFFFFFFFFULL) | 0x8000000000000000ULL;
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    ss << std::setw(8) << (a >> 32);
    ss << "-" << std::setw(4) << ((a >> 16) & 0xFFFF);
    ss << "-" << std::setw(4) << (a & 0xFFFF);
    ss << "-" << std::setw(4) << (b >> 48);
    ss << "-" << std::setw(12) << (b & 0x0000FFFFFFFFFFFFULL);
    return ss.str();
}

std::string Profile::nowISO8601() {
    auto t = std::time(nullptr);
    std::tm tm;
    gmtime_s(&tm, &t);
    std::ostringstream ss;
    ss << std::put_time(&tm, "%Y-%m-%dT%H:%M:%SZ");
    return ss.str();
}
