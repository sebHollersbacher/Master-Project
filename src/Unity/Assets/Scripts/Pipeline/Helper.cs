using UnityEngine;

public class Helper
{
    private static readonly Matrix4x4 FlipY = Matrix4x4.Scale(new Vector3(1, -1, 1));

    public static Matrix4x4 ComputeRGBExtrinsics(Pose lensOffset)
    {
        Matrix4x4 rgbLocal = Matrix4x4.TRS(lensOffset.position, lensOffset.rotation, Vector3.one);
        return FlipY * rgbLocal * FlipY;
    }

    public static Matrix4x4 ComputeDepthExtrinsics(Transform centerEyeAnchor, Transform rightEyeAnchor)
    {
        Matrix4x4 depthLocalToCenter = centerEyeAnchor.worldToLocalMatrix * rightEyeAnchor.localToWorldMatrix;
        return FlipY * depthLocalToCenter * FlipY;
    }

    public static (float fx, float fy, float cx, float cy) ComputeDepthIntrinsics(
        OVRPlugin.Node eye, int targetWidth = 320, int targetHeight = 320)
    {
        OVRPlugin.GetNodeFrustum2(eye, out var frustum);

        float left = frustum.Fov.LeftTan;
        float right = frustum.Fov.RightTan;
        float bottom = frustum.Fov.DownTan;
        float top = frustum.Fov.UpTan;

        float fx = targetWidth / (right + left);
        float fy = targetHeight / (top + bottom);
        float cx = left * fx;
        float cy = top * fy;

        return (fx, fy, cx, cy);
    }
}