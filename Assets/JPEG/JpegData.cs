using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[BurstCompile]
[Serializable]
public partial class JpegData
{
    private const int N = 8; // block dimensions
    
    private const byte EOB = 0x00;
    private const byte ZRL = 0xF0;
    
    public enum Format
    {
        BW,
        YUV420,
        YUV444,
    }
    
    private unsafe struct RawImageData
    {
        public byte* pixels;
        public ushort width;
        public ushort height;
        public byte channels;
    }

    public string imageName;
    public TextureWrapMode wrapMode = TextureWrapMode.Clamp;
    public FilterMode filterMode = FilterMode.Bilinear;

    public Format format;
    public int width;
    public int height;
    public bool srgb;
    
    public uint numMCUs;
    public byte[] packedOffsets;

    public HuffmanTable huffmanAC;
    public HuffmanTable huffmanDC;
    
    //private BitStreamWriter writer; 
    public uint exactBits;
    [HideInInspector] public uint[] bitstream;
    [HideInInspector] public byte[] quantTable;
    [HideInInspector] public byte[] quantTable2;

    public unsafe JpegData(Texture2D texture, bool downsample = true, int quality = 50, bool optimalCompression = false)
    {
        if (texture.isReadable == false)
        {
            Debug.LogError("Cannot create JPEG from unreadable texture.");
            return;
        }
            
        imageName = texture.name + "_JPEG";
        wrapMode = texture.wrapMode;
        filterMode = texture.filterMode;
        width = texture.width;
        height = texture.height;
        srgb = GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
        
        uint channels = GraphicsFormatUtility.GetComponentCount(texture.format);
        
        var data = texture.GetRawTextureData<byte>();
        byte* dataPtr = (byte*)data.GetUnsafePtr();
        Encode(dataPtr, width, height, channels, quality, optimalCompression, downsample);
        data.Dispose();
    }
    
    public unsafe JpegData(NativeArray<byte> data, int imageWidth, int imageHeight, uint imageChannels, bool srgb, int quality = 50, bool downsample = true, bool optimalCompression = false)
    {
        this.width = imageWidth;
        this.height = imageHeight;
        this.srgb = srgb;
        
        byte* dataPtr = (byte*)data.GetUnsafePtr();
        Encode(dataPtr, width, height, imageChannels, quality, optimalCompression, downsample);
    }

    private unsafe void Encode(byte* dataPtr, int imageWidth, int imageHeight, uint imageChannels, int quality, bool optimalHuffman, bool downsample)
    {
#if MATTDEVV_JPEG_TIMER
        MyTimer encodeTimer = new MyTimer("Encode");
#endif
        CreateScaledQuantTables(quality);
        
        if (imageChannels == 1)
        {
            format = Format.BW;
            numMCUs = Encode_BW(dataPtr, imageChannels, imageWidth, imageHeight, optimalHuffman);
        }
        else if (imageChannels >= 3)
        {
            if (downsample)
            {
                format = Format.YUV420;
                numMCUs = Encode_420(dataPtr, imageChannels, imageWidth, imageHeight, optimalHuffman);
            }
            else
            {
                format = Format.YUV444;
                numMCUs = Encode_444(dataPtr, imageChannels, imageWidth, imageHeight, optimalHuffman);
            }
        }
        
#if MATTDEVV_JPEG_TIMER
        Debug.Log($"Time to encode (ms): {encodeTimer.elapsedMilliseconds()}");
        Debug.Log("Num MCUs: " + numMCUs);
        
        Debug.Log("DC " + huffmanDC.ToString());
        Debug.Log("AC " + huffmanAC.ToString());

        uint bitsBitStream = exactBits;
        uint bitsOverhead = (uint)packedOffsets.Length * 8u;
        uint totalBits = bitsBitStream + bitsOverhead;
        Debug.Log($"Bits bitstream: {bitsBitStream} ({bitsBitStream / 8192f}kb)");
        Debug.Log($"Bits overhead: {bitsOverhead} ({bitsOverhead / 8192f}kb)");
        Debug.Log($"Total bits: {totalBits} ({totalBits / 8192f}kb)");
        Debug.Log($"Compression (%): " + 100f * (totalBits / 8f) / ((float)(imageWidth * imageHeight) * Mathf.Clamp((int)imageChannels, 1, 3)));
#endif
    }

