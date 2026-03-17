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
    
    private Model runtimeModel;
    private Tensor<float> inputTensor;
    private RenderTexture copyTexture;
    private Dictionary<Constants.TargetModel, Worker> workers = new();
    
    public struct YoloResult
    {
        public Rect boundingBox;
        public List<Vector2> keypoints;
        public float confidence;
        public bool isValid;
    }

    public void Start()
    {
        workers[Constants.TargetModel.Pikachu] = new Worker(ModelLoader.Load(pikachuModel), BackendType.GPUCompute);
        workers[Constants.TargetModel.Racket] = new Worker(ModelLoader.Load(racketModel), BackendType.GPUCompute);
        workers[Constants.TargetModel.Pen] = new Worker(ModelLoader.Load(penModel), BackendType.GPUCompute);

        inputTensor = new Tensor<float>(new TensorShape(1, 3, Constants.Height, Constants.Width));
        copyTexture = new RenderTexture(Constants.Width, Constants.Height, 0, RenderTextureFormat.ARGB32);
    }

    public void OnDestroy()
    {
        foreach (var worker in workers.Values)
        {
            worker?.Dispose();
        }
        inputTensor?.Dispose();
        if (copyTexture != null) copyTexture.Release();
    }
    
    public Matrix4x4 TransformDetectionToOrigin(Matrix4x4 newDetection, Pose lensOffset)
    {
        // M3T origin is now centerEyeAnchor, transform the Detection from RGB-Camera to Origin
        Matrix4x4 lensLocalUnity = Matrix4x4.TRS(lensOffset.position, lensOffset.rotation, Vector3.one);

        // convert to OpenCV
        Matrix4x4 flipY = Matrix4x4.Scale(new Vector3(1, -1, 1));
        Matrix4x4 lensLocalOpenCv = flipY * lensLocalUnity * flipY;

        return lensLocalOpenCv * newDetection;
    }

    public async Task<YoloResult> Inference(Constants.TargetModel target, Texture cameraTexture, float minScore)
    {
        // copy and scale if required
        Graphics.Blit(cameraTexture, copyTexture); 

        TextureConverter.ToTensor(copyTexture, inputTensor,
            new TextureTransform().SetTensorLayout(TensorLayout.NCHW));

        Worker activeWorker = workers[target];
        activeWorker.Schedule(inputTensor);

        Tensor<float> outputTensor = activeWorker.PeekOutput() as Tensor<float>;
        int numFeatures = outputTensor.shape[1]; // 1-4: bounding box, 5: confidence, rest: keypoints
        
        try
        {
            // get output to cpu
            Tensor<float> cpuTensor = await outputTensor.ReadbackAndCloneAsync();
            float[] data = cpuTensor.DownloadToArray();
            cpuTensor.Dispose();
            var result = ParseKeypoints(data, minScore, numFeatures);
            return result;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Detection] Error reading CPU tensor: {e.Message}");
            return new YoloResult { isValid = false };
        }
    }
    
    public YoloResult ParseKeypoints(float[] data, float minScore, int numFeatures)
    {
        YoloResult result = new YoloResult { isValid = false, keypoints = new List<Vector2>() };
        
        // Fine Grid (Stride 8): 6400
        // Medium Grid (Stride 16): 1600
        // Coarse Grid (Stride 32): 400
        int numAnchors = 8400; // for 640x640
        float highestScore = 0f;
        int bestAnchorIndex = -1;

        // get most confident prediction
        for (int a = 0; a < numAnchors; a++)
        {
            float score = data[4 * numAnchors + a];
            if (score > minScore && score > highestScore)
            {
                highestScore = score;
                bestAnchorIndex = a;
            }
        }

        // extract bounding box and keypoints
        if (bestAnchorIndex != -1)
        {
            result.isValid = true;
            result.confidence = highestScore;
            
            // bounding box
            float cx = data[(0 * numAnchors) + bestAnchorIndex];
            float cy = data[(1 * numAnchors) + bestAnchorIndex];
            float w  = data[(2 * numAnchors) + bestAnchorIndex];
            float h  = data[(3 * numAnchors) + bestAnchorIndex];
            
            result.boundingBox = new Rect(cx - (w / 2f), cy - (h / 2f), w, h);
            
            // keypoints
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