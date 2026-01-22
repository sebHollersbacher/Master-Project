using System.Collections.Generic;
using UnityEngine;

public class Constants
{
    public static readonly List<Vector3> ModelPoints = new List<Vector3>()
    {
        new(-0.033258f, 0.13947f, 0.007478f), // 0: Left Cheek
        new(0.036858f, 0.14298f, 0.00729f), // 1: Right Cheek
        new(0.001912f, 0.15352f, 0.028136f), // 2: Nose
        new(-0.084878f, 0.21636f, 0.00198f), // 3: Left Ear
        new(0.064939f, 0.23992f, 0.000426f), // 4: Right Ear
        new(-0.044187f, 0.051052f, 0.027608f), // 5: Left Foot
        new(0.026718f, 0.018913f, 0.031581f), // 6: Right Foot
        new(0.002733f, 0.13379f, 0.022499f), // 7: Mouth
        new(-0.00777f, 0.07498f, -0.08825f), // 8: Tail Start
        new(0.02418f, 0.09437f, -0.06211f), // 9: Brown Top
        new(0.02902f, 0.06065f, -0.05952f), // 10: Brown Bottom
        new(-0.00213f, 0.02467f, 0.00859f) // 11: Bottom Cross
    };
    
    public static readonly List<Vector3> CameraMatrixList = new List<Vector3>()
    {
        new(433.045f, 0, 318.24f), // Row 0: fx, 0, cx
        new(0, 324.834f, 238.9875f), // Row 1: 0, fy, cy
        new(0, 0, 1)  // Row 2: 0,  0,  1
    };
    
    public static readonly Vector2 ModelResolution = new Vector2(640, 480); 
    public static readonly int Width = 640;
    public static readonly int Height = 480;
}