#ifndef INCLUDE_JPEG_UTILS_HLSL
#define INCLUDE_JPEG_UTILS_HLSL

#include "JpegHeader.cs.hlsl"
#include "BitStream.hlsl"

#define EOB 0

ByteAddressBuffer _jpegData;
groupshared int mcuBlockData[64];

uint DivRoundUp(uint dividend, uint divisor)
{
    return (dividend + divisor - 1) / divisor;
}

int DecodeValue(uint encoded, uint size)
{
    if (asint(encoded) >= (1 << (asint(size) - 1))) // check if MSB is set
        return asint(encoded);
        
    return asint(encoded) - ((1 << asint(size)) - 1);
}

float3 RgbToYCbCr(float3 rgb)
{
    float Y  =  0.299f * rgb.r + 0.587f * rgb.g + 0.114f * rgb.b + 0;
    float Cb = -0.169f * rgb.r - 0.331f * rgb.g + 0.500f * rgb.b + 0.5;
    float Cr =  0.500f * rgb.r - 0.419f * rgb.g - 0.081f * rgb.b + 0.5;
        
    return float3(Y, Cb, Cr);
}

float3 YCbCrToRgb_LvlShift(float3 yCbCr)
{
    float Y  = yCbCr.r * (1. / 255.);
    float Cb = yCbCr.g * (1. / 255.);
    float Cr = yCbCr.b * (1. / 255.);

    float r = 0.5 + Y + Cr * +1.402f;
    float g = 0.5 + Y + Cb * -0.344136f + Cr * -0.714136f;
    float b = 0.5 + Y + Cb * +1.772f;
        
    return float3(r, g, b);
}

void YCbCrToRgb_LvlShift(float2 luminance, float2 CbCr, out float3 rgb1, out float3 rgb2)
{
    luminance *= 1. / 255.;
    
    float Cb = CbCr[0];
    float Cr = CbCr[1];

    float r = 0.5 + Cr * (+1.402 / 255.);
    float g = 0.5 + Cb * (-0.344136 / 255.) + Cr * (-0.714136f / 255.) ;
    float b = 0.5 + Cb * (+1.772 / 255.);

    rgb1 = luminance.x + float3(r, g, b);
    rgb2 = luminance.y + float3(r, g, b);
}

