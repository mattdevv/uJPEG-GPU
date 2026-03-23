
using System;
using NaughtyAttributes;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

public class JpegBuffer : IDisposable
{
    [ReadOnly]
    public JpegData.Format format;

    [ReadOnly] public int width, height;
    [ReadOnly] public uint exactBits, numMCUs;
    [ReadOnly] public Vector2Int spans; // holds the size in bytes of each section of the buffer (block offsets, encoded data)
    
    public TextureWrapMode wrapMode;
    public FilterMode filterMode;
    
    [ReadOnly] public GraphicsBuffer headerBuffer;
    [ReadOnly] public GraphicsBuffer bodyBuffer;
    
    public JpegBuffer(JpegData jpeg)
    {
        format = jpeg.format;
        width = jpeg.width;
        height = jpeg.height;
        exactBits = jpeg.exactBits;
        
        wrapMode = jpeg.wrapMode;
        filterMode = jpeg.filterMode;
        
        uint mcuWidth = (format == JpegData.Format.YUV420) ? 16u : 8u;
        uint mcuHeight = (format == JpegData.Format.YUV420) ? 16u : 8u;
        numMCUs = JpegHelpers.DivRoundUp((uint)width, mcuWidth) * JpegHelpers.DivRoundUp((uint)height, mcuHeight);

        JpegHeader[] jpegHeader = new JpegHeader[1];
        jpegHeader[0].Fill(jpeg);
        headerBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, UnsafeUtility.SizeOf<JpegHeader>());
        headerBuffer.SetData(jpegHeader);
        
        // need to align data to 16-bytes for GPU to easily read
        int alignedByteOffsetBlocks = JpegHelpers.RoundUp(jpeg.packedOffsets.Length, 4 * sizeof(uint));
        int alignedByteOffset = JpegHelpers.RoundUp(jpeg.bitstream.Length * sizeof(uint), 4 * sizeof(uint));
        spans = new Vector2Int(alignedByteOffsetBlocks, alignedByteOffset);
        byte[] buffer = new byte[alignedByteOffsetBlocks + alignedByteOffset];
        unsafe
        {
            fixed (byte* inputBlocks = jpeg.packedOffsets)
            fixed (uint* inputData = jpeg.bitstream)
            fixed (byte* outputPtr = buffer)
            {
                UnsafeUtility.MemCpy(outputPtr, inputBlocks, jpeg.packedOffsets.Length * sizeof(byte));
                UnsafeUtility.MemCpy(outputPtr + alignedByteOffsetBlocks, inputData, jpeg.bitstream.Length * sizeof(uint));

                // shift all the exact mcu block offsets to account for the concatenated data at front of buffer
                for (uint i = 0; i < jpeg.packedOffsets.Length; i += sizeof(uint) * 5)
                    (*(uint*)(outputPtr + i)) += (uint)alignedByteOffsetBlocks * 8u;
            }
        }
        bodyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, buffer.Length / 4, sizeof(uint));
        bodyBuffer.SetData(buffer);
    }

    public RenderTextureDescriptor GetRenderTextureDescriptor()
    {
        var textureFormat = (format == JpegData.Format.BW ? GraphicsFormat.R8_UNorm : GraphicsFormat.R8G8B8A8_SRGB);
        return new RenderTextureDescriptor(width, height, textureFormat, 0, 0);
    }

    public void SetupCompute(ComputeShader cs, RenderTexture rt)
    {
        // essentially a square to minimise warp groups needed
        int dispatchDim = (int)Math.Ceiling(Math.Sqrt(numMCUs));
        int x = dispatchDim;
        int y = (dispatchDim * (dispatchDim - 1) >= numMCUs) // check if we can skip final row
            ? dispatchDim - 1 
            : dispatchDim;
        
        int kernelId = (int)format;
        cs.SetInt("dispatchWidth", dispatchDim);
        cs.SetBuffer(kernelId, "jpegHeader", headerBuffer);
        cs.SetBuffer(kernelId, "jpegData", bodyBuffer);
        cs.SetTexture(kernelId, "_Result", rt);
    }

    public void DispatchCompute(ComputeShader cs)
    {
        // essentially a square to minimise warp groups needed
        int dispatchDim = (int)Math.Ceiling(Math.Sqrt(numMCUs));
        int x = dispatchDim;
        int y = (dispatchDim * (dispatchDim - 1) >= numMCUs) // check if we can skip final row
            ? dispatchDim - 1 
            : dispatchDim;

        int kernelId = (int)format;
        cs.Dispatch(kernelId, x, y, 1);
    }
    
    public void SetupCompute(CommandBuffer cmd, ComputeShader cs, RenderTexture rt)
    {
        // essentially a square to minimise warp groups needed
        int dispatchDim = (int)Math.Ceiling(Math.Sqrt(numMCUs));
        int x = dispatchDim;
        int y = (dispatchDim * (dispatchDim - 1) >= numMCUs) // check if we can skip final row
            ? dispatchDim - 1 
            : dispatchDim;
        
        int kernelId = (int)format;
        cmd.SetComputeIntParam(cs, "dispatchWidth", dispatchDim);
        cmd.SetComputeBufferParam(cs, kernelId, "jpegHeader", headerBuffer);
        cmd.SetComputeBufferParam(cs, kernelId, "jpegData", bodyBuffer);
        cmd.SetComputeTextureParam(cs, kernelId, "_Result", rt);
    }
    
    public void DispatchCompute(CommandBuffer cmd, ComputeShader cs)
    {
        // essentially a square to minimise warp groups needed
        int dispatchDim = (int)Math.Ceiling(Math.Sqrt(numMCUs));
        int x = dispatchDim;
        int y = (dispatchDim * (dispatchDim - 1) >= numMCUs) // check if we can skip final row
            ? dispatchDim - 1 
            : dispatchDim;

        int kernelId = (int)format;
        cmd.DispatchCompute(cs,kernelId, x, y, 1);
    }

    public void Dispose()
    {
        headerBuffer?.Dispose();
        bodyBuffer?.Dispose();
    }
}
