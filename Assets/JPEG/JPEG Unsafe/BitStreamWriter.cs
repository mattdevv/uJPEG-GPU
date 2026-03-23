using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public static unsafe class BitStreamWriterUnsafe
{
    // push upto 32 bits to the end of the stream
    public static void Push(uint* buffer, uint* bufferPtr, uint value, [AssumeRange(0ul, 32ul)] uint bitCount)
    {
        Debug.Assert(bitCount <= 32);
        
        uint writeIndex = *bufferPtr >> 5;
        uint offset = *bufferPtr & 31; // offset to start write at within uint
        
        uint mask = (1u << unchecked((int)offset)) - 1;
        uint old = buffer[writeIndex] & mask;
        uint @new = value << unchecked((int)offset);
        buffer[writeIndex] = old | @new;
        
        // check if bits need to overflow into next uint
        if (offset + bitCount > 32u) 
        {
            buffer[writeIndex+1] = value >> (32 - unchecked((int)offset));
        }

        *bufferPtr += bitCount;
    }
}

[BurstCompile]
public struct BitStreamWriter
{
    public uint[] bits;
    public uint p; // pointer to next bit index

    public BitStreamWriter(uint requiredBits)
    {
        uint requiredUInts = (requiredBits + 31) >> 5;
        bits = new uint[Mathf.Max((int)requiredUInts, 4)];
        p = 0;
    }
    
    public BitStreamWriter(uint[] bits) : this(bits, (uint)bits.Length * 32u) { }
    
    public BitStreamWriter(uint[] bits, uint bitCount)
    {
        this.bits = new uint[bits.Length];
        Array.Copy(bits, this.bits, bits.Length);
        p = bitCount;
    }

    // check that the BitStream has enough remaining space for adding N bits
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(uint N)
    {
        // how many uints are needed vs how many are allocated
        uint requiredSize = (p + N + 31) >> 5; // divide by 32 and round up 
        uint currentSize = (uint)bits.Length;

        if (currentSize >= requiredSize)
            return;

        // double proposed size until can fit the required size
        do { currentSize <<= 2; } 
        while (currentSize < requiredSize);
        
        Array.Resize(ref bits, (int)currentSize);
        Debug.Log("Had to resize");
    }

    // push a single bit to the end of the stream
    public void Push(bool bit)
    {
        EnsureCapacity(1);
        
        uint writeIndex = p >> 5;
        uint offset = p & 31;
        
        uint mask = (1u << (int)offset) - 1;
        uint old = bits[writeIndex] & mask;
        uint @new = (bit ? 1u : 0u) << (int)offset;
        
        bits[writeIndex] = old | @new;
        
        Shift(1);
    }

    // push a value with 32 or less bits to the end of the stream
    public void Push(uint value, uint bitCount)
    {
        Debug.Assert(bitCount <= 32);
        
        EnsureCapacity(bitCount);
        
        uint writeIndex = p >> 5;
        uint offset = p & 31; // offset to start write at within uint
        
        uint mask = (1u << unchecked((int)offset)) - 1;
        uint old = bits[writeIndex] & mask;
        uint @new = value << unchecked((int)offset);
        bits[writeIndex] = old | @new;
        
        // check if bits need to overflow into next uint
        if (offset + bitCount > 32u) 
        {
            bits[writeIndex+1] = value >> (32 - unchecked((int)offset));
        }

        Shift(bitCount);
    }
    
    // push a value with exactly 32 bits to the end of the stream
    public void Push(uint value)
    {
        EnsureCapacity(32);
        
        uint writeIndex = p >> 5;
        uint offset = p & 31; // offset to start write at within uint
        
        uint mask = (1u << unchecked((int)offset)) - 1;
        uint old = bits[writeIndex] & mask;
        uint @new = value << unchecked((int)offset);
        bits[writeIndex] = old | @new;
        
        // check if bits need to overflow into next uint
        if (offset > 0) 
        {
            bits[writeIndex+1] = value >> (32 - unchecked((int)offset));
        }

        Shift(32);
    }

    // push an entire unmanaged object to the end of the stream
    public unsafe void Push<T>(T value) where T : unmanaged
    {
        Push(&value, (uint)sizeof(T));
    }

    // push an amount of bytes to the end of the stream
    private unsafe void Push(void* ptr, uint byteCount)
    {
        EnsureCapacity(byteCount * 8);
        
        byte* bytePtr = (byte*)ptr;

        for (int i = 0; i < byteCount; i++)
        {
            Push(bytePtr[i]);
        }
    }

    // push a full byte to the end of the stream
    private void Push(byte value)
    {
        uint writeIndex = p >> 5;
        uint offset = p & 31; // offset to write at within uint
        
        uint mask = (1u << (int)offset) - 1;
        uint old = bits[writeIndex] & mask;
        uint @new = (uint)value << (int)offset;
        bits[writeIndex] = old | @new;
        
        // check if bits need to overflow into next uint
        if (offset + 8u > 32u)
        {
            bits[writeIndex+1] = (uint)value >> (int)(32 - offset);
        }

        Shift(8);
    }

    // Move the write index to the start of the next integer (4 bytes)
    public void RoundOffsetToInt()
    {
#if UNITY_EDITOR
        Shift(31 - (p & 0b11111u));
#else
        p = (p + 31) & (~0b11111u);
#endif
    }

    // Move the write index to the start of the next short (2 bytes)
    public void RoundOffsetToShort()
    {
#if UNITY_EDITOR
        Shift(15 - (p & 0b1111u));
#else
        p = (p + 15) & (~0b1111u);
#endif
    }

    // Move the write index to the start of the next byte
    public void RoundOffsetToByte()
    {
#if UNITY_EDITOR
        Shift(7 - (p & 0b111u));
#else
        p = (p + 7) & (~0b111u);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Shift(uint bits)
    {
        // it's a problem if we won't be able to index the new bits due to overflow
        Debug.Assert(p <= uint.MaxValue - bits);
        
        p += bits;
    }

    public string ToString()
    {
        char[] chars = new char[p];

        for (uint i = 0; i < p; i++)
        {
            uint blockIndex = i >> 5; // find which uint to read
            uint bitIndex = i & 31u; 
            uint bit = (bits[blockIndex] >> (int)bitIndex) & 1u; // check if the bit is set
            
            chars[i] = (char)('0' + bit);
        }
        
        return new string(chars);
    }
}