void CUDAsubroutineInplaceIDCTvector(uint base, uint step)
{
    const float C_a    = 1.387039845322148f;  //!< a = (2^0.5) * cos(    pi / 16);  Used in forward and inverse DCT.
    const float C_b    = 1.306562964876377f;  //!< b = (2^0.5) * cos(    pi /  8);  Used in forward and inverse DCT.
    const float C_c    = 1.175875602419359f;  //!< c = (2^0.5) * cos(3 * pi / 16);  Used in forward and inverse DCT.
    const float C_d    = 0.785694958387102f;  //!< d = (2^0.5) * cos(5 * pi / 16);  Used in forward and inverse DCT.
    const float C_e    = 0.541196100146197f;  //!< e = (2^0.5) * cos(3 * pi /  8);  Used in forward and inverse DCT.
    const float C_f    = 0.275899379282943f;  //!< f = (2^0.5) * cos(7 * pi / 16);  Used in forward and inverse DCT.
    const float C_norm = 0.3535533905932737f; // 1 / (8^0.5)
    
    float s0 = asfloat(mcuBlockData[base + step * 0]);
    float s1 = asfloat(mcuBlockData[base + step * 1]);
    float s2 = asfloat(mcuBlockData[base + step * 2]);
    float s3 = asfloat(mcuBlockData[base + step * 3]);
    float s4 = asfloat(mcuBlockData[base + step * 4]);
    float s5 = asfloat(mcuBlockData[base + step * 5]);
    float s6 = asfloat(mcuBlockData[base + step * 6]);
    float s7 = asfloat(mcuBlockData[base + step * 7]);

	float Y04P = s0 + s4;
	float Y2b6eP = C_b * s2 + C_e * s6;

	float Y04P2b6ePP = Y04P + Y2b6eP;
	float Y04P2b6ePM = Y04P - Y2b6eP;
	float Y7f1aP3c5dPP = C_f * s7 + C_a * s1 + C_c * s3 + C_d * s5;
	float Y7a1fM3d5cMP = C_a * s7 - C_f * s1 + C_d * s3 - C_c * s5;

	float Y04M = s0 - s4;
	float Y2e6bM = C_e * s2 - C_b * s6;

	float Y04M2e6bMP = Y04M + Y2e6bM;
	float Y04M2e6bMM = Y04M - Y2e6bM;
	float Y1c7dM3f5aPM = C_c * s1 - C_d * s7 - C_f * s3 - C_a * s5;
	float Y1d7cP3a5fMM = C_d * s1 + C_c * s7 - C_a * s3 + C_f * s5;

	mcuBlockData[base + step * 0] = asint(C_norm * (Y04P2b6ePP + Y7f1aP3c5dPP));
	mcuBlockData[base + step * 7] = asint(C_norm * (Y04P2b6ePP - Y7f1aP3c5dPP));
	mcuBlockData[base + step * 4] = asint(C_norm * (Y04P2b6ePM + Y7a1fM3d5cMP));
	mcuBlockData[base + step * 3] = asint(C_norm * (Y04P2b6ePM - Y7a1fM3d5cMP));
	mcuBlockData[base + step * 1] = asint(C_norm * (Y04M2e6bMP + Y1c7dM3f5aPM));
	mcuBlockData[base + step * 5] = asint(C_norm * (Y04M2e6bMM - Y1d7cP3a5fMM));
	mcuBlockData[base + step * 2] = asint(C_norm * (Y04M2e6bMM + Y1d7cP3a5fMM));
	mcuBlockData[base + step * 6] = asint(C_norm * (Y04M2e6bMP - Y1c7dM3f5aPM));
}

void idct8x8_optimized(uint lane)
{
    if (lane < 8)
    {
        // IDCT: columns
        CUDAsubroutineInplaceIDCTvector(lane, 8);
        // IDCT: rows
        CUDAsubroutineInplaceIDCTvector(lane * 8, 1);
    }
}

// assumes ushort is in low or high 16-bits of a 32-bit aligned read
uint LoadUShort(ByteAddressBuffer buffer, uint byteOffset)
{
    uint alignedOffset = byteOffset & ~3;
    uint shift = (byteOffset & 2u) * 8;
    return (buffer.Load(alignedOffset) >> shift) & 0xFFFF;
}

const static uint ZigZagLUT[64] =
{
     0,  1,  5,  6, 14, 15, 27, 28,
     2,  4,  7, 13, 16, 26, 29, 42,
     3,  8, 12, 17, 25, 30, 41, 43,
     9, 11, 18, 24, 31, 40, 44, 53,
    10, 19, 23, 32, 39, 45, 52, 54,
    20, 22, 33, 38, 46, 51, 55, 60,
    21, 34, 37, 47, 50, 56, 59, 61,
    35, 36, 48, 49, 57, 58, 62, 63,
};

const static uint InvZigZagLUT[64] =
{
    0,   1,  8, 16,  9,  2,  3, 10,
    17, 24, 32, 25, 18, 11,  4,  5,
    12, 19, 26, 33, 40, 48, 41, 34,
    27, 20, 13,  6,  7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36,
    29, 22, 15, 23, 30, 37, 44, 51,
    58, 59, 52, 45, 38, 31, 39, 46,
    53, 60, 61, 54, 47, 55, 62, 63,
};