    public Texture2D Decode()
    {
        Texture2D output = new Texture2D(width, height, 
            format == Format.BW 
                ? GraphicsFormat.R8_UNorm 
                : srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm, 
            0, TextureCreationFlags.DontUploadUponCreate) {
            name = imageName,
            filterMode = filterMode,
            wrapMode = wrapMode,
            hideFlags = HideFlags.HideAndDontSave,
        };

#if MATTDEVV_JPEG_TIMER
        MyTimer decodeTimer = new MyTimer("Decode");
#endif
        NativeArray<byte> decodedBytes;
        switch (format)
        {
            case Format.BW:
                decodedBytes = Decode_BW();
                break;
            case Format.YUV420:
                decodedBytes = Decode_420();
                break;
            case Format.YUV444:
                decodedBytes = Decode_444();
                break;
            default:
                throw new Exception($"Unknown format: {format}");
        }
#if MATTDEVV_JPEG_TIMER
        Debug.Log($"Time to decode (ms): {decodeTimer.elapsedMilliseconds()}");
#endif
        
        output.LoadRawTextureData(decodedBytes);
        output.Apply(false, true);
        decodedBytes.Dispose();

        return output;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PackACSymbol(uint run, uint size, out uint symbol)
    {
        symbol = run << 4 | size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnpackACSymbol(uint symbol, out uint run, out uint size)
    {
        run = symbol >> 4;
        size = symbol & 0x0F;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return:AssumeRange(0ul, 16ul)]
    private static uint BitsRequired(int value)
    {
        unchecked
        {
            if (value < 0)
            {
                return 32u - (uint)math.lzcnt(-value);
            }
            else
            {
                return 32u - (uint)math.lzcnt(value);
            }
        }
    }
    
    // converts a value to an encoded+size form
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EncodeValue(int value, out uint encoded, out uint size)
    {
        unchecked
        {
            if (value < 0)
            {
                size = 32u - (uint)(math.lzcnt(-value));
                encoded = (uint)(value + (1 << (int)size) - 1);
            }
            else
            {
                size = 32u - (uint)(math.lzcnt(value));
                encoded = (uint)(value);
            }
        }
    }
    
    // converts a packed non-zero value back into an int
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeValue(uint encoded, uint size)
    {
        unchecked
        {
            if ((int)encoded >= (1 << ((int)size - 1))) // check if MSB is set
                return (int)encoded;
        
            return (int)encoded - ((1 << (int)size) - 1);
        }
    }
    
    private void CreateScaledQuantTables(int quality)
    {
        quality = Mathf.Clamp(quality, 1, 100);
        
        int S;
        if (quality < 50) S = 5000 / quality;
        else              S = 200 - (quality * 2);
        
        quantTable = new byte[64];
        for (int i = 0; i < 64; i++)
        {
            quantTable[i] = (byte) Mathf.Clamp(Mathf.RoundToInt((MCUBlock.QuantizationTable[i] * S + 50f) / 100f), 1, 255);
        }
        
        quantTable2 = new byte[64];
        for (int i = 0; i < 64; i++)
        {
            quantTable2[i] = (byte) Mathf.Clamp(Mathf.RoundToInt((MCUBlock.highCompressionLumaQuant[i] * S + 50f) / 100f), 1, 255);
        }
    }
    
    private static readonly float[] scaleFactorAAN = {
        1.0f, 1.387039845f, 1.306562965f, 1.175875602f,
        1.0f, 0.785694958f, 0.541196100f, 0.275899379f
    };
    
    private static unsafe void GetQuantizationTableAAN(byte* rawTable, float* outTable)
    {
        for (int u = 0; u < 8; u++)
        for (int v = 0; v < 8; v++)
        {
            int i = v + u * 8;
            double factor = 1.0 / (8.0 * scaleFactorAAN[u] * scaleFactorAAN[v]);
            outTable[i] = (float)(factor / rawTable[i]);
        }
    }
    
    private static unsafe void AddBlockToHistogram(short* blockData, uint* dcHistogram, uint* acHistogram, short* lastDC)
    {
        {
            // Compute the DC delta
            short thisValue = blockData[0];
            int delta = thisValue - *lastDC;
            *lastDC = thisValue;
            uint encodedSize = BitsRequired(delta);
            
            Debug.Assert(encodedSize <= 12); // DC size should be in range [-2048, 2047]
            ++dcHistogram[encodedSize];
        }

        // build the AC symbol table histogram
        uint leadingZeros = 0;
        for (int j = 1; j < 64; j++)
        {
            int nextValue = blockData[j];

            // ignore zeros till later
            if (nextValue == 0)
            {
                leadingZeros++;
                continue;
            }
                    
            // else found a non-zero element
            // clamp count of leading zeros to max 15
            while (leadingZeros >= 16)
            {
                leadingZeros -= 16;
                ++acHistogram[ZRL]; // ZRL symbol
            }

            uint encodedSize = BitsRequired(nextValue);
            Debug.Assert(encodedSize <= 11); // AC should be in range [-1024, 1023]

            PackACSymbol(leadingZeros, encodedSize, out uint symbol);
            ++acHistogram[symbol];
            leadingZeros = 0;
        }

        // if there was any trailing zeros mark them
        if (leadingZeros > 0)
        {
            ++acHistogram[EOB]; // EOB symbol
        }
    }
    
    private static unsafe void EncodeBlockToStream(uint* writePtr, uint* writeIndex, float* currentMCU, float* quantAAN, int* lastDC, uint* dcLut, uint* acLut)
    {
        short* tempSpacePtr = stackalloc short[64];
        MCUBlock.Encode(currentMCU, tempSpacePtr, quantAAN);

        {   // push MCUs DC coefficient 
            // Compute the DC delta
            short thisValue = tempSpacePtr[0];
            int delta = thisValue - *lastDC;
            *lastDC = thisValue;
            EncodeValue(delta, out uint encodedDelta, out uint sizeEncoded);
    
            uint codePlusLength = dcLut[sizeEncoded];
            uint huffmanCode = codePlusLength & 0xFFFF;
            uint codeLength = codePlusLength >> 16;
            
            BitStreamWriterUnsafe.Push(writePtr, writeIndex, (encodedDelta << (int)codeLength) | huffmanCode, sizeEncoded + codeLength);
        }
        
        uint leadingZeros = 0;
        for (int j = 1; j < 64; j++)
        {
            int nextValue = tempSpacePtr[j];
            
            if (nextValue == 0)
            {
                // ignore zeros till later
                leadingZeros++;
                continue;
            }
            // else found a non-zero element
            
            // potentially need to remove excess leading zeros
            // (can only have max 15 before a non-zero number)
            while (leadingZeros >= 16)
            {
                // push ZRL to the stream
                // we only push a huffman code as ZRL has no encoded data after (0 length encoded)
                uint codeAndLength = acLut[ZRL];
                uint codeLength = codeAndLength >> 16;
                uint code = codeAndLength; // code is stored in the bottom 16 bits
                BitStreamWriterUnsafe.Push(writePtr, writeIndex, code, codeLength);
                
                leadingZeros -= 16;
            }
            
            {
                EncodeValue(nextValue, out uint encodedValue, out uint encodedSize);
                PackACSymbol(leadingZeros, encodedSize, out uint symbol);
                leadingZeros = 0;

                // Can push the huffman code and the encoded value that follows it simultaneously
                // both are 16 bit or less, so together they can fit in a single 32 bit line
                uint codeAndLength = acLut[symbol];
                uint codeLength = codeAndLength >> 16;
                uint code = codeAndLength & 0xFFFF;
                BitStreamWriterUnsafe.Push(writePtr, writeIndex, (encodedValue << (int)codeLength) | code, encodedSize + codeLength);
            }
        }

        // if there was any trailing zeros mark them
        if (leadingZeros > 0)
        {
            uint codeAndLength = acLut[EOB];
            uint codeLength = codeAndLength >> 16;
            BitStreamWriterUnsafe.Push(writePtr, writeIndex, codeAndLength, codeLength);
        }
    }
    
    private static unsafe void DecodeBlockFromStream(uint* streamPtr, uint* readOffsetPtr, HuffmanStruct* huffmanDC, HuffmanStruct* huffmanAC, byte* quantTable, short* lastDC, float* outputPtr)
    {
        { // decode DC
            // next 21-bits will contain both huffman code and the encoded delta value (if any)
            // possible bits: [1,9] for code + [0,12] for encoded
            uint bitsDC = BitStreamReaderUnsafe.PeakUInt(streamPtr, *readOffsetPtr); 

            int delta = 0;
            uint huffmanLength = HuffmanTable.GetSymbolFromCode(huffmanDC, bitsDC, out byte symbol);
            *readOffsetPtr += huffmanLength + symbol;

            // symbol shows how many bits encode the delta
            // these bits are always available in the same 32 bits after the huffman code
            if (symbol > 0)
            {
                // need to remove low bits with a shift, and high bits with a mask
                uint mask = ((1u << symbol) - 1);
                uint encoded = (bitsDC >> (int)huffmanLength) & mask;
                delta = DecodeValue(encoded, symbol);
            }

            *lastDC += (short)delta;

            outputPtr[0] = *lastDC * quantTable[0];
        }

        // decode 63 AC values
        uint decodedCount = 1;
        while (decodedCount < 64)
        {
            uint next32Bits = BitStreamReaderUnsafe.PeakUInt(streamPtr, *readOffsetPtr); 
            
            uint codeLength = HuffmanTable.GetSymbolFromCode(huffmanAC, next32Bits, out byte symbol);
            if (codeLength == 0) Debug.LogError("Incorrectly read the bitstream");

            // check for special case (end of block), need to fill remaining values 0's
            if (symbol == EOB)
            {
                *readOffsetPtr += (codeLength);
                break; // MCU is always full after this, can stop decoding
            }

            UnpackACSymbol(symbol, out var run, out var size);

            // there is always a value after a run of 0's
            // the code bits are at most 16 and were read before when we read the huffmancode
            short finalValue;
            if (size > 0)
            {
                // we already read all the bits necessary above
                // just need to remove the low bits (current huffman code) and the high bits (next huffman)
                uint bits = unchecked(next32Bits >> (int)codeLength);
                uint mask = unchecked((1u << (int)size) - 1u);
                finalValue = (short)DecodeValue(bits & mask, size);

                int index = MCUBlock.InvZigZagLUT[decodedCount + run];
                outputPtr[index] = finalValue * quantTable[index];
            }

            // add to count of decoded values
            decodedCount += run + 1u;

            *readOffsetPtr += (codeLength + size);
        }
        
        MCUBlock.Decode(outputPtr);
    }
    
    private static unsafe void DecodeBlockFromStream_LUT(uint* streamPtr, uint* readOffsetPtr, byte* dcLUT, ushort* acLUT, byte* quantTable, short* lastDC, float* outputPtr)
    {
        { // decode DC
            // next 21-bits will contain both huffman code and the encoded delta value (if any)
            // possible bits: [1,9] for code + [0,12] for encoded
            uint bitsDC = BitStreamReaderUnsafe.PeakUInt(streamPtr, *readOffsetPtr); 

            int delta = 0;
            byte packed = dcLUT[bitsDC & 0x3F]; // use lowest 6 bits as key
            byte codeLength = (byte)(packed >> 4);
            uint symbol = (uint)(packed & 0xF);
            *readOffsetPtr += codeLength + symbol;

            // symbol shows how many bits encode the delta
            // these bits are always available in the same 32 bits after the huffman code
            if (symbol > 0)
            {
                // need to remove low bits with a shift, and high bits with a mask
                uint mask = (1u << unchecked((int)symbol)) - 1;
                uint encoded = (bitsDC >> codeLength) & mask;
                delta = DecodeValue(encoded, symbol);
            }

            *lastDC += (short)delta;

            outputPtr[0] = *lastDC * quantTable[0];
        }

        // decode 63 AC values
        uint decodedCount = 1;
        while (decodedCount < 64)
        {
            uint next32Bits = BitStreamReaderUnsafe.PeakUInt(streamPtr, *readOffsetPtr); 
            
            ushort packed = acLUT[next32Bits & 0xFFF]; // use lowest 12 bits as key
            uint codeLength = (uint)(packed >> 8);
            uint symbol = (uint)(packed & 0xFF);

            if (codeLength == 0)
                Debug.LogError("Incorrectly read the bitstream");

            // if special case (end of block), need to fill remaining values 0's
            if (symbol == EOB)
            {
                *readOffsetPtr += codeLength;
                break; // MCU is always full after this, can stop decoding
            }

            UnpackACSymbol(symbol, out var run, out var size);

            // there is always a value after a run of 0's
            // the code bits are at most 16 and were read before when we read the huffmancode
            if (size > 0)
            {
                // we already read all the bits necessary above
                // just need to remove the low bits (current huffman code) and the high bits (next huffman)
                uint bits = unchecked(next32Bits >> (int)codeLength);
                uint mask = unchecked((1u << (int)size) - 1u);
                var finalValue = (short)DecodeValue(bits & mask, size);

                int index = MCUBlock.InvZigZagLUT[decodedCount + run];
                outputPtr[index] = finalValue * quantTable[index];
            }

            // add to count of decoded values
            decodedCount += run + 1u;

            *readOffsetPtr += (codeLength + size);
        }
        
        MCUBlock.Decode(outputPtr);
    }
}
