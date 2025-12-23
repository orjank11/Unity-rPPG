using System;using UnityEngine;
using UnityEngine.UIElements;

public enum ResultTypes
{
    Green = 0,
    Chrom = 1,
    Pos = 2,
}
public class Ui : MonoBehaviour
{
    private UIDocument _document;
    private Image _webcamImage;
    private Image _roiImage;

    private Label _greenResult;
    private Label _chromResult;
    private Label _posResult;
    
    [SerializeField] Webcam webcam;

    private bool _hasImage = false;
    void Awake()
    {
        _document = GetComponent<UIDocument>();
        
        _webcamImage = _document.rootVisualElement.Q<Image>("WebcamImage");
        _roiImage = _document.rootVisualElement.Q<Image>("RoiImage");
        
        _greenResult = _document.rootVisualElement.Q<Label>("GreenResult");
        _chromResult  = _document.rootVisualElement.Q<Label>("ChromResult");
        _posResult = _document.rootVisualElement.Q<Label>("PosResult");
    }

    void Start()
    {
        webcam.Initialize(_webcamImage);
    }
    
    // Update is called once per frame
    void Update()
    {
        if (!_hasImage)
        {
            Debug.Log(_webcamImage);
            Debug.Log(_roiImage);
            _hasImage = true;
        }
    }

    public void SetResult(ResultTypes resultType, string value)
    {
        switch (resultType)
        {
            case ResultTypes.Green:
                _greenResult.text = value;
                break;
            case ResultTypes.Chrom:
                _chromResult.text = value;
                break;
            case ResultTypes.Pos:
                _posResult.text = value;
                break;
            default:
                Debug.LogError($"Unknown result type {resultType}, with value {value}");
                break;
        }
    }
}
