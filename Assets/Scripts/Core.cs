using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;
using UnityEngine.UI;

public class Core : MonoBehaviour
{
    [SerializeField] private Transform _transform;
    [SerializeField] private Transform _ref;
    [SerializeField] private RawImage rgbImage;
    
    private const float secondsPerFrame = 1f;

    private PassthroughCameraAccess _cameraAccess;
    private Detection _detectionScript;
    private PnP _pnpScript;
    private Tracking _trackingScript;
    private Mapping _mappingScript;
    private KeypointVisualizer _keypointVisualizerScript;

    private float sendTimer;

    private void Awake()
    {
        _cameraAccess = GetComponent<PassthroughCameraAccess>();
        _detectionScript = GetComponent<Detection>();
        _pnpScript = new PnP();
        _trackingScript = new Tracking();
        _mappingScript = new Mapping(_ref);
        _keypointVisualizerScript = GetComponent<KeypointVisualizer>();
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
    }

    private async void Update()
    {
        sendTimer += Time.deltaTime;
        if (sendTimer >= secondsPerFrame)
        {
            sendTimer = 0f;

            var cameraTexture = _cameraAccess.GetTexture();
            if (cameraTexture == null) return;

            var keypoints = await _detectionScript.Inference(cameraTexture, 0.7f);
            if (keypoints.Count == 0)
            {
                Debug.Log("[DEBUG] No Pikachu");
            }
            else
            {
                Debug.Log($"[DEBUG] Pikachu {keypoints.Count}");
                _keypointVisualizerScript.UpdateVisuals(keypoints);
                var matrix = _pnpScript.Solve(keypoints);
                if (matrix != null)
                    _trackingScript.UpdateTrackerDetection(matrix.Value);
            }
        }

        _trackingScript.UpdateTrackerImage(_cameraAccess.GetColors());
        _trackingScript.UpdateTracker();
        var newPose = _trackingScript.GetPose();
        _mappingScript.UpdateTrackingPose(_transform, newPose);
    }

    public async void StartCamera()
    {
        Debug.Log("[DEBUG] Start Camera");

        while (!_cameraAccess.IsPlaying)
        {
            await Task.Yield();
        }

        Debug.Log("[DEBUG] Camera Started");
        rgbImage.texture = _cameraAccess.GetTexture();
        Debug.Log($"[DEBUG] FocalLength: {_cameraAccess.Intrinsics.FocalLength}, \n" +
                  $"[DEBUG] LensOffset: {_cameraAccess.Intrinsics.LensOffset}, \n" +
                  $"[DEBUG] PrincipalPoint: {_cameraAccess.Intrinsics.PrincipalPoint}, \n" +
                  $"[DEBUG] Resolution: {_cameraAccess.Intrinsics.SensorResolution}, \n");
    }
}