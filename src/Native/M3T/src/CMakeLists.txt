cmake_minimum_required(VERSION 3.10)

set(SOURCES
    common.cpp
    body.cpp
    renderer_geometry.cpp
    renderer.cpp
    normal_renderer.cpp
    silhouette_renderer.cpp
    basic_depth_renderer.cpp
    model.cpp
    region_model.cpp
    depth_model.cpp
    camera.cpp
    loader_camera.cpp
    color_histograms.cpp
    modality.cpp
    region_modality.cpp
    depth_modality.cpp
    texture_modality.cpp
    link.cpp
    constraint.cpp
    soft_constraint.cpp
    optimizer.cpp
    detector.cpp 
    static_detector.cpp
    refiner.cpp
    publisher.cpp
    subscriber.cpp
    tracker.cpp
    unity_color_camera.cpp
    unity_bridge.cpp
)

set(HEADERS 
        ../include/m3t/common.h
        ../include/m3t/body.h
        ../include/m3t/renderer_geometry.h
        ../include/m3t/renderer.h
        ../include/m3t/normal_renderer.h
        ../include/m3t/silhouette_renderer.h
        ../include/m3t/basic_depth_renderer.h
        ../include/m3t/model.h
        ../include/m3t/region_model.h
        ../include/m3t/depth_model.h
        ../include/m3t/camera.h
        ../include/m3t/loader_camera.h
        ../include/m3t/color_histograms.h
        ../include/m3t/modality.h
        ../include/m3t/region_modality.h
        ../include/m3t/depth_modality.h
        ../include/m3t/texture_modality.h
        ../include/m3t/link.h
        ../include/m3t/constraint.h
        ../include/m3t/soft_constraint.h
        ../include/m3t/optimizer.h
        ../include/m3t/detector.h
        ../include/m3t/static_detector.h
        ../include/m3t/refiner.h
        ../include/m3t/publisher.h
        ../include/m3t/subscriber.h
        ../include/m3t/tracker.h
        ../include/m3t/generator.h
        ../include/m3t/unity_color_camera.h)

# Create the Library
add_library(m3t SHARED ${SOURCES})

# Link the libraries
target_link_libraries(m3t PUBLIC 
    Eigen3::Eigen 
    ${OpenCV_LIBRARIES}
    ${GRAPHICS_LIBS}
)

# Set Include Paths
target_include_directories(m3t PUBLIC 
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../include>
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../third_party>
    ${EIGEN3_INCLUDE_DIR}
)
