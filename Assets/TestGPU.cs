using NaughtyAttributes;
using UnityEngine;

public class TestGPU : MonoBehaviour
{
    public Texture2D input;
    
    public JpegAsset jpegAsset;
    public JpegBuffer runtimeBuffer;
    private RenderTexture runtimeRT;
    
    public Material material;
    public ComputeShader computeShader;
    
    [Range(1, 100)] public int quality = 50;
    public bool downsampleChroma;
    public bool optimalHuffman;
    
    void Start()
    {
        JpegData j;
        
        if (jpegAsset != null)
        {
            j = jpegAsset.jpeg;
        }
        else
        {
            if (!JpegHelpers.IsValidForJPEG(input))
                return;

            if (JpegHelpers.IsReadableTexture(input))
            {
                j = new (input, downsampleChroma, quality, optimalHuffman);
            }
            else
            {
                Debug.Log("Could not read, created a copy of input texture.");
                Texture2D readableTexture = JpegHelpers.GetReadableTexture(input);
                j = new (readableTexture, downsampleChroma, quality, optimalHuffman);
                if (Application.isPlaying) Destroy(readableTexture);
                else DestroyImmediate(readableTexture);
            }
        }
        
        runtimeBuffer = new JpegBuffer(j);
        
        runtimeRT = new RenderTexture(runtimeBuffer.GetRenderTextureDescriptor());
        runtimeRT.filterMode = runtimeBuffer.filterMode;
        runtimeRT.wrapMode = runtimeBuffer.wrapMode;
        runtimeRT.enableRandomWrite = true;
        runtimeRT.hideFlags = HideFlags.HideAndDontSave;
        
        material.mainTexture = runtimeRT; 
    }

    private void Update()
    {
        //if (Time.frameCount > 1)
        //    return;
        
        if (runtimeBuffer != null)
        {
            runtimeBuffer.SetupCompute(computeShader, runtimeRT);
            runtimeBuffer.DispatchCompute(computeShader);
        }
    }

    private void OnDestroy()
    {
        runtimeBuffer.Dispose();
        runtimeRT.Release();
        runtimeRT = null;
    }
}
