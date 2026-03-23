using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

//[PreferBinarySerialization]
[CreateAssetMenu(fileName = "Serializable Buffer", menuName = "ScriptableObjects/SerializableBuffer", order = 1)]
public class SerializableBuffer : ScriptableObject
{
    [HideInInspector] public byte[] buffer;
    
    public bool uploaded => _internalBuffer == null;

    protected GraphicsBuffer _internalBuffer;
    public GraphicsBuffer InternalBuffer
    {
        get
        {
            if (_internalBuffer == null)
            {
                _internalBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Raw, 
                    GraphicsBuffer.UsageFlags.None,
                    (buffer.Length + 3) / 4, // need to allocate a multiple of 4 bytes, round up to nearest 4 then divide by 4
                    4);
                
                _internalBuffer.SetData(buffer);
            }
            
            Debug.Assert(_internalBuffer.IsValid());
            
            return _internalBuffer;
        }
        private set
        {
            if (_internalBuffer != null)
            {
                _internalBuffer.Dispose();
                _internalBuffer = null;
            }

            _internalBuffer = value;
        }
    }

    public static implicit operator GraphicsBuffer(SerializableBuffer sb) => sb.InternalBuffer;

    public void Fill<T>(T input) where T : unmanaged
    {
        unsafe
        {
            buffer = new byte[sizeof(T)];
            
            fixed (void* output = buffer)
                UnsafeUtility.MemCpy(output, &input, sizeof(T));
        }
    }
    
    public void Fill<T>(T[] array) where T : unmanaged
    {
        unsafe
        {
            buffer = new byte[sizeof(T) * array.Length];
            
            fixed (void* input = array)
            fixed (void* output = buffer)
                UnsafeUtility.MemCpy(output, input, array.Length * sizeof(T));
        }
    }
    
    public void SetupManual(GraphicsBuffer.Target target, GraphicsBuffer.UsageFlags flags = GraphicsBuffer.UsageFlags.None)
    {
        // set on the property as it will ensure no duplicates
        InternalBuffer = new GraphicsBuffer(
            target, 
            flags,
            (buffer.Length + 3) / 4, // need to allocate a multiple of 4 bytes, round up to nearest 4 then divide by 4
            4);
        
        _internalBuffer.SetData(buffer);
    }
    
    public void Release()
    {
        if (_internalBuffer == null)
            return;
        
        _internalBuffer.Release();
        _internalBuffer = null;
    }
    
    private void OnDestroy()
    {
        Release();
    }
}
