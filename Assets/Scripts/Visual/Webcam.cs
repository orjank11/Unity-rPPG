using System;
using UnityEngine;
using UnityEngine.UIElements;

public class Webcam : MonoBehaviour
{
    public static event Action<WebCamTexture> OnFrameReady;
    private WebCamTexture _webcamTexture;
    private Image _displayImage;

    public void Initialize(Image imageRef)
    {
        _displayImage = imageRef;
        _webcamTexture = new WebCamTexture(1280, 800);
        _webcamTexture.Play();

        _displayImage.style.position = Position.Absolute;
    
        // Force it to span the entire UI Document
        _displayImage.style.width = Length.Percent(100);
        _displayImage.style.height = Length.Percent(100);
    
        // This is the "magic" line that prevents stretching/squishing
        _displayImage.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
        
        if (_displayImage != null)
            _displayImage.image = _webcamTexture;
    }

    void Update()
    {
        if (_webcamTexture != null && _webcamTexture.didUpdateThisFrame)
            OnFrameReady?.Invoke(_webcamTexture);
    }
}