using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Meta.XR;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public class Core : MonoBehaviour
{
    [SerializeField] private RawImage rgbImage;
    [SerializeField] private Constants.TargetModel currentTarget = Constants.TargetModel.Pikachu;
    [SerializeField] private float detectionDelay = 1f;
    [SerializeField] private float detectionScore = 0.7f;
    [SerializeField] private float divergenceThreshold = 0.5f;
    [SerializeField] private int nCorrIterations = 7;
    [SerializeField] private int nUpdateIterations = 2;
    [SerializeField] private int trackingResolution = 320;

    [SerializeField] private Transform centerEyeAnchor;
    [SerializeField] private Transform rightEyeAnchor;

    private PassthroughCameraAccess _cameraAccess;
    private Detection _detectionScript;
    private PnP _pnpScript;
    private Tracking _trackingScript;
    private Mapping _mappingScript;
    private KeypointVisualizer _keypointVisualizerScript;
    private DepthHelper _depthHelper;

    private RenderTexture downscaledTexture;
    private float sendTimer;
    private Task _initTask;

    private Matrix4x4? _poseAtDetectionStart;

    private void Awake()
    {
        Constants.TrackingRGBResolution = trackingResolution;
        _cameraAccess = GetComponent<PassthroughCameraAccess>();
        _detectionScript = GetComponent<Detection>();
        _mappingScript = GetComponent<Mapping>();

        _pnpScript = new PnP();
        _trackingScript = new Tracking();
        _keypointVisualizerScript = GetComponent<KeypointVisualizer>();
        _depthHelper = GetComponent<DepthHelper>();

        downscaledTexture = new RenderTexture(Constants.TrackingRGBResolution, Constants.TrackingRGBResolution, 0,
            RenderTextureFormat.ARGB32);
        downscaledTexture.Create();
    }

    private void Start()
    {
        _initTask = _trackingScript.Init(nCorrIterations, nUpdateIterations, currentTarget);
        StartCamera();
    }

    private void OnDestroy()
    {
        _detectionScript.OnDestroy();
        _trackingScript.OnDestroy();
        downscaledTexture.Release();
        Destroy(downscaledTexture);
    }

    private async void Update()
    {
        if (!_trackingScript.IsInitialized) return;

        var cameraTexture = _cameraAccess.GetTexture();
        if (cameraTexture == null) return;
        Graphics.Blit(cameraTexture, downscaledTexture);

        sendTimer += Time.deltaTime;
        int targetId = (int)currentTarget;
        int lines = M3TNative.GetTrackingValidLines(targetId);
        float divergence = M3TNative.GetHistogramDivergence(targetId);
        Debug.Log($"[Histogram] targetId={targetId} lines={lines} divergence={divergence:F3}");
        
        if (!_trackingScript.IsTracking(currentTarget))
        {
            if (sendTimer >= detectionDelay)
            {
                Debug.Log("[Histogram] Detection Init");
                sendTimer = 0f;
                await RunDetection(cameraTexture, detectionScore);
            }
        }
        else
        {
            if (divergence >= 0f && divergence >= divergenceThreshold && sendTimer >= detectionDelay)
            {
                sendTimer = 0f;
                await RunDetection(cameraTexture, detectionScore);
            }
        }

        NativeArray<Color32> colorsBuffer = new NativeArray<Color32>(
            Constants.TrackingRGBResolution * Constants.TrackingRGBResolution,
            Allocator.Persistent);
        AsyncGPUReadback.RequestIntoNativeArray(ref colorsBuffer, downscaledTexture).WaitForCompletion();
        _trackingScript.UpdateTrackerRGBImage(colorsBuffer);
        colorsBuffer.Dispose();

        var depthData = _depthHelper.CaptureAndSendDepth();
        if (depthData != null)
            _trackingScript.UpdateTrackerDepthImage(depthData.Value);
        else
            Debug.Log("[Depth] No Depth Data");
            

        _trackingScript.UpdateTracker();

        var newPose = _trackingScript.GetPose(currentTarget);
        _mappingScript.UpdatePose(newPose);
    }

    private async Task RunDetection(Texture cameraTexture, float minScore)
    {
        bool wasTracking = _trackingScript.IsTracking(currentTarget);
        if (wasTracking)
            _poseAtDetectionStart = _trackingScript.GetPose(currentTarget);

        var result = await _detectionScript.Inference(cameraTexture, minScore);
        if (!result.isValid)
        {
            _poseAtDetectionStart = null;
            return;
        }

        _keypointVisualizerScript.UpdateVisuals(result.keypoints);
        var matrix = _pnpScript.Solve(currentTarget, result);
        if (matrix != null)
        {
            var detectedPose = _detectionScript.TransformDetectionToOrigin(
                matrix.Value, _cameraAccess.Intrinsics.LensOffset);

            if (wasTracking && _poseAtDetectionStart.HasValue)
            {
                var currentPose = _trackingScript.GetPose(currentTarget);
                var correction = detectedPose * _poseAtDetectionStart.Value.inverse;
                var correctedPose = correction * currentPose;
                _trackingScript.UpdateTrackerDetection(currentTarget, correctedPose);
            }
            else
            {
                // Not tracking — use absolute detection pose
                _trackingScript.UpdateTrackerDetection(currentTarget, detectedPose);
            }
        }

        _poseAtDetectionStart = null;
    }

    public async void StartCamera()
    {
        while (!_cameraAccess.IsPlaying)
            await Task.Yield();
        await _initTask;

        var intr = _cameraAccess.Intrinsics;
        Debug.Log($"[Camera] PrincipalPoint {intr.PrincipalPoint}");
        Debug.Log($"[Camera] FocalLength {intr.FocalLength}");
        Debug.Log($"[Camera] SensorResolution {intr.SensorResolution}");
        Debug.Log($"[Camera] LensOffset {intr.LensOffset}");

        // --- RGB intrinsics scaled ---
        float scale = (float)Constants.TrackingRGBResolution / intr.SensorResolution.x;
        float rgbFx = intr.FocalLength.x * scale;
        float rgbFy = intr.FocalLength.y * scale;
        float rgbCx = intr.PrincipalPoint.x * scale;
        float rgbCy = (intr.SensorResolution.y - intr.PrincipalPoint.y) * scale;
        
        // --- RGB extrinsics ---
        Matrix4x4 rgbExtrinsics = Helper.ComputeRGBExtrinsics(intr.LensOffset);

        // --- Depth intrinsics ---
        var (depthFx, depthFy, depthCx, depthCy) =
            Helper.ComputeDepthIntrinsics(OVRPlugin.Node.EyeRight, 320, 320);
        
        // --- Depth extrinsics ---
        Matrix4x4 depthExtrinsics = (centerEyeAnchor != null && rightEyeAnchor != null)
            ? Helper.ComputeDepthExtrinsics(centerEyeAnchor, rightEyeAnchor)
            : Matrix4x4.TRS(new Vector3(0.031034f, 0, 0), Quaternion.identity, Vector3.one);

        _trackingScript.SetCameraParams(
            rgbFx, rgbFy, rgbCx, rgbCy, Constants.TrackingRGBResolution, Constants.TrackingRGBResolution, rgbExtrinsics,
            depthFx, depthFy, depthCx, depthCy, 320, 320, depthExtrinsics);
        _trackingScript.Setup();

        Debug.Log("[Camera] Camera started, tracker configured with live intrinsics.");
    }
}