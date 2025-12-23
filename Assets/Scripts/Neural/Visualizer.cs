using UnityEngine;
using UI = UnityEngine.UI;
using System.Collections.Generic;

namespace Neural {

public sealed class Visualizer : MonoBehaviour
{
    #region Editable attributes

    [SerializeField] private Webcam source = null;
    [SerializeField, Range(0, 1)] private float threshold = 0.5f;
    [SerializeField] private ResourceSet resources = null;
    [SerializeField] private UI.RawImage previewUI = null;
    [SerializeField] private UI.RawImage facePreviewUI = null; 
    [SerializeField] private int faceTextureSize = 256; 

    #endregion

    #region Private objects

    private FaceDetector _detector;
    private RenderTexture _faceTexture;

    #endregion

    #region MonoBehaviour implementation

    void Start()
    {
        _detector = new FaceDetector(resources);
        _faceTexture = new RenderTexture(faceTextureSize, faceTextureSize, 0);
    }

    void OnDestroy()
    {
        _detector?.Dispose();
        if (_faceTexture != null) _faceTexture.Release();
    }

    void Update()
    {
        if (source.DidUpdateThisFrame())
        {
            _detector.ProcessImage(source.GetTexture(), threshold);
            previewUI.texture = source.GetTexture();

            // Extract the first detected face
            ExtractFace();
        }
    }

    void ExtractFace()
{
    var detections = new List<Detection>(_detector.Detections);
    
    if (detections.Count > 0)
    {
        var face = detections[0]; // Get first face
        
        float faceWidth = face.x2 - face.x1;
        float faceHeight = face.y2 - face.y1;
        
        float foreheadHeightRatio = 0.3f; 
        float foreheadWidthPadding = 0.1f;
        
        float foreheadX1 = face.x1 + (faceWidth * foreheadWidthPadding);
        float foreheadY1 = face.y1; 
        float foreheadX2 = face.x2 - (faceWidth * foreheadWidthPadding);
        float foreheadY2 = face.y1 + (faceHeight * foreheadHeightRatio); 
        
        float foreheadWidth = foreheadX2 - foreheadX1;
        float foreheadHeight = foreheadY2 - foreheadY1;
        
        Texture sourceTexture = source.GetTexture();
        
        Graphics.Blit(sourceTexture, _faceTexture, 
            new Vector2(foreheadWidth, foreheadHeight),
            new Vector2(foreheadX1, 1f - foreheadY2));
        
        if (facePreviewUI != null)
            facePreviewUI.texture = _faceTexture;
    }
}

    #endregion
}

} 