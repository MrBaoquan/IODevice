cmake_minimum_required(VERSION 3.18)

project(IODEVICE_ANDROID_LITE LANGUAGES CXX)

add_library(iodevice-android-lite SHARED
    Source/Private/CoreTypes/IOTypes.cpp
    Source/Private/InputBinding/InputActionBinding.cpp
    Source/Private/InputBinding/InputAxisBinding.cpp
    Source/Private/InputBinding/InputBinding.cpp
    Source/Private/InputBinding/InputKeyBinding.cpp
    Source/Private/InputChord.cpp
    Source/Private/InputCoreTypes.cpp
    Source/Private/InputKeyManager.cpp
    Source/Private/InputSettings.cpp
    Source/Private/IOApplication.cpp
    Source/Private/IODevice.cpp
    Source/Private/IODeviceController.cpp
    Source/Private/IODeviceDetails.cpp
    Source/Private/IOLog.cpp
    Source/Private/IOSettings.cpp
    Source/Private/IOStatics.cpp
    Source/Private/KeyDetails.cpp
    Source/Private/KeyState.cpp
    Source/Private/Math/Vector2D.cpp
    Source/Private/Math/Vector3D.cpp
    Source/Private/MotionPlayer.cpp
    Source/Private/PlayerInput.cpp
    Source/Private/RawIO/CustomIOBase.cpp
    Source/Private/RawIO/ExternalIO.cpp
    Source/Private/RawIO/RawIO.cpp
    Source/Private/RawIOFactory.cpp
)

if(ANDROID)
    target_sources(iodevice-android-lite PRIVATE
        Source/Private/Unity/IOUnityBridge.cpp
    )
endif()

target_compile_features(iodevice-android-lite PRIVATE cxx_std_17)

target_include_directories(iodevice-android-lite
    PRIVATE
        Source/Public
        Source/Public/CoreTypes
        Source/Public/InputBinding
        Source/Public/Math
        Source/Public/RawIO
        Source/Vendor
        Source/Vendor/DevelopHelper
        Source/Vendor/nlohmann
        Source/Vendor/rapidxml
        Source/Vendor/spdlog-1.x/include
)

target_compile_definitions(iodevice-android-lite
    PRIVATE
        IODEVICEEXPORTS
        FMT_USE_WINDOWS_H=0
)

set_target_properties(iodevice-android-lite PROPERTIES
    OUTPUT_NAME iodevice
    PREFIX ""
    POSITION_INDEPENDENT_CODE ON
)

if(ANDROID)
    find_library(ANDROID_LOG_LIB log)
    if(ANDROID_LOG_LIB)
        target_link_libraries(iodevice-android-lite PRIVATE ${ANDROID_LOG_LIB})
    endif()
endif()

if(CMAKE_CXX_COMPILER_ID MATCHES "Clang|GNU")
    target_compile_options(iodevice-android-lite PRIVATE -fvisibility=hidden)
endif()

if(MSVC)
    target_compile_options(iodevice-android-lite PRIVATE /bigobj /utf-8)
endif()