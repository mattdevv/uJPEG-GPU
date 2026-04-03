using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public unsafe struct JpegHeader
{
    public struct QuantizationTable {

        public fixed byte table[64];

        public void Fill(byte[] quant)
        {
            // rearrange for reading in the order ( i, i+32, (i+1), (i+1)+32, ... )
            //quant = JpegHelpers.PackBlockForShader(quant);
            
            // clone array
            fixed (byte* sourcePtr = quant)
                for (int i=0; i<64; i++)
                    table[i] = sourcePtr[i];
        }
    };

    public struct HuffmanTableDC
    {
        public fixed uint codes[12];

        public void Fill(HuffmanTable table)
        {
            int symbolCount = table.symbols.Length;
            
            // DC table should have at most 12 symbols
            Debug.Assert(symbolCount <= 12);
            
            fixed (byte* ptrLengthCounts = table.lengthCounts)
            {
                // build codes
                ushort* tempCodes = stackalloc ushort[symbolCount];
                HuffmanTable.GetCodes(ptrLengthCounts, table.longestCodeLength, tempCodes);

                // pack code info as AoS
                for (int i = 0; i < symbolCount; i++)
                {
                    codes[i] = tempCodes[i] | ((uint)table.symbols[i] << 16) | ((uint)table.GetCodeLength(i) << 24);
                }
            }
        }
    }

    public struct HuffmanTableAC
    {
        public fixed uint codes[256];
        
        public void Fill(HuffmanTable table)
        {
            int symbolCount = table.symbols.Length;
            
            // DC table should have at most 12 symbols
            Debug.Assert(symbolCount <= 256);
            
            fixed (byte* ptrLengthCounts = table.lengthCounts)
            {
                // build codes
                ushort* tempCodes = stackalloc ushort[symbolCount];
                HuffmanTable.GetCodes(ptrLengthCounts, table.longestCodeLength, tempCodes);

                // pack code info as AoS
                for (int i = 0; i < symbolCount; i++)
                {
                    codes[i] = (uint)tempCodes[i] | ((uint)table.symbols[i] << 16) | ((uint)table.GetCodeLength(i) << 24);
                }
            }
        }
    }
    
    public uint width;
    public uint height;
    public uint numMCUs;
    public uint totalBits;
    
    public QuantizationTable luminance;
    public QuantizationTable chroma;
    
    public HuffmanTableDC dcHuffmanTable;
    public HuffmanTableAC acHuffmanTable;

    public void Fill(JpegData jpeg)
    {
        // for safety zero the buffers
        fixed (void* ptr = &this)
            UnsafeUtility.MemSet(ptr, 0xFF, UnsafeUtility.SizeOf<JpegHeader>());
        
        width  = (uint)jpeg.width;
        height = (uint)jpeg.height;
        totalBits = jpeg.exactBits;
        numMCUs = jpeg.numMCUs;
        
        luminance.Fill(jpeg.lumaninceQuantTable);
        chroma.Fill(jpeg.chromaQuantTable);
        
        dcHuffmanTable.Fill(jpeg.huffmanDC);
        acHuffmanTable.Fill(jpeg.huffmanAC);
    }
};
