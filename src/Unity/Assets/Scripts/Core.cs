using System.Diagnostics;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public class Core : MonoBehaviour
{
    [SerializeField] private RawImage rgbImage;

    private const float secondsPerFrame = 0.5f;

    private PassthroughCameraAccess _cameraAccess;
    private Detection _detectionScript;
    private PnP _pnpScript;
    private Tracking _trackingScript;
    private Mapping _mappingScript;
    private KeypointVisualizer _keypointVisualizerScript;
    
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
        
        downscaledTexture = new RenderTexture(640, 640, 0, RenderTextureFormat.ARGB32);
        downscaledTexture.Create();

        Debug.Log($"[DEBUG] PrincipalPoint {_cameraAccess.Intrinsics.PrincipalPoint}");
        Debug.Log($"[DEBUG] FocalLength {_cameraAccess.Intrinsics.FocalLength}");
        Debug.Log($"[DEBUG] SensorResolution {_cameraAccess.Intrinsics.SensorResolution}");
    }

    private void Start()
    {
        // _trackingScript.Init();
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
        sendTimer += Time.deltaTime;
        if (sendTimer >= secondsPerFrame)
        {
            sendTimer = 0f;
        
            var cameraTexture = _cameraAccess.GetTexture();
            if (cameraTexture == null) return;
            Graphics.Blit(cameraTexture, downscaledTexture);
            rgbImage.texture = downscaledTexture;
        
            var result = await _detectionScript.Inference(cameraTexture, 0.7f);
            if (result.isValid)
            {
                Debug.Log($"[DEBUG] Found Object {result.keypoints.Count}");
                _keypointVisualizerScript.UpdateVisuals(result.keypoints);
                var matrix = _pnpScript.Solve(result);
                if (matrix != null)
                {
                    _mappingScript.UpdateTrackingPose(result.target, matrix.Value);
                    // _trackingScript.UpdateTrackerDetection(matrix.Value);
                }
            }
        }
        
        // _trackingScript.UpdateTrackerImage(_cameraAccess.GetColors());
        // Stopwatch sw = new Stopwatch();
        // for (var i = 0; i < 10; i++)
        // {
        //     sw.Start();
        //     _trackingScript.UpdateTracker();
        //     sw.Stop();
        //     Debug.Log($"Execution Time: {sw.Elapsed.TotalMilliseconds} ms");
        // }
        //
        // var newPose = _trackingScript.GetPose();
        // _mappingScript.UpdateTrackingPose(newPose);
    }

    public async void StartCamera()
    {
        while (!_cameraAccess.IsPlaying)
        {
            await Task.Yield();
        }

        Debug.Log("[DEBUG] Camera Started");
        Debug.Log($"[DEBUG] PrincipalPoint {_cameraAccess.Intrinsics.PrincipalPoint}"); // (636.47, 637.35)
        Debug.Log($"[DEBUG] FocalLength {_cameraAccess.Intrinsics.FocalLength}");   // (866.16, 866.16)
        Debug.Log($"[DEBUG] SensorResolution {_cameraAccess.Intrinsics.SensorResolution}"); // (1280, 1280)
        Debug.Log($"[DEBUG] LensOffset {_cameraAccess.Intrinsics.LensOffset}");
    }
}