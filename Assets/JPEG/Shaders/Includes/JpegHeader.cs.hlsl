#ifndef INCLUDE_JPEG_HEADER_CS_HLSL
#define INCLUDE_JPEG_HEADER_CS_HLSL

struct QuantizationTable {
    uint table[16];

    uint GetAt(uint index)
    {
        uint word = table[index >> 2];
        uint shift = (index & 0x3) << 3;
        uint mask = 0xFF;
        
        return word >> shift & mask;
    }

    uint2 GetPairAt(uint index)
    {
        uint word = table[index >> 2];
        uint shift = (index & 0x3) << 3;
        uint mask = 0xFF;

        return (uint2(word, word) >> uint2(shift, shift + 8)) & mask;
    }
};

struct HuffmanTableDC
{
    uint codes[12];
};

struct HuffmanTableAC
{
    uint codes[256];
};

struct JpegHeader
{
    /* uint         */ uint width;
    /* uint         */ uint height;
    /* uint         */ uint numMCUs;
    /* uint         */ uint totalBits;
    
    /* byte   [64]  */ QuantizationTable luminanceQuant;
    /* byte   [64]  */ QuantizationTable chromaQuant;
    
                       HuffmanTableDC dcHuffmanTable;
                       HuffmanTableAC acHuffmanTable;
};

StructuredBuffer<JpegHeader> jpegHeader;

#endif