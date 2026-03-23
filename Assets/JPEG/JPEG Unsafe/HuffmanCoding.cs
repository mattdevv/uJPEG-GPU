// using: https://create.stephan-brumme.com/length-limited-prefix-codes/

using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using UnityEngine;

// struct to bundle references to huffman resources for burst functions
public unsafe struct HuffmanStruct
{
    public byte* symbols;
    public ushort* codes;
    public byte* lengthCounts;
}

[BurstCompile]
[Serializable]
public class HuffmanTable
{
    public byte[] symbols;
    public byte[] lengthCounts;
    
    // how many bits would be required to store all the codes appearing according to their frequency
    [SerializeField] private uint _encodedSize;
    public uint encodedSize => _encodedSize; // *in bits*
    // what is the largest code length [0, 16]
    [SerializeField] private uint _longestCodeLength;
    public uint longestCodeLength => _longestCodeLength;

    /*public HuffmanTable(byte[] symbols, byte[] lengths)
    {
        Debug.Assert(symbols.Length == lengths.Length);
        Debug.Assert(symbols.Length <= 256);
    }*/
    
    public unsafe HuffmanTable(uint* histogram, int length, uint maxCodeLength = 16, bool keepAllSymbols = false)
    {
        if (length > 256)
        {
            Debug.LogError("Symbol count is too high");
            return;
        }

        if (maxCodeLength > 16)
        {
            Debug.LogError("Code will be too long");
            return;
        }
        
        // scan how many non-zero frequencies there are
        int count = 0;
        if (keepAllSymbols)
        {
            count = length;
        }
        else
        {
            for (int i = 0; i < length; i++)
                if (histogram[i] != 0) count++;
        }
        
        // allocate memory
        symbols = new byte[count];
        uint[] frequencies = new uint[count];
        byte[] codeLengths = new byte[count];

        if (keepAllSymbols)
        {
            // pure copy
            for (int i = 0; i < count; i++)
            {
                symbols[i] = (byte)i;
                frequencies[i] = histogram[i];
            }
        }
        else
        {
            // scan to discard zeros and densely pack remaining values
            count = 0;
            for (int i = 0; i < length; i++)
            {
                if (histogram[i] != 0)
                {
                    symbols[count] = (byte)i;
                    frequencies[count] = histogram[i];
                    count++;
                }
            }
        }
        
        // arrays needs to be sorted by ascending frequency
        Array.Sort(frequencies, symbols);
        
        fixed (uint* frequenciesPtr = frequencies)
        fixed (byte* codeLengthsPtr = codeLengths)
        {
            _longestCodeLength = PackageMergeSortedInPlace(maxCodeLength, (uint)count, frequenciesPtr, codeLengthsPtr);
        }
        
        if (longestCodeLength == 0)
        {
            Debug.LogError("Error constructing huffman codes");
            return;
        }
        
        // calculate the space required to encode the symbols (alone, no trailing encoded values)
        _encodedSize = 0;
        for (int i = 0; i < count; i++)
        {
            _encodedSize += frequencies[i] * codeLengths[i];
        }
        
        // tally the lengths, this is used to canonize the codes
        lengthCounts = new byte[16];
        foreach (var codeLength in codeLengths)
        {
            lengthCounts[codeLength-1]++;
        }
        
        // better to have high frequency symbols at the start of the array (will be first searched)
        Array.Reverse(symbols); 
    }
    
    
    /// compute limited prefix code lengths based on Larmore/Hirschberg's package-merge algorithm
    /** - histogram must be in ascending order and no entry must be zero
     *  - the function rejects maxLength > 63 but I don't see any practical reasons you would need a larger limit ...
     *  @param  maxLength  maximum code length, e.g. 15 for DEFLATE or JPEG
     *  @param  numCodes   number of codes, equals the array size of histogram and codeLength
     *  @param  A [in]     how often each code/symbol was found
     *            [out]    computed code lengths
     *  @result actual maximum code length, 0 if error
     */
    private static unsafe byte PackageMergeSortedInPlace(uint maxLength, uint numCodes, uint* histogram, byte* codeLengths)
    {
        // at least one code needs to be in use
        if (numCodes == 0 || maxLength == 0)
            return 0;

        // one or two codes are always encoded with a single bit
        if (numCodes <= 2)
        {
            codeLengths[0] = 1;
            if (numCodes == 2)
                codeLengths[1] = 1;
            return 1;
        }
        
        // my allround variable for various loops
        int i;

        // check maximum bit length
        if (maxLength > 8 * sizeof(uint) - 1) // 8*4-1 = 31
            return 0;

        // at least log2(numCodes) bits required for every valid prefix code
        uint encodingLimit = 1u << (int)maxLength;
        if (encodingLimit < numCodes)
            return 0;

        // need two buffers to process iterations and an array of bitmasks
        uint maxBuffer = 2 * numCodes;
        // allocate memory
        uint[] current = new uint[maxBuffer];
        uint[] previous = new uint[maxBuffer];
        uint[] isMerged = new uint[maxBuffer];

        // initial value of "previous" is a plain copy of the sorted histogram
        for (i = 0; i < numCodes; i++)
            previous[i] = histogram[i];
        uint numPrevious = numCodes;
        // no need to initialize "current", it's completely rebuild every iteration

        // keep track which packages are merged (compact bitmasks):
        // if package p was merged in iteration i then (isMerged[p] & (1 << i)) != 0
        for (i = 0; i < maxBuffer; i++)
            isMerged[i] = 0; // there are no merges before the first iteration

        // the last 2 packages are irrelevant
        uint numRelevant = 2 * numCodes - 2;

        // ... and preparation is finished

        // //////////////////////////////////////////////////////////////////////
        // iterate through potential bit lengths while packaging and merging pairs
        // (step 1 of the algorithm)
        // - the histogram is sorted (prerequisite of the function)
        // - the output must be sorted, too
        // - thus we have to copy the histogram and every and then insert a new package
        // - the code keeps track of the next package and compares it to
        //   the next item to be copied from the history
        // - the smaller value is chosen (if equal, the histogram is chosen)
        // - a bitmask named isMerged is used to keep track which items were packages
        // - repeat until the whole histogram was copied and all packages inserted

        // bitmask for isMerged
        uint mask = 1;
        byte bits;
        for (bits = (byte)(maxLength - 1u); bits > 0; bits--)
        {
            // ignore last element if numPrevious is odd (can't be paired)
            numPrevious &= ~1u; // bit-twiddling trick to clear the lowest bit, same as numPrevious -= numPrevious % 2

            // first merged package
            current[0] = histogram[0]; // a sum can't be smaller than its parts
            current[1] = histogram[1]; // therefore it's impossible to find a package at index 0 or 1
            uint sum = current[0] + current[1]; // same as previous[0] + previous[1]

            // copy histogram and insert merged sums whenever possible
            uint numCurrent = 2; // current[0] and current[1] were already set
            uint numHist = numCurrent; // we took them from the histogram
            uint numMerged = 0; // but so far no package inserted (however, it's precomputed in "sum")
            for (;;) // stop/break is inside the loop
            {
                // the next package isn't better than the next histogram item ?
                if (numHist < numCodes && histogram[numHist] <= sum)
                {
                    // copy histogram item
                    current[numCurrent++] = histogram[numHist++];
                    continue;
                }

                // okay, we have a package being smaller than next histogram item

                // mark output value as being "merged", i.e. a package
                isMerged[numCurrent] |= mask;

                // store package
                current[numCurrent] = sum;
                numCurrent++;

                // already finished last package ?
                numMerged++;
                if (numMerged * 2 >= numPrevious)
                    break;

                // precompute next sum
                sum = previous[numMerged * 2] + previous[numMerged * 2 + 1];
            }

            // make sure every code from the histogram is included
            // (relevant if histogram is very skewed with a few outliers)
            while (numHist < numCodes)
                current[numCurrent++] = histogram[(int)numHist++];

            // prepare next mask
            mask <<= 1;

            // performance tweak: abort as soon as "previous" and "current" are identical
            if (numPrevious >= numRelevant) // ... at least their relevant elements
            {
                // basically a bool: FALSE == 0, TRUE == 1
                byte keepGoing = 0;

                // compare both arrays: if they are identical then stop
                for (i = (int)(numRelevant - 1u); i > 0; i--) // collisions are most likely at the end
                    if (previous[i] != current[i])
                    {
                        keepGoing++;
                        break;
                    }

                // early exit ?
                if (keepGoing == 0)
                    break;
            }

            // swap pointers "previous" and "current"
            (previous, current) = (current, previous);

            // no need to swap their sizes because only numCurrent needed in next iteration
            numPrevious = numCurrent;
        }

        // shifted one bit too far
        mask >>= 1;

        // keep only isMerged
        previous = null;
        current = null;

        // //////////////////////////////////////////////////////////////////////
        // tracking all merges will produce the code lengths
        // (step 2 of the algorithm)
        // - analyze each bitlength's mask in isMerged:
        //   * a "pure" symbol => increase bitlength of that symbol
        //   * a merged code   => just increase counter
        // - stop if no more merged codes found
        // - if m merged codes were found then only examine
        //   the first 2*m elements in the next iteration
        //   (because only they formed these merged codes)

        // reset code lengths
        for (i = 0; i < numCodes; i++)
            codeLengths[i] = 0;

        // start with analyzing the first 2n-2 values
        uint numAnalyze = numRelevant;
        while (mask != 0) // stops if nothing but symbols are found in an iteration
        {
            // number of merged packages seen so far
            uint numMerged = 0;

            // the first two elements must be symbols, they can't be packages
            codeLengths[0]++;
            codeLengths[1]++;
            uint symbol = 2;

            // look at packages
            for (i = (int)symbol; i < numAnalyze; i++)
            {
                // check bitmask: not merged if bit is 0
                if ((isMerged[i] & mask) == 0)
                {
                    // we have a single non-merged symbol, which needs to be one bit longer
                    codeLengths[(int)symbol]++;
                    symbol++;
                }
                else
                {
                    // we have a merged package, so that its parts need to be checked next iteration
                    numMerged++;
                }
            }

            // look only at those values responsible for merged packages
            numAnalyze = 2 * numMerged;

            // note that the mask was originally slowly shifted left by the merging loop
            mask >>= 1;
        }

        // last iteration can't have any merges
        for (i = 0; i < numAnalyze; i++)
            codeLengths[i]++;

        // it's a free world ...
        isMerged = null;

        // first symbol has the longest code because it's the least frequent in the sorted histogram
        return (byte)codeLengths[0];
    }
    
