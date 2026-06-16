#include "osc_sender.h"
#include <winsock2.h>
#include <ws2tcpip.h>
#include <vector>
#include <cstring>
#include <iostream>

#pragma comment(lib, "ws2_32.lib")

OscSender::OscSender() : socketHandle(INVALID_SOCKET) {}

OscSender::~OscSender() {
    if (socketHandle != INVALID_SOCKET) {
        closesocket((SOCKET)socketHandle);
        WSACleanup();
    }
}

bool OscSender::init(const std::string& host, int port) {
    WSADATA wsaData;
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
        return false;
    }

    socketHandle = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (socketHandle == INVALID_SOCKET) {
        WSACleanup();
        return false;
    }

    sockaddr_in serverAddr;
    serverAddr.sin_family = AF_INET;
    serverAddr.sin_port = htons(port);
    inet_pton(AF_INET, host.c_str(), &serverAddr.sin_addr);

    if (connect((SOCKET)socketHandle, (sockaddr*)&serverAddr, sizeof(serverAddr)) == SOCKET_ERROR) {
        closesocket((SOCKET)socketHandle);
        WSACleanup();
        socketHandle = INVALID_SOCKET;
        return false;
    }

    return true;
}

static size_t padSize(size_t size) {
    return (size + 3) & ~3;
}

void OscSender::sendFloat(const std::string& address, float value) {
    if (socketHandle == INVALID_SOCKET) return;

    std::vector<char> packet;
    
    // Address pattern
    size_t addrLen = address.length() + 1;
    size_t addrPad = padSize(addrLen);
    packet.insert(packet.end(), address.c_str(), address.c_str() + address.length());
    packet.insert(packet.end(), addrPad - address.length(), 0);

    // Type tag string ",f"
    const char* typeTag = ",f";
    size_t typeLen = 3; // ",f\0"
    size_t typePad = padSize(typeLen);
    packet.insert(packet.end(), typeTag, typeTag + 2);
    packet.insert(packet.end(), typePad - 2, 0);

    // Float argument (big-endian)
    uint32_t netValue;
    std::memcpy(&netValue, &value, sizeof(float));
    netValue = htonl(netValue);
    
    char* valuePtr = reinterpret_cast<char*>(&netValue);
    packet.insert(packet.end(), valuePtr, valuePtr + 4);

    send((SOCKET)socketHandle, packet.data(), packet.size(), 0);
}
