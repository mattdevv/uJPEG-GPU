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
    private static unsafe void FetchMCU_420(RawImageData* image, float* mcuDataOut, uint mcuIndex)
    {
        float* yuv = stackalloc float[3];
        
        // clear the chroma values
        UnsafeUtility.MemClear(mcuDataOut + (64 * 4), 64 * 2 * sizeof(float));
        
        uint numMCUsX = (uint)JpegHelpers.DivRoundUp(image->width, 2 * N);
        Vector2Int mcuCoord = new Vector2Int((int)(mcuIndex % numMCUsX), (int)(mcuIndex / numMCUsX));
        Vector2Int basePixelCoord = mcuCoord * 16;

        for (int q = 0; q < 4; q++)
        {
            int qX = q & 1;
            int qY = q >> 1;
            Vector2Int basePixelQuadrant = basePixelCoord + new Vector2Int(qX * 8, qY * 8);
            
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int outputIndex = x + y * N;
                    int chromaIndex = ((x + qX * 8) >> 1) + ((y + qY * 8) >> 1) * N;
                    
                    Vector2Int sampleCoord = basePixelQuadrant + new Vector2Int(x, y); 

                    if (sampleCoord.x >= image->width || sampleCoord.y >= image->height)
                    {
                        mcuDataOut[outputIndex + (q * N * N)] = 0;
                        mcuDataOut[chromaIndex + (4 * N * N)] += 0;
                        mcuDataOut[chromaIndex + (5 * N * N)] += 0;
                    }
                    else
                    {
                        yuv[0] = image->pixels[(sampleCoord.x + sampleCoord.y * image->width) * image->channels + 0];
                        yuv[1] = image->pixels[(sampleCoord.x + sampleCoord.y * image->width) * image->channels + 1];
                        yuv[2] = image->pixels[(sampleCoord.x + sampleCoord.y * image->width) * image->channels + 2];

                        JpegHelpers.RgbToYCbCr_LvlShift(yuv);

                        mcuDataOut[outputIndex + (q * N * N)] = yuv[0];
                        mcuDataOut[chromaIndex + (4 * N * N)] += yuv[1] * (1f / 4f);
                        mcuDataOut[chromaIndex + (5 * N * N)] += yuv[2] * (1f / 4f);
                    }
                }
            }
        }
    }
    
    [BurstCompile]
    private static unsafe void BuildHistograms_420(RawImageData* imageData, uint numMCUs, uint* DCHistogram, uint* ACHistogram, float* quant_L, float* quant_Cb, float* quant_Cr)
    {
        float* mcuPtr = stackalloc float[6 * N * N];
        
        short* tempSpacePtr = stackalloc short[64];
        
        short lastDC_Y  = 0;
        short lastDC_Cb = 0;
        short lastDC_Cr = 0;
        for (uint i = 0; i < numMCUs; i++)
        {
            FetchMCU_420(imageData, mcuPtr, i);
            
            // luminance
            for (int q = 0; q < 4; q++)
            {
                MCUBlock.Encode(mcuPtr + (q * N * N), tempSpacePtr, quant_L);
                AddBlockToHistogram(tempSpacePtr, DCHistogram, ACHistogram, &lastDC_Y);
            }
            lastDC_Y  = 0;
        
            // chroma blue
            MCUBlock.Encode(mcuPtr + (4 * N * N), tempSpacePtr, quant_Cb);
            AddBlockToHistogram(tempSpacePtr, DCHistogram, ACHistogram, &lastDC_Cb);
            lastDC_Cb = 0;
        
            // chroma red
            MCUBlock.Encode(mcuPtr + (5 * N * N), tempSpacePtr, quant_Cr);
            AddBlockToHistogram(tempSpacePtr, DCHistogram, ACHistogram, &lastDC_Cr);
            lastDC_Cr = 0;
            
        }
    }
    
    [BurstCompile]
    private static unsafe void EncodeToBitStream_420(RawImageData* imageData, uint numMCUs, byte* offsets, uint* writePtr, uint* writeIndex, uint* dcLUT, uint* acLUT, float* quant_L, float* quant_Cb, float* quant_Cr)
    {
        float* mcuPtr = stackalloc float[6 * N * N];
        
        byte* offsetStoreAddr = offsets;
        uint lastAbsolute = 0;
        
        int lastDC_Y  = 0;
        int lastDC_Cb = 0;
        int lastDC_Cr = 0;
        
        for (uint i = 0; i < numMCUs; i++)
        {
            FetchMCU_420(imageData, mcuPtr, i);
            
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

            // encode chroma blue
            EncodeBlockToStream(writePtr, writeIndex, mcuPtr + (4 * N * N), quant_Cb, &lastDC_Cb, dcLUT, acLUT);
            lastDC_Cb = 0;
            // encode chroma red
            EncodeBlockToStream(writePtr, writeIndex, mcuPtr + (5 * N * N), quant_Cr, &lastDC_Cr, dcLUT, acLUT);
            lastDC_Cr = 0;
            // encode 4x luminance
            for (int q = 0; q < 4; q++)
            {
                EncodeBlockToStream(writePtr, writeIndex, mcuPtr + (q * N * N), quant_L, &lastDC_Y, dcLUT, acLUT);
            }
            lastDC_Y  = 0;
        } 
    }
    
    private unsafe uint Encode_420(byte* rawPixelData, uint inputChannels, int width, int height, bool doOptimal)
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
        
        int numMCUsX = JpegHelpers.DivRoundUp(width, 2 * N);
        int numMCUsY = JpegHelpers.DivRoundUp(height, 2 * N);
        uint numMCUs = (uint)(numMCUsX * numMCUsY);
        
        // loop over all MCU's to build a histograms of the DC and AC symbols
        uint* DCHistogram = stackalloc uint[12];
        UnsafeUtility.MemClear(DCHistogram, 12 * sizeof(uint));
        uint* ACHistogram = stackalloc uint[256];
        UnsafeUtility.MemClear(ACHistogram, 256 * sizeof(uint));
        BuildHistograms_420(&imageData, numMCUs, DCHistogram, ACHistogram, quantAAN, quantAAN2, quantAAN2);

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
        
        exactBits = totalEncodedBits + (uint)(huffmanAC.encodedSize + huffmanDC.encodedSize);
        bitstream = new uint[(exactBits + 63ul) / 32ul]; // ensures later we can over-read by upto 32 bits by adding 1 extra element to array
        
        uint writeIndex = 0; // index of next bit to write at
        fixed (byte* offsets = packedOffsets)
        fixed (uint* ptr = bitstream)
        {
            // alias to reuse the memory
            uint* dcLUT = DCHistogram;
            huffmanDC.GetEncodingLUT(dcLUT);
            uint* acLUT = ACHistogram;
            huffmanAC.GetEncodingLUT(acLUT);
            
            EncodeToBitStream_420(&imageData, numMCUs, offsets, ptr, &writeIndex, dcLUT, acLUT, quantAAN, quantAAN2, quantAAN2);
        }

        return numMCUs;
    }
    
    [BurstCompile]
    private static unsafe void DecodeBytes_420(RawImageData* imageData, uint* readPtr, uint readLength, byte* offsets, 
        HuffmanStruct* huffmanDC, HuffmanStruct* huffmanAC, byte* quantTable_L, byte* quantTable_Cb, byte* quantTable_Cr)
    {
        uint numMCUsX = (uint)JpegHelpers.DivRoundUp(imageData->width, (N + N));
        uint numMCUsY = (uint)JpegHelpers.DivRoundUp(imageData->height, (N + N));
        uint numMCUs = numMCUsX * numMCUsY;
        
        float* tempSpacePtrF = stackalloc float[(N * N) * 6];
        byte* mcu = stackalloc byte[(N + N) * (N + N) * 4];
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

            UnsafeUtility.MemClear(tempSpacePtrF, 6 * N * N * sizeof(float));

            // load chroma blue (Cb)
            DecodeBlockFromStream(readPtr, &readOffsetBits, huffmanDC, huffmanAC, quantTable_Cb, &lastDC_Cb, tempSpacePtrF + (4 * N * N));
            lastDC_Cb = 0;

            // load chroma red (Cr)
            DecodeBlockFromStream(readPtr, &readOffsetBits, huffmanDC, huffmanAC, quantTable_Cr, &lastDC_Cr, tempSpacePtrF + (5 * N * N));
            lastDC_Cr = 0;

            // load luminance (Y)
            for (int q = 0; q < 4; q++)
            {
                DecodeBlockFromStream(readPtr, &readOffsetBits, huffmanDC, huffmanAC, quantTable_L, &lastDC_Y, tempSpacePtrF + (q * N * N));
            }
            lastDC_Y = 0;

            uint mcuX = i % numMCUsX;
            uint mcuY = i / numMCUsX;

            // output pixels AABB
            uint startX = mcuX * (2 * N);
            uint startY = mcuY * (2 * N);
            uint endX = startX + (2 * N);
            if (endX > imageData->width)
                endX = imageData->width;
            uint endY = startY + (2 * N);
            if (endY > imageData->height)
                endY = imageData->height;
            
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            {
                int lumaIndex = ((x & 8) * 8) + ((y & 8) * 16) + (x & 7) + ((y & 7) * 8);
                int chromaIndex = (x >> 1) + (y >> 1) * 8;
                color[0] = tempSpacePtrF[lumaIndex];
                color[1] = tempSpacePtrF[N*N*4 + chromaIndex];
                color[2] = tempSpacePtrF[N*N*5 + chromaIndex];
                
                JpegHelpers.YCbCrToRgb_LvlShift(color);
                
                mcu[(x + y * 16) * 4 + 0] = (byte)math.clamp(color[0] + 0.5f, 0.5f, 255.5f);
                mcu[(x + y * 16) * 4 + 1] = (byte)math.clamp(color[1] + 0.5f, 0.5f, 255.5f);
                mcu[(x + y * 16) * 4 + 2] = (byte)math.clamp(color[2] + 0.5f, 0.5f, 255.5f);
            }
            
            uint outputWidth = endX - startX;
            uint outputHeight = endY - startY;

            const byte bytesPerPixel = 4;
            UnsafeUtility.MemCpyStride(
                imageData->pixels + (startX + startY * imageData->width) * bytesPerPixel,
                imageData->width * bytesPerPixel, 
                mcu, 
                16 * bytesPerPixel, 
                (int)outputWidth * bytesPerPixel,
                (int)outputHeight);
        }
    }
    
    [BurstCompile]
    private static unsafe void DecodeBytes_420_LUT(RawImageData* imageData, uint* readPtr, uint readLength, byte* offsets, 
        byte* dcLUT, ushort* acLUT, byte* quantTable_L, byte* quantTable_Cb, byte* quantTable_Cr)
    {
        uint numMCUsX = (uint)JpegHelpers.DivRoundUp(imageData->width, 2 * N);
        uint numMCUsY = (uint)JpegHelpers.DivRoundUp(imageData->height, 2 * N);
        uint numMCUs = numMCUsX * numMCUsY;
        
        float* tempSpacePtrF = stackalloc float[6 * N * N];
        byte* mcu = stackalloc byte[16 * 16 * 4];
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

            UnsafeUtility.MemClear(tempSpacePtrF, 6 * N * N * sizeof(float));
            
            // load chroma blue (Cb)
            DecodeBlockFromStream_LUT(readPtr, &readOffsetBits, dcLUT, acLUT, quantTable_Cb, &lastDC_Cb, tempSpacePtrF + (4 * N * N));
            lastDC_Cb = 0;

            // load chroma red (Cr)
            DecodeBlockFromStream_LUT(readPtr, &readOffsetBits, dcLUT, acLUT, quantTable_Cr, &lastDC_Cr, tempSpacePtrF + (5 * N * N));
            lastDC_Cr = 0;

            // load luminance (Y)
            for (int q = 0; q < 4; q++)
            {
                DecodeBlockFromStream_LUT(readPtr, &readOffsetBits, dcLUT, acLUT, quantTable_L, &lastDC_Y, tempSpacePtrF + (q * N * N));
            }
            lastDC_Y = 0;

            uint mcuX = i % numMCUsX;
            uint mcuY = i / numMCUsX;

            // output pixels AABB
            uint startX = mcuX * (2 * N);
            uint startY = mcuY * (2 * N);
            uint endX = startX + (2 * N);
            if (endX > imageData->width)
                endX = imageData->width;
            uint endY = startY + (2 * N);
            if (endY > imageData->height)
                endY = imageData->height;
            
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            {
                int lumaIndex = ((x & 8) * 8) + ((y & 8) * 16) + (x & 7) + ((y & 7) * 8);
                int chromaIndex = (x >> 1) + (y >> 1) * 8;
                color[0] = tempSpacePtrF[lumaIndex];
                color[1] = tempSpacePtrF[N*N*4 + chromaIndex];
                color[2] = tempSpacePtrF[N*N*5 + chromaIndex];
                
                JpegHelpers.YCbCrToRgb_LvlShift(color);
                
                mcu[(x + y * 16) * 4 + 0] = (byte)math.clamp(color[0] + 0.5f, 0.5f, 255.5f);
                mcu[(x + y * 16) * 4 + 1] = (byte)math.clamp(color[1] + 0.5f, 0.5f, 255.5f);
                mcu[(x + y * 16) * 4 + 2] = (byte)math.clamp(color[2] + 0.5f, 0.5f, 255.5f);
            }
            
            uint outputWidth = endX - startX;
            uint outputHeight = endY - startY;

            const byte bytesPerPixel = 4;
            UnsafeUtility.MemCpyStride(
                imageData->pixels + (startX + startY * imageData->width) * bytesPerPixel,
                imageData->width * bytesPerPixel, 
                mcu, 
                16 * bytesPerPixel, 
                (int)outputWidth * bytesPerPixel,
                (int)outputHeight);
        }
    }
    
    private unsafe NativeArray<byte> Decode_420()
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
                    DecodeBytes_420_LUT(&imageData, readPtr, (uint)bitstream.Length, offsets, dcLutPtr, acLutPtr, quantAAN, quantAAN2, quantAAN2);
            }
            else
            {
                DecodeBytes_420(&imageData, readPtr, (uint)bitstream.Length, offsets, &hfDC, &hfAC, quantAAN, quantAAN2, quantAAN2);
            }
        }
        
        return output;
    }
}
