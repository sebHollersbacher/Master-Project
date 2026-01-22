using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class PnP
{
    [DllImport("opencv_wrapper")] // Drop Lib prefix and .so
    private static extern void SolvePnP_Ransac_Entry(
        float[] p3D, // 1D Array of x,y,z
        int count, // Number of points
        float[] p2D, // 1D Array of x,y
        float[] camMatrix, // 9 floats
        float[] output // 7 floats (tx, ty, tz, rx, ry, rz, success)
    );

    public Matrix4x4? Solve(List<Vector2> imagePoints)
    {
        Debug.Log("[DEBUG] PnP Start");
        // 1. Safety Checks
        var modelPoints = Constants.ModelPoints;
        if (imagePoints == null || imagePoints.Count != modelPoints.Count)
        {
            Debug.LogWarning(
                $"Point count mismatch! Model: {modelPoints.Count}, Detected: {(imagePoints?.Count ?? 0)}");
            return null;
        }

        int count = modelPoints.Count;

        // 2. Prepare Data Arrays
        float[] flat3D = new float[count * 3];
        float[] flat2D = new float[count * 2];

        // Fill arrays
        for (int i = 0; i < count; i++)
        {
            // Model (3D)
            flat3D[i * 3 + 0] = modelPoints[i].x;
            flat3D[i * 3 + 1] = modelPoints[i].y;
            flat3D[i * 3 + 2] = modelPoints[i].z;

            // Image (2D)
            flat2D[i * 2 + 0] = imagePoints[i].x;
            flat2D[i * 2 + 1] = imagePoints[i].y;
        }

        var cameraMatrixList = Constants.CameraMatrixList;
        float[] camMatFlat = new float[]
        {
            cameraMatrixList[0].x, cameraMatrixList[0].y, cameraMatrixList[0].z,
            cameraMatrixList[1].x, cameraMatrixList[1].y, cameraMatrixList[1].z,
            cameraMatrixList[2].x, cameraMatrixList[2].y, cameraMatrixList[2].z
        };

        float[] result = new float[7];

        // 4. Call OpenCV Native
        SolvePnP_Ransac_Entry(flat3D, count, flat2D, camMatFlat, result);
        
        // 5. Apply Result
        if (result[6] > 0.5f) // Success flag
        {
            var res =ParseResult(result);
            return res;
        }

        return null;
    }

    private Matrix4x4 ParseResult(float[] result)
    {
        Vector3 tvec = new Vector3(result[0], result[1], result[2]);
        Vector3 rvec = new Vector3(result[3], result[4], result[5]);
        
        // 1. Convert Rodrigues (Angle-Axis) to Quaternion
        // The magnitude of rvec is the Angle (in Radians).
        // The normalized rvec is the Axis.
        float angleRad = rvec.magnitude;
        Quaternion rotation = Quaternion.identity;

        // Safety check: If angle is near 0, it's the identity rotation
        if (angleRad > 0.0001f)
        {
            Vector3 axis = rvec.normalized;
            // Unity expects Degrees for AngleAxis
            rotation = Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, axis);
        }

        // 2. Build the Matrix
        // Note: We use Vector3.one for scale (PnP doesn't estimate scale)
        return Matrix4x4.TRS(tvec, rotation, Vector3.one);
    }
}