// precomputed packed table of zigzag indices
// each value holds 1 byte for [i] [i+32] [(i+1)] [(i+1)+32]
const static uint ZigZagPacked[16] =
{
    318835200, 537270021,
    755967758, 907818011,
    369366018, 638394631,
    857353744, 1009399581,
    570955011, 789652748,
    941503001, 1026243369,
    604709641, 823668754,
    975714591, 1060453932, 
};

uint2 UnpackZigZag(uint warpID)
{
    uint packed = ZigZagPacked[warpID >> 1];
    packed >>= (warpID & 1) * 16;
    return uint2(packed & 0xFF, packed >> 8);
}

// each thread will output sequential 2 pixels
uint2 GetBlockIndices(uint warpID)
{
    return uint2(
        warpID * 2 + 0,
        warpID * 2 + 1);
}

void UndoZigZagQuantize(uint warpID, QuantizationTable quantTable)
{
    //uint2 outputIndices = uint2(warpID * 2, warpID * 2 + 1);
    uint2 outputIndices = uint2(warpID, warpID + 32); // this processing order allows starting IDCT without group sync
    //uint2 outputIndices = GetBlockIndices(warpID);

    int2 quants = asint(quantTable.GetPairAt(outputIndices.x));
    uint2 ZigZagIndex = UnpackZigZag(warpID);
    
    uint A = asuint(mcuBlockData[ZigZagIndex.x] * quants.x);
    uint B = asuint(mcuBlockData[ZigZagIndex.y] * quants.y);

    mcuBlockData[outputIndices.x] = asint((float)asint(A));
    mcuBlockData[outputIndices.y] = asint((float)asint(B));
}

// calculates and loads the offset to start reading an MCU at
uint GetBitOffsetMCU(uint mcuIndex)
{
    uint divide = mcuIndex / 9;
    uint modulo = mcuIndex % 9;

    // 20 bytes is 1 uint + 8 ushort
    uint offsetFull = divide * 20;
    uint fullBitOffset = _jpegData.Load(offsetFull);
    
    if (modulo == 0)
        return fullBitOffset;

    // if not a multiple of 9, need to also read an offset from the last 9th value
    uint offsetRelative = offsetFull + 4 + (modulo - 1) * 2;
    uint relativeBitOffset = LoadUShort(_jpegData, offsetRelative); // mask to be ushort
    return fullBitOffset + relativeBitOffset;
}

// returns how many bits were read
// the dc value is output through 'lastDC'
uint DecodeDC(uint warpID, HuffmanTableDC hfTable, uint nextBits32, out uint decodedSymbol)
{
    // default will always be invalid
    uint codeToCheck = 0xFFFFFFFF; 
    uint codeLength = 31;

    // dc has max 12 symbols, so limit which threads load symbols
    if (warpID < 12) 
    {
        uint packed = hfTable.codes[warpID];
        codeToCheck = packed;
        codeLength = (packed >> 24);
    }

    // check masked bits against a unique code
    uint codeMask = (1 << codeLength) - 1;
    bool foundMatch = (codeToCheck & codeMask) == (nextBits32 & codeMask);

    // only winner broadcasts it's ID
    // if a code above the maximum huffman symbol count wins, it will also be 0xFFFFFFFF as we default all codes to that
    uint winnerCode = WaveActiveMin(foundMatch ? codeToCheck : 0xFFFFFFFF); 
    if (winnerCode != 0xFFFFFFFF)
    {
        decodedSymbol = (winnerCode >> 16) & 0xFF;
        codeLength = (winnerCode >> 24) & 0xFF;
        
        // return how many bits we have read
        return codeLength;
    }

    decodedSymbol = EOB;
    return 0;
}

