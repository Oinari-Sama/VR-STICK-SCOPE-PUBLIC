#pragma once

#include <atomic>
#include <functional>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include <windows.h>

class IpcServer {
public:
    static constexpr wchar_t PIPE_NAME[] = L"\\\\.\\pipe\\InariKontrollerEngine";

    using CommandCallback = std::function<void(const std::string& jsonCmd)>;

    IpcServer() = default;
    ~IpcServer();

    bool start(CommandCallback onCommand);
    void stop();
    void broadcast(const std::string& jsonState);

    bool isClientConnected() const { return clientCount_.load() > 0; }

private:
    void acceptLoop();
    void clientLoop(HANDLE pipe);
    void removeClient(HANDLE pipe);

    std::thread acceptThread_;
    std::atomic<bool> running_{false};
    std::atomic<int> clientCount_{0};
    HANDLE stopEvent_{NULL};
    CommandCallback commandCallback_;

    mutable std::mutex clientsMutex_;
    std::vector<HANDLE> clients_;
};
