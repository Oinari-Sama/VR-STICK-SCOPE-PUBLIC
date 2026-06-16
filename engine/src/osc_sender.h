#pragma once
#include <string>

class OscSender {
public:
    OscSender();
    ~OscSender();

    bool init(const std::string& host, int port);
    void sendFloat(const std::string& address, float value);

private:
    unsigned long long socketHandle; // Use generic type to avoid windows.h in header
};