// returns how many bits were read
// the ac symbol is returned as an argument
uint DecodeAC(uint warpID, HuffmanTableAC hfTable, uint nextBits32, out uint decodedSymbol)
{
    // loop over the 256 possible codes, check them in groups the size of the wave
    for (int i=0; i < 256; i += WaveGetLaneCount())
    {
        uint symbolIndex = i + warpID;
        
        uint packed = hfTable.codes[symbolIndex];
        uint codeLength = (packed >> 24);
        
        // check masked bits against a unique code
        uint codeMask = (1 << codeLength) - 1;
        bool foundMatch = (packed & codeMask) == (nextBits32 & codeMask);

        // only winner broadcasts it's ID
        // if a code above the maximum huffman symbol count wins, it will also be 0xFFFFFFFF as we default all codes to that
        uint winnerCode = WaveActiveMin(foundMatch ? packed : 0xFFFFFFFF); 
        if (winnerCode != 0xFFFFFFFF)
        {
            decodedSymbol = (winnerCode >> 16) & 0xFF;
            codeLength = (winnerCode >> 24) & 0xFF;
            return codeLength;
        }
    }
    
    // no match was found, end the block with all zeros
    decodedSymbol = EOB;
    return 0;
}

void DecodeBlock(uint warpID, inout BitStream stream, HuffmanTableDC tableDC, HuffmanTableAC tableAC, QuantizationTable quantTable, inout int lastDC)
{
    uint2 outputIndices = GetBlockIndices(warpID);
    
    // reset groupshared memory
    mcuBlockData[warpID * 2 + 0] = 0;
    mcuBlockData[warpID * 2 + 1] = 0;

    // decode the DC values
    {
        uint nextBits = stream.Peak(6 + 12);
        
        uint dcSymbol;
        uint dcCodeLength = DecodeDC(warpID, tableDC, nextBits, dcSymbol);
        uint encodedBitCount = dcSymbol;
        stream.MoveForward(dcCodeLength + encodedBitCount);
        
        uint mask = (1u << encodedBitCount) - 1u;
        uint encoded = (nextBits >> dcCodeLength) & mask;
        int valueDC = DecodeValue(encoded, encodedBitCount);
        lastDC += valueDC; // value is a delta from last DC

        // only 1 thread needs to write this value
        if (warpID == 0)
            mcuBlockData[0] = lastDC;
    }

    // decode the AC values
    for (uint i=1; i<64; i++)
    {
        uint nextBits = stream.Peak(16 + 11);
        
        uint acSymbol;
        uint acCodeLength = DecodeAC(warpID, tableAC, nextBits, acSymbol);
        uint encodedBitCount = acSymbol & 0x0F;
        stream.MoveForward(acCodeLength + encodedBitCount);
        
        // check if we have read an end-of-block symbol and can early-exit
        if (acSymbol == EOB)
            break;
        
        // jump slots which should be zeros before the next value
        uint zerosRun = acSymbol >> 4;
        i += zerosRun;
        
        // decode this value
        uint mask = (1u << encodedBitCount) - 1u;
        uint encoded = (nextBits >> acCodeLength) & mask;
        uint valueAC = DecodeValue(encoded, encodedBitCount);
        
        // only 1 thread needs to write this value
        if (warpID == 0)
            mcuBlockData[i] = valueAC;
    }
    
    //time to decode the mcu block, dequantize & zigzag, then undo DCT
    UndoZigZagQuantize(warpID, quantTable);
    idct8x8_optimized(warpID);
}

