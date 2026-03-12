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

    private const float secondsPerFrame = 3f;

    private PassthroughCameraAccess _cameraAccess;
    private Detection _detectionScript;
    private PnP _pnpScript;
    private Tracking _trackingScript;
    private Mapping _mappingScript;
    private KeypointVisualizer _keypointVisualizerScript;

    private Constants.TargetModel _currentTarget = Constants.TargetModel.Pikachu;
    private RenderTexture downscaledTexture;
    private float sendTimer;

    private void Awake()
    {
        _cameraAccess = GetComponent<PassthroughCameraAccess>();
        _detectionScript = GetComponent<Detection>();
        _mappingScript = GetComponent<Mapping>();

        _mappingScript.lensOffset = _cameraAccess.Intrinsics.LensOffset;
        _pnpScript = new PnP();
        _trackingScript = new Tracking();
        _keypointVisualizerScript = GetComponent<KeypointVisualizer>();

        downscaledTexture = new RenderTexture(320, 320, 0, RenderTextureFormat.ARGB32);
        downscaledTexture.Create();

        Debug.Log($"[DEBUG] PrincipalPoint {_cameraAccess.Intrinsics.PrincipalPoint}");
        Debug.Log($"[DEBUG] FocalLength {_cameraAccess.Intrinsics.FocalLength}");
        Debug.Log($"[DEBUG] SensorResolution {_cameraAccess.Intrinsics.SensorResolution}");
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
        // rgbImage.texture = downscaledTexture;
        
        sendTimer += Time.deltaTime;
        if (sendTimer >= secondsPerFrame)
        {
            sendTimer = 0f;
            var result  = await _detectionScript.Inference(_currentTarget, cameraTexture, 0.7f);
            if (result.isValid)
            {
                Debug.Log($"[DEBUG] Found Object {result.keypoints.Count}");
                // _keypointVisualizerScript.UpdateVisuals(result.keypoints);
                var matrix = _pnpScript.Solve(_currentTarget, result);
                if (matrix != null)
                {
                    _mappingScript.UpdateTrackingPose(_currentTarget, matrix.Value);
                    if(_currentTarget == Constants.TargetModel.Pikachu)
                        _trackingScript.UpdateTrackerDetection(matrix.Value);
                }
            }

            // _currentTarget = _currentTarget switch
            // {
            //     Constants.TargetModel.Pikachu => Constants.TargetModel.Racket,
            //     Constants.TargetModel.Racket => Constants.TargetModel.Pen,
            //     Constants.TargetModel.Pen => Constants.TargetModel.Pikachu,
            // };
        }

        NativeArray<Color32>
            colorsBuffer = new NativeArray<Color32>(320 * 320, Allocator.Persistent);
        AsyncGPUReadback.RequestIntoNativeArray(ref colorsBuffer, downscaledTexture).WaitForCompletion();
        
        _trackingScript.UpdateTrackerImage(colorsBuffer);
        Stopwatch sw = new Stopwatch();
        for (var i = 0; i < 4; i++)
        {
            sw.Start();
            _trackingScript.UpdateTracker();
            sw.Stop();
            Debug.Log($"Execution Time: {sw.Elapsed.TotalMilliseconds} ms");
        }
        
        var newPose = _trackingScript.GetPose();
        _mappingScript.UpdateTrackingPose(Constants.TargetModel.Pikachu, newPose);
    }

    public async void StartCamera()
    {
        while (!_cameraAccess.IsPlaying)
        {
            await Task.Yield();
        }

        Debug.Log("[DEBUG] Camera Started");
        Debug.Log($"[DEBUG] PrincipalPoint {_cameraAccess.Intrinsics.PrincipalPoint}"); // (636.47, 637.35)
        Debug.Log($"[DEBUG] FocalLength {_cameraAccess.Intrinsics.FocalLength}"); // (866.16, 866.16)
        Debug.Log($"[DEBUG] SensorResolution {_cameraAccess.Intrinsics.SensorResolution}"); // (1280, 1280)
        Debug.Log($"[DEBUG] LensOffset {_cameraAccess.Intrinsics.LensOffset}");
    }
}