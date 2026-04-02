using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

/*public unsafe struct JpegHeader
{
    public struct QuantizationTable {

        public fixed byte table[64];

        public void Fill(byte[] quant)
        {
            // clone array
            fixed (byte* sourcePtr = quant)
                for (int i=0; i<64; i++)
                    table[i] = sourcePtr[i];
        }
    };
    
    public struct HuffmanTableDC {
        public fixed ushort codes[16];
        public fixed byte symbols[16];
        public fixed byte lengths[16]; // histogram of lengths
        public fixed byte codeLengths[16];

        public void Fill(HuffmanTable table)
        {
            uint symbolCount = (uint)table.symbols.Length;
            
            // DC table should have at most 12 symbols
            Debug.Assert(symbolCount <= 12);
            
            // for safety zero the buffers
            fixed (ushort* codesPtr = codes) 
            fixed (byte* symbolsPtr = symbols) 
            fixed (byte* lengthsPtr = lengths) 
            fixed (byte* codeLengthsPtr = codeLengths) 
            {
                UnsafeUtility.MemSet(codesPtr, 0, sizeof(ushort) * 16);
                UnsafeUtility.MemSet(symbolsPtr, 0, sizeof(byte) * 16);
                UnsafeUtility.MemSet(lengthsPtr, 0, sizeof(byte) * 16);
                UnsafeUtility.MemSet(codeLengthsPtr, 0, sizeof(byte) * 16);
                
                fixed (byte* ptrLengthCounts = table.lengthCounts)
                {
                    // build codes
                    HuffmanTable.GetCodes(ptrLengthCounts, table.maxCodeLength, codesPtr);
                    
                    // copy code lengths histogram
                    for (byte i = 0; i < table.maxCodeLength; i++)
                        lengths[i] = table.lengthCounts[i];
                }
                
                for (byte i = 0; i < symbolCount; i++)
                {
                    symbols[i] = table.symbols[i];
                    
                    // value can only be 0-15 as we are storing in 4 bits
                    byte codeLength = (byte)(table.GetCodeLength((byte)i) - 1);
                    
                    // store the code length (4 bits) in the upper or lower nibble of a byte
                    codeLengths[i / 2] |= (byte)(codeLength << ((i & 1) * 4));
                }
            }
        }
    };  

    public struct HuffmanTableAC {
        public fixed ushort codes[256];
        public fixed byte symbols[256];
        public fixed byte lengths[16];
        public fixed byte codeLengths[128];
        
        public void Fill(HuffmanTable table)
        {
            uint symbolCount = (uint)table.symbols.Length;
            
            // DC table should have at most 12 symbols
            Debug.Assert(symbolCount <= 256);
            
            // for safety zero the buffers
            fixed (ushort* codesPtr = codes) 
            fixed (byte* symbolsPtr = symbols) 
            fixed (byte* lengthsPtr = lengths) 
            fixed (byte* codeLengthsPtr = codeLengths) 
            {
                UnsafeUtility.MemSet(codesPtr, 0, sizeof(ushort) * 256);
                UnsafeUtility.MemSet(symbolsPtr, 0, sizeof(byte) * 256);
                UnsafeUtility.MemSet(lengthsPtr, 0, sizeof(byte) * 16);
                UnsafeUtility.MemSet(codeLengthsPtr, 0, sizeof(byte) * 128);
                
                fixed (byte* ptrLengthCounts = table.lengthCounts)
                {
                    // build codes
                    HuffmanTable.GetCodes(ptrLengthCounts, table.maxCodeLength, codesPtr);
                    
                    // copy code lengths histogram
                    for (byte i = 0; i < table.maxCodeLength; i++)
                        lengths[i] = table.lengthCounts[i];
                }
                
                for (uint i = 0; i < symbolCount; i++)
                {
                    symbols[i] = table.symbols[i];
                    
                    // value can only be 0-15 as we are storing in 4 bits
                    byte codeLength = (byte)(table.GetCodeLength((byte)i) - 1);
                    
                    // store the code length (4 bits) in the upper or lower nibble of a byte
                    codeLengths[i / 2] |= (byte)(codeLength << (((byte)i & 1) * 4));
                }
            }
        }
    };

    public struct _HuffmanTableDC
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
                HuffmanTable.GetCodes(ptrLengthCounts, table.maxCodeLength, tempCodes);

                // pack code info as AoS
                for (int i = 0; i < symbolCount; i++)
                {
                    codes[i] = tempCodes[i] | ((uint)table.symbols[i] << 16) | ((uint)table.GetCodeLength(i) << 24);
                }
            }
        }
    }

    public struct _HuffmanTableAC
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
                HuffmanTable.GetCodes(ptrLengthCounts, table.maxCodeLength, tempCodes);

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

    public void Fill(JpegFile jpeg)
    {
        // for safety zero the buffers
        fixed (void* ptr = &this)
            UnsafeUtility.MemSet(ptr, 0xFF, UnsafeUtility.SizeOf<JpegHeader>());
        
        width  = (uint)jpeg.width;
        height = (uint)jpeg.height;
        numMCUs = JpegHelpers.DivRoundUp(width, 8u) * JpegHelpers.DivRoundUp(height, 8u);
        totalBits = (uint)(jpeg.huffmanDC.encodedSize + jpeg.huffmanAC.encodedSize);
        
        luminance.Fill(jpeg.quantTable);
        chroma.Fill(jpeg.quantTable2);
        
        dcHuffmanTable.Fill(jpeg.huffmanDC);
        acHuffmanTable.Fill(jpeg.huffmanAC);
        
        /*HuffmanTableDC dc1;
        _HuffmanTableDC dc2;
        HuffmanTableAC ac1;
        _HuffmanTableAC ac2;
        
        dc1.Fill(jpeg.huffmanDC);
        dc2.Fill(jpeg.huffmanDC);
        ac1.Fill(jpeg.huffmanAC);
        ac2.Fill(jpeg.huffmanAC);

        for (int i = 0; i < jpeg.huffmanDC.symbols.Length; i++)
        {
            uint code = dc1.codes[i];
            uint symbol = dc1.symbols[i];
            uint length = 1 + (((uint)dc1.codeLengths[i >> 1] >> ((i & 1) * 4)) & 0xF);

            uint _code = dc2.codes[i] & 0xFFFF;
            uint _symbol = (dc2.codes[i] >> 16) & 0xFF;
            uint _length = (dc2.codes[i] >> 24);
            
            Debug.Log((code == _code) + " " + (symbol == _symbol) + " " + (length == _length));
        }
        
        for (int i = 0; i < jpeg.huffmanAC.symbols.Length; i++)
        {
            uint code = ac1.codes[i];
            uint symbol = ac1.symbols[i];
            uint length = 1 + (((uint)ac1.codeLengths[i >> 1] >> ((i & 1) * 4)) & 0xF);
            Debug.Log(length + " " + symbol + " " + code);

            uint _code = ac2.codes[i] & 0xFFFF;
            uint _symbol = (ac2.codes[i] >> 16) & 0xFF;
            uint _length = (ac2.codes[i] >> 24);
            Debug.Log(_length + " " + _symbol + " " + _code);
                
            Debug.Log((code == _code) + " " + (symbol == _symbol) + " " + (length == _length));
        }#1#
    }
};*/

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
