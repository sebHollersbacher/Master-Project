#include <EGL/egl.h>
#include <GLES3/gl3.h>
#include <android/log.h>
#include <jni.h>
#include <iostream>
#include <memory>
#include <mutex>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>
#include <unordered_map>
#include <vector>

#include <m3t/body.h>
#include <m3t/depth_modality.h>
#include <m3t/depth_model.h>
#include <m3t/link.h>
#include <m3t/normal_renderer.h>
#include <m3t/optimizer.h>
#include <m3t/region_modality.h>
#include <m3t/region_model.h>
#include <m3t/renderer_geometry.h>
#include <m3t/static_detector.h>
#include <m3t/texture_modality.h>
#include <m3t/tracker.h>
#include <m3t/unity_color_camera.h>
#include <m3t/unity_depth_camera.h>

#define LOG_TAG "M3T_NATIVE"
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)

// --- OpenMP Stubs for Android ---
extern "C" {
int omp_get_thread_num() { return 0; }
int omp_get_max_threads() { return 1; }
int omp_get_num_threads() { return 1; }
}

// --- Global Pointers ---
static std::shared_ptr<m3t::Tracker> g_tracker;
static std::shared_ptr<m3t::UnityColorCamera> g_camera;
static std::shared_ptr<m3t::UnityDepthCamera> g_d_camera;
static std::shared_ptr<m3t::RendererGeometry> g_renderer_geo;
static std::shared_ptr<m3t::FocusedSilhouetteRenderer> g_silhouette_renderer;

static std::unordered_map<int, std::shared_ptr<m3t::Body>> g_bodies;
static std::unordered_map<int, std::shared_ptr<m3t::StaticDetector>>
    g_detectors;
static std::unordered_map<int, std::array<float, 16>> g_safe_poses;

static std::mutex g_bridge_mutex;
static std::mutex g_pose_mutex;