    [BurstCompile]
    private static unsafe void BuildCanonicalCodes(byte* lengthCounts, uint maxDigits, ushort* codes)
    {
        ushort code = 0;

        uint codeIndex = 0;
        for (uint i = 0; i < maxDigits; i++)
        {
            uint len = i + 1;
            
            for (int j = 0; j < lengthCounts[i]; j++)
            {
                // need to reverse code as they are written to stream in LSB order
                codes[codeIndex++] = (ushort)ReverseBits(code++, len);
            }

            code <<= 1;
        }
    }
    
    public uint GetSymbolFromCode(in uint[] codeTable, in uint next16bits, out byte decodedSymbol)
    {
        unchecked
        {
            uint index = 0;
            for (uint l = 1; l <= 16; l++)
            {
                uint bitmask = (1u << (int)l) - 1u;
                ushort code = (ushort)(next16bits & bitmask);

                uint codeCount = lengthCounts[l - 1u];
                for (uint end = index + codeCount; index < end; index++)
                {
                    if (codeTable[index] == code)
                    {
                        decodedSymbol = symbols[index];
                        return l;
                    }
                }
            }

            decodedSymbol = 0;
            return 0;
        }
    }
    
    public unsafe uint CalculateEncodedSize(uint* histogram)
    {
        fixed (byte* lengthCountsPtr = lengthCounts)
        fixed (byte* symbolsPtr = lengthCounts)
        {
            HuffmanStruct huffmanCodesDC = new HuffmanStruct()
            {
                lengthCounts = lengthCountsPtr,
                symbols = symbolsPtr,
            };

            return CalculateEncodedSize(&huffmanCodesDC, histogram);
        }
    }

