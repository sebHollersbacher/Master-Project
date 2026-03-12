#include <EGL/egl.h>
#include <GLES3/gl3.h>
#include <android/log.h>
#include <jni.h>
#include <iostream>
#include <memory>
#include <mutex>
#include <vector>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include <m3t/body.h>
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
static std::shared_ptr<m3t::Body> g_body;
static std::shared_ptr<m3t::RendererGeometry> g_renderer_geo;
static std::shared_ptr<m3t::StaticDetector> g_static_detector;
static std::mutex g_bridge_mutex;
static std::shared_ptr<m3t::RegionModality> g_region_modality;
static std::shared_ptr<m3t::TextureModality> g_texture_modality;
static std::shared_ptr<m3t::RegionModel> g_region_model;
static std::shared_ptr<m3t::FocusedSilhouetteRenderer> g_silhouette_renderer;

static float g_safe_pose[16] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
static std::mutex g_pose_mutex;

extern "C" {

void InitTracker(const char* body_meta_path, const char* model_bin_path) {
  g_tracker = std::make_shared<m3t::Tracker>("tracker");
  g_renderer_geo = std::make_shared<m3t::RendererGeometry>("renderer_geo");

  // Camera
  g_camera = std::make_shared<m3t::UnityColorCamera>("unity_cam");
  m3t::Intrinsics intrinsics{216.54f, 216.54f, 159.115f, 159.3375f, 320, 320};
  // m3t::Intrinsics intrinsics{324.81f, 324.81f, 238.6725f, 239.00625f, 480, 480};
  // m3t::Intrinsics intrinsics{433.08f, 433.08f, 318.23f, 318.675, 640, 640};
  // m3t::Intrinsics intrinsics{866.16f, 866.16f, 636.46f, 637.35f, 1280, 1280};
  g_camera->SetIntrinsics(intrinsics);

  // Body
  g_body = std::make_shared<m3t::Body>("object_body", body_meta_path);
  g_renderer_geo->AddBody(g_body);
  g_region_model = std::make_shared<m3t::RegionModel>("region_model", g_body,
                                                      model_bin_path);

  // Link and Optimizer
  auto link = std::make_shared<m3t::Link>("link", g_body);
  auto optimizer = std::make_shared<m3t::Optimizer>("optimizer", link);
  optimizer->set_tikhonov_parameter_rotation(100.0f);
  g_tracker->AddOptimizer(optimizer);

  // Region Modality
  g_region_modality = std::make_shared<m3t::RegionModality>(
      "region_modality", g_body, g_camera, g_region_model);
  link->AddModality(g_region_modality);

  // Texture Modality
  g_silhouette_renderer = std::make_shared<m3t::FocusedSilhouetteRenderer>(
      "silhouette_renderer", g_renderer_geo, g_camera);
  g_silhouette_renderer->AddReferencedBody(g_body);
  g_texture_modality = std::make_shared<m3t::TextureModality>(
      "texture_modality", g_body, g_camera, g_silhouette_renderer);
  g_texture_modality->set_descriptor_distance_threshold(0.85f);
  g_texture_modality->set_max_keyframe_age(60);
  link->AddModality(g_texture_modality);

  // Detector
  g_static_detector = std::make_shared<m3t::StaticDetector>(
      "static_detector", std::filesystem::path{}, optimizer);
  g_tracker->AddDetector(g_static_detector);
}

void RenderThreadInit(int eventID) {
  // --- EVENT 1: SETUP ---
  if (eventID == 1) {
    if (g_tracker->SetUp(true)) {
      g_tracker->StartTracking();
    } else {
      LOGE("TRACKER SETUP: FAILED. Check shaders and FBO.");
    }
  }

  // --- EVENT 2: TRACKING LOOP ---
  if (eventID == 2) {
    if (g_tracker && g_camera) {
      std::lock_guard<std::mutex> lock(g_bridge_mutex);

      static int frame_idx = 0;
      frame_idx++;
      g_tracker->UpdateCameras(frame_idx);
      g_tracker->ExecuteStartingStep(frame_idx);
      g_tracker->ExecuteTrackingStep(frame_idx);

      if (g_body) {
        std::lock_guard<std::mutex> pose_lock(g_pose_mutex);
        Eigen::Matrix4f pose = g_body->body2world_pose().matrix();
        for (int i = 0; i < 16; ++i) {
          g_safe_pose[i] = pose.data()[i];
        }
      }
    }
  }
}
void* GetRenderEventFunc() { return (void*)RenderThreadInit; }

void PassCameraFrame(unsigned char* data, int width, int height) {
  if (!data) return;

  cv::Mat raw_frame(height, width, CV_8UC4, data);
  cv::Mat bgr_frame;
  cv::cvtColor(raw_frame, bgr_frame, cv::COLOR_RGBA2BGR);
  cv::flip(bgr_frame, bgr_frame, 0);

  if (g_camera) {
    std::lock_guard<std::mutex> lock(g_bridge_mutex);
    g_camera->FeedImage(bgr_frame.clone());
  }
}

void PassNewPose(float* matrix_ptr) {
  if (!g_static_detector || !g_body) return;
  
  m3t::Transform3fA new_pose;
  new_pose.matrix() = Eigen::Map<Eigen::Matrix4f>(matrix_ptr);

  g_static_detector->set_link2world_pose(new_pose);
  g_body->set_body2world_pose(new_pose);
}

void GetBodyPose(float* out_pose_matrix) {
  std::lock_guard<std::mutex> lock(g_pose_mutex);
  memcpy(out_pose_matrix, g_safe_pose, 16 * sizeof(float));
}

}  // extern "C"