extern "C" {

void InitTracker(const char* body_meta_path, const char* model_bin_path) {
  g_tracker = std::make_shared<m3t::Tracker>("tracker");
  g_renderer_geo = std::make_shared<m3t::RendererGeometry>("renderer_geo");

  // RGB-Camera
  g_camera = std::make_shared<m3t::UnityColorCamera>("unity_color_cam");
  m3t::Intrinsics intrinsics{216.54f, 216.54f, 159.115f, 159.3375f, 320, 320};
  g_camera->SetIntrinsics(intrinsics);

  // position
  Eigen::Matrix4f rgb_camera2world_mat;
  rgb_camera2world_mat << 0.999948f, 0.008509f, -0.005664f, 0.031619f,
      -0.007279f, 0.981792f, 0.189818f, 0.017903f, 0.007176f, -0.189766f,
      0.981803f, 0.062940f, 0.000000f, 0.000000f, 0.000000f, 1.000000f;
  m3t::Transform3fA rgb_camera2world(rgb_camera2world_mat);
  g_camera->set_camera2world_pose(rgb_camera2world);

  // Depth-Camera
  g_d_camera = std::make_shared<m3t::UnityDepthCamera>("unity_depth_cam");
  m3t::Intrinsics depth_intrinsics{144.4381f, 133.6766f, 121.198f,
                                   129.09f,   320,       320};
  g_d_camera->SetIntrinsics(depth_intrinsics);

  // position
  Eigen::Matrix4f depth_camera2world_mat;
  depth_camera2world_mat << 1.000000f, 0.000000f, 0.000000f, 0.031034f,
      0.000000f, 1.000000f, 0.000000f, 0.000000f, 0.000000f, 0.000000f,
      1.000000f, 0.000000f, 0.000000f, 0.000000f, 0.000000f, 1.000000f;
  m3t::Transform3fA depth_camera2world(depth_camera2world_mat);
  g_d_camera->set_camera2world_pose(depth_camera2world);

  g_silhouette_renderer = std::make_shared<m3t::FocusedSilhouetteRenderer>(
      "silhouette_renderer", g_renderer_geo, g_camera);
}

void AddObjectToTracker(int target_id, const char* body_meta_path,
                        const char* region_model_path,
                        const char* depth_model_path) {
  std::string name = std::to_string(target_id);
  // Body
  auto g_body = std::make_shared<m3t::Body>(name + "_body", body_meta_path);
  g_renderer_geo->AddBody(g_body);
  auto region_model = std::make_shared<m3t::RegionModel>(
      name + "_region_model", g_body, region_model_path);
  auto depth_model = std::make_shared<m3t::DepthModel>(
      name + "_depth_model", g_body, depth_model_path);

  // Link and Optimizer
  auto link = std::make_shared<m3t::Link>(name + "_link", g_body);
  auto optimizer = std::make_shared<m3t::Optimizer>(name + "_optimizer", link);
  optimizer->set_tikhonov_parameter_rotation(100.0f);
  g_tracker->AddOptimizer(optimizer);

  // Region Modality
  auto region_modality = std::make_shared<m3t::RegionModality>(
      name + "_region_modality", g_body, g_camera, region_model);
  link->AddModality(region_modality);

  // Texture Modality
  g_silhouette_renderer->AddReferencedBody(g_body);
  auto g_texture_modality = std::make_shared<m3t::TextureModality>(
      name + "_texture_modality", g_body, g_camera, g_silhouette_renderer);
  g_texture_modality->set_descriptor_distance_threshold(0.85f);
  g_texture_modality->set_max_keyframe_age(60);
  link->AddModality(g_texture_modality);

  // Depth Modality
  auto depth_modality = std::make_shared<m3t::DepthModality>(
      name + "_depth_modality", g_body, g_d_camera, depth_model);
  depth_modality->set_considered_distances({0.08f, 0.04f, 0.02f});
  depth_modality->set_standard_deviations({0.08f, 0.05f, 0.03f});
  link->AddModality(depth_modality);

  // Detector
  auto g_static_detector = std::make_shared<m3t::StaticDetector>(
      name + "_detector", std::filesystem::path{}, optimizer);
  g_tracker->AddDetector(g_static_detector);

  g_bodies[target_id] = g_body;
  g_safe_poses[target_id] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
  g_detectors[target_id] = g_static_detector;
}

bool SetupTrackerHeadless() {
  if (!g_tracker) return false;

  if (g_tracker->SetUp(true)) {
    g_tracker->StartTracking();
    return true;
  }
  return false;
}

void UpdateTrackerHeadless() {
  if (g_tracker && g_camera) {
    std::lock_guard<std::mutex> lock(g_bridge_mutex);

    static int frame_idx = 0;
    frame_idx++;

    g_tracker->UpdateCameras(frame_idx);
    g_tracker->ExecuteStartingStep(frame_idx);
    g_tracker->ExecuteTrackingStep(frame_idx);

    std::lock_guard<std::mutex> pose_lock(g_pose_mutex);
    for (auto const& [id, body] : g_bodies) {
      Eigen::Matrix4f pose = body->body2world_pose().matrix();
      for (int j = 0; j < 16; ++j) {
        g_safe_poses[id][j] = pose.data()[j];
      }
    }
  }
}

void PassRGBCameraFrame(unsigned char* data, int width, int height) {
  if (!data) return;

  cv::Mat raw_frame(height, width, CV_8UC4, data);
  cv::Mat bgr_frame;
  cv::cvtColor(raw_frame, bgr_frame, cv::COLOR_RGBA2BGR);
  // flip for OpenCV coordinate system alignment
  cv::flip(bgr_frame, bgr_frame, 0);

  if (g_camera) {
    std::lock_guard<std::mutex> lock(g_bridge_mutex);
    g_camera->FeedImage(bgr_frame.clone());
  }
}

void PassDepthCameraFrame(void* data, int width, int height) {
  if (!data || !g_d_camera) return;
  cv::Mat m3t_depth(height, width, CV_16UC1, data);

  // flip for OpenCV coordinate system alignment
  cv::Mat flipped_m3t;
  cv::flip(m3t_depth, flipped_m3t, 0);

  std::lock_guard<std::mutex> lock(g_bridge_mutex);
  g_d_camera->FeedImage(flipped_m3t.clone());
}

void PassNewPose(int body_id, float* matrix_ptr) {
  if (g_bodies.find(body_id) == g_bodies.end()) return;

  m3t::Transform3fA new_pose;
  new_pose.matrix() = Eigen::Map<Eigen::Matrix4f>(matrix_ptr);

  g_detectors[body_id]->set_link2world_pose(new_pose);
  g_bodies[body_id]->set_body2world_pose(new_pose);
}

void GetBodyPose(int body_id, float* out_pose_matrix) {
  if (g_bodies.find(body_id) == g_bodies.end()) return;

  std::lock_guard<std::mutex> lock(g_pose_mutex);
  memcpy(out_pose_matrix, g_safe_poses[body_id].data(), 16 * sizeof(float));
}

}  // extern "C"