using System;
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

    [DllImport("opencv_wrapper")]
    private static extern void FitLine2D_Entry(
        float[] p2D, // Array of x,y (size = count * 2)
        int count, // Number of points
        float[] outputArr //  4 floats (vx, vy, x0, y0)
    );

    public Matrix4x4? Solve(Constants.TargetModel target, Detection.YoloResult result)
    {
        return target switch
        {
            Constants.TargetModel.Pen => SolvePnP(result.keypoints, Constants.PenPoints),
            Constants.TargetModel.Racket => SolvePnP(result.keypoints, Constants.RacketPoints),
            Constants.TargetModel.Pikachu => SolvePnP(result.keypoints, Constants.PikachuPoints),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public Matrix4x4? SolvePnP(List<Vector2> imagePoints, List<Vector3> modelPoints)
    {
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
        SolvePnP_Ransac_Entry(flat3D, count, flat2D, camMatFlat, result);

        if (result[6] > 0.5f) // Success flag
        {
            var res = ParseResult(result);
            return res;
        }

        Debug.Log("[PnP] PnP Unsuccessful");
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

    public Matrix4x4? GetPenPoseMatrix(Rect boundingBox, List<Vector2> keypoints)
    {
        if (keypoints == null || keypoints.Count < 2) return null;

        // 1. Flatten the List<Vector2> into a 1D float array for C++
        float[] p2D = new float[keypoints.Count * 2];
        for (int i = 0; i < keypoints.Count; i++)
        {
            p2D[i * 2] = keypoints[i].x;
            p2D[i * 2 + 1] = keypoints[i].y;
        }

        // 2. Prepare the output array and call your C++ function!
        float[] lineParams = new float[4];
        FitLine2D_Entry(p2D, keypoints.Count, lineParams);

        // 3. Extract the line data returned from C++
        Vector2 V = new Vector2(lineParams[0], lineParams[1]).normalized; // Direction
        Vector2 P0 = new Vector2(lineParams[2], lineParams[3]); // Point on line

        // 3. Define the 4 corners of the Bounding Box
        Vector2[] corners =
        {
            new(boundingBox.xMin, boundingBox.yMin), // Top-Left
            new(boundingBox.xMax, boundingBox.yMin), // Top-Right
            new(boundingBox.xMin, boundingBox.yMax), // Bottom-Left
            new(boundingBox.xMax, boundingBox.yMax) // Bottom-Right
        };

        // 4. Project corners onto the infinite line
        float minT = float.MaxValue;
        float maxT = float.MinValue;

        foreach (Vector2 corner in corners)
        {
            float t = Vector2.Dot(corner - P0, V);
            if (t < minT) minT = t;
            if (t > maxT) maxT = t;
        }

        // The absolute physical extremes of the pen
        Vector2 extremeA = P0 + (V * minT);
        Vector2 extremeB = P0 + (V * maxT);

        // 5. Determine which is the Tip and which is the Base
        // Assumes keypoints[0] is your noisy tip from YOLO
        Vector2 noisyTip = keypoints[0];
        Vector2 trueTip, trueBase;

        if (Vector2.Distance(noisyTip, extremeA) < Vector2.Distance(noisyTip, extremeB))
        {
            trueTip = extremeA;
            trueBase = extremeB;
        }
        else
        {
            trueTip = extremeB;
            trueBase = extremeA;
        }

        float fx = Constants.CameraMatrixList[0].x; // Row 0, Col 0
        float cx = Constants.CameraMatrixList[0].z; // Row 0, Col 2

        float fy = Constants.CameraMatrixList[1].y; // Row 1, Col 1
        float cy = Constants.CameraMatrixList[1].z; // Row 1, Col 2

        float penRealLength = 0.1726f; // 17.26 cm

        // 6. Calculate Rock-Solid Depth (Z)
        float pixelLength = Vector2.Distance(trueTip, trueBase);
        if (pixelLength < 1.0f) return null; // Prevent divide-by-zero

        float Z = (fx * penRealLength) / pixelLength;

        // 7. Unproject 2D to 3D Position (OpenCV Space)
        Vector2 center = (trueTip + trueBase) / 2f;
        float X = (center.x - cx) * Z / fx;
        float Y = (center.y - cy) * Z / fy;

        // 8. Construct the Orthogonal Rotation Vectors
        Vector2 dir = (trueTip - trueBase).normalized;

        // OpenCV Right-Handed System (X cross Y = Z). 
        // Local Y points along the pen. Local Z points straight into the scene.
        // Local X is the perpendicular vector (dir.y, -dir.x).
        Vector4 colX = new Vector4(dir.y, -dir.x, 0, 0);
        Vector4 colY = new Vector4(dir.x, dir.y, 0, 0);
        Vector4 colZ = new Vector4(0, 0, 1, 0);
        Vector4 colW = new Vector4(X, Y, Z, 1); // Position vector

        // 9. Build and return the raw OpenCV Matrix in one clean pass
        return new Matrix4x4(colX, colY, colZ, colW);
    }
}