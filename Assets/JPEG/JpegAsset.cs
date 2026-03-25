using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "JpegObject", menuName = "Scriptable Objects/JpegObject")]
public class JpegAsset : ScriptableObject
{
    public JpegData jpeg;

    [ShowNativeProperty]
    public long totalBytes => (jpeg.exactBits + 7) / 8 + jpeg.packedOffsets.Length;
}
