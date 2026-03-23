using System;
using NaughtyAttributes;
using UnityEngine;

public class TestBitStream : MonoBehaviour
{
    public uint[] value;

    [Button]
    private void PrintValue()
    {
        BitStreamWriter bs = new BitStreamWriter(value);
        Debug.Log(bs.ToString());
    }

    [Range(0, 31)] public uint zeros;
    [Range(0, 31)] public uint ones;
    [Range(0, 31)] public uint zeros2;
    [Range(0, 31)] public uint ones2;

    [Button]
    private unsafe void TestPush()
    {
        BitStreamWriter bs = new BitStreamWriter(2);
        
        bs.Push(0u, zeros);
        bs.Push(UInt32.MaxValue, ones);
        bs.Push(0u, zeros2);
        bs.Push(UInt32.MaxValue, ones2);
        
        Debug.Log(bs.ToString());

        fixed (uint* ptr = bs.bits)
        {
            BitStreamReader bsr = new BitStreamReader(ptr, (uint)bs.bits.Length * 32u);

            Debug.Log(bsr.ReadBits(zeros));
            Debug.Log(bsr.ReadBits(ones));
            Debug.Log(bsr.ReadBits(zeros2));
            Debug.Log(bsr.ReadBits(ones2));
        }
    }

    public string seq = "000110101010100001010010100001";
    [Button]
    private unsafe void TestRead()
    {
        BitStreamWriter writer = new BitStreamWriter(2);
        for (int i=0; i<seq.Length; i++)
            writer.Push(seq[i] == '1');

        fixed (uint* ptr = writer.bits)
        {
            BitStreamReader reader = new BitStreamReader(ptr, writer.p);
            string s = "";
            for (int i = 0; i < reader.bitCount; i++)
            {
                s += reader.ReadBit() > 0 ? "1" : "0";
            }

            Debug.Log(reader.bitCount);
            Debug.Log(writer.ToString());
            Debug.Log(seq);
            Debug.Log(s);
            Debug.Log(s == seq && s == writer.ToString());
        }
    }
}
