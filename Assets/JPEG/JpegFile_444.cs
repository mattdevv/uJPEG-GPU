using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

public partial class JpegData
{
    [BurstCompile]
    [SkipLocalsInit]
    private static unsafe void FetchMCU_444(RawImageData* image, float* mcuDataOut, uint mcuIndex)
    {
        float* yuv = stackalloc float[3];
        
        uint numMCUsX = (uint)JpegHelpers.DivRoundUp(image->width, N);
        Vector2Int mcuCoord = new Vector2Int((int)(mcuIndex % numMCUsX), (int)(mcuIndex / numMCUsX));
        Vector2Int basePixelCoord = mcuCoord * 8;
        
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                int outputIndex = x + y * N;
                Vector2Int sampleCoord = basePixelCoord + new Vector2Int(x, y); 

                if (sampleCoord.x >= image->width || sampleCoord.y >= image->height)
                {
                    mcuDataOut[outputIndex + 0] = 0;
                    mcuDataOut[outputIndex + 64] = 0;
                    mcuDataOut[outputIndex + 128] = 0;
                }
                else
                {
                    yuv[0] = image->pixels[(sampleCoord.x + sampleCoord.y * image->width) * image->channels + 0];
                    yuv[1] = image->pixels[(sampleCoord.x + sampleCoord.y * image->width) * image->channels + 1];
                    yuv[2] = image->pixels[(sampleCoord.x + sampleCoord.y * image->width) * image->channels + 2];

                    JpegHelpers.RgbToYCbCr_LvlShift(yuv);

                    mcuDataOut[outputIndex + 0] = yuv[0];
                    mcuDataOut[outputIndex + 64] = yuv[1];
                    mcuDataOut[outputIndex + 128] = yuv[2];
                }
            }
        }
    }
    
    [BurstCompile]
    private static unsafe void BuildHistograms_444(RawImageData* imageData, uint numMCUs, uint* DCHistogram, uint* ACHistogram, float* quant_L, float* quant_Cb, float* quant_Cr)
    {
        float* mcuPtr = stackalloc float[3 * N * N];
        short* tempSpacePtr = stackalloc short[64];
        
        short lastDC_Y  = 0;
        short lastDC_Cb = 0;
        short lastDC_Cr = 0;
        for (uint i = 0; i < numMCUs; i++)
        {
            FetchMCU_444(imageData, mcuPtr, i);
            
            // luminance
            MCUBlock.Encode(mcuPtr, tempSpacePtr, quant_L);
            AddBlockToHistogram(tempSpacePtr, DCHistogram, ACHistogram, &lastDC_Y);
        
            // chroma blue
            MCUBlock.Encode(mcuPtr + 64, tempSpacePtr, quant_Cb);
            AddBlockToHistogram(tempSpacePtr, DCHistogram, ACHistogram, &lastDC_Cb);
        
            // chroma red
            MCUBlock.Encode(mcuPtr + 128, tempSpacePtr, quant_Cr);
            AddBlockToHistogram(tempSpacePtr, DCHistogram, ACHistogram, &lastDC_Cr);
            
            lastDC_Y  = 0;
            lastDC_Cb = 0;
            lastDC_Cr = 0;
        }
    }
    
    [BurstCompile]
    private static unsafe void EncodeToBitStream_444(RawImageData* imageData, uint numMCUs, byte* offsets, uint* writePtr, uint* writeIndex, uint* dcLUT, uint* acLUT, float* quant_L, float* quant_Cb, float* quant_Cr)
    {
        float* mcuPtr = stackalloc float[3 * N * N];
        byte* offsetStoreAddr = offsets;
        uint lastAbsolute = 0;
        
        int lastDC_Y  = 0;
        int lastDC_Cb = 0;
        int lastDC_Cr = 0;
        for (uint i = 0; i < numMCUs; i++)
        {
            FetchMCU_444(imageData, mcuPtr, i);
            
            uint mod = i % 9u;
            if (mod > 0) // write 16-bit relative offset to last exact offset
            {
                uint deltaIndex = *writeIndex - lastAbsolute;
                Debug.Assert(deltaIndex <= ushort.MaxValue);
                    
                // store as 16 bit
                *(ushort*)offsetStoreAddr = unchecked((ushort)deltaIndex);
                offsetStoreAddr += sizeof(ushort);
            }
            else // write exact offset as 32 bit
            {
                lastAbsolute = *writeIndex;
                
                *(uint*)offsetStoreAddr = lastAbsolute;
                offsetStoreAddr += sizeof(uint);
            }

            EncodeBlockToStream(writePtr, writeIndex, mcuPtr, quant_L, &lastDC_Y, dcLUT, acLUT);
            lastDC_Y  = 0;
            EncodeBlockToStream(writePtr, writeIndex, mcuPtr + 64, quant_Cb, &lastDC_Cb, dcLUT, acLUT);
            lastDC_Cb  = 0;
            EncodeBlockToStream(writePtr, writeIndex, mcuPtr + 128, quant_Cr, &lastDC_Cr, dcLUT, acLUT);
            lastDC_Cr = 0;
        } 
    }
    
    private unsafe uint Encode_444(byte* rawPixelData, uint inputChannels, int width, int height, bool doOptimal)
    { 
        RawImageData imageData = new RawImageData()
        {
            pixels = rawPixelData,
            width = (ushort)width,
            height = (ushort)height,
            channels = (byte)inputChannels,
        };
        
        float* quantAAN = stackalloc float[N*N];   
        fixed (byte* rawQuant = lumaninceQuantTable)
            GetQuantizationTableAAN(rawQuant, quantAAN);
        
        float* quantAAN2 = stackalloc float[N*N];   
        fixed (byte* rawQuant = chromaQuantTable)
            GetQuantizationTableAAN(rawQuant, quantAAN2);
        
        int numMCUsX = JpegHelpers.DivRoundUp(width, N);
        int numMCUsY = JpegHelpers.DivRoundUp(height, N);
        uint numMCUs = (uint)(numMCUsX * numMCUsY);
        
        // loop over all MCU's to build a histograms of the DC and AC symbols
        uint* DCHistogram = stackalloc uint[12];
        UnsafeUtility.MemClear(DCHistogram, 12 * sizeof(uint));
        uint* ACHistogram = stackalloc uint[256];
        UnsafeUtility.MemClear(ACHistogram, 256 * sizeof(uint));
        BuildHistograms_444(&imageData, numMCUs, DCHistogram, ACHistogram, quantAAN, quantAAN2, quantAAN2);
        
        // count how many bits will be required to encode the DC and AC values
        uint totalEncodedBits = 0;
        for (uint symbol=0; symbol<12; symbol++)
        {
            uint frequency = DCHistogram[symbol];
            uint followingBits = symbol & 0xF;
            totalEncodedBits += frequency * followingBits;
        }
        for (uint symbol=0; symbol<256; symbol++)
        {
            uint frequency = ACHistogram[symbol];
            uint followingBits = symbol & 0xF;
            totalEncodedBits += frequency * followingBits;
        }

        // build the huffman tables from the histograms
        huffmanDC = new HuffmanTable(DCHistogram, 12, 6, true);
        huffmanAC = new HuffmanTable(ACHistogram, 256, doOptimal ? 16u : 12u);

        packedOffsets = new byte[JpegHelpers.CalculateMcuOffset_420((uint)numMCUs)];
        
        ulong exactBitsRequired = totalEncodedBits + huffmanAC.encodedSize + huffmanDC.encodedSize;
        exactBits = (uint)exactBitsRequired;
        
        bitstream = new uint[(exactBitsRequired + 63ul) / 32ul]; // ensures later we can over-read by upto 32 bits by adding 1 extra element to array
        uint writeIndex = 0;
        
        fixed (byte* offsets = packedOffsets)
        fixed (uint* ptr = bitstream)
        {
            // alias to reuse the memory
            uint* dcLUT = DCHistogram;
            huffmanDC.GetEncodingLUT(dcLUT);
            uint* acLUT = ACHistogram;
            huffmanAC.GetEncodingLUT(acLUT);
            
            EncodeToBitStream_444(&imageData, numMCUs, offsets, ptr, &writeIndex, dcLUT, acLUT, quantAAN, quantAAN2, quantAAN2);
        }

        return numMCUs;
    }
    
    [BurstCompile]
    private static unsafe void DecodeBytes_444(RawImageData* imageData, uint* readPtr, uint readLength, byte* offsets, 
        HuffmanStruct* huffmanDC, HuffmanStruct* huffmanAC, byte* quantTable_L, byte* quantTable_Cb, byte* quantTable_Cr)
    {
        
        uint numMCUsX = (uint)JpegHelpers.DivRoundUp(imageData->width, N);
        uint numMCUsY = (uint)JpegHelpers.DivRoundUp(imageData->height, N);
        uint numMCUs = numMCUsX * numMCUsY;
        
        float* tempSpacePtrF = stackalloc float[3 * N * N];
        byte* mcu = stackalloc byte[N * N * 4];
        float* color = stackalloc float[3];
        
        short lastDC_Y = 0;
        short lastDC_Cb = 0;
        short lastDC_Cr = 0;
        
        uint readOffsetBits = 0;
        for (uint i = 0; i < numMCUs; i++)
        {
            // load the bit index at which this MCU begins at
            uint readBit;
            byte* offsetStoreAddr = offsets + JpegHelpers.CalculateMcuOffset_420(i);
            uint mod = i % 9u;
            if (mod == 0)
            {
                // can read exact offset
                readBit = *(uint*)offsetStoreAddr;
            }
            else
            {
                byte* readAddr = offsetStoreAddr - (sizeof(uint) + (mod - 1u) * sizeof(ushort));
                // read exact offset and then add delta
                readBit = *(uint*)readAddr;
                readBit += *(ushort*)offsetStoreAddr;
            }
            Debug.Assert(readOffsetBits == readBit);

            UnsafeUtility.MemClear(tempSpacePtrF, 3 * 64 * sizeof(float));
            
            // load luminance (Y)
            DecodeBlockFromStream(readPtr, &readOffsetBits, huffmanDC, huffmanAC, quantTable_L, &lastDC_Y, tempSpacePtrF);
            lastDC_Y = 0;
        
            // load chroma blue (Cb)
            DecodeBlockFromStream(readPtr, &readOffsetBits, huffmanDC, huffmanAC, quantTable_Cb, &lastDC_Cb, tempSpacePtrF + 64);
            lastDC_Cb = 0;
            
            // load chroma red (Cr)
            DecodeBlockFromStream(readPtr, &readOffsetBits, huffmanDC, huffmanAC, quantTable_Cr, &lastDC_Cr, tempSpacePtrF + 128);
            lastDC_Cr = 0;
            
            uint mcuX = i % numMCUsX;
            uint mcuY = i / numMCUsX;

            // output pixels AABB
            uint startX = mcuX * 8;
            uint startY = mcuY * 8;
            uint endX = startX + 8;
            if (endX > imageData->width) endX = imageData->width;
            uint endY = startY + 8;
            if (endY > imageData->height) endY = imageData->height;
            
            for (int j = 0; j < 64; j++)
            {
                // get RGB color
                color[0] = tempSpacePtrF[j];
                color[1] = tempSpacePtrF[j + 64];
                color[2] = tempSpacePtrF[j + 128];
                JpegHelpers.YCbCrToRgb_LvlShift(color);
                mcu[j * 4 + 0] = (byte)math.clamp(color[0] + 0.5f, 0.5f, 255.5f);
                mcu[j * 4 + 1] = (byte)math.clamp(color[1] + 0.5f, 0.5f, 255.5f);
                mcu[j * 4 + 2] = (byte)math.clamp(color[2] + 0.5f, 0.5f, 255.5f);
            }
            
            uint outputWidth = endX - startX;
            uint outputHeight = endY - startY;

            const byte bytesPerPixel = 4;
            UnsafeUtility.MemCpyStride(
                imageData->pixels + (startX + startY * imageData->width) * bytesPerPixel,
                imageData->width * bytesPerPixel, 
                mcu, 
                8 * bytesPerPixel, 
                (int)outputWidth * bytesPerPixel,
                (int)outputHeight);
        }
    }
    
    [BurstCompile]
    private static unsafe void DecodeBytes_444_LUT(RawImageData* imageData, uint* readPtr, uint readLength, byte* offsets, 
        byte* dcLUT, ushort* acLUT, byte* quantTable_L, byte* quantTable_Cb, byte* quantTable_Cr)
    {
        uint numMCUsX = (uint)JpegHelpers.DivRoundUp(imageData->width, N);
        uint numMCUsY = (uint)JpegHelpers.DivRoundUp(imageData->height, N);
        uint numMCUs = numMCUsX * numMCUsY;
        
        float* tempSpacePtrF = stackalloc float[3 * 64];
        byte* mcu = stackalloc byte[N * N * 4];
        float* color = stackalloc float[3];
        
        short lastDC_Y = 0;
        short lastDC_Cb = 0;
        short lastDC_Cr = 0;
        
        uint readOffsetBits = 0;
        for (uint i = 0; i < numMCUs; i++)
        {
            // load the bit index at which this MCU begins at
            uint readBit;
            byte* offsetStoreAddr = offsets + JpegHelpers.CalculateMcuOffset_420(i);
            uint mod = i % 9u;
            if (mod == 0)
            {
                // can read exact offset
                readBit = *(uint*)offsetStoreAddr;
            }
            else
            {
                byte* readAddr = offsetStoreAddr - (sizeof(uint) + (mod - 1u) * sizeof(ushort));
                // read exact offset and then add delta
                readBit = *(uint*)readAddr;
                readBit += *(ushort*)offsetStoreAddr;
            }

            readOffsetBits = readBit;
            Debug.Assert(readOffsetBits == readBit);

            UnsafeUtility.MemClear(tempSpacePtrF, 3 * 64 * sizeof(float));
            
            // load luminance (Y)
            DecodeBlockFromStream_LUT(readPtr, &readOffsetBits, dcLUT, acLUT, quantTable_L, &lastDC_Y, tempSpacePtrF);
            lastDC_Y = 0;
        
            // load chroma blue (Cb)
            DecodeBlockFromStream_LUT(readPtr, &readOffsetBits, dcLUT, acLUT, quantTable_Cb, &lastDC_Cb, tempSpacePtrF + 64);
            lastDC_Cb = 0;
            
            // load chroma red (Cr)
            DecodeBlockFromStream_LUT(readPtr, &readOffsetBits, dcLUT, acLUT, quantTable_Cr, &lastDC_Cr, tempSpacePtrF + 128);
            lastDC_Cr = 0;
            
            uint mcuX = i % numMCUsX;
            uint mcuY = i / numMCUsX;

            // output pixels AABB
            uint startX = mcuX * 8;
            uint startY = mcuY * 8;
            uint endX = startX + 8;
            uint endY = startY + 8;
            if (endX > imageData->width) endX = imageData->width;
            if (endY > imageData->height) endY = imageData->height;

            for (int j = 0; j < 64; j++)
            {
                // get RGB color
                color[0] = tempSpacePtrF[j];
                color[1] = tempSpacePtrF[j + 64];
                color[2] = tempSpacePtrF[j + 128];
                JpegHelpers.YCbCrToRgb_LvlShift(color);
                mcu[j * 4 + 0] = (byte)math.clamp(color[0] + 0.5f, 0.5f, 255.5f);
                mcu[j * 4 + 1] = (byte)math.clamp(color[1] + 0.5f, 0.5f, 255.5f);
                mcu[j * 4 + 2] = (byte)math.clamp(color[2] + 0.5f, 0.5f, 255.5f);
            }
            
            uint outputWidth = endX - startX;
            uint outputHeight = endY - startY;

            const byte bytesPerPixel = 4;
            UnsafeUtility.MemCpyStride(
                imageData->pixels + (startX + startY * imageData->width) * bytesPerPixel,
                imageData->width * bytesPerPixel, 
                mcu, 
                8 * bytesPerPixel, 
                (int)outputWidth * bytesPerPixel,
                (int)outputHeight);
        }
    }
    
    private unsafe NativeArray<byte> Decode_444()
    {
        NativeArray<byte> output = new NativeArray<byte>(width * height * 4, Allocator.Persistent);
        
        fixed (byte* offsets = packedOffsets)
        fixed (uint* readPtr = bitstream)
        fixed (byte* symbolsDC = huffmanDC.symbols)
        fixed (byte* codeLengthsDC = huffmanDC.lengthCounts)
        fixed (byte* symbolsAC = huffmanAC.symbols)
        fixed (byte* codeLengthsAC = huffmanAC.lengthCounts)
        fixed (byte* quantAAN = lumaninceQuantTable)
        fixed (byte* quantAAN2 = chromaQuantTable)
        {
            RawImageData imageData = new RawImageData()
            {
                width = (ushort)this.width,
                height = (ushort)this.height,
                channels = 4,
                pixels = (byte*)output.GetUnsafePtr(),
            };
            
            ushort* codesDC = stackalloc ushort[12];
            HuffmanTable.GetCodes(codeLengthsDC, huffmanDC.longestCodeLength, codesDC);
            HuffmanStruct hfDC = new HuffmanStruct()
            {
                symbols = symbolsDC,
                codes = codesDC,
                lengthCounts = codeLengthsDC,
            };
            
            ushort* codesAC = stackalloc ushort[256];
            HuffmanTable.GetCodes(codeLengthsAC, huffmanAC.longestCodeLength, codesAC);
            HuffmanStruct hfAC = new HuffmanStruct()
            {
                symbols = symbolsAC,
                codes = codesAC,
                lengthCounts = codeLengthsAC,
            };

            if (huffmanAC.longestCodeLength <= 12)
            {
                byte[] dcDecodeLut = HuffmanTable.GetDCDecodingLUT(ref hfDC);
                ushort[] acDecodeLut = HuffmanTable.GetACDecodingLUT(ref hfAC);
                
                fixed (byte* dcLutPtr = dcDecodeLut)
                fixed (ushort* acLutPtr = acDecodeLut)
                    DecodeBytes_444_LUT(&imageData, readPtr, (uint)bitstream.Length, offsets, dcLutPtr, acLutPtr, quantAAN, quantAAN2, quantAAN2);
            }
            else
            {
                DecodeBytes_444(&imageData, readPtr, (uint)bitstream.Length, offsets, &hfDC, &hfAC, quantAAN, quantAAN2, quantAAN2);
            }
        }
        
        return output;
    }
}
