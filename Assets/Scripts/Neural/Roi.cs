using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class Roi : MonoBehaviour
{
    private Image _roi;
    [SerializeField] private Pulse pulse;
    [SerializeField] private float fps = 30f; // TODO: fix this so not hard coded value

    public void Initialize(Image refImage)
    {
        _roi = refImage;
        StartCoroutine(ProcessLoop());
    } 

    IEnumerator ProcessLoop()
    {
        while (true)
        {
            Processing();
            yield return new WaitForSeconds(1f / fps);
        }
    }

    void Processing()
    {
        if (_roi.image == null) return;
        Texture2D texture2D = null;
        
        if (_roi.image is Texture2D)
        {
            texture2D = _roi.image as Texture2D;
        }
        else if (_roi.image is RenderTexture)
        {
            RenderTexture rt = _roi.image as RenderTexture;
            texture2D = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            
            RenderTexture.active = rt;
            texture2D.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture2D.Apply();
            RenderTexture.active = null;
        }
        
        if (texture2D != null)
        {
            pulse.ProcessFrame(texture2D);
            if (_roi.image is RenderTexture)
            {
                Destroy(texture2D);
            }
        }
    }
}