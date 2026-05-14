using System.Collections.Generic;
using UnityEngine;

public class Constants
{
    public enum TargetModel { Pikachu, Racket, Pen }
    
    public static readonly List<Vector3> PikachuPoints = new List<Vector3>()
    {
        new(-0.032902f, 0.04224f, 0.02885f),  // left cheeck
        new(0.039567f, 0.044448f, 0.029131f),  // right cheeck
        new(-0.071736f, 0.107214f, 0.022556f),  // left ear
        new(0.059494f, 0.121871f, 0.021735f),  // right ear
        new(-0.05225f, -0.037227f, 0.033301f),  // left foot
        new(0.031411f, -0.106078f, 0.03378f),  // right foot
        new(0.003817f, 0.031145f, 0.045193f),  // mouth
        new(-0.007097f, -0.033314f, -0.067379f),  // Tail Start
        new(0.046248f, -0.010721f, -0.020798f),  // Brown top
        new(0.009193f, -0.084459f, -0.031145f)  // tail bottom
    };
    
    public static readonly List<Vector3> RacketPoints = new List<Vector3>()
    {
        new(0.001079f, -0.142991f, -0.01148f),    // 0: label/sticker
        new(0.066402f, -0.031275f, -0.00055f),     // 1: head left (widest)
        new(-0.075178f, 0.01246f, 0.000103f),    // 2: head right (widest)
        new(-0.001795f, 0.105068f, -0.001177f),    // 3: head top center
        new(0.001159f, -0.052217f, -0.006652f),    // 4: junction center purple side
        new(-0.012959f, -0.054215f, 0.002821f),     // 5: junction right black side
        new(-0.041474f, -0.05266f, -0.003546f),    // 6: junction left purple side
        new(-0.031743f, -0.03634f, -0.007068f),    // 7: rubber bottom-right purple side
        new(0.004968f, -0.044034f, 0.006658f),     // 8: rubber bottom-left black side
    };
    
    public static readonly List<Vector3> PenPoints = new List<Vector3>()
    {
        new(0.000000f, 0.094079f, 0.000000f),  // 0: Tip
        new(-0.002321f, 0.08273f, -0.002907f),  // 1: corner wood 1
        new(-0.00221f, 0.082837f, 0.002933f),  // 2: corner wood 2
        new(0.003344f, 0.083185f, -0.000018f),  // 3: corner wood 3
        new(0.000181f, 0.013831f, -0.003172f),  // 4: gold middle
        new(0.000268f, -0.072056f, -0.003084f),  // 5: end 1
        new(-0.00282f, -0.072235f, 0.001861f),  // 6: end 2
        new(0.003255f, -0.072035f, 0.001596f),  // 7: end 3
        new(0.000000f, -0.088983f, 0.000000f),  // 8: rubber
    };
    
    public static readonly List<Vector3> CameraMatrixList = new List<Vector3>()
    {
        new(433.08f, 0, 318.235f), // Row 0: fx, 0, cx
        new(0, 433.08f, 318.675f), // Row 1: 0, fy, cy
        new(0, 0, 1)  // Row 2: 0,  0,  1
    };
    
    public static readonly Vector2 ModelResolution = new Vector2(640, 640); 
    public static readonly int Width = 640;
    public static readonly int Height = 640;
}