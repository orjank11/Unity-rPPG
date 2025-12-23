using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class Webcam : MonoBehaviour
{
    [Header("Camera Settings")]
    public string requestedDeviceName = "";
    public int requestedWidth = 640;
    public int requestedHeight = 480;
    public float requestedFPS = 30f;
    public bool requestedIsFrontFacing = false;
    public bool autoPlayAfterInitialize = true;
    
    [Header("Performance")]
    public float settingsFPS = 30f;
    
    [Header("Display")]
    public Image displayImage; // Optional: to show the feed
    public bool flipHorizontal = false;
    public bool flipVertical = false; 
    
    [Header("Events")]
    public UnityEngine.Events.UnityEvent onInitialized;
    public UnityEngine.Events.UnityEvent<string> onErrorOccurred;

    // Private fields
    private WebCamTexture _webCamTexture;
    private WebCamDevice _webCamDevice;
    private RenderTexture _renderTexture;
    private Texture2D _outputTexture;
    private Color32[] _pixelBuffer;
    
    private bool _hasInitDone = false;
    private bool _isInitWaiting = false;
    private bool _didUpdateThisFrame = false;
    
    private AsyncGPUReadbackRequest _asyncGPUReadbackRequestBuffer;
    private ManualResetEventSlim _resetEvent;
    private IEnumerator _waitForEndOfFrameCoroutine;
    private IEnumerator _initCoroutine;
    
    // Screen orientation tracking
    private ScreenOrientation _screenOrientation;
    private int _screenWidth;
    private int _screenHeight;
    private bool _isScreenSizeChangeWaiting = false;
    
    // ======= FPS management
    private float _lastFrameTime = 0f;
    private float _frameInterval = 0f;
    
    // Timeout
    private const int TIMEOUT_FRAME_COUNT = 300;

    void Start()
    {
        _frameInterval = settingsFPS > 0 ? 1f / settingsFPS : 0f;
    }

    void OnValidate()
    {
        settingsFPS = Mathf.Clamp(settingsFPS, -1f, float.MaxValue);
        _frameInterval = settingsFPS > 0 ? 1f / settingsFPS : 0f;
    }

    void Update()
    {
        if (_hasInitDone)
        {
            if (_screenOrientation != Screen.orientation)
            {
                AsyncGPUReadback.WaitAllRequests();

                if (!_isScreenSizeChangeWaiting)
                {
                    _isScreenSizeChangeWaiting = true;
                    return;
                }
                _isScreenSizeChangeWaiting = false;

                HandleOrientationChange();
            }

            if (!_webCamTexture.isPlaying) return;

            if (settingsFPS >= _webCamTexture.requestedFPS)
            {
                if (!_webCamTexture.didUpdateThisFrame) return;
                CallReadback();
            }
            else if (settingsFPS > 0)
            {
                if (Time.time - _lastFrameTime >= _frameInterval)
                {
                    if (_webCamTexture.didUpdateThisFrame)
                    {
                        CallReadback();
                        _lastFrameTime = Time.time;
                    }
                }
            }
            else
            {
                if (_webCamTexture.didUpdateThisFrame)
                    CallReadback();
            }
        }
    }

    public void Initialize(Image imageRef)
    {
        displayImage = imageRef;
        if (_initCoroutine != null)
        {
            StopCoroutine(_initCoroutine);
        }
        _initCoroutine = InitializeCoroutine();
        StartCoroutine(_initCoroutine);
    }

    private IEnumerator InitializeCoroutine()
    {
        if (_hasInitDone)
        {
            CancelWaitForEndOfFrameCoroutine();
            ReleaseResources();
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            Debug.LogError("VisionWebcam: AsyncGPUReadback is not supported on this platform");
            onErrorOccurred?.Invoke("AsyncGPUReadback not supported");
            yield break;
        }

        _isInitWaiting = true;
        yield return null; 

        #if (UNITY_IOS || UNITY_WEBGL || UNITY_ANDROID) && !UNITY_EDITOR
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Debug.LogError("VisionWebcam: Camera permission denied");
            OnErrorOccurred?.Invoke("Camera permission denied");
            _isInitWaiting = false;
            yield break;
        }
        #endif

        if (!CreateWebCamTexture())
        {
            onErrorOccurred?.Invoke("No camera device found");
            _isInitWaiting = false;
            yield break;
        }

        _webCamTexture.Play();

        int initFrameCount = 0;
        while (true)
        {
            if (initFrameCount > TIMEOUT_FRAME_COUNT)
            {
                Debug.LogError("VisionWebcam: Camera initialization timeout");
                onErrorOccurred?.Invoke("Camera initialization timeout");
                ReleaseResources();
                _isInitWaiting = false;
                yield break;
            }
            
            if (_webCamTexture.didUpdateThisFrame)
            {
                Debug.Log($"VisionWebcam initialized: {_webCamTexture.deviceName} " +
                         $"{_webCamTexture.width}x{_webCamTexture.height} @{_webCamTexture.requestedFPS}fps");
                break;
            }
            
            initFrameCount++;
            yield return null;
        }

        CreateRenderTextures();

        _resetEvent = new ManualResetEventSlim(true);

        _screenOrientation = Screen.orientation;
        _screenWidth = Screen.width;
        _screenHeight = Screen.height;

        GetFirstFrameSynchronously();

        if (_waitForEndOfFrameCoroutine != null) 
            StopCoroutine(_waitForEndOfFrameCoroutine);
        _waitForEndOfFrameCoroutine = WaitForEndOfFrameCoroutine();
        StartCoroutine(_waitForEndOfFrameCoroutine);

        if (displayImage != null)
        {
            displayImage.image = _outputTexture;
        }

        _isInitWaiting = false;
        _hasInitDone = true;
        _initCoroutine = null;

        if (!autoPlayAfterInitialize)
            _webCamTexture.Stop();

        onInitialized?.Invoke();
    }

    private bool CreateWebCamTexture()
    {
        var devices = WebCamTexture.devices;
        if (devices.Length == 0) return false;

        if (!string.IsNullOrEmpty(requestedDeviceName))
        {
            if (int.TryParse(requestedDeviceName, out int deviceIndex))
            {
                if (deviceIndex >= 0 && deviceIndex < devices.Length)
                {
                    _webCamDevice = devices[deviceIndex];
                    CreateWebCamTextureFromDevice();
                    return true;
                }
            }

            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].name == requestedDeviceName)
                {
                    _webCamDevice = devices[i];
                    CreateWebCamTextureFromDevice();
                    return true;
                }
            }
        }

        var prioritizedKinds = new WebCamKind[]
        {
            WebCamKind.WideAngle,
            WebCamKind.Telephoto,
            WebCamKind.UltraWideAngle,
            WebCamKind.ColorAndDepth
        };

        foreach (var kind in prioritizedKinds)
        {
            foreach (var device in devices)
            {
                if (device.kind == kind && device.isFrontFacing == requestedIsFrontFacing)
                {
                    _webCamDevice = device;
                    CreateWebCamTextureFromDevice();
                    return true;
                }
            }
        }

        _webCamDevice = devices[0];
        CreateWebCamTextureFromDevice();
        return true;
    }

    private void CreateWebCamTextureFromDevice()
    {
        if (requestedFPS < 0)
            _webCamTexture = new WebCamTexture(_webCamDevice.name, requestedWidth, requestedHeight);
        else
            _webCamTexture = new WebCamTexture(_webCamDevice.name, requestedWidth, requestedHeight, (int)requestedFPS);
    }


    private void CreateRenderTextures()
    {
        _renderTexture = new RenderTexture(_webCamTexture.width, _webCamTexture.height, 0, GraphicsFormat.R8G8B8A8_SRGB);
    
        _outputTexture = new Texture2D(_webCamTexture.width, _webCamTexture.height, TextureFormat.RGB24, false);
    
        _pixelBuffer = new Color32[_webCamTexture.width * _webCamTexture.height];
    
        if (!SystemInfo.IsFormatSupported(_renderTexture.graphicsFormat, GraphicsFormatUsage.ReadPixels))
        {
            Debug.LogError($"VisionWebcam: Graphics format {_renderTexture.graphicsFormat} not supported for readback");
            onErrorOccurred?.Invoke($"Graphics format {_renderTexture.graphicsFormat} not supported");
        }
    }

    private void GetFirstFrameSynchronously()
    {
        // Get RGBA data from webcam
        _webCamTexture.GetPixels32(_pixelBuffer);
    
        // Convert RGBA to RGB and set to output texture
        ConvertAndSetRGBPixels(_pixelBuffer);
    
        _didUpdateThisFrame = true;
        _resetEvent.Set();
    }
    
    private void ConvertAndSetRGBPixels(Color32[] rgbaPixels)
    {
        // Convert RGBA Color32 array to RGB Color array
        Color[] rgbPixels = new Color[rgbaPixels.Length];
        for (int i = 0; i < rgbaPixels.Length; i++)
        {
            rgbPixels[i] = new Color(
                rgbaPixels[i].r / 255f,
                rgbaPixels[i].g / 255f,
                rgbaPixels[i].b / 255f,
                1.0f // Alpha not needed for RGB24
            );
        }
    
        _outputTexture.SetPixels(rgbPixels);
        _outputTexture.Apply();
    }

    private void CallReadback()
    {
        Vector2 scale = new Vector2(
            flipHorizontal ? -1 : 1,
            flipVertical  ? -1 : 1
        );
        
        Vector2 offset = new Vector2(
            flipHorizontal ? 1 : 0,
            flipVertical ? 1 : 0
        );
        
        Graphics.Blit(_webCamTexture, _renderTexture, scale, offset);
        AsyncGPUReadback.Request(_renderTexture, 0, OnCompleteReadback);
    }

    private void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {
        if (!gameObject.activeInHierarchy) return;

        if (request.hasError)
        {
            Debug.LogWarning("VisionWebcam: GPU readback error detected");
        }
        else if (request.done)
        {
            _asyncGPUReadbackRequestBuffer = request;
            _resetEvent.Reset();
        }
    }

    private IEnumerator WaitForEndOfFrameCoroutine()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();

            if (!_resetEvent.IsSet)
            {
                if (_asyncGPUReadbackRequestBuffer.hasError)
                {
                    _resetEvent.Set();
                    continue;
                }

                var data = _asyncGPUReadbackRequestBuffer.GetData<Color32>();
                if (data.Length > 0)
                {
                    _pixelBuffer = data.ToArray();
                    _outputTexture.SetPixels32(_pixelBuffer);
                    _outputTexture.Apply();
                }

                _resetEvent.Set();
                _didUpdateThisFrame = true;
            }
            else
            {
                _didUpdateThisFrame = false;
            }
        }
    }

    private void HandleOrientationChange()
    {
        _screenOrientation = Screen.orientation;
        _screenWidth = Screen.width;
        _screenHeight = Screen.height;
        onInitialized?.Invoke();
    }

    private void CancelWaitForEndOfFrameCoroutine()
    {
        if (_waitForEndOfFrameCoroutine != null)
        {
            StopCoroutine(_waitForEndOfFrameCoroutine);
            _waitForEndOfFrameCoroutine = null;
        }
    }

    private void ReleaseResources()
    {
        _isInitWaiting = false;
        _hasInitDone = false;
        _didUpdateThisFrame = false;

        AsyncGPUReadback.WaitAllRequests();

        if (_webCamTexture != null)
        {
            _webCamTexture.Stop();
            Destroy(_webCamTexture);
            _webCamTexture = null;
        }

        if (_renderTexture != null)
        {
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        if (_outputTexture != null)
        {
            DestroyImmediate(_outputTexture);
            _outputTexture = null;
        }

        _resetEvent?.Set();
        _resetEvent = null;
        _pixelBuffer = null;
    }

    public void Play()
    {
        if (_hasInitDone && _webCamTexture != null)
        {
            _webCamTexture.Play();
        }
    }

    public void Pause()
    {
        if (_hasInitDone && _webCamTexture != null)
        {
            _webCamTexture.Pause();
        }
    }

    public void Stop()
    {
        if (_hasInitDone && _webCamTexture != null)
        {
            _webCamTexture.Stop();
        }
    }

    public bool DidUpdateThisFrame()
    {
        return _hasInitDone && _didUpdateThisFrame;
    }

    public Texture2D GetTexture()
    {
        return _hasInitDone ? _outputTexture : null;
    }

    public Color32[] GetPixels()
    {
        return _hasInitDone && _pixelBuffer != null ? (Color32[])_pixelBuffer.Clone() : null;
    }

    public float GetFPS()
    {
        if (!_hasInitDone) return -1f;
        
        if (settingsFPS >= _webCamTexture.requestedFPS)
            return _webCamTexture.requestedFPS;
        else
            return settingsFPS;
    }

    public Vector2Int GetResolution()
    {
        return _hasInitDone ? new Vector2Int(_webCamTexture.width, _webCamTexture.height) : Vector2Int.zero;
    }

    public bool IsInitialized => _hasInitDone;
    public bool IsPlaying => _hasInitDone && _webCamTexture.isPlaying;

    void OnDestroy()
    {
        CancelWaitForEndOfFrameCoroutine();
        ReleaseResources();
    }

    public void Dispose()
    {
        if (_isInitWaiting)
        {
            if (_initCoroutine != null)
            {
                StopCoroutine(_initCoroutine);
                _initCoroutine = null;
            }
            CancelWaitForEndOfFrameCoroutine();
            ReleaseResources();
        }
        else if (_hasInitDone)
        {
            CancelWaitForEndOfFrameCoroutine();
            ReleaseResources();
        }
    }
}