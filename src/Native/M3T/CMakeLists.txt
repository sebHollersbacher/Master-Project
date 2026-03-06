cmake_minimum_required(VERSION 3.10)

# Force static Runtime (prevent "libc++_shared.so not found")
set(CMAKE_ANDROID_STL_TYPE c++_static)
project(M3T)

# 1. Force Shared Library for Unity
set(BUILD_SHARED_LIBS ON)

# 2. Set C++ Standard
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# 3. Find Dependencies
find_package(Eigen3 REQUIRED)
find_package(OpenCV REQUIRED COMPONENTS core imgproc imgcodecs calib3d features2d)

# 4. Handle Android-specific libraries
set(GRAPHICS_LIBS GLESv3 EGL log)
add_definitions(-D__ANDROID__)


# 5. Build Source
add_subdirectory(src)