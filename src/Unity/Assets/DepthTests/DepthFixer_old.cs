using Meta.XR;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.UI;

public class DepthFixer_old : MonoBehaviour
{
    private static readonly int DepthTextureID = Shader.PropertyToID("_EnvironmentDepthTexture");
    private static readonly int ReprojectionMatricesID = Shader.PropertyToID("_EnvironmentDepthReprojectionMatrices");
    private static readonly int ZBufferParamsID = Shader.PropertyToID("_EnvironmentDepthZBufferParams");

    public ComputeShader compute;
    public Camera xrCamera;
    [SerializeField] private RawImage depthImage;
    [SerializeField] private RawImage depthImage2;
    [SerializeField] private EnvironmentDepthManager _envDepthManager;

    RenderTexture outRT;
    int kernel;

    Matrix4x4[] reproj; // From Meta API (global shader property)
    Matrix4x4[] reprojInv; // Our inverse matrices

    private void Start()
    {
        kernel = compute.FindKernel("DepthMapping");

        if (!EnvironmentDepthManager.IsSupported)
        {
            Debug.LogError("[DEBUG] Depth API is not supported on this device");
            return;
        }

        // Enable the depth manager
        _envDepthManager.enabled = true;
    }

    private void Update()
    {
        if (_envDepthManager.IsDepthAvailable)
        {
            var rawDepth = Shader.GetGlobalTexture(DepthTextureID) as RenderTexture;

            if (rawDepth == null)
            {
                // Depth not available yet
                return;
            }

            if (rawDepth.dimension != UnityEngine.Rendering.TextureDimension.Tex2DArray)
            {
                Debug.LogWarning("EnvironmentDepth texture is not Tex2DArray");
                return;
            }

            int w = rawDepth.width;
            int h = rawDepth.height;

            //--------------------------------------------
            // 2. Prepare the output RT (camera-aligned depth)
            //--------------------------------------------
            if (outRT == null || outRT.width != w || outRT.height != h)
            {
                if (outRT != null) outRT.Release();

                outRT = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat)
                {
                    enableRandomWrite = true,
                    dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                outRT.Create();
                
                if (depthImage != null)
                    depthImage.texture = rawDepth;

                if (depthImage2 != null)
                    depthImage2.texture = outRT;
            }

            //--------------------------------------------
            // 3. Get reprojection matrices from Meta runtime
            //--------------------------------------------
            Matrix4x4[] reproj = Shader.GetGlobalMatrixArray(ReprojectionMatricesID);

            if (reproj == null || reproj.Length < 1)
                return;

            // Mono depth: only slice 0 exists
            Matrix4x4[] reprojInv = new Matrix4x4[2];

            // Compute inverse for left/right (XR uses stereo)
            reprojInv[0] = reproj[0].inverse;
            if (reproj.Length > 1)
                reprojInv[1] = reproj[1].inverse;
            else
                reprojInv[1] = reprojInv[0];

            //--------------------------------------------
            // 4. Get XR camera view matrices
            //--------------------------------------------
            Matrix4x4 viewL = xrCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 viewR = xrCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);

            //--------------------------------------------
            // 5. Pass everything into compute shader
            //--------------------------------------------
            var trackingToUnity = xrCamera.transform.localToWorldMatrix;
            var unityToTracking = xrCamera.transform.worldToLocalMatrix;

            compute.SetMatrix("_TrackingToUnity", trackingToUnity);
            compute.SetMatrix("_UnityToTracking", unityToTracking);
            
            compute.SetTexture(kernel, "_RawDepth", rawDepth);
            compute.SetTexture(kernel, "_OutDepth", outRT);

            compute.SetInt("_Width", w);
            compute.SetInt("_Height", h);

            compute.SetInt("_EyeIndex", 0); // mono depth: always use eye 0

            compute.SetMatrixArray("_EnvReprojInv", reprojInv);

            compute.SetMatrix("_XRCameraView_L", viewL);
            compute.SetMatrix("_XRCameraView_R", viewR);

            //--------------------------------------------
            // 6. Dispatch
            //--------------------------------------------
            int gx = Mathf.CeilToInt(w / 8.0f);
            int gy = Mathf.CeilToInt(h / 8.0f);

            compute.Dispatch(kernel, gx, gy, 1);
        }
    }
}