using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.InferenceEngine;

public class Detection : MonoBehaviour
{
    [SerializeField] private ModelAsset model;
    
    private Model runtimeModel;
    private Tensor<float> inputTensor;
    private RenderTexture copyTexture;
    private Worker worker;
    
    public struct YoloResult
    {
        public Rect boundingBox;
        public List<Vector2> keypoints;
        public float confidence;
        public bool isValid;
    }

    public void Start()
    {
        var wrappedModel = WrapModelWithPostprocess(model);
        worker = new Worker(wrappedModel, BackendType.GPUCompute);

        inputTensor = new Tensor<float>(new TensorShape(1, 3, Constants.Height, Constants.Width));
        copyTexture = new RenderTexture(Constants.Width, Constants.Height, 0, RenderTextureFormat.ARGB32);
    }

    public void OnDestroy()
    {
        worker?.Dispose();
        
        inputTensor?.Dispose();
        if (copyTexture != null) copyTexture.Release();
    }
    
    public Matrix4x4 TransformDetectionToOrigin(Matrix4x4 newDetection, Pose lensOffset)
    {
        // M3T origin is now centerEyeAnchor, transform the Detection from RGB-Camera to Origin
        Matrix4x4 lensLocalUnity = Matrix4x4.TRS(lensOffset.position, lensOffset.rotation, Vector3.one);

        Matrix4x4 flipY = Matrix4x4.Scale(new Vector3(1, -1, 1));
        Matrix4x4 lensLocalOpenCv = flipY * lensLocalUnity * flipY;

        return lensLocalOpenCv * newDetection;
    }
    
    private Model WrapModelWithPostprocess(ModelAsset modelAsset)
    {
        Model baseModel = ModelLoader.Load(modelAsset);

        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(baseModel);

        FunctionalTensor output = Functional.Forward(baseModel, inputs)[0];
        FunctionalTensor confidenceScores = output[0, 4];
        FunctionalTensor bestIdx = Functional.ArgMax(confidenceScores, dim: 0);

        // reshape to use best prediction
        FunctionalTensor idxTensor = Functional.Reshape(bestIdx, new[] { 1 });
        FunctionalTensor bestPred = output.IndexSelect(2, idxTensor);
        bestPred = bestPred[0, .., 0];

        return graph.Compile(bestPred);
    }

    public async Task<YoloResult> Inference(Texture cameraTexture, float minScore)
    {
        Graphics.Blit(cameraTexture, copyTexture);
        TextureConverter.ToTensor(copyTexture, inputTensor,
            new TextureTransform().SetTensorLayout(TensorLayout.NCHW));
        worker.Schedule(inputTensor);

        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
        int numFeatures = outputTensor.shape[0];

        try
        {
            Tensor<float> cpuTensor = await outputTensor.ReadbackAndCloneAsync();
            float[] data = cpuTensor.DownloadToArray();
            cpuTensor.Dispose();
            return ParseKeypoints(data, minScore, numFeatures);
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

        // data layout: [cx, cy, w, h, confidence, kx0, ky0, kc0, kx1, ky1, kc1, ...]
        float confidence = data[4];

        if (confidence < minScore)
            return result;

        result.isValid = true;
        result.confidence = confidence;

        float cx = data[0];
        float cy = data[1];
        float w  = data[2];
        float h  = data[3];
        result.boundingBox = new Rect(cx - (w / 2f), cy - (h / 2f), w, h);

        int numKeypoints = (numFeatures - 5) / 3;
        for (int k = 0; k < numKeypoints; k++)
        {
            int baseIdx = 5 + (k * 3);
            float kptX = data[baseIdx + 0];
            float kptY = data[baseIdx + 1];
            // float kptConf = data[baseIdx + 2]; // available if needed
            result.keypoints.Add(new Vector2(kptX, kptY));
        }

        return result;
    }
}