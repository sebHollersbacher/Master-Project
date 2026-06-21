using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct M3TObjectConfig
{
    // --- Tracker ---
    public int tracker_n_corr_iterations;
    public int tracker_n_update_iterations;

    // --- Region Modality ---
    public int region_n_lines_max;
    public int region_use_adaptive_coverage;
    public float region_min_continuous_distance;
    public int region_function_length;
    public int region_distribution_length;
    public float region_function_amplitude;
    public float region_function_slope;
    public float region_learning_rate;
    public int region_n_global_iterations;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public int[] region_scales;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public float[] region_standard_deviations;

    public int region_n_iterations;

    public int region_n_histogram_bins;
    public float region_learning_rate_f;
    public float region_learning_rate_b;
    public float region_unconsidered_line_length;
    public float region_max_considered_line_length;

    public int region_use_region_checking;
    public int region_measure_occlusions;

    public float region_measured_depth_offset_radius;
    public float region_measured_occlusion_radius;
    public float region_measured_occlusion_threshold;
    public int region_n_unoccluded_iterations;
    public int region_min_n_unoccluded_lines;

    // --- Depth Modality ---
    public int use_depth_modality;
    public int depth_n_points_max;
    public float depth_stride_length;
    public int depth_use_depth_scaling;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public float[] depth_considered_distances;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public float[] depth_standard_deviations;

    public int depth_n_iterations;
    public int depth_measure_occlusions;

    // --- Texture Modality ---
    public int use_texture_modality;
    public int texture_descriptor_type; // 0=BRISK 1=DAISY 2=FREAK 3=SIFT 4=ORB
    public float texture_descriptor_distance_threshold;
    public float texture_tukey_norm_constant;
    public float texture_max_keyframe_rotation_difference;
    public int texture_max_keyframe_age;
    public int texture_n_keyframes;
    public int texture_focused_image_size;

    public int texture_orb_n_features;
    public float texture_orb_scale_factor;
    public int texture_orb_n_levels;

    public int texture_sift_n_features;
    public int texture_sift_n_octave_layers;
    public float texture_sift_contrast_threshold;
    public float texture_sift_edge_threshold;
    public float texture_sift_sigma;

    // --- Optimizer ---
    public float tikhonov_parameter_rotation;
    public float tikhonov_parameter_translation;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public int[] free_directions;
    
    public int use_refiner;
    public int refiner_n_corr_iterations;
    public int refiner_n_update_iterations;
}

public static class M3TNative
{
    private const string DLL = "m3t";

    [DllImport(DLL)]
    public static extern void GetDefaultConfig(ref M3TObjectConfig config);

    [DllImport(DLL)]
    public static extern void InitTracker(int n_corr_iterations, int n_update_iterations);

    [DllImport(DLL)]
    public static extern void SetRGBCameraParams(
        float fx, float fy, float cx, float cy,
        int width, int height, float[] extrinsics_4x4);

    [DllImport(DLL)]
    public static extern void SetDepthCameraParams(
        float fx, float fy, float cx, float cy,
        int width, int height, float[] extrinsics_4x4);

    [DllImport(DLL)]
    public static extern void AddObjectToTracker(
        int target_id,
        string body_meta_path,
        string region_model_path,
        string depth_model_path,
        string texture_path,
        ref M3TObjectConfig config);

    [DllImport(DLL)]
    public static extern float GetHistogramDivergence(int body_id);

    [DllImport(DLL)]
    public static extern bool SetupTrackerHeadless();

    [DllImport(DLL)]
    public static extern void UpdateTrackerHeadless();

    [DllImport(DLL)]
    public static extern void PassRGBCameraFrame(IntPtr data, int width, int height);

    [DllImport(DLL)]
    public static extern void PassDepthCameraFrame(IntPtr data, int width, int height);

    [DllImport(DLL)]
    public static extern void PassNewPose(int body_id, float[] matrix);

    [DllImport(DLL)]
    public static extern void GetBodyPose(int body_id, float[] out_matrix);

    [DllImport(DLL)]
    public static extern int GetTrackingValidLines(int body_id);

