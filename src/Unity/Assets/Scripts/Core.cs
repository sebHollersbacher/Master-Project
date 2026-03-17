using System;
using System.Diagnostics;
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

    private const float secondsPerFrame = 2f;

    private PassthroughCameraAccess _cameraAccess;
    private Detection _detectionScript;
    private PnP _pnpScript;
    private Tracking _trackingScript;
    private Mapping _mappingScript;
    private KeypointVisualizer _keypointVisualizerScript;
    private DepthHelper _depthHelper;

    private Constants.TargetModel _currentTarget = Constants.TargetModel.Pikachu;
    private RenderTexture downscaledTexture;
    private float sendTimer;

    private void Awake()
    {
        _cameraAccess = GetComponent<PassthroughCameraAccess>();
        _detectionScript = GetComponent<Detection>();
        _mappingScript = GetComponent<Mapping>();

        _pnpScript = new PnP();
        _trackingScript = new Tracking();
        _keypointVisualizerScript = GetComponent<KeypointVisualizer>();
        _depthHelper = GetComponent<DepthHelper>();
        _depthHelper.setTrackingScript(_trackingScript);

        downscaledTexture = new RenderTexture(320, 320, 0, RenderTextureFormat.ARGB32);
        downscaledTexture.Create();
    }

    private void Start()
    {
        _trackingScript.Init();
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
        var cameraTexture = _cameraAccess.GetTexture();
        if (cameraTexture == null) return;
        Graphics.Blit(cameraTexture, downscaledTexture);
        
        sendTimer += Time.deltaTime;
        if (sendTimer >= secondsPerFrame)
        {
            sendTimer = 0f;
            var result  = await _detectionScript.Inference(_currentTarget, cameraTexture, 0.7f);
            if (result.isValid)
            {
                _keypointVisualizerScript.UpdateVisuals(result.keypoints);
                var matrix = _pnpScript.Solve(_currentTarget, result);
                if (matrix != null)
                {
                    var correctedPose = _detectionScript.TransformDetectionToOrigin(matrix.Value,
                        _cameraAccess.Intrinsics.LensOffset);
                    _trackingScript.UpdateTrackerDetection(_currentTarget, correctedPose);
                }
            }

            _currentTarget = _currentTarget switch
            {
                Constants.TargetModel.Pikachu => Constants.TargetModel.Pen,
                // Constants.TargetModel.Racket => Constants.TargetModel.Pen,
                Constants.TargetModel.Pen => Constants.TargetModel.Pikachu,
            };
        }

        NativeArray<Color32>
            colorsBuffer = new NativeArray<Color32>(320 * 320, Allocator.Persistent);
        AsyncGPUReadback.RequestIntoNativeArray(ref colorsBuffer, downscaledTexture).WaitForCompletion();
        _trackingScript.UpdateTrackerRGBImage(colorsBuffer);
        
        for (var i = 0; i < 4; i++)
        {
            _trackingScript.UpdateTracker();
        }

        foreach (Constants.TargetModel target in Enum.GetValues(typeof(Constants.TargetModel)))
        {
            var newPose = _trackingScript.GetPose(target);
            _mappingScript.UpdatePose(target, newPose);
        }
    }

    public async void StartCamera()
    {
        while (!_cameraAccess.IsPlaying)
        {
            await Task.Yield();
        }

        Debug.Log("[Camera] Camera Started");
        Debug.Log($"[Camera] PrincipalPoint {_cameraAccess.Intrinsics.PrincipalPoint}"); // (636.47, 637.35)
        Debug.Log($"[Camera] FocalLength {_cameraAccess.Intrinsics.FocalLength}"); // (866.16, 866.16)
        Debug.Log($"[Camera] SensorResolution {_cameraAccess.Intrinsics.SensorResolution}"); // (1280, 1280)
        Debug.Log($"[Camera] LensOffset {_cameraAccess.Intrinsics.LensOffset}"); // ((0.03, -0.02, 0.06), (0.09526, -0.00290, 0.00387, 0.99544))
    }
}