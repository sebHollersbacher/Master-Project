using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.InferenceEngine;

public class Detection :  MonoBehaviour
{
    public ModelAsset modelAsset;
    private Model runtimeModel;
    private Worker worker;
    private Tensor<float> inputTensor;
    private RenderTexture copyTexture;

    public void Start()
    {
        runtimeModel = ModelLoader.Load(modelAsset);
        worker = new Worker(runtimeModel, BackendType.GPUCompute);

        inputTensor = new Tensor<float>(new TensorShape(1, 3, 480, 640));
        copyTexture = new RenderTexture(640, 480, 0, RenderTextureFormat.ARGB32);
    }

    public void OnDestroy()
    {
        worker?.Dispose();
        inputTensor?.Dispose();
    }

    public async Task<List<Vector2>> Inference(Texture cameraTexture, float minScore)
    {
        Graphics.Blit(cameraTexture, copyTexture); // copy texture for Detection

        // 4. Fill the Tensor (Every Frame)
        // TextureConverter automatically handles resizing if cam size != tensor size
        // It also handles swizzling (RGBA -> RGB) if your tensor is 3 channels
        TextureConverter.ToTensor(copyTexture, inputTensor,
            new TextureTransform().SetDimensions(640, 480).SetTensorLayout(TensorLayout.NCHW));

        // 5. Run Inference
        worker.Schedule(inputTensor);

        // 6. Get Output (Non-blocking reference)
        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
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
            return ParseKeypoints(data, minScore);
        }
        catch (Exception)
        {
            Debug.Log("[DEBUG] Error while reading output in cpu");
            return new List<Vector2>();
        }
    }

    private List<Vector2> ParseKeypoints(float[] data, float minScore)
    {
        var imagePoints = new List<Vector2>();

        int numAnchors = 6300;

        // 1. Find Best Anchor (The Pikachu with the highest score)
        int bestAnchor = -1;
        float bestMaxScore = 0f;
        int scoreOffset = 4 * numAnchors;

        for (int i = 0; i < numAnchors; i++)
        {
            float score = data[scoreOffset + i];
            if (score > bestMaxScore)
            {
                bestMaxScore = score;
                bestAnchor = i;
            }
        }

        // If no Pikachu found above the minimum score, return empty list
        if (bestAnchor == -1 || bestMaxScore < minScore)
            return imagePoints;

        // 2. Extract ALL 12 Keypoints (No confidence check)
        // Keypoints start at Channel 5. Format: [X, Y, Conf, X, Y, Conf...]
        for (int k = 0; k < 12; k++)
        {
            int baseChan = 5 + (k * 3);

            int idxX = (baseChan + 0) * numAnchors + bestAnchor;
            int idxY = (baseChan + 1) * numAnchors + bestAnchor;

            // Directly add the point, regardless of confidence
            imagePoints.Add(new Vector2(data[idxX], data[idxY]));
        }

        return imagePoints;
    }
}