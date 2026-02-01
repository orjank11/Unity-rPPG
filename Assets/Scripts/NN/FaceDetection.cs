using System;
using Unity.Mathematics;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UIElements;

public class FaceDetection : MonoBehaviour
{
    public static event Action<Color32[]> OnRoiExtracted;

    [SerializeField] private ModelAsset faceDetector;
    [SerializeField] private TextAsset anchorsCsv;
    [SerializeField] private UIDocument uiDocument;

    [Header("Settings")] 
    [SerializeField] private bool cameraReversed = false;
    [SerializeField] private bool dynamicSizing = false;
    
    private VisualElement _roiBox;
    private Worker _faceDetectorWorker;
    private Tensor<float> _detectorInput;
    private float[,] _mAnchors;
    private float _textureWidth;
    private float _textureHeight;

    private const int KNumAnchors = 896;
    private const int DetectorInputSize = 128;
    
    public float iouThreshold = 0.3f;
    public float scoreThreshold = 0.5f;

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
        
        if (uiDocument != null)
        {
            var root = uiDocument.rootVisualElement;
            _roiBox = new VisualElement();
            _roiBox.style.position = Position.Absolute;
            _roiBox.style.borderBottomWidth = 2;
            _roiBox.style.borderTopWidth = 2;
            _roiBox.style.borderLeftWidth = 2;
            _roiBox.style.borderRightWidth = 2;
            _roiBox.style.borderBottomColor = Color.green;
            _roiBox.style.borderTopColor = Color.green;
            _roiBox.style.borderLeftColor = Color.green;
            _roiBox.style.borderRightColor = Color.green;
            _roiBox.style.display = DisplayStyle.None;
            root.Add(_roiBox);
        }
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
        if (indices.shape.length == 0)
        {
            if (_roiBox != null) _roiBox.style.display = DisplayStyle.None;
            return;
        }

        int i = 0; 
        int anchorIdx = indices[i];
        
        float2 anchor = new float2(_mAnchors[anchorIdx, 0], _mAnchors[anchorIdx, 1]) * DetectorInputSize;

        float2 rawRightEye = new float2(boxes[0, i, 4], boxes[0, i, 5]);
        float2 rawLeftEye  = new float2(boxes[0, i, 6], boxes[0, i, 7]);
        float2 rawNose     = new float2(boxes[0, i, 8], boxes[0, i, 9]);

        float2 rightEye = BlazeUtils.mul(M, anchor + rawRightEye);
        float2 leftEye  = BlazeUtils.mul(M, anchor + rawLeftEye);
        float2 nose     = BlazeUtils.mul(M, anchor + rawNose);

        float2 eyeCenter = (rightEye + leftEye) * 0.5f;
        float2 faceDownDir = nose - eyeCenter; 
        float faceScale = math.length(faceDownDir); 
        float2 faceUpDir = -math.normalize(faceDownDir);

        float2 foreheadCenter = eyeCenter + (faceUpDir * faceScale * 1.8f);
        
        float2 roiSize = dynamicSizing ? new float2(faceScale * 2.5f, faceScale * 1.0f) : new float2(100f, 100f);

        UpdateRoiUI(foreheadCenter, roiSize);
        
        ExtractAndSendRoi(frame, foreheadCenter, roiSize);
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
            int sourceIndex = (startY + y) * frame.width + startX;
            
            int destIndex = y * cropW;

            Array.Copy(fullPixels, sourceIndex, croppedPixels, destIndex, cropW);
        }

        OnRoiExtracted?.Invoke(croppedPixels);
    }

    private void UpdateRoiUI(float2 centerPixels, float2 sizePixels)
    {
        if (_roiBox == null || uiDocument == null) return;

        float uvX = (centerPixels.x / _textureWidth);
        float uvY = centerPixels.y / _textureHeight;
        float uvW = sizePixels.x / _textureWidth;
        float uvH = sizePixels.y / _textureHeight;

        var uiRoot = uiDocument.rootVisualElement.contentRect;
        float panelW = uiRoot.width;
        float panelH = uiRoot.height;

        _roiBox.style.width  = uvW * panelW;
        _roiBox.style.height = uvH * panelH;
                
        _roiBox.style.left   = (uvX * panelW) - (_roiBox.style.width.value.value / 2);
                
        _roiBox.style.top    = ((1 - uvY) * panelH) - (_roiBox.style.height.value.value / 2);
                
        _roiBox.style.display = DisplayStyle.Flex;
    }

    void OnDestroy()
    {
        _faceDetectorWorker?.Dispose();
        _detectorInput?.Dispose();
        _faceDetectorWorker = null;
        _detectorInput = null;
    }
}