    [BurstCompile]
    public static unsafe uint CalculateEncodedSize(HuffmanStruct* huffmanCodes, uint* histogram)
    {
        uint encodedSize = 0;
        
        uint codeIndex = 0;
        for (uint l = 0; l < 16; l++)
        {
            uint lengthCount = huffmanCodes->lengthCounts[l];
            uint length = l + 1;

            for (uint i = 0; i < lengthCount; i++)
            {
                uint symbolIndex = codeIndex++;
                byte symbol = huffmanCodes->symbols[symbolIndex];
                
                encodedSize += histogram[symbol] * length;
            }
        }
        
        return encodedSize;
    }
    
    [BurstCompile]
    public static unsafe uint GetSymbolFromCode(HuffmanStruct* huffman, uint next16bits, out byte symbol)
    {
        unchecked
        {
            uint offset = 0;

            for (uint l = 1; l <= 16; l++)
            {
                uint bitmask = (1u << (int)l) - 1u;
                ushort code = (ushort)(next16bits & bitmask);
                
                uint codeCount = huffman->lengthCounts[l - 1u]; // there is no length=0 so it's shifted down
                for (uint i = offset, end = offset + codeCount; i < end; i++)
                {
                    if (huffman->codes[i] == code)
                    {
                        symbol = huffman->symbols[i];
                        return l;
                    }
                }

                offset += codeCount;
            }

            symbol = 0;
            return 0;
        }
    }
    
