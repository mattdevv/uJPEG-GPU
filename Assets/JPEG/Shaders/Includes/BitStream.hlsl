#ifndef INCLUDE_BITSTREAM_HLSL
#define INCLUDE_BITSTREAM_HLSL

struct BitStream
{
    ByteAddressBuffer stream;
    uint bitOffset;
    
    uint PeakUInt()
    {
        uint readIndex = bitOffset >> 5;
        uint offset = bitOffset & 31;

        uint lowerBitCount = 32u - offset;

        uint alignedRead = readIndex << 2;
    
        // clip low bits in lower word
        uint lower = stream.Load(alignedRead) >> (int)offset;

        if (offset == 0)
            return lower;
    
        // clip high bits in upper word
        uint upper = stream.Load(alignedRead + 4) << (int)lowerBitCount;
    
        return upper | lower;
    }

    void MoveForward(uint bits)
    {
        bitOffset += bits;
    }
};

#endif