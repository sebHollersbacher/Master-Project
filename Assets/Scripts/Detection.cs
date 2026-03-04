using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.InferenceEngine;

public class Detection : MonoBehaviour
{
    [SerializeField] private ModelAsset pikachuModel;
    [SerializeField] private ModelAsset penModel;
    [SerializeField] private ModelAsset racketModel;
    public TargetModel mode;
    
    private Model runtimeModel;
    private Worker worker;
    private Tensor<float> inputTensor;
    private RenderTexture copyTexture;
    
    public enum TargetModel { Pikachu, Racket, Pen }
    public struct YoloResult
    {
        public Rect boundingBox;
        public List<Vector2> keypoints;
        public float confidence;
        public TargetModel target;
        public bool isValid;
    }

    public void Start()
    {
        runtimeModel = mode switch
        {
            TargetModel.Pikachu => ModelLoader.Load(pikachuModel),
            TargetModel.Racket => ModelLoader.Load(racketModel),
            TargetModel.Pen => ModelLoader.Load(penModel),
            _ => throw new ArgumentOutOfRangeException()
        };
        
        worker = new Worker(runtimeModel, BackendType.GPUCompute);

        inputTensor = new Tensor<float>(new TensorShape(1, 3, Constants.Height, Constants.Width));
        copyTexture = new RenderTexture(Constants.Width, Constants.Height, 0, RenderTextureFormat.ARGB32);
    }

    public void OnDestroy()
    {
        worker?.Dispose();
        inputTensor?.Dispose();
    }

    public async Task<YoloResult> Inference(Texture cameraTexture, float minScore)
    {
        Graphics.Blit(cameraTexture, copyTexture); // copy texture for Detection

        // 4. Fill the Tensor (Every Frame)
        // TextureConverter automatically handles resizing if cam size != tensor size
        // It also handles swizzling (RGBA -> RGB) if your tensor is 3 channels
        TextureConverter.ToTensor(copyTexture, inputTensor,
            new TextureTransform().SetDimensions(Constants.Width, Constants.Height).SetTensorLayout(TensorLayout.NCHW));

        // 5. Run Inference
        worker.Schedule(inputTensor);

        // 6. Get Output (Non-blocking reference)
        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
        int numFeatures = outputTensor.shape[1];
        // TensorFloat(1, 41, 6300), 4 box + 1 score + 12 kpts * 3 = 41
        // Fine Grid (Stride 8): 4800
        // Medium Grid (Stride 16): 1200
        // Coarse Grid (Stride 32): 300

        try
        {
            // get output to cpu
            Tensor<float> cpuTensor = await outputTensor.ReadbackAndCloneAsync();
            float[] data = cpuTensor.DownloadToArray();
            cpuTensor.Dispose();
            return ParseKeypoints(data, minScore, numFeatures);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DEBUG] Error reading CPU tensor: {e.Message}");
            return new YoloResult { isValid = false };
        }
    }
    
    public YoloResult ParseKeypoints(float[] data, float minScore, int numFeatures)
    {
        YoloResult result = new YoloResult { isValid = false, keypoints = new List<Vector2>() };
        
        int numAnchors = 8400; 
        float highestScore = 0f;
        int bestAnchorIndex = -1;

        // 1. Find the single best bounding box (Basic approach without full NMS)
        for (int a = 0; a < numAnchors; a++)
        {
            // The object confidence score is at feature index 4
            float score = data[4 * numAnchors + a];

            if (score > minScore && score > highestScore)
            {
                highestScore = score;
                bestAnchorIndex = a;
            }
        }

        // 2. Extract keypoints only for that best anchor
        if (bestAnchorIndex != -1)
        {
            result.isValid = true;
            result.target = mode;
            result.confidence = highestScore;
            
            // --- EXTRACT BOUNDING BOX ---
            float cx = data[(0 * numAnchors) + bestAnchorIndex];
            float cy = data[(1 * numAnchors) + bestAnchorIndex];
            float w  = data[(2 * numAnchors) + bestAnchorIndex];
            float h  = data[(3 * numAnchors) + bestAnchorIndex];
            
            // Convert center coordinates to Unity Rect (xMin, yMin, width, height)
            result.boundingBox = new Rect(cx - (w / 2f), cy - (h / 2f), w, h);
            
            int numKeypoints = (numFeatures - 5) / 3;
            for (int k = 0; k < numKeypoints; k++)
            {
                // Keypoints start at feature index 5. Each has X, Y, Conf
                int kptBaseFeature = 5 + (k * 3);
            
                float kptX = data[(kptBaseFeature + 0) * numAnchors + bestAnchorIndex];
                float kptY = data[(kptBaseFeature + 1) * numAnchors + bestAnchorIndex];
                // float kptConf = data[(kptBaseFeature + 2) * numAnchors + bestAnchorIndex];   // not used but available

                result.keypoints.Add(new Vector2(kptX, kptY)); 
            }
        }

        return result;
    }
}