    [BurstCompile]
    public static unsafe uint GetSymbolFromCode(byte* symbols, ushort* codes, byte* lengthCounts, uint next16bits, out byte symbol)
    {
        unchecked
        {
            uint offset = 0;

            for (uint l = 1; l <= 16; l++)
            {
                uint bitmask = (1u << (int)l) - 1u;
                ushort code = (ushort)(next16bits & bitmask);
                
                uint codeCount = lengthCounts[l - 1u]; // there is no length=0 so it's shifted down
                for (uint i = offset, end = offset + codeCount; i < end; i++)
                {
                    if (codes[i] == code)
                    {
                        symbol = symbols[i];
                        return l;
                    }
                }

                offset += codeCount;
            }

            symbol = 0;
            return 0;
        }
    }
    
    /*[BurstCompile]
    public static uint ReverseBits(uint code, [AssumeRange(0ul, 16ul)] uint length)
    {
        uint reversed = 0;
        for (int i = 0; i < length; i++)
        {
            uint bit = (code >> i) & 1u;
            reversed |= (bit << unchecked((int)length - i - 1));
        }
        return reversed;
    }*/
    
    [BurstCompile]
    public static uint ReverseBits(uint code, [AssumeRange(0ul, 16ul)] uint length)
    {
        uint reversed = 0;

        for (uint i = 0; i < length; i++)
        {
            reversed = (reversed << 1) | (code & 1);
            code >>= 1;
        }

        return reversed;
    }

    public unsafe void GetEncodingLUT(uint* ptrLut)
    {
        fixed(byte* symbolsPtr = symbols)
        fixed(byte* ptrLengths = lengthCounts)
            GetEncodingLUT(symbolsPtr, symbols.Length, ptrLengths, (uint)lengthCounts.Length, ptrLut);
    }

    public static unsafe void GetEncodingLUT(byte[] symbols, byte[] lengthCounts, uint[] lut)
    {
        uint maxDigits = (uint)lengthCounts.Length;
        
        fixed(byte* symbolsPtr = symbols)
        fixed(byte* ptrLengths = lengthCounts)
        fixed(uint* ptrLut = lut)
            GetEncodingLUT(symbolsPtr, symbols.Length, ptrLengths, maxDigits, ptrLut);
    }
    
    // LUT uses symbol as index, returns packed code + codeLength
    public static unsafe void GetEncodingLUT(byte* symbols, int symbolCount, byte* lengthCounts, uint maxDigits, uint* outputLut)
    {
        uint[] codes = new uint[symbolCount];
        
        fixed (uint* codesPtr = codes)
        {
            // first write the canonical code to the lower 16 bits of the lut
            BuildCanonicalCodes(lengthCounts, maxDigits, (ushort*)codesPtr);
        }

        // expand the ushort array into uint array
        // needs to be in reverse to prevent overwriting unprocessed data
        fixed (uint* uintPtr = codes)
        {
            ushort* ushortPtr = (ushort*)uintPtr;
            for (int i = symbolCount - 1; i >= 0; i--)
            {
                uintPtr[i] = ushortPtr[i];
            }
        }

        // then add the code's length to the upper 16 bits of the lut
        uint codeIndex = 0;
        for (uint l = 0; l < maxDigits; l++)
        {
            uint length = l + 1;
            for (int j = 0; j < lengthCounts[l]; j++)
            {
                codes[codeIndex++] |= length << 16;
            }
        }

        // rearrange so codes can be looked up by using symbol as index
        for (int i = 0; i < symbolCount; i++)
        {
            outputLut[symbols[i]] = codes[i];
        }
    }
    
