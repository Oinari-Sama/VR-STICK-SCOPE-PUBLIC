cmake_minimum_required(VERSION 3.20)
project(InariKontrollerEngine VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# OpenVR SDK headers are placed under thirdparty/openvr/.
include_directories(${CMAKE_SOURCE_DIR}/thirdparty/openvr/headers)
include_directories(${CMAKE_SOURCE_DIR}/src)

# OpenVR shared library
if(WIN32)
    set(OPENVR_LIB ${CMAKE_SOURCE_DIR}/thirdparty/openvr/lib/win64/openvr_api.lib)
endif()

add_executable(InariKontrollerEngine
    src/main.cpp
    src/openvr_manager.cpp
    src/correction_lut.cpp
    src/ipc_server.cpp
    src/profile.cpp
    src/osc_sender.cpp
)

add_executable(InariKontrollerLauncher WIN32
    src/launcher.cpp
)

set_target_properties(InariKontrollerLauncher PROPERTIES
    OUTPUT_NAME "InariKontroller"
)

if(MSVC)
    target_compile_options(InariKontrollerEngine PRIVATE /utf-8)
    target_compile_options(InariKontrollerLauncher PRIVATE /utf-8)
endif()

target_link_libraries(InariKontrollerEngine
    ${OPENVR_LIB}
    Pathcch
    shlwapi
    ole32      # CoTaskMemFree (SHGetKnownFolderPath)
    Shell32    # SHGetKnownFolderPath
    ws2_32     # Windows Sockets 2 for OSC
)

# Copy OpenVR DLL to the engine output directory.
add_custom_command(TARGET InariKontrollerEngine POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        ${CMAKE_SOURCE_DIR}/thirdparty/openvr/bin/win64/openvr_api.dll
        $<TARGET_FILE_DIR:InariKontrollerEngine>/openvr_api.dll
    COMMENT "Copying openvr_api.dll"
)
