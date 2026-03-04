cmake_minimum_required(VERSION 3.18)
project(QuestCVWrapper)

# 1. OpenCV
set(OpenCV_DIR "${OPENCV_SDK_PATH}/sdk/native/jni")
find_package(OpenCV REQUIRED)

# 2. Define Wrapper
add_library(opencv_wrapper SHARED QuestCV.cpp)
set_target_properties(opencv_wrapper PROPERTIES PREFIX "lib")

# 3. LINKING
find_library(log-lib log)
target_link_libraries(opencv_wrapper
    ${OpenCV_LIBS}
    ${log-lib}
)