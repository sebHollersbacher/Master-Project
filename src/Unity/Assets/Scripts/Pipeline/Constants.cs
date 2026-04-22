using System.Collections.Generic;
using UnityEngine;

public class Constants
{
    public enum TargetModel { Pikachu, Racket, Pen }
    
    public static readonly List<Vector3> PikachuPoints = new List<Vector3>()
    {
        new(-0.03317f, 0.032222f, 0.026848f),  // 0: Left Cheek
        new(0.038824f, 0.033323f, 0.027113f),  // 1: Right Cheek
        new(0.003446f, 0.044969f, 0.048924f),  // 2: Nose
        new(-0.082297f, 0.110011f, 0.022695f), // 3: Left Ear
        new(0.06744f, 0.135588f, 0.020059f),   // 4: Right Ear
        new(-0.05225f, -0.037227f, 0.033301f), // 5: Left Foot
        new(0.031411f, -0.106078f, 0.03378f),  // 6: Right Foot
        new(0.004526f, 0.024382f, 0.042954f),  // 7: Mouth
        new(-0.007097f, -0.033314f, -0.067379f), // 8: Tail Start
        new(0.017621f, -0.016944f, -0.043715f), // 9: Brown Top
        new(0.021618f, -0.048971f, -0.042163f), // 10: Brown Bottom
        new(-0.000972f, -0.083431f, 0.030024f)  // 11: Bottom Cross
    };
    
    public static readonly List<Vector3> RacketPoints = new List<Vector3>()
    {
        new(0.000162f, -0.128434f, -0.011431f),   // 0: Blue
        new(0.033001f, -0.041097f, -0.006767f),   // 1: Purple Left
        new(-0.031743f, -0.03634f, -0.007068f),   // 2: Purple Right
        new(-0.000218f, 0.100594f, -0.007402f),   // 3: Purple Top
        new(-0.003323f, 0.098523f, 0.006255f),    // 4: Black Top
        new(0.035348f, -0.037745f, 0.006527f),    // 5: Black Right
        new(-0.028726f, -0.040778f, 0.006669f),   // 6: Black Left
        new(0.066402f, -0.031275f, -0.00055f),    // 7: Side Left
        new(-0.062846f, -0.03584f, -0.001009f),   // 8: Side Right
        new(-0.001795f, 0.105068f, -0.001177f),   // 9: Side Top
        new(0.000657f, -0.154482f, 0.00027f),     // 10: Bottom
        new(0.000125f, -0.054361f, 0.005995f),    // 11: Black Handle
        new(0.002068f, -0.055594f, -0.006402f)    // 12: Purple Handle
    };
    
    public static readonly List<Vector3> PenPoints_old = new List<Vector3>()
    {
        new(0.0f, -0.077072f, 0.0f),  // 0: Tip
        new(0.0f, -0.067886f, 0.0f),  // 1: Wood
        new(0.0f, -0.065458f, 0.0f),  // 2: First Points
        new(0.0f, 0.025107f, 0.0f),  // 3: Last Points
        new(0.0f, 0.032303f, 0.0f),  // 4: G
        new(0.0f, 0.052052f, 0.0f),  // 5: Logo
        new(0.0f, 0.088626f, 0.0f),  // 6: Border Top
        new(0.0f, 0.090359f, 0.0f),  // 7: Top
    };
    
    public static readonly List<Vector3> PenPoints = new List<Vector3>()
    {
        new(0.000000f, -0.082259f, 0.000000f),  // 0: Tip
        new(0.001036f, 0.090359f, -0.001282f),  // 1: Top
        new(-0.002065f, -0.059794f, -0.003431f),  // 2: Corner 1, grip
        new(0.002856f, -0.062973f, -0.002380f),  // 3: Corner 2, grip
        new(-0.001383f, -0.060672f, 0.004303f),  // 4: Corner 3, grip
        new(-0.001565f, 0.040400f, -0.004019f),  // 5: Corner 1, upper
        new(0.004084f, 0.042871f, -0.002889f),  // 6: Corner 2, upper
        new(-0.001036f, 0.038890f, 0.003492f),  // 7: Corner 3, upper
        new(-0.001404f, -0.065066f, -0.000396f),  // 8: Grip dot
    };
    
    public static readonly List<Vector3> CameraMatrixList640 = new List<Vector3>()
    {
        new(433.08f, 0, 318.235f), // Row 0: fx, 0, cx
        new(0, 433.08f, 318.675f), // Row 1: 0, fy, cy
        new(0, 0, 1)  // Row 2: 0,  0,  1
    };
    
    public static readonly List<Vector3> CameraMatrixList = new List<Vector3>()
    {
        new(324.81f, 0, 238.6725f), // Row 0: fx, 0, cx
        new(0, 324.81f, 239.00625f), // Row 1: 0, fy, cy
        new(0, 0, 1)  // Row 2: 0,  0,  1
    };
    
    public static readonly Vector2 ModelResolution = new Vector2(480, 480); 
    public static readonly int Width = 480;
    public static readonly int Height = 480;
}