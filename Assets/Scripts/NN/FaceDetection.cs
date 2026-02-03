using System;
using Unity.Mathematics;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;

public class FaceDetection : MonoBehaviour
{
    public static event Action<Color32[]> OnRoiExtracted;

    [SerializeField] private ModelAsset faceDetector;
    [SerializeField] private TextAsset anchorsCsv;

    [Header("Settings")] 
    [SerializeField] private bool cameraReversed = false;
    [SerializeField] private bool dynamicSizing = false;
    
    [Header("Extraction Adjustments")]
    [SerializeField] private bool flipXExtraction = false; 
    [SerializeField] private bool flipYExtraction = false;
    [SerializeField] private bool debugSize = true;
    
    [Header("Results")]
    [SerializeField] private RawImage roiBox;
    [SerializeField] private RawImage roiResultImage;
    [SerializeField] private RectTransform cameraFeedRect;
    [SerializeField] private Vector2 offset = new Vector2(0f, 0.25f);
    
    private Worker _faceDetectorWorker;
    private Tensor<float> _detectorInput;
    private float[,] _mAnchors;
    private float _textureWidth;
    private float _textureHeight;

    private const int KNumAnchors = 896;
    private const int DetectorInputSize = 128;
    
    public float iouThreshold = 0.3f;
    public float scoreThreshold = 0.5f;

    private Texture2D _roiTexture;

    void Awake()
    {
        _detectorInput = new Tensor<float>(new TensorShape(1, DetectorInputSize, DetectorInputSize, 3));
        _mAnchors = BlazeUtils.LoadAnchors(anchorsCsv.text, KNumAnchors);
        
        Model faceDetectorModel = ModelLoader.Load(faceDetector);
        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor input = graph.AddInput(faceDetectorModel, 0);
        
        FunctionalTensor[] outputs = Functional.Forward(faceDetectorModel, 2 * input - 1);
        FunctionalTensor boxes = outputs[0]; 
        FunctionalTensor scores = outputs[1];
        
        var anchorsData = new float[KNumAnchors * 4];
        Buffer.BlockCopy(_mAnchors, 0, anchorsData, 0, anchorsData.Length * sizeof(float));
        var anchors = Functional.Constant(new TensorShape(KNumAnchors, 4), anchorsData);
        
        var idx_scores_boxes = BlazeUtils.NMSFiltering(boxes, scores, anchors, DetectorInputSize, iouThreshold, scoreThreshold);
        faceDetectorModel = graph.Compile(idx_scores_boxes.Item1, idx_scores_boxes.Item2, idx_scores_boxes.Item3);
    
        _faceDetectorWorker = new Worker(faceDetectorModel, BackendType.GPUCompute);
    }

    void OnEnable() => Webcam.OnFrameReady += ProcessFrame;
    void OnDisable() => Webcam.OnFrameReady -= ProcessFrame;

