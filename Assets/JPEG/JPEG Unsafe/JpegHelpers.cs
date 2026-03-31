using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class JpegHelpers
{
    public static bool IsValidForJPEG(in Texture2D texture)
    {
        GraphicsFormat graphicsFormat = texture.graphicsFormat;

        if (GraphicsFormatUtility.IsDepthStencilFormat(graphicsFormat))
        {
            Debug.LogWarning("Can't compress depth/stencil format: " + texture.name);
            return false;
        }
        if (GraphicsFormatUtility.IsHDRFormat(graphicsFormat))
        {
            Debug.LogWarning("Can't compress hdr format: " + texture.name);
            return false;
        }
        if (GraphicsFormatUtility.IsIntegerFormat(graphicsFormat))
        {
            Debug.LogWarning("Can't compress integer format: " + texture.name);
            return false;
        }
        if (GraphicsFormatUtility.IsSwizzleFormat(graphicsFormat))
        {
            Debug.LogWarning("Can't compress swizzled format: " + texture.name);
            return false;
        }
        if (GraphicsFormatUtility.IsPackedFormat(graphicsFormat))
        {
            Debug.LogWarning("Can't compress packed format: " + texture.name);
            return false;
        }
        if (GraphicsFormatUtility.GetComponentCount(graphicsFormat) == 2)
        {
            Debug.LogWarning("Can't compress format with only 2 channels: " + texture.name);
            return false;
        }

        return true;
    }

    public static bool IsReadableTexture(in Texture2D texture)
    {
        if (texture.isReadable == false)
            return false;
        
        if (GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat))
            return false;

        return true;
    }
    
    public static Texture2D GetReadableTexture(Texture2D source)
    {
        GraphicsFormat graphicsFormat = (GraphicsFormatUtility.GetComponentCount(source.graphicsFormat) == 1)
            ? GraphicsFormat.R8_UNorm
            : GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat)
                ? GraphicsFormat.R8G8B8A8_SRGB
                : GraphicsFormat.R8G8B8A8_UNorm;
        
        // 1. Create a temporary RenderTexture with the same dimensions
        RenderTexture rt = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            graphicsFormat
        );

        // 2. Copy the non-readable texture to the RenderTexture
        Graphics.Blit(source, rt);

        // 3. Store the current active RT so we can restore it later
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        // 4. Create a new readable Texture2D and read the pixels from the RT
        Texture2D readableText = new Texture2D(source.width, source.height, graphicsFormat, TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate) {
            name = source.name,
            wrapMode = source.wrapMode,
            filterMode = source.filterMode,
            hideFlags = HideFlags.DontSave
        };
        readableText.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readableText.Apply();

        // 5. Clean up
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return readableText;
    }
    
    public static unsafe void RgbToYCbCr(float* c, byte* YCbCr)
    {
        float r = c[0];
        float g = c[1];
        float b = c[2];
        
        float Y  =  0.299f * r + 0.587f * g + 0.114f * b + 0;
        float Cb = -0.169f * r - 0.331f * g + 0.500f * b + 128f;
        float Cr =  0.500f * r - 0.419f * g - 0.081f * b + 128f;
        
        YCbCr[0] = (byte)Mathf.Clamp(Mathf.RoundToInt(Y ), 0, 255);
        YCbCr[1] = (byte)Mathf.Clamp(Mathf.RoundToInt(Cb), 0, 255);
        YCbCr[2] = (byte)Mathf.Clamp(Mathf.RoundToInt(Cr), 0, 255);
    }
    
    // performs conversion and level shift simultaneously
    public static unsafe void RgbToYCbCr_LvlShift(float* c)
    {
        float r = c[0];
        float g = c[1];
        float b = c[2];
        
        float Y  =  0.299f * r + 0.587f * g + 0.114f * b;
        float Cb = -0.169f * r - 0.331f * g + 0.500f * b;
        float Cr =  0.500f * r - 0.419f * g - 0.081f * b;
        
        c[0] =  Y - 128f;
        c[1] = Cb + 0f;
        c[2] = Cr + 0f;
    }

    public static unsafe void YCbCrToRgb(byte* c, float* color)
    {
        float Y  = c[0];
        float Cb = c[1] - 128f;
        float Cr = c[2] - 128f;

        float r = Y + 1.402f * Cr;
        float g = Y - 0.344136f * Cb - 0.714136f * Cr;
        float b = Y + 1.772f * Cb;
        
        color[0] = r;
        color[1] = g;
        color[2] = b;
    }
    
    // performs conversion and level shift simultaneously
    public static unsafe void YCbCrToRgb_LvlShift(float* color)
    {
        float Y  = color[0] + 128f;
        float Cb = color[1];
        float Cr = color[2];

        float r = Y + 1.402f * Cr;
        float g = Y - 0.344136f * Cb - 0.714136f * Cr;
        float b = Y + 1.772f * Cb;
        
        color[0] = r;
        color[1] = g;
        color[2] = b;
    }
    
    // optimal packing of offsets for MCUs in YUV420 format is: 1 absolute, 8 relative
    public static uint CalculateMcuOffset_420(uint mcuIndex)
    {
        uint fullGroup = mcuIndex / 9;
        uint remainder = mcuIndex % 9;
        
        uint extraBytes = remainder > 0
            ? 2 * remainder + 2
            : 0;
        
        return fullGroup * 20 + extraBytes;
    }
    
    public static int RoundUp(int dividend, int divisor)
    {
        return (dividend + divisor - 1) / divisor * divisor;
    }
    
    public static uint RoundUp(uint dividend, uint divisor)
    {
        return (dividend + divisor - 1) / divisor * divisor;
    }

    public static int DivRoundUp(int dividend, int divisor)
    {
        return (dividend + divisor - 1) / divisor;
    }
    
    public static uint DivRoundUp(uint dividend, uint divisor)
    {
        return (dividend + divisor - 1) / divisor;
    }
    
    public static void PrintGrid<T>(NativeArray<T> data) where T : struct
    {
        Debug.Assert(data.Length == 64);
        
        string str = "";
        
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int index = (x + (y * 8));

                str += $"{data[index]},\t";
            }
            str += "\r\n";
        }
        
        Debug.Log(str);
    }
    
    public static void PrintGrid<T>(T[] data)
    {
        Debug.Assert(data.Length == 64);
        
        string str = "";
        
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int index = (x + (y * 8));

                str += $"{data[index]},\t";
            }
            str += "\r\n";
        }
        
        Debug.Log(str);
    }
    
    public static unsafe void PrintGrid<T>(T* data) where T : unmanaged
    {
        string str = "";
        
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int index = (x + (y * 8));

                str += $"{data[index]},\t";
            }
            str += "\r\n";
        }
        
        Debug.Log(str);
    }
    
    public static void PrintLine<T>(NativeArray<T> data) where T : struct
    {
        string str = "";
        
        for (int i = 0; i < data.Length; i++)
        {
            str += $"{data[i]}, ";
        }
        
        Debug.Log(str);
    }
    
    public static void PrintLine<T>(T[] data)
    {
        string str = "";
        
        for (int i = 0; i < data.Length; i++)
        {
            str += $"{data[i]}, ";
        }
        
        Debug.Log(str);
    }
    
    public static unsafe void PrintLine<T>(T* data, uint count) where T : unmanaged
    {
        string str = "";
        
        for (int i = 0; i < count; i++)
        {
            str += $"{data[i]}, ";
        }
        
        Debug.Log(str);
    }

    public static string BitStringLSB(uint value, uint bitCount)
    {
        char[] result = new char[bitCount];
        for (uint i=0; i<bitCount; i++)
        {
            result[i] = (char)('0' + ((value >> (int)i) & 1u)) ;
        }
        return new string(result);
    }
    
    public static string BitStringMSB(uint value, uint bitCount)
    {
        char[] result = new char[bitCount];
        for (uint i=0; i<bitCount; i++)
        {
            result[(bitCount-1) - i] = (char)('0' + ((value >> (int)i) & 1u)) ;
        }
        return new string(result);
    }
    
    public static uint GetMCUWidth(JpegData.Format format)
    {
        switch (format)
        {
            case JpegData.Format.BW:
                return 8;
            case JpegData.Format.YUV420:
                return 16;
            case JpegData.Format.YUV444:
                return 8;
            default:
                throw new Exception($"Unknown format: {format}");
        }
    }
    
    public static uint GetMCUHeight(JpegData.Format format)
    {
        switch (format)
        {
            case JpegData.Format.BW:
                return 8;
            case JpegData.Format.YUV420:
                return 16;
            case JpegData.Format.YUV444:
                return 8;
            default:
                throw new Exception($"Unknown format: {format}");
        }
    }
    
    public static Vector2Int GetMCUSize(JpegData.Format format)
    {
        switch (format)
        {
            case JpegData.Format.BW:
                return new Vector2Int(8, 8);
            case JpegData.Format.YUV420:
                return new Vector2Int(16, 16);
            case JpegData.Format.YUV444:
                return new Vector2Int(8, 8);
            default:
                throw new Exception($"Unknown format: {format}");
        }
    }
    
    public static uint CalculateNumMCUsForTexture(uint imageWidth, uint imageHeight, JpegData.Format format)
    {
        var mcuSize = GetMCUSize(format);
        
        var numMCUsX = DivRoundUp(imageWidth,  (uint)mcuSize.x);
        var numMCUsY = DivRoundUp(imageHeight, (uint)mcuSize.y);

        return numMCUsX * numMCUsY;
    }
    
    public static uint GetMCUsPerWave(JpegData.Format format)
    {
        switch (format)
        {
            case JpegData.Format.BW:
                return 1;
            case JpegData.Format.YUV420:
                return 1; // 2 if you wish to use the faster compute kernel
            case JpegData.Format.YUV444:
                return 1;
            default:
                throw new Exception($"Unknown format: {format}");
        }
    }
}
