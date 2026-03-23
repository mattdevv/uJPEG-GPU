using NaughtyAttributes;
using UnityEngine;

[DefaultExecutionOrder(-1)]
public class TestJPEG : MonoBehaviour
{
    public Material material;
    
    public Texture2D input;
    [NaughtyAttributes.ReadOnly] 
    public Texture2D output;
    
    [Range(1, 100)] public int quality = 50;
    public bool downsampleChroma;
    public bool optimalHuffman;
    
    private void Start()
    {
        DoEncodeDecode();
    }

    [Button]
    public void DoEncodeDecode()
    {
        if (output != null)
        {
            if (Application.isPlaying) Destroy(output);
            else DestroyImmediate(output);
        }
        
        if (!JpegHelpers.IsValidTexture(input))
            return;

        if (JpegHelpers.IsReadableTexture(input))
        {
            JpegData j = new (input, downsampleChroma, quality, optimalHuffman);
            output = j.Decode();
        }
        else
        {
            Debug.Log("Could not read, created a copy of input texture.");
            Texture2D readableTexture = JpegHelpers.GetReadableTexture(input);
            JpegData j = new (readableTexture, downsampleChroma, quality, optimalHuffman);
            output = j.Decode();
            DestroyImmediate(readableTexture);
        }
        
        material.mainTexture = output;
    }
}