    private async void ProcessFrame(WebCamTexture frame)
    {
        if (_faceDetectorWorker == null || _detectorInput == null || !frame.isPlaying) return;

        _textureWidth = frame.width;
        _textureHeight = frame.height;

        int size = Mathf.Max(frame.width, frame.height);
        float scale = size / (float)DetectorInputSize;
        
        float2x3 M = BlazeUtils.mul(
            BlazeUtils.TranslationMatrix(0.5f * (new Vector2(frame.width, frame.height) + new Vector2(-size, size))), 
            BlazeUtils.ScaleMatrix(new Vector2(scale, -scale))
        );

        BlazeUtils.SampleImageAffine(frame, _detectorInput, M);
        _faceDetectorWorker.Schedule(_detectorInput);

        try
        {
            using Tensor<int> outputIndices = await (_faceDetectorWorker.PeekOutput(0) as Tensor<int>).ReadbackAndCloneAsync();
            using Tensor<float> outputScores = await (_faceDetectorWorker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
            using Tensor<float> outputBoxes = await (_faceDetectorWorker.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync();

            if (this != null) CalculateRoi(outputIndices, outputBoxes, M, frame);
        }
        catch
        {
            return;
        }
    }

    private void CalculateRoi(Tensor<int> indices, Tensor<float> boxes, float2x3 M, WebCamTexture frame)
    {
        int i = 0; 
        int anchorIdx = indices[i];
        float2 anchor = new float2(_mAnchors[anchorIdx, 0], _mAnchors[anchorIdx, 1]) * DetectorInputSize;

        float2 boxCenterDelta = new float2(boxes[0, i, 0], boxes[0, i, 1]);
        float2 boxSizeRaw     = new float2(boxes[0, i, 2], boxes[0, i, 3]);

        float2 inputCenter = anchor + boxCenterDelta;
    
        float2 roiCenter = BlazeUtils.mul(M, inputCenter);

        float2 inputCorner = inputCenter + (boxSizeRaw * 0.5f);
        float2 roiCorner   = BlazeUtils.mul(M, inputCorner);
        float2 faceBoxPixelSize = math.abs(roiCorner - roiCenter) * 2.0f;

        float2 calculatedOffset = faceBoxPixelSize * new float2(offset.x, offset.y);
        float2 finalCenter = roiCenter + calculatedOffset;

        float2 roiSize = dynamicSizing 
            ? faceBoxPixelSize * new float2(0.5f, 0.2f) 
            : new float2(100f, 100f);

        UpdateRoiUI(finalCenter, roiSize);
        ExtractAndSendRoi(frame, finalCenter, roiSize);
    }

    private void ExtractAndSendRoi(WebCamTexture frame, float2 center, float2 size)
    {
        int cropW = Mathf.RoundToInt(size.x);
        int cropH = Mathf.RoundToInt(size.y);
        int startX = Mathf.RoundToInt(center.x - cropW / 2f);
        int startY = Mathf.RoundToInt(center.y - cropH / 2f);

        startX = Mathf.Clamp(startX, 0, frame.width - 1);
        startY = Mathf.Clamp(startY, 0, frame.height - 1);
        if (startX + cropW > frame.width) cropW = frame.width - startX;
        if (startY + cropH > frame.height) cropH = frame.height - startY;

        if (cropW <= 0 || cropH <= 0) return;

        Color32[] fullPixels = frame.GetPixels32();
        Color32[] croppedPixels = new Color32[cropW * cropH];

        for (int y = 0; y < cropH; y++)
        {
            int srcY = flipYExtraction ? (startY + (cropH - 1 - y)) : (startY + y);
            
            for (int x = 0; x < cropW; x++)
            {
                int srcX = flipXExtraction ? (startX + (cropW - 1 - x)) : (startX + x);

                int sourceIndex = srcY * frame.width + srcX;
                int destIndex = y * cropW + x;

                if (sourceIndex >= 0 && sourceIndex < fullPixels.Length)
                {
                    croppedPixels[destIndex] = fullPixels[sourceIndex];
                }
            }
        }

        OnRoiExtracted?.Invoke(croppedPixels);
        DrawImage(croppedPixels, cropW, cropH);
    }

    private void DrawImage(Color32[] pixels, int width, int height)
    {   
        if (roiResultImage == null) return;
    
        // Initialize or Resize Texture if needed
        if (_roiTexture == null || _roiTexture.width != width || _roiTexture.height != height)
        {
            _roiTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            roiResultImage.texture = _roiTexture;
        }

        _roiTexture.SetPixels32(pixels);
        _roiTexture.Apply();
    }
    
    private void UpdateRoiUI(float2 centerPixels, float2 sizePixels)
    {
        if (roiBox == null || cameraFeedRect == null) return;

        roiBox.gameObject.SetActive(true);

        float uvX = centerPixels.x / _textureWidth;
        float uvY = centerPixels.y / _textureHeight;
        float uvW = sizePixels.x / _textureWidth;
        float uvH = sizePixels.y / _textureHeight;

        float panelW = cameraFeedRect.rect.width;
        float panelH = cameraFeedRect.rect.height;

        roiBox.rectTransform.sizeDelta = new Vector2(uvW * panelW, uvH * panelH);

        float anchoredX = (uvX - 0.5f) * panelW;
        float anchoredY = (uvY - 0.5f) * panelH;

        roiBox.rectTransform.anchoredPosition = new Vector2(anchoredX, anchoredY);
    }

    void OnDestroy()
    {
        _faceDetectorWorker?.Dispose();
        _detectorInput?.Dispose();
        _faceDetectorWorker = null;
        _detectorInput = null;
    }
}