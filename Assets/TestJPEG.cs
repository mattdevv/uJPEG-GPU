using NaughtyAttributes;
using UnityEngine;

[DefaultExecutionOrder(-1)]
public class TestJPEG : MonoBehaviour
{
    public Material material;
    
    public Texture2D input;
    public JpegAsset inputAsset;
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

        JpegData jpeg;

        if (inputAsset != null)
        {
            jpeg = inputAsset.jpeg;
        }
        else if (input != null)
        {
            if (!JpegHelpers.IsValidTexture(input))
            {
                Debug.LogWarning($"The input texture is invalid: {input.name}", input);
                return;
            }

            if (JpegHelpers.IsReadableTexture(input))
            {
                jpeg = new (input, downsampleChroma, quality, optimalHuffman);
            }
            else
            {
                Debug.Log("Could not read, created a copy of input texture.");
                Texture2D readableTexture = JpegHelpers.GetReadableTexture(input);
                jpeg = new (readableTexture, downsampleChroma, quality, optimalHuffman);
                DestroyImmediate(readableTexture);
            }
        }
        else
        {
            Debug.LogWarning("No input to use");
            return;
        }
        
        
        output = jpeg.Decode();
        material.mainTexture = output;
    }
}
