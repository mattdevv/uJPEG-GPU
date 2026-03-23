using System.IO;
using NaughtyAttributes;
using UnityEngine;

public class TestUnityJpeg : MonoBehaviour
{
#if UNITY_EDITOR
    public Texture2D jpeg;
    [ReadOnly] public Texture2D jpegLoaded;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        TimeLoadJPEG();
    }
    
    [Button]
    private void TimeLoadJPEG()
    {
        if (jpeg == null)
            return;
        
        string path = UnityEditor.AssetDatabase.GetAssetPath(jpeg);
        if (string.IsNullOrEmpty(path))
            return;
        
        string extension = Path.GetExtension(path);
        if (extension != ".jpg")
            return;

        byte[] bytes = File.ReadAllBytes(path);

        if (jpegLoaded != null)
        {
            if (Application.isPlaying)
                Destroy(jpegLoaded);
            else
                DestroyImmediate(jpegLoaded);
        }
        
        jpegLoaded = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        MyTimer fileLoadTimer = new MyTimer("Load Jpeg");
        jpegLoaded.LoadImage(bytes);
        Debug.Log($"Time to load .JPEG: {fileLoadTimer.elapsedMilliseconds()}");
    }
#endif
}