    // LUT uses bytes as symbols are 4 bit and lengths are 4 bit
    public static unsafe byte[] GetDCDecodingLUT(ref HuffmanStruct hf)
    {
        const int maxCodeLengthDC = 6;
        byte[] decodeLut = new byte[1 << maxCodeLengthDC]; // 4096

        uint codeIndex = 0;
        for (int l = 0; l < maxCodeLengthDC; l++)
        {
            uint lengthCount = hf.lengthCounts[l];
            int length = l + 1;

            for (uint i = 0; i < lengthCount; i++)
            {
                uint symbolIndex = codeIndex++;
                ushort code = hf.codes[symbolIndex];
                byte symbol = hf.symbols[symbolIndex];
                byte payload = (byte)((length << 4) | symbol);
                
                for (int start = 0, end = 1 << (maxCodeLengthDC - length); start < end; start++)
                {
                    int lutIndex = (start << length) | code;
                    decodeLut[lutIndex] = payload;
                }
                
            }
        }
#if UNITY_EDITOR
        // check that its identical
        fixed (HuffmanStruct* hfPtr = &hf)
        {
            for (uint i = 0; i < decodeLut.Length; i++)
            {
                uint length = HuffmanTable.GetSymbolFromCode(hfPtr, i, out byte symbol);
                Debug.Assert(length == (decodeLut[i & 0xFFF] >> 4));
                Debug.Assert(symbol == (decodeLut[i & 0xFFF] & 0xF));
            }
        }
#endif
        return decodeLut;
    }
    
    // LUT uses ushorts as symbols are 8 bit and lengths are 4 bit
    public static unsafe ushort[] GetACDecodingLUT(ref HuffmanStruct hf)
    {
        const int maxCodeLengthAC = 12;
        ushort[] acDecodeLut = new ushort[1 << maxCodeLengthAC]; // 4096

        uint codeIndex = 0;
        for (int l = 0; l < maxCodeLengthAC; l++)
        {
            uint lengthCount = hf.lengthCounts[l];
            int length = l + 1;

            for (uint i = 0; i < lengthCount; i++)
            {
                uint symbolIndex = codeIndex++;
                ushort code = hf.codes[symbolIndex];
                byte symbol = hf.symbols[symbolIndex];
                ushort payload = (ushort)((length << 8) | symbol);
                
                for (int start = 0, end = 1 << (maxCodeLengthAC - length); start < end; start++)
                {
                    int lutIndex = (start << length) | code;
                    acDecodeLut[lutIndex] = payload;
                }
                
            }
        }
#if UNITY_EDITOR
        // check that its identical
        fixed (HuffmanStruct* hfPtr = &hf)
        {
            for (uint i = 0; i < acDecodeLut.Length; i++)
            {
                uint length = HuffmanTable.GetSymbolFromCode(hfPtr, i, out byte symbol);
                Debug.Assert(length == (acDecodeLut[i & 0xFFF] >> 8));
                Debug.Assert(symbol == (acDecodeLut[i & 0xFFF] & 0xFF));
            }
        }
#endif
        return acDecodeLut;
    }
    
    public static unsafe void GetCodes(byte* lengthCounts, uint maxDigits, ushort* output)
    {
        // first write the canonical code to the lower 16 bits of the lut
        BuildCanonicalCodes(lengthCounts, maxDigits, output);
    }

    public unsafe byte GetCodeLength(int index)
    {
        Debug.Assert(index < symbols.Length);
        
        fixed (byte* ptrLengths = lengthCounts)
        {
            return GetCodeLength(ptrLengths, longestCodeLength, (byte)index);
        }
    }

    private static unsafe byte GetCodeLength(byte* lengthCounts, uint maxDigits, byte index)
    {
        uint codeIndex = 0;
        for (uint i = 0; i < maxDigits; i++)
        {
            codeIndex += lengthCounts[i];
            
            if (index < codeIndex)
            {
                uint length = i + 1;
                return unchecked((byte)length);
            }
        }

        return 0;
    }

    public override string ToString()
    {
        ushort[] codes = new ushort[symbols.Length];
        unsafe
        {
            fixed(ushort* codesPtr = codes)
            fixed(byte* lengthsPtr = lengthCounts)
                GetCodes(lengthsPtr, longestCodeLength, codesPtr);
        }
        
        string huffman = $"Huffman Table (Count: {symbols.Length}, Minimum Size: {encodedSize})\r\n";
        for (int i = 0; i < symbols.Length; i++)
        {
            var symbol = symbols[i];
            var code = codes[i];
            byte codeLength = GetCodeLength((byte)i);
            
            byte run = (byte)((symbol & 0xF0) >> 4) ;
            byte size = (byte)(symbol & 0x0F);

            huffman += $"SYMBOL({i}) (Run:{run}, Size:{size}), Code:{JpegHelpers.BitStringLSB(ReverseBits(code, codeLength), codeLength)} ({ReverseBits(code, codeLength)})\r\n";
        }

        return huffman;
    }
}