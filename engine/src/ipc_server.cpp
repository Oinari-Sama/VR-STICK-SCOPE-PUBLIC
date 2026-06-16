#include "ipc_server.h"

#include <algorithm>
#include <cstdint>
#include <vector>

namespace {
bool writePipeWithTimeout(HANDLE pipe, const void* data, DWORD len, HANDLE stopEvent) {
    const char* cursor = static_cast<const char*>(data);
    DWORD remaining = len;
    while (remaining > 0) {
        OVERLAPPED overlapped = {};
        overlapped.hEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
        if (!overlapped.hEvent) return false;

        DWORD written = 0;
        BOOL ok = WriteFile(pipe, cursor, remaining, &written, &overlapped);
        if (!ok) {
            DWORD err = GetLastError();
            if (err != ERROR_IO_PENDING) {
                CloseHandle(overlapped.hEvent);
                return false;
            }

            HANDLE waitHandles[2] = { overlapped.hEvent, stopEvent };
            DWORD wait = WaitForMultipleObjects(stopEvent ? 2 : 1, waitHandles, FALSE, 20);
            if (wait != WAIT_OBJECT_0) {
                CancelIoEx(pipe, &overlapped);
                CloseHandle(overlapped.hEvent);
                return false;
            }
            if (!GetOverlappedResult(pipe, &overlapped, &written, FALSE)) {
                CloseHandle(overlapped.hEvent);
                return false;
            }
        }

        CloseHandle(overlapped.hEvent);
        if (written == 0 || written > remaining) return false;
        cursor += written;
        remaining -= written;
    }
    return true;
}

bool readPipeCancelable(HANDLE pipe, void* data, DWORD len, HANDLE stopEvent) {
    char* cursor = static_cast<char*>(data);
    DWORD remaining = len;
    while (remaining > 0) {
        OVERLAPPED overlapped = {};
        overlapped.hEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
        if (!overlapped.hEvent) return false;

        DWORD bytesRead = 0;
        BOOL ok = ReadFile(pipe, cursor, remaining, &bytesRead, &overlapped);
        if (!ok) {
            DWORD err = GetLastError();
            if (err != ERROR_IO_PENDING) {
                CloseHandle(overlapped.hEvent);
                return false;
            }

            HANDLE waitHandles[2] = { overlapped.hEvent, stopEvent };
            DWORD wait = WaitForMultipleObjects(stopEvent ? 2 : 1, waitHandles, FALSE, INFINITE);
            if (wait != WAIT_OBJECT_0) {
                CancelIoEx(pipe, &overlapped);
                CloseHandle(overlapped.hEvent);
                return false;
            }
            if (!GetOverlappedResult(pipe, &overlapped, &bytesRead, FALSE)) {
                CloseHandle(overlapped.hEvent);
                return false;
            }
        }

        CloseHandle(overlapped.hEvent);
        if (bytesRead == 0 || bytesRead > remaining) return false;
        cursor += bytesRead;
        remaining -= bytesRead;
    }
    return true;
}
}

IpcServer::~IpcServer() {
    stop();
}

bool IpcServer::start(CommandCallback onCommand) {
    if (running_) return true;
    commandCallback_ = onCommand;
    stopEvent_ = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (!stopEvent_) return false;
    running_ = true;
    acceptThread_ = std::thread(&IpcServer::acceptLoop, this);
    return true;
}

void IpcServer::stop() {
    if (!running_ && !acceptThread_.joinable()) return;
    running_ = false;
    if (stopEvent_) SetEvent(stopEvent_);
    {
        std::lock_guard<std::mutex> lock(clientsMutex_);
        for (HANDLE pipe : clients_) {
            CancelIoEx(pipe, NULL);
            CloseHandle(pipe);
        }
        clients_.clear();
        clientCount_ = 0;
    }
    if (acceptThread_.joinable()) acceptThread_.join();
    if (stopEvent_) {
        CloseHandle(stopEvent_);
        stopEvent_ = NULL;
    }
}

void IpcServer::broadcast(const std::string& jsonState) {
    std::vector<HANDLE> snapshot;
    {
        std::lock_guard<std::mutex> lock(clientsMutex_);
        snapshot = clients_;
    }

    uint32_t len = static_cast<uint32_t>(jsonState.size());
    for (HANDLE pipe : snapshot) {
        if (!writePipeWithTimeout(pipe, &len, sizeof(len), stopEvent_) ||
            !writePipeWithTimeout(pipe, jsonState.data(), len, stopEvent_)) {
            removeClient(pipe);
            continue;
        }
    }
}

void IpcServer::acceptLoop() {
    while (running_) {
        HANDLE pipe = CreateNamedPipeW(
            PIPE_NAME,
            PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
            PIPE_UNLIMITED_INSTANCES,
            65536,
            65536,
            0,
            NULL);

        if (pipe == INVALID_HANDLE_VALUE) {
            Sleep(1000);
            continue;
        }

        OVERLAPPED overlapped = {};
        overlapped.hEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
        if (!overlapped.hEvent) {
            CloseHandle(pipe);
            Sleep(1000);
            continue;
        }

        BOOL connected = ConnectNamedPipe(pipe, &overlapped);
        if (!connected) {
            DWORD err = GetLastError();
            if (err == ERROR_IO_PENDING) {
                HANDLE waitHandles[2] = { overlapped.hEvent, stopEvent_ };
                DWORD waitResult = WaitForMultipleObjects(2, waitHandles, FALSE, INFINITE);
                if (waitResult != WAIT_OBJECT_0) {
                    CancelIoEx(pipe, &overlapped);
                    CloseHandle(overlapped.hEvent);
                    CloseHandle(pipe);
                    break;
                }
                DWORD bytes = 0;
                if (!GetOverlappedResult(pipe, &overlapped, &bytes, FALSE)) {
                    CloseHandle(overlapped.hEvent);
                    CloseHandle(pipe);
                    continue;
                }
            } else if (err != ERROR_PIPE_CONNECTED) {
                CloseHandle(overlapped.hEvent);
                CloseHandle(pipe);
                continue;
            }
        }
        CloseHandle(overlapped.hEvent);

        {
            std::lock_guard<std::mutex> lock(clientsMutex_);
            clients_.push_back(pipe);
            clientCount_ = static_cast<int>(clients_.size());
        }
        std::thread(&IpcServer::clientLoop, this, pipe).detach();
    }
}

void IpcServer::clientLoop(HANDLE pipe) {
    while (running_) {
        uint32_t len = 0;
        if (!readPipeCancelable(pipe, &len, sizeof(len), stopEvent_)) break;
        if (len == 0 || len > 1024 * 1024) break;

        std::vector<char> buf(len);
        if (!readPipeCancelable(pipe, buf.data(), len, stopEvent_)) break;

        if (commandCallback_) commandCallback_(std::string(buf.data(), buf.size()));
    }
    removeClient(pipe);
}

void IpcServer::removeClient(HANDLE pipe) {
    bool removed = false;
    {
        std::lock_guard<std::mutex> lock(clientsMutex_);
        auto it = std::find(clients_.begin(), clients_.end(), pipe);
        if (it != clients_.end()) {
            clients_.erase(it);
            clientCount_ = static_cast<int>(clients_.size());
            removed = true;
        }
    }
    if (removed) {
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }
}