    public static M3TObjectConfig PenConfig()
    {
        var cfg = new M3TObjectConfig();
        GetDefaultConfig(ref cfg);

        cfg.region_n_lines_max = 280;
        cfg.region_use_adaptive_coverage = 1;
        cfg.region_min_continuous_distance = 1.8f;
        cfg.region_function_length = 8;
        cfg.region_distribution_length = 10;
        cfg.region_function_amplitude = 0.43f;
        cfg.region_function_slope = 0.35f;
        cfg.region_learning_rate = 1.3f;
        cfg.region_n_global_iterations = 2;
        cfg.region_scales = new[] { 5, 3, 2, 1, 1, 1, 0, 0 };
        cfg.region_standard_deviations = new[] { 25f, 15f, 8f, 4f, 2f, 1f, 0, 0 };
        cfg.region_n_iterations = 6;
        cfg.region_n_histogram_bins = 32;
        cfg.region_learning_rate_f = 0.03f;
        cfg.region_unconsidered_line_length = 0.5f;
        cfg.region_max_considered_line_length = 20.0f;
        cfg.region_measure_occlusions = 1;
        cfg.region_measured_depth_offset_radius = 0.025f;
        cfg.region_measured_occlusion_radius = 0.025f;
        cfg.region_measured_occlusion_threshold = 0.06f;
        cfg.region_n_unoccluded_iterations = 10;

        cfg.use_depth_modality = 1;
        cfg.depth_n_points_max = 80;
        cfg.depth_stride_length = 0.005f;
        cfg.depth_considered_distances = new[] { 0.20f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        cfg.depth_standard_deviations  = new[] { 0.20f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        cfg.depth_n_iterations = 1;
        
        cfg.use_texture_modality = 1;
        cfg.texture_focused_image_size = 200;
        cfg.texture_orb_n_features = 200;
        cfg.texture_orb_n_levels = 3;
        cfg.texture_n_keyframes = 6;
        cfg.texture_max_keyframe_rotation_difference = 15f;

        cfg.free_directions = new[] { 1, 0, 1, 1, 1, 1 };
        cfg.tikhonov_parameter_rotation = 300f;
        cfg.tikhonov_parameter_translation = 24000f;

        return cfg;
    }

    public static M3TObjectConfig RacketConfig()
    {
        var cfg = new M3TObjectConfig();
        GetDefaultConfig(ref cfg);

        cfg.region_use_adaptive_coverage = 1;
        cfg.region_n_lines_max = 350;
        cfg.region_function_length = 8;
        cfg.region_distribution_length = 12;
        cfg.region_function_slope = 0.35f;
        cfg.region_min_continuous_distance = 1.8f;
        cfg.region_max_considered_line_length = 25.0f;
        cfg.region_n_global_iterations = 2;
        cfg.region_scales = new[] { 8, 5, 3, 1, 1, 0, 0, 0 };
        cfg.region_standard_deviations = new[] { 40f, 15f, 7f, 3f, 1.5f, 0, 0, 0 };
        cfg.region_n_iterations = 5;
        cfg.region_measure_occlusions = 1;
        cfg.region_measured_depth_offset_radius = 0.025f;
        cfg.region_measured_occlusion_radius = 0.025f;
        cfg.region_measured_occlusion_threshold = 0.05f;

        cfg.use_depth_modality = 1;
        cfg.depth_n_points_max = 200;
        cfg.depth_stride_length = 0.005f;
        cfg.depth_considered_distances = new[] { 0.20f, 0.10f, 0.05f, 0.03f, 0f, 0f, 0f, 0f };
        cfg.depth_standard_deviations = new[] { 0.20f, 0.10f, 0.05f, 0.03f, 0f, 0f, 0f, 0f };
        cfg.depth_n_iterations = 4;
        cfg.depth_measure_occlusions = 1;

        cfg.use_texture_modality = 1;
        cfg.texture_orb_n_features = 200;
        cfg.texture_focused_image_size = 200;
        cfg.texture_n_keyframes = 8;
        cfg.texture_max_keyframe_rotation_difference = 20f;

        cfg.tikhonov_parameter_rotation = 300f;
        cfg.tikhonov_parameter_translation = 8000f;

        return cfg;
    }

    public static M3TObjectConfig PikachuConfig()
    {
        var cfg = new M3TObjectConfig();
        GetDefaultConfig(ref cfg);

        cfg.region_use_adaptive_coverage = 1;
        cfg.region_n_lines_max = 300;
        cfg.region_measure_occlusions = 1;
        cfg.region_measured_depth_offset_radius = 0.025f;
        cfg.region_measured_occlusion_radius = 0.025f;
        cfg.region_measured_occlusion_threshold = 0.05f;
        cfg.region_min_n_unoccluded_lines = 15;

        cfg.use_depth_modality = 1;
        cfg.depth_considered_distances = new[] { 0.08f, 0.03f, 0.02f, 0, 0, 0, 0, 0 };
        cfg.depth_standard_deviations = new[] { 0.08f, 0.04f, 0.03f, 0, 0, 0, 0, 0 };
        cfg.depth_measure_occlusions = 1;

        cfg.use_texture_modality = 1;
        cfg.texture_orb_n_features = 250;
        cfg.texture_focused_image_size = 150;
        cfg.texture_n_keyframes = 5;
        cfg.texture_max_keyframe_rotation_difference = 20f;

        cfg.tikhonov_parameter_rotation = 300f;

        return cfg;
    }
}