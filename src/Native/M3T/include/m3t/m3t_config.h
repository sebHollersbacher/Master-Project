// m3t_config.h — C-compatible config struct for Unity interop
// Include from C++ side. Mirror as StructLayout on C# side.
#ifndef M3T_CONFIG_H
#define M3T_CONFIG_H

#define M3T_MAX_ITERATIONS 8

#ifdef __cplusplus
extern "C" {
#endif

struct M3TObjectConfig {
  // --- Tracker (set once via InitTracker) ---
  // Not per-object, but included here so Unity has one place to tweak.
  // Only read from the FIRST AddObjectToTracker call.
  int tracker_n_corr_iterations;   // default: 3
  int tracker_n_update_iterations; // default: 2

  // --- Region Modality (always active) ---
  int   region_n_lines_max;               // default: 200
  int   region_use_adaptive_coverage;     // bool, default: 0
  float region_min_continuous_distance;   // default: 3.0
  int   region_function_length;           // default: 8
  int   region_distribution_length;       // default: 12
  float region_function_amplitude;        // default: 0.43
  float region_function_slope;            // default: 0.5
  float region_learning_rate;             // default: 1.3
  int   region_n_global_iterations;       // default: 1

  int   region_scales[M3T_MAX_ITERATIONS];              // default: {6,4,2,1,0,...}
  float region_standard_deviations[M3T_MAX_ITERATIONS]; // default: {15,5,3.5,1.5,0,...}
  int   region_n_iterations;  // how many entries in scales/standard_deviations are valid

  int   region_n_histogram_bins;   // default: 16  (must be 2,4,8,16,32,64)
  float region_learning_rate_f;    // default: 0.2
  float region_learning_rate_b;    // default: 0.2
  float region_unconsidered_line_length;   // default: 0.5
  float region_max_considered_line_length;  // default: 20.0

  int   region_use_region_checking;    // bool, default: 0 (needs silhouette renderer)
  int   region_measure_occlusions;     // bool, default: 1 (uses depth camera for hand occlusion)

  float region_measured_depth_offset_radius;   // default: 0.01
  float region_measured_occlusion_radius;       // default: 0.01
  float region_measured_occlusion_threshold;    // default: 0.03
  int   region_n_unoccluded_iterations;         // default: 10
  int   region_min_n_unoccluded_lines;          // default: 0

  // --- Depth Modality (optional) ---
  int   use_depth_modality;        // bool, default: 1
  int   depth_n_points_max;        // default: 200
  float depth_stride_length;       // default: 0.005
  int   depth_use_depth_scaling;   // bool, default: 0

  float depth_considered_distances[M3T_MAX_ITERATIONS]; // default: {0.05,0.02,0.01,0,...}
  float depth_standard_deviations[M3T_MAX_ITERATIONS];  // default: {0.05,0.03,0.02,0,...}
  int   depth_n_iterations;  // how many entries are valid

  int   depth_measure_occlusions;  // bool, default: 1

  // --- Texture Modality (optional) ---
  int   use_texture_modality;      // bool, default: 0

  // DescriptorType: 0=BRISK, 1=DAISY, 2=FREAK, 3=SIFT, 4=ORB
  int   texture_descriptor_type;                   // default: 4 (ORB)
  float texture_descriptor_distance_threshold;     // default: 0.7
  float texture_tukey_norm_constant;               // default: 20.0
  float texture_max_keyframe_rotation_difference;  // default: 10.0 (degrees, converted to rad in C++)
  int   texture_max_keyframe_age;                  // default: 100
  int   texture_n_keyframes;                       // default: 1
  int   texture_focused_image_size;                // default: 200

  // ORB-specific
  int   texture_orb_n_features;    // default: 300
  float texture_orb_scale_factor;  // default: 1.2
  int   texture_orb_n_levels;      // default: 3

  // SIFT-specific
  int    texture_sift_n_features;          // default: 0 (unlimited)
  int    texture_sift_n_octave_layers;     // default: 5
  float  texture_sift_contrast_threshold;  // default: 0.04
  float  texture_sift_edge_threshold;      // default: 10.0
  float  texture_sift_sigma;               // default: 0.7

  // --- Optimizer ---
  float tikhonov_parameter_rotation;      // default: 1000.0
  float tikhonov_parameter_translation;   // default: 30000.0

  // --- Link DoF ---
  int free_directions[6];

  // --- Refiner ---
  int   use_refiner;                 // bool, default: 0
  int   refiner_n_corr_iterations;   // default: 5
  int   refiner_n_update_iterations; // default: 2
};

// Fill a config struct with sensible defaults.
// Call this from C# before overriding specific fields.
static inline void M3TObjectConfig_Defaults(struct M3TObjectConfig* c) {
  // Tracker
  c->tracker_n_corr_iterations   = 5;
  c->tracker_n_update_iterations = 2;

  // Region
  c->region_n_lines_max             = 200;
  c->region_use_adaptive_coverage   = 0;
  c->region_min_continuous_distance = 3.0f;
  c->region_function_length         = 8;
  c->region_distribution_length     = 12;
  c->region_function_amplitude      = 0.43f;
  c->region_function_slope          = 0.5f;
  c->region_learning_rate           = 1.3f;
  c->region_n_global_iterations     = 1;

  c->region_scales[0] = 6; c->region_scales[1] = 4;
  c->region_scales[2] = 2; c->region_scales[3] = 1;
  c->region_standard_deviations[0] = 15.0f;
  c->region_standard_deviations[1] = 5.0f;
  c->region_standard_deviations[2] = 3.5f;
  c->region_standard_deviations[3] = 1.5f;
  c->region_n_iterations = 4;
  for (int i = 4; i < M3T_MAX_ITERATIONS; ++i) {
    c->region_scales[i] = 0;
    c->region_standard_deviations[i] = 0.0f;
  }

  c->region_n_histogram_bins           = 16;
  c->region_learning_rate_f            = 0.2f;
  c->region_learning_rate_b            = 0.2f;
  c->region_unconsidered_line_length   = 0.5f;
  c->region_max_considered_line_length = 20.0f;

  c->region_use_region_checking          = 0;
  c->region_measure_occlusions           = 1;
  c->region_measured_depth_offset_radius = 0.01f;
  c->region_measured_occlusion_radius    = 0.01f;
  c->region_measured_occlusion_threshold = 0.03f;
  c->region_n_unoccluded_iterations      = 10;
  c->region_min_n_unoccluded_lines       = 0;

  // Depth
  c->use_depth_modality    = 1;
  c->depth_n_points_max    = 200;
  c->depth_stride_length   = 0.005f;
  c->depth_use_depth_scaling = 0;

  c->depth_considered_distances[0] = 0.05f;
  c->depth_considered_distances[1] = 0.02f;
  c->depth_considered_distances[2] = 0.01f;
  c->depth_standard_deviations[0]  = 0.05f;
  c->depth_standard_deviations[1]  = 0.03f;
  c->depth_standard_deviations[2]  = 0.02f;
  c->depth_n_iterations = 3;
  for (int i = 3; i < M3T_MAX_ITERATIONS; ++i) {
    c->depth_considered_distances[i] = 0.0f;
    c->depth_standard_deviations[i]  = 0.0f;
  }

  c->depth_measure_occlusions = 1;

  // Texture
  c->use_texture_modality                     = 0;
  c->texture_descriptor_type                  = 4;  // ORB
  c->texture_descriptor_distance_threshold    = 0.7f;
  c->texture_tukey_norm_constant              = 20.0f;
  c->texture_max_keyframe_rotation_difference = 10.0f;  // degrees
  c->texture_max_keyframe_age                 = 100;
  c->texture_n_keyframes                      = 1;
  c->texture_focused_image_size               = 200;

  c->texture_orb_n_features    = 300;
  c->texture_orb_scale_factor  = 1.2f;
  c->texture_orb_n_levels      = 3;

  c->texture_sift_n_features         = 0;
  c->texture_sift_n_octave_layers    = 5;
  c->texture_sift_contrast_threshold = 0.04f;
  c->texture_sift_edge_threshold     = 10.0f;
  c->texture_sift_sigma              = 0.7f;

  // Optimizer
  c->tikhonov_parameter_rotation    = 1000.0f;
  c->tikhonov_parameter_translation = 30000.0f;
  
  // Link DoF — all free by default
  for (int i = 0; i < 6; ++i) c->free_directions[i] = 1;

  // Refiner
  c->use_refiner                 = 0;
  c->refiner_n_corr_iterations   = 7;
  c->refiner_n_update_iterations = 2;
}

#ifdef __cplusplus
}
#endif

#endif // M3T_CONFIG_H