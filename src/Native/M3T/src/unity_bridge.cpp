#include <EGL/egl.h>
#include <GLES3/gl3.h>
#include <android/log.h>
#include <jni.h>
#include <iostream>
#include <memory>
#include <mutex>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>
#include <set>
#include <unordered_map>
#include <vector>

#include <m3t/body.h>
#include <m3t/depth_modality.h>
#include <m3t/depth_model.h>
#include <m3t/link.h>
#include <m3t/m3t_config.h>
#include <m3t/optimizer.h>
#include <m3t/refiner.h>
#include <m3t/region_modality.h>
#include <m3t/region_model.h>
#include <m3t/renderer_geometry.h>
#include <m3t/silhouette_renderer.h>
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

// =============================================================================
// Global State
// =============================================================================
static std::shared_ptr<m3t::Tracker> g_tracker;
static std::shared_ptr<m3t::UnityColorCamera> g_camera;
static std::shared_ptr<m3t::UnityDepthCamera> g_d_camera;
static std::shared_ptr<m3t::RendererGeometry> g_renderer_geo;
static std::shared_ptr<m3t::FocusedSilhouetteRenderer> g_silhouette_renderer;
static std::shared_ptr<m3t::Refiner> g_refiner;

static std::unordered_map<int, std::shared_ptr<m3t::Body>> g_bodies;
static std::unordered_map<int, std::shared_ptr<m3t::StaticDetector>>
    g_detectors;
static std::unordered_map<int, std::shared_ptr<m3t::RegionModality>>
    g_region_modalities;
static std::unordered_map<int, std::array<float, 16>> g_safe_poses;
static std::unordered_map<int, std::string> g_pending_textures;

static std::mutex g_bridge_mutex;
static std::mutex g_pose_mutex;

static bool g_tracker_initialized = false;

// =============================================================================
// Helpers
// =============================================================================
static std::vector<int> ToVecInt(const int* arr, int count) {
  return std::vector<int>(arr, arr + count);
}
static std::vector<float> ToVecFloat(const float* arr, int count) {
  return std::vector<float>(arr, arr + count);
}

static constexpr float kDeg2Rad = 3.14159265358979f / 180.0f;

