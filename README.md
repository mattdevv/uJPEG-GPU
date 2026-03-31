# uJPEG GPU

Optimised Unity package to turn `Texture2D`'s into a JPEG *like* format that achieves high compression ratio and is able to be decompressed on CPU or GPU. Compression follows standard JPEG pipeline and supports 1-channel or 3-channel SDR images with optional chroma downsampling.

Why use this? Storing textures as JPEGs bitstreams in VRAM allows higher compression ratios than traditional formats and smaller sizes, at the cost of needing a decoding pass before use.

How is GPU compression enabled? A bit offset to each JPEG MCU in the bitstream is stored and no delta compression is used between MCUs. With both of these techniques any MCU can be decoded without dependancies like is usual in the JPEG format. There is an additional overhead for the extra stored data of about 2.222 
__________________________
# Performance

__________________________
### Supports:
- CPU Encoding
- CPU/GPU Decoding
- Arbituary resolution (up-to 16k)
- Greyscale/RGB
- Chroma Subsampling
- Quality levels 1->100
- Batch encoding

### Changes from standard JPEG:
- Store bit offset to begining of each MCU (Adds overhead of ~2.222 bytes per MCU block)
  - A 32-bit absolute offset is stored for the first of every 9 MCUs, and a 16-bit relative offset to this is stored for the next 8 MCUs
- No delta compression for DC values
- In subsampled formats write luminance bits last to bitstream to reduce memory footprint when decoding with GPU

### Technical Details
- Each MCU is decoded on GPU by 1 thread group of size 32
- Threadgroup is size 32 to eliminate need for group synchronization 
- Wave intrinsics must be supported by target hardware
- 6 bit DC codes
  - DC values are not delta compressed unless the format uses subsampling (MCUs contain multiple blocks belonging to the same color channel)
- 16 or 12 bit choice for AC codes
  - 16 bit mode has slightly better compression and GPU-decode throughput
  - 12 bit mode is faster for CPU as it can use a size 4096 look-up-table for Huffman decoding
- 2 Huffman tables per image, 1 each for DC and AC symbols
- 2 quantization tables for luminance/chroma values (possible to add a 3rd) 

## Future Work:
- Support other GPU manufacturers besides NVIDIA
- Allow sharing Huffman-Tables between images to allow GPUs to use 1 optimised table to decode many images
- Use a different MCU offset distribution for different compression formats, currently optimizes for YUV422
- Add an optimized preview for judging quality loss without waiting for full encoding
- Support for 2 and 4 color channels
- More subsampling formats
- Add GPU encoder
- Add multithread CPU encoder/decoder using Unity's Jobs System

## Special Thanks
Inspired after reading: [Variable-Rate Texture Compression: Real-Time Rendering with JPEG](https://arxiv.org/abs/2510.08166)
Great explaination of Huffman Tables: (https://create.stephan-brumme.com/length-limited-prefix-codes/)
