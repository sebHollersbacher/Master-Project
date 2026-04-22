using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class Evaluation : MonoBehaviour
{
    public enum EvalMode
    {
        DetectionOnly,
        TrackingOnly,
        FullPipeline
    }

    [Header("Session")] [SerializeField] private string sessionPath;
    [SerializeField] private Constants.TargetModel currentTarget = Constants.TargetModel.Pikachu;

    [Header("Detection")] [SerializeField] private float minScore = 0.7f;

    [Tooltip("FullPipeline: run detection every N frames (≈0.5s at 72Hz → 36)")] [SerializeField]
    private int detectionInterval = 36;

    [Header("Tracking")] [SerializeField] private int trackingIterationsPerFrame = 4;

    [Header("Timing")] [Tooltip("First N frames excluded from timing averages (GPU warm-up)")] [SerializeField]
    private int warmupFrames = 5;

    [Header("References (auto-resolved)")] [SerializeField]
    private Detection detectionScript;

    private PnP _pnpScript;
    private Tracking _trackingScript;
    private Worker _activeWorker;
    private Tensor<float> _inputTensor;
    private RenderTexture _copyTexture;

    // Ground truth for TrackingOnly init
    private Dictionary<string, float[]> _gtMatrices;

    // T_centerEye = _cameraToCenter * T_camera
    // T_camera    = _centerToCamera * T_centerEye
    private Matrix4x4 _cameraToCenter; // feed to tracker
    private Matrix4x4 _centerToCamera; // read from tracker

    private List<string> _trkFiles;
    private HashSet<string> _detFileSet;
    
    [Serializable]
    private class FrameResult
    {
        public string frame_id;
        public string mode;

        // Detection
        public bool detection_ran;
        public bool yolo_valid;
        public float yolo_confidence;
        public float preprocess_ms;
        public float gpu_infer_ms;
        public float readback_ms;
        public float postprocess_ms;
        public float yolo_total_ms;
        public float[] bounding_box;

        // PnP
        public bool pnp_valid;
        public float pnp_time_ms;
        public float yolo_pnp_time_ms;
        public float[] pnp_matrix;

        // Tracking
        public bool tracking_ran;
        public float tracking_time_ms;
        public float[] tracking_matrix;
    }

    [Serializable]
    private class EvaluationOutput
    {
        public string session;
        public string target;
        public string eval_mode;
        public string timestamp;
        public int total_frames;
        public int warmup_frames;
        public int detection_interval;
        public int tracking_iterations;
        public int detection_frames;
        public int yolo_valid_count;
        public int pnp_valid_count;
        public int tracking_frames;
        public float avg_preprocess_ms;
        public float avg_gpu_infer_ms;
        public float avg_readback_ms;
        public float avg_postprocess_ms;
        public float avg_yolo_total_ms;
        public float avg_pnp_ms;
        public float avg_yolo_pnp_ms;
        public float avg_tracking_ms;
        public List<FrameResult> frames;
    }

    // ────────────────────────────────────────────────────────────
    //  Lifecycle
    // ────────────────────────────────────────────────────────────

    private void Awake()
    {
        sessionPath = Path.Combine(Application.persistentDataPath, "dataset", "pen");

        if (detectionScript == null)
            detectionScript = GetComponent<Detection>();

        _pnpScript = new PnP();
        _trackingScript = new Tracking();

        _inputTensor = new Tensor<float>(new TensorShape(1, 3, Constants.Height, Constants.Width));
        _copyTexture = new RenderTexture(Constants.Width, Constants.Height, 0, RenderTextureFormat.ARGB32);
        _copyTexture.Create();

        // Precompute coordinate transforms (same math as Detection.TransformDetectionToOrigin)
        Vector3 lensPos = new Vector3(0.031619f, -0.017903f, 0.062940f);
        Quaternion lensRot = new Quaternion(0.09541f, -0.00329f, 0.00397f, 0.99542f);
        Matrix4x4 lensLocalUnity = Matrix4x4.TRS(lensPos, lensRot, Vector3.one);
        Matrix4x4 flipY = Matrix4x4.Scale(new Vector3(1, -1, 1));
        _cameraToCenter = flipY * lensLocalUnity * flipY; // camera → center-eye (OpenCV)
        _centerToCamera = _cameraToCenter.inverse; // center-eye → camera (OpenCV)
    }

    private async void Start()
    {
        await _trackingScript.Init();
        LoadGroundTruthMatrices();

        _activeWorker = GetWorkerViaReflection(currentTarget);
        if (_activeWorker == null)
        {
            Debug.LogError("[Eval] Could not get Sentis Worker. Make sure Detection.Start() has run.");
            return;
        }

        if (string.IsNullOrEmpty(sessionPath) || !Directory.Exists(sessionPath))
        {
            Debug.LogError($"[Eval] Session path not found: {sessionPath}");
            return;
        }

        _trkFiles = new List<string>(Directory.GetFiles(sessionPath, "frame_*_trk.png"));
        _trkFiles.Sort();
        _detFileSet = new HashSet<string>();
        foreach (var f in Directory.GetFiles(sessionPath, "frame_*_det.png"))
            _detFileSet.Add(Path.GetFileNameWithoutExtension(f).Replace("_det", ""));

        Debug.Log(
            $"[Eval] {_trkFiles.Count} tracker frames, {_detFileSet.Count} detection images, target: {currentTarget}");

        Debug.Log("[Eval] ═══ RunAll: executing DetectionOnly → TrackingOnly → FullPipeline ═══");

        await RunSingleMode(EvalMode.DetectionOnly);
        await ResetTracker();
        await RunSingleMode(EvalMode.TrackingOnly);
        await ResetTracker();
        await RunSingleMode(EvalMode.FullPipeline);

        Debug.Log("[Eval] ═══ RunAll complete — 3 result files written ═══");
    }

    private void OnDestroy()
    {
        detectionScript?.OnDestroy();
        _trackingScript?.OnDestroy();
        _inputTensor?.Dispose();
        if (_copyTexture != null)
        {
            _copyTexture.Release();
            Destroy(_copyTexture);
        }
    }

    /// <summary>
    /// Re-initialize the tracker so the next mode starts with a clean state.
    /// We re-call Init() which calls InitTracker + AddObjects + SetupHeadless on the native side.
    /// </summary>
    private async Task ResetTracker()
    {
        _trackingScript.OnDestroy();
        _trackingScript = new Tracking();
        await _trackingScript.Init();
        Debug.Log("[Eval] Tracker reset for next mode");
    }

    // ════════════════════════════════════════════════════════════════
    //  Core evaluation loop for a single mode
    // ════════════════════════════════════════════════════════════════

    private async Task RunSingleMode(EvalMode currentMode)
    {
        Debug.Log($"[Eval] ── Starting {currentMode} ──");

        var results = new List<FrameResult>();
        bool trackerHasPose = false;
        var sw = new Stopwatch();
        string modeStr = currentMode.ToString();

        bool modeNeedsDetection = currentMode == EvalMode.DetectionOnly || currentMode == EvalMode.FullPipeline;
        bool modeNeedsTracking = currentMode == EvalMode.TrackingOnly || currentMode == EvalMode.FullPipeline;

        for (int i = 0; i < _trkFiles.Count; i++)
        {
            string trkPath = _trkFiles[i];
            string frameId = Path.GetFileNameWithoutExtension(trkPath).Replace("_trk", "");
            var result = new FrameResult { frame_id = frameId, mode = modeStr };

            // ══════════════════════════════════════════════════
            //  DETECTION
            // ══════════════════════════════════════════════════
            bool runDetection = false;
            if (modeNeedsDetection)
            {
                runDetection = currentMode == EvalMode.DetectionOnly
                    ? _detFileSet.Contains(frameId)
                    : (i % detectionInterval == 0) && _detFileSet.Contains(frameId);
            }

            Matrix4x4? detectionPose = null;

            if (runDetection)
            {
                result.detection_ran = true;
                string detPath = Path.Combine(sessionPath, $"{frameId}_det.png");
                Texture2D detTex = LoadPNG(detPath);

                if (detTex != null)
                {
                    RenderTexture rt = RenderTexture.GetTemporary(480, 480, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(detTex, rt);

                    // Preprocess
                    sw.Restart();
                    Graphics.Blit(rt, _copyTexture);
                    TextureConverter.ToTensor(_copyTexture, _inputTensor,
                        new TextureTransform().SetTensorLayout(TensorLayout.NCHW));
                    sw.Stop();
                    result.preprocess_ms = (float)sw.Elapsed.TotalMilliseconds;

                    // GPU inference
                    sw.Restart();
                    _activeWorker.Schedule(_inputTensor);
                    sw.Stop();
                    result.gpu_infer_ms = (float)sw.Elapsed.TotalMilliseconds;

                    // Readback
                    Tensor<float> outputTensor = _activeWorker.PeekOutput() as Tensor<float>;
                    int numFeatures = outputTensor.shape[1];
                    float[] data = null;

                    sw.Restart();
                    try
                    {
                        Tensor<float> cpuTensor = await outputTensor.ReadbackAndCloneAsync();
                        data = cpuTensor.DownloadToArray();
                        cpuTensor.Dispose();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Eval] Readback failed {frameId}: {e.Message}");
                    }

                    sw.Stop();
                    result.readback_ms = (float)sw.Elapsed.TotalMilliseconds;

                    if (data != null)
                    {
                        // Postprocess
                        sw.Restart();
                        var yoloResult = detectionScript.ParseKeypoints(data, minScore, numFeatures);
                        sw.Stop();
                        result.postprocess_ms = (float)sw.Elapsed.TotalMilliseconds;

                        result.yolo_valid = yoloResult.isValid;
                        result.yolo_confidence = yoloResult.confidence;
                        result.yolo_total_ms = result.preprocess_ms + result.gpu_infer_ms
                                                                    + result.readback_ms + result.postprocess_ms;

                        if (yoloResult.isValid)
                        {
                            result.bounding_box = new[]
                            {
                                yoloResult.boundingBox.x, yoloResult.boundingBox.y,
                                yoloResult.boundingBox.width, yoloResult.boundingBox.height
                            };

                            // PnP
                            sw.Restart();
                            var pnpPose = _pnpScript.Solve(currentTarget, yoloResult);
                            sw.Stop();
                            result.pnp_time_ms = (float)sw.Elapsed.TotalMilliseconds;
                            result.pnp_valid = pnpPose.HasValue;
                            result.yolo_pnp_time_ms = result.yolo_total_ms + result.pnp_time_ms;

                            if (pnpPose.HasValue)
                            {
                                result.pnp_matrix = Matrix4x4ToArray(pnpPose.Value);
                                detectionPose = pnpPose.Value;
                            }
                        }
                    }

                    RenderTexture.ReleaseTemporary(rt);
                    Destroy(detTex);
                }
            }

            // ══════════════════════════════════════════════════
            //  TRACKER INITIALIZATION
            // ══════════════════════════════════════════════════

            // TrackingOnly: init from GT on first frame
            // GT is in camera space → transform to center-eye space for M3T
            if (currentMode == EvalMode.TrackingOnly && !trackerHasPose && _gtMatrices != null)
            {
                if (_gtMatrices.ContainsKey(frameId))
                {
                    Matrix4x4 gtPoseCamera = GTToMatrix4x4(_gtMatrices[frameId]);
                    Matrix4x4 gtPoseCenter = _cameraToCenter * gtPoseCamera;
                    _trackingScript.UpdateTrackerDetection(currentTarget, gtPoseCenter);
                    trackerHasPose = true;
                    Debug.Log($"[Eval] TrackingOnly: tracker initialized from GT at {frameId}");
                }
            }

            // FullPipeline: feed detection to tracker
            // PnP outputs camera space → transform to center-eye space (like Core.cs line 77-79)
            if (currentMode == EvalMode.FullPipeline && detectionPose.HasValue)
            {
                Matrix4x4 poseForTracker = _cameraToCenter * detectionPose.Value;
                _trackingScript.UpdateTrackerDetection(currentTarget, poseForTracker);
                trackerHasPose = true;
            }

            // ══════════════════════════════════════════════════
            //  TRACKING — every frame
            // ══════════════════════════════════════════════════
            if (modeNeedsTracking && trackerHasPose)
            {
                result.tracking_ran = true;

                // Feed RGB
                Texture2D trkTex = LoadPNG(trkPath);
                if (trkTex != null)
                {
                    var colors = trkTex.GetPixels32();
                    var nativeColors = new NativeArray<Color32>(colors, Allocator.Persistent);
                    _trackingScript.UpdateTrackerRGBImage(nativeColors);
                    nativeColors.Dispose();
                    Destroy(trkTex);
                }

                // Feed depth
                string depthPath = Path.Combine(sessionPath, $"{frameId}.depth");
                if (File.Exists(depthPath))
                {
                    byte[] depthBytes = File.ReadAllBytes(depthPath);
                    var depthNative = new NativeArray<ushort>(320 * 320, Allocator.Persistent);
                    unsafe
                    {
                        fixed (byte* src = depthBytes)
                        {
                            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpy(
                                Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(depthNative),
                                src, depthBytes.Length);
                        }
                    }

                    _trackingScript.UpdateTrackerDepthImage(depthNative);
                    depthNative.Dispose();
                }

                // Time tracker iterations
                sw.Restart();
                for (int iter = 0; iter < trackingIterationsPerFrame; iter++)
                    _trackingScript.UpdateTracker();
                sw.Stop();
                result.tracking_time_ms = (float)sw.Elapsed.TotalMilliseconds;

                // Read tracker pose (center-eye space) and convert to camera space for GT comparison
                var trackPoseCenter = _trackingScript.GetPose(currentTarget);
                var trackPoseCamera = _centerToCamera * trackPoseCenter;
                result.tracking_matrix = Matrix4x4ToArray(trackPoseCamera);
            }

            results.Add(result);

            if (i % 50 == 0)
            {
                string det = result.detection_ran ? $"det={result.yolo_total_ms:F1}+pnp={result.pnp_time_ms:F1}ms" : "";
                string trk = result.tracking_ran ? $"trk={result.tracking_time_ms:F1}ms" : "";
                Debug.Log($"[Eval][{currentMode}] {i + 1}/{_trkFiles.Count}  {det}  {trk}");
                await Task.Yield();
            }
        }

        SaveResults(currentMode, results);
        Debug.Log($"[Eval] ── {currentMode} complete ──");
    }

    // ════════════════════════════════════════════════════════════════
    //  Save
    // ════════════════════════════════════════════════════════════════

    private void SaveResults(EvalMode currentMode, List<FrameResult> results)
    {
        int detFrames = 0, yoloValid = 0, pnpValid = 0, trkFrames = 0;
        float sumPre = 0, sumGpu = 0, sumRead = 0, sumPost = 0;
        float sumYolo = 0, sumPnp = 0, sumCombined = 0, sumTrack = 0;
        int detStat = 0, pnpStat = 0, trkStat = 0;

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            if (r.detection_ran) detFrames++;
            if (r.yolo_valid) yoloValid++;
            if (r.pnp_valid) pnpValid++;
            if (r.tracking_ran) trkFrames++;

            bool past = i >= warmupFrames;
            if (r.detection_ran && past)
            {
                detStat++;
                sumPre += r.preprocess_ms;
                sumGpu += r.gpu_infer_ms;
                sumRead += r.readback_ms;
                sumPost += r.postprocess_ms;
                sumYolo += r.yolo_total_ms;
                sumCombined += r.yolo_pnp_time_ms;
                if (r.pnp_valid)
                {
                    sumPnp += r.pnp_time_ms;
                    pnpStat++;
                }
            }

            if (r.tracking_ran && past)
            {
                sumTrack += r.tracking_time_ms;
                trkStat++;
            }
        }

        int n = results.Count;
        int detInt = currentMode == EvalMode.FullPipeline ? detectionInterval
            : currentMode == EvalMode.DetectionOnly ? 1 : 0;

        var output = new EvaluationOutput
        {
            session = sessionPath,
            target = currentTarget.ToString(),
            eval_mode = currentMode.ToString(),
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            total_frames = n,
            warmup_frames = warmupFrames,
            detection_interval = detInt,
            tracking_iterations = trackingIterationsPerFrame,
            detection_frames = detFrames,
            yolo_valid_count = yoloValid,
            pnp_valid_count = pnpValid,
            tracking_frames = trkFrames,
            avg_preprocess_ms = detStat > 0 ? sumPre / detStat : 0,
            avg_gpu_infer_ms = detStat > 0 ? sumGpu / detStat : 0,
            avg_readback_ms = detStat > 0 ? sumRead / detStat : 0,
            avg_postprocess_ms = detStat > 0 ? sumPost / detStat : 0,
            avg_yolo_total_ms = detStat > 0 ? sumYolo / detStat : 0,
            avg_pnp_ms = pnpStat > 0 ? sumPnp / pnpStat : 0,
            avg_yolo_pnp_ms = detStat > 0 ? sumCombined / detStat : 0,
            avg_tracking_ms = trkStat > 0 ? sumTrack / trkStat : 0,
            frames = results
        };

        string json = JsonUtility.ToJson(output, true);
        string outName = $"_eval_{currentMode.ToString().ToLower()}.json";
        string outPath = Path.Combine(sessionPath, outName);
        File.WriteAllText(outPath, json);

        Debug.Log($"[Eval] Saved {outPath}");
        Debug.Log(
            $"[Eval] {currentMode}: {n} frames | Det: {detFrames} | YOLO: {yoloValid} | PnP: {pnpValid} | Tracked: {trkFrames}");
        if (detStat > 0)
            Debug.Log(
                $"[Eval]   Pre:{output.avg_preprocess_ms:F1} GPU:{output.avg_gpu_infer_ms:F1} Read:{output.avg_readback_ms:F1} Post:{output.avg_postprocess_ms:F1} YOLO:{output.avg_yolo_total_ms:F1} PnP:{output.avg_pnp_ms:F1}ms");
        if (trkStat > 0)
            Debug.Log($"[Eval]   Track: {output.avg_tracking_ms:F2}ms/frame ({trackingIterationsPerFrame} iters)");
    }

    // ════════════════════════════════════════════════════════════════
    //  Ground-truth loader
    // ════════════════════════════════════════════════════════════════

    private void LoadGroundTruthMatrices()
    {
        _gtMatrices = new Dictionary<string, float[]>();
        string gtPath = Path.Combine(sessionPath, "_ground_truth.json");
        if (!File.Exists(gtPath))
        {
            Debug.LogWarning($"[Eval] No _ground_truth.json found — TrackingOnly will be skipped if selected.");
            return;
        }

        string json = File.ReadAllText(gtPath);
        _gtMatrices = MiniJsonParse(json);
        Debug.Log($"[Eval] Loaded {_gtMatrices.Count} ground-truth entries");
    }

    private Dictionary<string, float[]> MiniJsonParse(string json)
    {
        var result = new Dictionary<string, float[]>();
        int pos = 0;
        while (true)
        {
            int keyStart = json.IndexOf("\"frame_", pos);
            if (keyStart < 0) break;
            int keyEnd = json.IndexOf("\"", keyStart + 1);
            string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

            int tStart = json.IndexOf("\"T_camera_object\"", keyEnd);
            if (tStart < 0) break;
            int arrStart = json.IndexOf("[", tStart);
            if (arrStart < 0) break;

            var floats = new List<float>();
            int searchPos = arrStart;
            while (floats.Count < 16)
            {
                int numStart = -1;
                for (int i = searchPos; i < json.Length; i++)
                {
                    char c = json[i];
                    if (c == '-' || c == '.' || char.IsDigit(c))
                    {
                        numStart = i;
                        break;
                    }
                }

                if (numStart < 0) break;

                int numEnd = numStart + 1;
                while (numEnd < json.Length)
                {
                    char c = json[numEnd];
                    if (c == ',' || c == ']' || c == '\n' || c == ' ') break;
                    numEnd++;
                }

                if (float.TryParse(json.Substring(numStart, numEnd - numStart).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float val))
                    floats.Add(val);
                searchPos = numEnd + 1;
            }

            if (floats.Count == 16)
                result[key] = floats.ToArray();
            pos = searchPos;
        }

        return result;
    }

    private Matrix4x4 GTToMatrix4x4(float[] gt16)
    {
        Matrix4x4 m = Matrix4x4.identity;
        for (int row = 0; row < 4; row++)
        for (int col = 0; col < 4; col++)
            m[col * 4 + row] = gt16[row * 4 + col];
        return m;
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    private Worker GetWorkerViaReflection(Constants.TargetModel target)
    {
        try
        {
            var field = typeof(Detection).GetField("workers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null) return null;
            var dict = field.GetValue(detectionScript) as Dictionary<Constants.TargetModel, Worker>;
            return dict != null && dict.ContainsKey(target) ? dict[target] : null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Eval] Reflection failed: {e.Message}");
            return null;
        }
    }

    private static Texture2D LoadPNG(string path)
    {
        if (!File.Exists(path)) return null;
        byte[] bytes = File.ReadAllBytes(path);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Destroy(tex);
            return null;
        }

        return tex;
    }

    private static float[] Matrix4x4ToArray(Matrix4x4 m)
    {
        float[] arr = new float[16];
        for (int i = 0; i < 16; i++) arr[i] = m[i];
        return arr;
    }
}