void DecodeMCU_444(uint warpID, uint mcuIndex, JpegHeader jpegInfo, RWTexture2D<float4> output)
{    
    uint bitOffset = GetBitOffsetMCU(mcuIndex);
    BitStream stream;
    stream.Setup(_jpegData, bitOffset);

    uint2 outputIndices = GetBlockIndices(warpID);
    float4 colorCache = 0;
    
    // decode luminance
    int lastDC_Y = 0;
    DecodeBlock(warpID, stream, jpegInfo.dcHuffmanTable, jpegInfo.acHuffmanTable, jpegInfo.luminanceQuant, lastDC_Y);
    colorCache.xz = asfloat(int2(mcuBlockData[outputIndices.x], mcuBlockData[outputIndices.y]));
    // decode chroma blue
    int lastDC_cB = 0;
    DecodeBlock(warpID, stream, jpegInfo.dcHuffmanTable, jpegInfo.acHuffmanTable, jpegInfo.chromaQuant, lastDC_cB);
    colorCache.yw = asfloat(int2(mcuBlockData[outputIndices.x], mcuBlockData[outputIndices.y]));
    // decode chroma red
    int lastDC_cR = 0;
    DecodeBlock(warpID, stream, jpegInfo.dcHuffmanTable, jpegInfo.acHuffmanTable, jpegInfo.chromaQuant, lastDC_cR);
    
    // figure out output coord on texture
    uint2 resolution = uint2(jpegInfo.width, jpegInfo.height);
    uint numMCUsX = DivRoundUp(resolution.x, 8);
    //uint numMCUsY = DivRoundUp(resolution.y, 8);
    uint mcuX = mcuIndex % numMCUsX;
    uint mcuY = mcuIndex / numMCUsX;

    uint4 blockCoord = uint4(outputIndices.x % 8, outputIndices.x / 8, outputIndices.y % 8, outputIndices.y / 8);
    uint4 outputCoord = (uint2(mcuX, mcuY) * 8).xyxy + blockCoord;
    
    // first pixel
    float3 result = float3(colorCache.xy, asfloat(mcuBlockData[outputIndices.x]));
    if (all(outputCoord.xy < resolution))
        output[outputCoord.xy] = float4(YCbCrToRgb_LvlShift(result), 1);

    // second pixel
    result = float3(colorCache.zw, asfloat(mcuBlockData[outputIndices.y]));
    if (all(outputCoord.zw < resolution))
        output[outputCoord.zw] = float4(YCbCrToRgb_LvlShift(result), 1);
}

// for YUV 420
// maps a coordinate within a 8x8 block quadrant to a 4x4 down sampled block
uint MapSequenceIndex(uint blockIndex, uint quadrant)
{
    // i bit 1 -> y bit 4 (value 16)
    // i bit 0 -> y bit 1 (value 2)
    // x bits 5,4,2 -> y bits 3,2,0 (values 8, 4, 1)
    return ((quadrant & 2u) << 3u) | ((quadrant & 1u) << 1u) | ((blockIndex & 0x34u) >> 2u);
}

