#ifndef INCLUDE_BITSTREAM_HLSL
#define INCLUDE_BITSTREAM_HLSL

/*struct BitStream
{
    ByteAddressBuffer buffer;
    uint bitOffset;

    void Setup(ByteAddressBuffer buf, uint startBitOffset)
    {
        buffer = buf;
        bitOffset = startBitOffset;
    }
    
    uint Peak(uint count) // count is ignored, 32 bits returned everytime
    {
        uint readIndex = bitOffset >> 5;
        uint offset = bitOffset & 31;

        uint lowerBitCount = 32u - offset;

        uint alignedRead = readIndex << 2;
    
        // clip low bits in lower word
        uint lower = buffer.Load(alignedRead) >> (int)offset;

        if (offset == 0)
            return lower;
    
        // clip high bits in upper word
        uint upper = buffer.Load(alignedRead + 4) << (int)lowerBitCount;
    
        return upper | lower;
    }

    void MoveForward(uint bits)
    {
        bitOffset += bits;
    }
};*/

struct BitStream
{
    ByteAddressBuffer buffer;
    uint bitPos;
    uint currentWord;
    uint nextWord;

    // Initialize state and perform the initial cache load
    void Setup(ByteAddressBuffer buf, uint startBitOffset)
    {
        buffer = buf;
        bitPos = startBitOffset;

        // Preload the cache
        uint byteAddress = (bitPos >> 5) << 2; 
        currentWord = buf.Load(byteAddress);
        nextWord = buf.Load(byteAddress + 4);
    }

    // Peek 0-32 bits entirely from the cached registers
    // returned bits has N or greater bits (don't assume high bits were masked off)
    uint Peak(uint numBits) 
    {
        uint byteOffset = bitPos & 31;
        if (byteOffset + numBits <= 32)
        {
            // The requested bits live entirely inside our current cached word
            return (currentWord >> byteOffset);
        }
        
        // The bits span across the boundary of currentWord and nextWord
        uint lowPart = currentWord >> byteOffset;
        uint highPart = nextWord << (32 - byteOffset); 

        return lowPart | highPart;
    }

    // Advance 0-32 bits, only loading from the buffer when a boundary is crossed
    void MoveForward(uint numBits)
    {
        uint byteOffset = bitPos & 31;
        bitPos += numBits;

        // check if we have crossed
        if (byteOffset + numBits >= 32)
        {
            // Shift the cache window forward
            currentWord = nextWord;
            
            // Perform the ONLY buffer load required
            uint byteAddress = (bitPos >> 5) << 2; 
            nextWord = buffer.Load(byteAddress + 4);
        }
    }

    // Convenience function
    uint Read(uint numBits)
    {
        uint result = Peak(numBits);
        MoveForward(numBits);
        return result;
    }
};

#endif