using UnityEngine;

public class Helper
{
    public static void PrintRelativeRGBPoses(Pose lensOffset)
    {
        // Pose is already Local to Origin
        Matrix4x4 rgbLocal = Matrix4x4.TRS(lensOffset.position, lensOffset.rotation, Vector3.one);

        // flip for OpenCV (Unity Left-Handed Y-Up -> M3T Right-Handed Y-Down)
        Matrix4x4 flipY = Matrix4x4.Scale(new Vector3(1, -1, 1));
        Matrix4x4 rgbM3T = flipY * rgbLocal * flipY;
        
        // 0.999948f, 0.008509f, -0.005664f, 0.031619f,
        // -0.007279f, 0.981792f, 0.189818f, 0.017903f,
        // 0.007176f, -0.189766f,0.981803f, 0.062940f,
        // 0.000000f, 0.000000f, 0.000000f, 1.000000f;
        Debug.Log($"[Helper] RGB-Pose: {GetEigenCppString(rgbM3T)}");
    }

    public static void PrintRelativeDepthPoses(Transform centerEyeAnchor, Transform rightEyeAnchor)
    {
        Matrix4x4 depthLocalToCenter = centerEyeAnchor.worldToLocalMatrix * rightEyeAnchor.localToWorldMatrix;
        
        // flip for OpenCV (Unity Left-Handed Y-Up -> M3T Right-Handed Y-Down)
        Matrix4x4 flipY = Matrix4x4.Scale(new Vector3(1, -1, 1));
        Matrix4x4 depthM3T = flipY * depthLocalToCenter * flipY;

        // 1.000000f, 0.000000f, 0.000000f, 0.031034f,
        // 0.000000f, 1.000000f, 0.000000f, 0.000000f,
        // 0.000000f, 0.000000f, 1.000000f, 0.000000f,
        // 0.000000f, 0.000000f, 0.000000f, 1.000000f;
        Debug.Log($"[Helper] Depth-Pose: {GetEigenCppString(depthM3T)}");
    }

    private static string GetEigenCppString(Matrix4x4 m)
    {
        return $@"{m.m00:F6}f, {m.m01:F6}f, {m.m02:F6}f, {m.m03:F6}f,
            {m.m10:F6}f, {m.m11:F6}f, {m.m12:F6}f, {m.m13:F6}f,
            {m.m20:F6}f, {m.m21:F6}f, {m.m22:F6}f, {m.m23:F6}f,
            {m.m30:F6}f, {m.m31:F6}f, {m.m32:F6}f, {m.m33:F6}f;";
    }

    public static void CalculateDepthIntrinsics(OVRPlugin.Node eye)
    {
        OVRPlugin.GetNodeFrustum2(eye, out var frustum);

        float left = frustum.Fov.LeftTan;
        float right = frustum.Fov.RightTan;
        float bottom = frustum.Fov.DownTan;
        float top = frustum.Fov.UpTan;

        float fx = 320 / (right + left);
        float fy = 320 / (top + bottom);
        float cx = left * fx;
        float cy = top * fy;

        // fx: 144.4381, fy: 133.6766; cx: 121.198, cy: 129.09 (flipped for OpenCV)
        Debug.Log($"[Helper] Depth Intrinsics: fx: {fx}, fy: {fy}; cx: {cx}, cy: {cy}");
    }
}