void DecodeMCU_420(uint warpID, uint mcuIndex, JpegHeader jpegInfo, RWTexture2D<float4> output)
{    
    uint bitOffset = GetBitOffsetMCU(mcuIndex);
    BitStream stream;
    stream.Setup(_jpegData, bitOffset);

    uint2 outputIndices = GetBlockIndices(warpID);

    // DC value might be stored as a delta, so store DCs between blocks
    int lastDC_Y = 0;
    int lastDC_cB = 0;
    int lastDC_cR = 0;
    
    float4 chromaCbCr = 0;
    
    // decode chroma blue
    DecodeBlock(warpID, stream, jpegInfo.dcHuffmanTable, jpegInfo.acHuffmanTable, jpegInfo.chromaQuant, lastDC_cB);
    chromaCbCr.xz = asfloat(int2(mcuBlockData[outputIndices.x], mcuBlockData[outputIndices.y]));
    // decode chroma red
    DecodeBlock(warpID, stream, jpegInfo.dcHuffmanTable, jpegInfo.acHuffmanTable, jpegInfo.chromaQuant, lastDC_cR);
    chromaCbCr.yw = asfloat(int2(mcuBlockData[outputIndices.x], mcuBlockData[outputIndices.y]));
    
    // figure out output coord on texture
    uint2 resolution = uint2(jpegInfo.width, jpegInfo.height);
    uint numMCUsX = DivRoundUp(resolution.x, 16);
    //uint numMCUsY = DivRoundUp(resolution.y, 16);
    uint mcuX = mcuIndex % numMCUsX;
    uint mcuY = mcuIndex / numMCUsX;

    uint4 blockCoord = (uint2(mcuX, mcuY) * 16).xyxy + uint4(outputIndices.x % 8, outputIndices.x / 8, outputIndices.y % 8, outputIndices.y / 8);
    uint downsampledIndex = MapSequenceIndex(outputIndices.x, 0);
    
    // loop and decode 4x luminance quadrants
    [UNITY_UNROLL]
    for (int q = 0; q < 4; q++)
    {
        // decode luminance
        DecodeBlock(warpID, stream, jpegInfo.dcHuffmanTable, jpegInfo.acHuffmanTable, jpegInfo.luminanceQuant, lastDC_Y);
        
        // if statements will be compiled away when unrolling
        if (q == 0) blockCoord += uint2( 0, 0).xyxy; // bottom left
        if (q == 1) blockCoord += uint2( 8, 0).xyxy; // bottom right
        if (q == 2) blockCoord += uint2(-8, 8).xyxy; // top left
        if (q == 3) blockCoord += uint2( 8, 0).xyxy; // top right
        
        if (q == 0) downsampledIndex += 0;
        if (q == 1) downsampledIndex += 2;
        if (q == 2) downsampledIndex += 14;
        if (q == 3) downsampledIndex += 2;
        
        // each thread holds 4 samples (2 pixels chroma blue/red)
        // use intrinsics to exchange the correct samples between threads
        // each loop (q) exports a 2x1 pixel grid, these samples cover 2x2 and are shared for both
        float4 writeCbCr = WaveReadLaneAt(chromaCbCr, downsampledIndex);
        float2 CbCr = (warpID & 1) ? writeCbCr.zw : writeCbCr.xy; // will receive 2 pixels, decide which we need
        
        float3 rgb1, rgb2;
        YCbCrToRgb_LvlShift(asfloat(int2(mcuBlockData[outputIndices.x], mcuBlockData[outputIndices.x + 1])), CbCr, rgb1, rgb2);
        
        // first pixel
        if (all(blockCoord.xy < resolution))
            output[blockCoord.xy] = float4(rgb1, 1);
        // second pixel
        if (all(blockCoord.zw < resolution))
            output[blockCoord.zw] = float4(rgb2, 1);
    }
}

void DecodeMCU_BW(uint warpID, uint mcuIndex, JpegHeader jpegInfo, RWTexture2D<float4> output)
{    
    uint bitOffset = GetBitOffsetMCU(mcuIndex);
    BitStream stream;
    stream.Setup(_jpegData, bitOffset);

    uint2 outputIndices = GetBlockIndices(warpID);
    
    // decode luminance
    int lastDC_Y = 0;
    DecodeBlock(warpID, stream, jpegInfo.dcHuffmanTable, jpegInfo.acHuffmanTable, jpegInfo.luminanceQuant, lastDC_Y);
    float2 luminances = asfloat(int2(mcuBlockData[outputIndices.x], mcuBlockData[outputIndices.y]));
    
    // figure out output coord on texture
    uint2 resolution = uint2(jpegInfo.width, jpegInfo.height);
    uint numMCUsX = DivRoundUp(resolution.x, 8);
    uint numMCUsY = DivRoundUp(resolution.y, 8);
    uint mcuX = mcuIndex % numMCUsX;
    uint mcuY = mcuIndex / numMCUsX;

    uint4 blockCoord = uint4(outputIndices.x % 8, outputIndices.x / 8, outputIndices.y % 8, outputIndices.y / 8);
    uint4 outputCoord = (uint2(mcuX, mcuY) * 8).xyxy + blockCoord;
    
    // first pixel
    if (all(outputCoord.xy < resolution))
        output[outputCoord.xy] = luminances.x;

    // second pixel
    if (all(outputCoord.zw < resolution))
        output[outputCoord.zw] = luminances.y;
}

#endif