extern "C" {

// =============================================================================
// GetDefaultConfig — Unity calls this to get a config pre-filled with defaults
// =============================================================================
void GetDefaultConfig(M3TObjectConfig* out) {
  if (out) M3TObjectConfig_Defaults(out);
}

// =============================================================================
// InitTracker — call once before any AddObjectToTracker
// =============================================================================
void InitTracker(int n_corr_iterations, int n_update_iterations) {
  g_tracker = std::make_shared<m3t::Tracker>("tracker", n_corr_iterations,
                                             n_update_iterations);
  g_renderer_geo = std::make_shared<m3t::RendererGeometry>("renderer_geo");

  g_camera = std::make_shared<m3t::UnityColorCamera>("unity_color_cam");
  g_d_camera = std::make_shared<m3t::UnityDepthCamera>("unity_depth_cam");

  g_silhouette_renderer = std::make_shared<m3t::FocusedSilhouetteRenderer>(
      "silhouette_renderer", g_renderer_geo, g_camera);

  g_tracker_initialized = true;
  LOGI("Tracker initialized (corr_iter=%d, update_iter=%d)", n_corr_iterations,
       n_update_iterations);
}

// =============================================================================
// Camera parameter setters — call after InitTracker, before
// SetupTrackerHeadless
// =============================================================================
void SetRGBCameraParams(float fx, float fy, float cx, float cy, int width,
                        int height, float* extrinsics_4x4) {
  if (!g_camera) return;
  m3t::Intrinsics intr{fx, fy, cx, cy, width, height};
  g_camera->SetIntrinsics(intr);

  if (extrinsics_4x4) {
    Eigen::Matrix4f mat = Eigen::Map<Eigen::Matrix4f>(extrinsics_4x4);
    g_camera->set_camera2world_pose(m3t::Transform3fA(mat));
  }

  LOGI("RGB camera: fx=%.2f fy=%.2f cx=%.2f cy=%.2f %dx%d", fx, fy, cx, cy,
       width, height);
}

void SetDepthCameraParams(float fx, float fy, float cx, float cy, int width,
                          int height, float* extrinsics_4x4) {
  if (!g_d_camera) return;
  m3t::Intrinsics intr{fx, fy, cx, cy, width, height};
  g_d_camera->SetIntrinsics(intr);

  if (extrinsics_4x4) {
    Eigen::Matrix4f mat = Eigen::Map<Eigen::Matrix4f>(extrinsics_4x4);
    g_d_camera->set_camera2world_pose(m3t::Transform3fA(mat));
  }

  LOGI("Depth camera: fx=%.2f fy=%.2f cx=%.2f cy=%.2f %dx%d", fx, fy, cx, cy,
       width, height);
}

// =============================================================================
// AddObjectToTracker — config struct comes from Unity
// =============================================================================
void AddObjectToTracker(int target_id, const char* body_meta_path,
                        const char* region_model_path,
                        const char* depth_model_path, const char* texture_path,
                        const M3TObjectConfig* cfg) {
  if (!g_tracker_initialized || !cfg) {
    LOGE("AddObjectToTracker: tracker not initialized or null config");
    return;
  }

  std::string name = std::to_string(target_id);

  // --- Body ---
  auto body = std::make_shared<m3t::Body>(name + "_body", body_meta_path);
  g_renderer_geo->AddBody(body);

  // --- Region Model ---
  auto region_model = std::make_shared<m3t::RegionModel>(
      name + "_region_model", body, region_model_path);

  // --- Link + Optimizer ---
  std::array<bool, 6> free_dirs;
  for (int i = 0; i < 6; ++i) free_dirs[i] = (cfg->free_directions[i] != 0);

  auto link = std::make_shared<m3t::Link>(
      name + "_link", body,
      m3t::Transform3fA::Identity(),  // body2joint_pose
      m3t::Transform3fA::Identity(),  // joint2parent_pose
      m3t::Transform3fA::Identity(),  // link2world_pose
      free_dirs);

  auto optimizer = std::make_shared<m3t::Optimizer>(name + "_optimizer", link);
  optimizer->set_tikhonov_parameter_rotation(cfg->tikhonov_parameter_rotation);
  optimizer->set_tikhonov_parameter_translation(
      cfg->tikhonov_parameter_translation);
  g_tracker->AddOptimizer(optimizer);

  // --- Refiner (optional, created once) ---
  if (cfg->use_refiner) {
    if (!g_refiner) {
      g_refiner = std::make_shared<m3t::Refiner>(
          "refiner", cfg->refiner_n_corr_iterations,
          cfg->refiner_n_update_iterations);
    }
    g_refiner->AddOptimizer(optimizer);
  }

  // =========================================================================
  // Region Modality (always active)
  // =========================================================================
  auto region = std::make_shared<m3t::RegionModality>(
      name + "_region_modality", body, g_camera, region_model);

  region->set_n_lines_max(cfg->region_n_lines_max);
  region->set_use_adaptive_coverage(cfg->region_use_adaptive_coverage != 0);
  region->set_min_continuous_distance(cfg->region_min_continuous_distance);
  region->set_function_length(cfg->region_function_length);
  region->set_distribution_length(cfg->region_distribution_length);
  region->set_function_amplitude(cfg->region_function_amplitude);
  region->set_function_slope(cfg->region_function_slope);
  region->set_learning_rate(cfg->region_learning_rate);
  region->set_n_global_iterations(cfg->region_n_global_iterations);

  int rn = cfg->region_n_iterations;
  if (rn > 0 && rn <= M3T_MAX_ITERATIONS) {
    region->set_scales(ToVecInt(cfg->region_scales, rn));
    region->set_standard_deviations(
        ToVecFloat(cfg->region_standard_deviations, rn));
  }

  region->set_n_histogram_bins(cfg->region_n_histogram_bins);
  region->set_learning_rate_f(cfg->region_learning_rate_f);
  region->set_learning_rate_b(cfg->region_learning_rate_b);
  region->set_unconsidered_line_length(cfg->region_unconsidered_line_length);
  region->set_max_considered_line_length(
      cfg->region_max_considered_line_length);

  // Measured occlusion: real depth camera detects hand/world occlusion
  if (cfg->region_measure_occlusions) {
    region->MeasureOcclusions(g_d_camera);
    region->set_measured_depth_offset_radius(
        cfg->region_measured_depth_offset_radius);
    region->set_measured_occlusion_radius(
        cfg->region_measured_occlusion_radius);
    region->set_measured_occlusion_threshold(
        cfg->region_measured_occlusion_threshold);
    region->set_n_unoccluded_iterations(cfg->region_n_unoccluded_iterations);
    region->set_min_n_unoccluded_lines(cfg->region_min_n_unoccluded_lines);
  }

  // Region checking: silhouette validates fg/bg on both sides of contour
  if (cfg->region_use_region_checking) {
    region->UseRegionChecking(g_silhouette_renderer);
  }

  link->AddModality(region);
  g_region_modalities[target_id] = region;

  // --- Histogram ---
  if (texture_path && strlen(texture_path) > 0) {
    g_pending_textures[target_id] = std::string(texture_path);
    LOGI("Queued foreground texture for body %d", target_id);
  }

  // =========================================================================
  // Depth Modality (optional)
  // =========================================================================
  if (cfg->use_depth_modality) {
    auto depth_model = std::make_shared<m3t::DepthModel>(
        name + "_depth_model", body, depth_model_path);
    auto depth = std::make_shared<m3t::DepthModality>(
        name + "_depth_modality", body, g_d_camera, depth_model);

    depth->set_n_points_max(cfg->depth_n_points_max);
    depth->set_stride_length(cfg->depth_stride_length);
    depth->set_use_depth_scaling(cfg->depth_use_depth_scaling != 0);

    int dn = cfg->depth_n_iterations;
    if (dn > 0 && dn <= M3T_MAX_ITERATIONS) {
      depth->set_considered_distances(
          ToVecFloat(cfg->depth_considered_distances, dn));
      depth->set_standard_deviations(
          ToVecFloat(cfg->depth_standard_deviations, dn));
    }

    // Measured occlusion on depth modality (already knows its depth camera)
    if (cfg->depth_measure_occlusions) {
      depth->MeasureOcclusions();
    }

    link->AddModality(depth);
  }

  // =========================================================================
  // Texture Modality (optional)
  // =========================================================================
  if (cfg->use_texture_modality) {
    g_silhouette_renderer->AddReferencedBody(body);
    auto texture = std::make_shared<m3t::TextureModality>(
        name + "_texture_modality", body, g_camera, g_silhouette_renderer);

    texture->set_descriptor_type(
        static_cast<m3t::TextureModality::DescriptorType>(
            cfg->texture_descriptor_type));
    texture->set_descriptor_distance_threshold(
        cfg->texture_descriptor_distance_threshold);
    texture->set_tukey_norm_constant(cfg->texture_tukey_norm_constant);
    texture->set_max_keyframe_rotation_difference(
        cfg->texture_max_keyframe_rotation_difference * kDeg2Rad);
    texture->set_max_keyframe_age(cfg->texture_max_keyframe_age);
    texture->set_n_keyframes(cfg->texture_n_keyframes);
    texture->set_focused_image_size(cfg->texture_focused_image_size);

    // ORB params (always set; only used when descriptor_type == ORB)
    texture->set_orb_n_features(cfg->texture_orb_n_features);
    texture->set_orb_scale_factor(cfg->texture_orb_scale_factor);
    texture->set_orb_n_levels(cfg->texture_orb_n_levels);

    // SIFT params (always set; only used when descriptor_type == SIFT)
    texture->set_sift_n_features(cfg->texture_sift_n_features);
    texture->set_sift_n_octave_layers(cfg->texture_sift_n_octave_layers);
    texture->set_sift_contrast_threshold(cfg->texture_sift_contrast_threshold);
    texture->set_sift_edge_threshold(cfg->texture_sift_edge_threshold);
    texture->set_sift_sigma(cfg->texture_sift_sigma);

    link->AddModality(texture);
  }

  // --- Detector ---
  auto detector = std::make_shared<m3t::StaticDetector>(
      name + "_detector", std::filesystem::path{}, optimizer);
  g_tracker->AddDetector(detector);

  // --- Store ---
  g_bodies[target_id] = body;
  g_safe_poses[target_id] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
  g_detectors[target_id] = detector;

  LOGI(
      "Added object %d (region=%d lines, depth=%s, texture=%s, "
      "measure_occ=%s, region_check=%s, refiner=%s)",
      target_id, cfg->region_n_lines_max,
      cfg->use_depth_modality ? "ON" : "OFF",
      cfg->use_texture_modality ? "ON" : "OFF",
      cfg->region_measure_occlusions ? "ON" : "OFF",
      cfg->region_use_region_checking ? "ON" : "OFF",
      cfg->use_refiner ? "ON" : "OFF");
}

// =============================================================================
// Lifecycle
// =============================================================================
bool SetupTrackerHeadless() {
  if (!g_tracker) return false;

  if (g_tracker->SetUp(true)) {
    if (g_refiner) g_refiner->SetUp(true);

    // Apply pending textures now that ColorHistograms exist and are set up
    for (auto& [id, path] : g_pending_textures) {
      auto rm_it = g_region_modalities.find(id);
      if (rm_it == g_region_modalities.end()) continue;

      auto hist = rm_it->second->color_histograms_ptr();
      if (!hist) continue;

      cv::Mat texture = cv::imread(path, cv::IMREAD_UNCHANGED);
      if (!texture.empty()) {
        hist->SetForegroundTexture(texture);
        LOGI("Applied foreground texture for body %d", id);
      } else {
        LOGE("Failed to load texture: %s", path.c_str());
      }
    }
    g_pending_textures.clear();

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

// =============================================================================
// Camera Input
// =============================================================================
void PassRGBCameraFrame(unsigned char* data, int width, int height) {
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

void PassDepthCameraFrame(void* data, int width, int height) {
  if (!data || !g_d_camera) return;
  cv::Mat m3t_depth(height, width, CV_16UC1, data);

  cv::Mat flipped_m3t;
  cv::flip(m3t_depth, flipped_m3t, 0);

  std::lock_guard<std::mutex> lock(g_bridge_mutex);
  g_d_camera->FeedImage(flipped_m3t.clone());
}

// =============================================================================
// Pose
// =============================================================================
void PassNewPose(int body_id, float* matrix_ptr) {
  if (g_bodies.find(body_id) == g_bodies.end()) return;

  std::lock_guard<std::mutex> lock(g_bridge_mutex);

  m3t::Transform3fA new_pose;
  new_pose.matrix() = Eigen::Map<Eigen::Matrix4f>(matrix_ptr);

  g_detectors[body_id]->set_link2world_pose(new_pose);
  g_bodies[body_id]->set_body2world_pose(new_pose);

  // Reset histogram and start learning boost
  auto rm_it = g_region_modalities.find(body_id);
  if (rm_it != g_region_modalities.end()) {
    auto hist = rm_it->second->color_histograms_ptr();
    if (hist) {
      if (hist->ResetForegroundFromTexture()) {
        hist->StartLearningBoost(30, 0.3f);
      }
    }
  }

  if (g_refiner) {
    std::set<std::string> names = {std::to_string(body_id) + "_optimizer"};
    g_refiner->RefinePoses(names);
  }
}

int GetTrackingValidLines(int body_id) {
  if (g_region_modalities.find(body_id) == g_region_modalities.end()) return 0;
  return g_region_modalities[body_id]->n_valid_lines();
}

float GetHistogramDivergence(int body_id) {
  auto it = g_region_modalities.find(body_id);
  if (it == g_region_modalities.end()) return -1.0f;

  auto hist = it->second->color_histograms_ptr();
  if (!hist) return -1.0f;
  return hist->ForegroundDivergence();
}

void GetBodyPose(int body_id, float* out_pose_matrix) {
  if (g_bodies.find(body_id) == g_bodies.end()) return;

  std::lock_guard<std::mutex> lock(g_pose_mutex);
  memcpy(out_pose_matrix, g_safe_poses[body_id].data(), 16 * sizeof(float));
}

}  // extern "C"