// WARNING, currently no safety around reading past end of buffer

using System;
using System.Runtime.CompilerServices;
using UnityEngine;

public static unsafe class BitStreamReaderUnsafe
{
    public static uint PeakUInt(uint* data, uint p)
    {
        uint readIndex = p >> 5;
        int offset = unchecked((int)(p & 31));
        
        // is potential unaligned read a problem?
        ulong data2 = *(ulong*)(data + readIndex);
        return (uint)(data2 >> offset);
    }
}

public unsafe class BitStreamReader
{
    private uint* data;
    public uint bitCount; // how many bits are possible to read
    public uint p; // read location (in bits)
    
    public BitStreamReader(uint* dataStart, uint bitCount)
    {
        this.data = dataStart;
        this.bitCount = bitCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MoveForward(uint count)
    {
        Shift(count);
    }

    public void Seek(uint position)
    {
        p = position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Shift(uint count)
    {
        p += count;
    }

    // pop the next bit off the stream
    public uint ReadBit()
    {
        uint result = PeakBit();
        Shift(1);
        return result;
    }
    
    // pop the next N bits off the stream
    public uint ReadBits(uint count)
    {
        uint result = PeakBits(count);
        Shift(count);
        return result;
    }

    // pop the next 32 bits off the stream
    public uint ReadUInt()
    {
        uint result = PeakUInt();
        Shift(32);
        return result;
    }

    // returns the next bit to be read but does not move the read offset
    public uint PeakBit()
    {
        uint readIndex = p >> 5;
        int offset = unchecked((int)(p & 31));
        
        return (data[readIndex] >> offset) & 1u;
    }
    
    // does not guarantee high bits are zeroed
    public uint PeakBitsUnsafe(uint bits)
    {
        Debug.Assert(bits <= 32);

        uint result;

        uint readIndex = p >> 5;
        uint offset = p & 31;

        // check if we need to read multiple uint's
        if (offset + bits > 32)
        {
            uint lowerBitCount = 32u - offset;

            result = (data[readIndex + 1] << (int)lowerBitCount) | (data[readIndex] >> (int)offset);
        }
        else
        {
            result = (data[readIndex] >> (int)offset);
        }

        return result;
    }

    public uint PeakBits(uint bits)
    {
        Debug.Assert(bits <= 32);

        uint result;

        uint readIndex = p >> 5;
        uint offset = p & 31;

        // check if we need to read multiple uint's
        if (offset + bits > 32)
        {
            uint lowerBitCount = 32u - offset;
            uint upperBitCount = bits - lowerBitCount;

            // clip low bits in lower
            uint lower = (data[readIndex] >> (int)offset);
            // mask off high bits in upper
            uint mask = unchecked((uint)((1ul << (int)upperBitCount) - 1ul));
            uint upper = data[readIndex + 1] & mask;

            result = (upper << (int)lowerBitCount) | lower;
        }
        else
        {
            // bit shift to clip low bits, use mask to remove high bits (had to use ulong in case bitCount=32)
            uint mask = unchecked((uint)((1ul << (int)bits) - 1ul));
            result = (data[readIndex] >> (int)offset) & mask;
        }

        return result;
    }

    public uint PeakUInt()
    {
        uint readIndex = p >> 5;
        uint offset = p & 31;
        
        // is potential unaligned read a problem?
        ulong data2 = *(ulong*)(data + readIndex);
        return (uint)(data2 >> (int)offset);
        
        /*uint result;

        uint readIndex = ptr >> 5;
        uint offset = ptr & 31;

        uint lowerBitCount = 32u - offset;
        uint upperBitCount = 32u - lowerBitCount;

        // clip low bits in lower
        uint lower = (data[readIndex] >> (int)offset);
        // mask off high bits in upper
        ulong upper = (ulong)data[readIndex + 1];

        result = (uint)(upper << (int)lowerBitCount) | lower;
        
        return result;*/
    }
}
