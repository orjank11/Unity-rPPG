using System;
using UnityEngine;
using UnityEngine.UI;
public class Webcam : MonoBehaviour
{
    public static event Action<WebCamTexture> OnFrameReady;
    private WebCamTexture _webcamTexture;
    [SerializeField] private RawImage displayImage;

    void Start()
    {
        _webcamTexture = new WebCamTexture(1280, 800);
        _webcamTexture.Play();
        
        displayImage.texture = _webcamTexture;
    }

    void Update()
    {
        if (_webcamTexture != null && _webcamTexture.didUpdateThisFrame)
            OnFrameReady?.Invoke(_webcamTexture);
    }
}