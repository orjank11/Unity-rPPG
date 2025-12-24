using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Neural {

    public class Visualizer : MonoBehaviour
    {
        [SerializeField] private Webcam source = null;
        [SerializeField, Range(0, 1)] private float threshold = 0.5f;
        [SerializeField] private ResourceSet resources = null;
        [SerializeField] private int faceTextureSize = 256; 

        private bool _initialized = false;
        private Image _roi = null; 
        private FaceDetector _detector;
        private RenderTexture _faceTexture;

        public void Initialize(Image refImage)
        {
            _roi = refImage;
            
            // Add null checks
            if (resources == null)
            {
                Debug.LogError("Visualizer: ResourceSet is not assigned in the Inspector!");
                return;
            }
            
            if (refImage == null)
            {
                Debug.LogError("Visualizer: Reference Image is null!");
                return;
            }
            
            try
            {
                _detector = new FaceDetector(resources);
                _faceTexture = new RenderTexture(faceTextureSize, faceTextureSize, 0);
                _initialized = true;
                Debug.Log("Visualizer initialized successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to initialize FaceDetector: {e.Message}\n{e.StackTrace}");
                _initialized = false;
            }
        } 

        void OnDestroy()
        {
            _detector?.Dispose();
            if (_faceTexture != null) _faceTexture.Release();
        }

        void Update()
        {
            if (!_initialized) return;
            if (source == null || !source.DidUpdateThisFrame()) return;
            
            _detector.ProcessImage(source.GetTexture(), threshold);
            ExtractFace();
        }

        void ExtractFace()
        {
            var detections = new List<Detection>(_detector.Detections);
            if (detections.Count > 0)
            {
                var face = detections[0];
                
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
                
                if (_roi != null)
                    _roi.image = _faceTexture;
            }
        }
    }
}