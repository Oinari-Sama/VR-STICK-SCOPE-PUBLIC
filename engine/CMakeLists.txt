cmake_minimum_required(VERSION 3.20)
project(VRStickScopeEngine VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# OpenVR SDK - ヘッダーはthirdparty/openvr/に配置
include_directories(${CMAKE_SOURCE_DIR}/thirdparty/openvr/headers)
include_directories(${CMAKE_SOURCE_DIR}/thirdparty/nlohmann)
include_directories(${CMAKE_SOURCE_DIR}/src)

# OpenVR shared library
if(WIN32)
    set(OPENVR_LIB ${CMAKE_SOURCE_DIR}/thirdparty/openvr/lib/win64/openvr_api.lib)
endif()

add_executable(VRStickScopeEngine
    src/main.cpp
    src/openvr_manager.cpp
    src/correction_lut.cpp
    src/ipc_server.cpp
    src/profile.cpp
    src/osc_sender.cpp
)

add_executable(VRStickScopeLauncher WIN32
    src/launcher.cpp
)

set_target_properties(VRStickScopeLauncher PROPERTIES
    OUTPUT_NAME "VRStickScope"
)

if(MSVC)
    target_compile_options(VRStickScopeEngine PRIVATE /utf-8)
    target_compile_options(VRStickScopeLauncher PRIVATE /utf-8)
endif()

target_link_libraries(VRStickScopeEngine
    ${OPENVR_LIB}
    Pathcch
    shlwapi
    ole32      # CoTaskMemFree (SHGetKnownFolderPath)
    Shell32    # SHGetKnownFolderPath
    ws2_32     # Windows Sockets 2 for OSC
)

# OpenVR DLL をビルド出力にコピー
add_custom_command(TARGET VRStickScopeEngine POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        ${CMAKE_SOURCE_DIR}/thirdparty/openvr/bin/win64/openvr_api.dll
        $<TARGET_FILE_DIR:VRStickScopeEngine>/openvr_api.dll
    COMMENT "Copying openvr_api.dll"
)
