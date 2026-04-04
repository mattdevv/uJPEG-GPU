# uJPEG GPU
![ezgif-894b5099c92c1c31](https://github.com/user-attachments/assets/5649c0be-d9a5-4c28-9e49-8a639813cf79)

Optimised Unity package to turn `Texture2D`'s into a custom JPEG *like* format that achieves high compression ratio and is able to be decompressed on CPU or GPU. Compression follows standard JPEG pipeline and supports 1-channel or 3-channel SDR images with optional chroma downsampling.

Why use this? Storing textures as JPEGs bitstreams in VRAM allows higher compression ratios than traditional formats and smaller sizes, at the cost of needing a decoding pass before use.

How is GPU compression implemented? A bit offset to each JPEG MCU in the bitstream is stored and no delta compression is used between MCUs. With both of these techniques any MCU can be decoded without dependancies like is usual in the JPEG format. There is an additional overhead for the extra stored data of about 2.222 bytes per MCU.

__________________________
### Usage
Open the window found at 'Window/mattdevv/Convert Textures to JPEG', any selected Texture2Ds will be displayed in the window with a preview of the compression. At the bottom of the window you can choose where to save the created JPEGs and process them individually or use the same settings to process all selected Texture2Ds. The outputted objects contain a `JpegData` instance which is CPU decompressable, or they can be converted to a `JpegBuffer` which is GPU decodable. See scripts 'JpegTest.cs' and 'GPUTest.cs' for examples of each.
__________________________
# Performance
- Testing was performed on an AMD 7950X CPU and NVIDIA RTX 4090 GPU. 
- YUV422 subsampling was enabled, and 16-bit AC codes was enabled.
- Compression is measured as final size as a percentage of uncompressed size.  

Two images are used for testing: 
- A 1080x1920 scale/crop of [Image 1](https://images.nasa.gov/details/NHQ202603290004)
- A 16k tiling of [big_building](https://imagecompression.info/test_images/)
  
### CPU Encode Time

| Quality | 1080p || 16k ||
|-:|-:|-:|-:|-:|
| | Compression (%) | Time (ms) | Compression (%) | Time (ms) |
|  25 |  2.56 | 15.96 |  2.19 | 2007.17 |
|  50 |  3.82 | 16.98 |  3.26 | 2114.12 |
|  75 |  5.48 | 18.45 |  4.74 | 2270.18 |
| 100 | 24.74 | 30.21 | 24.52 | 4197.40 |

### Decode Time (ms)
| Quality | CPU || GPU||
|-:|-:|:-:|-:|-:|
| | 1080p | 16k | 1080p | 16k|
|  25 |   5.056 |  772.422 | 0.031 | 2.539 |
|  50 |   6.011 |  866.248 | 0.039 | 3.069 |
|  75 |   7.395 | 1012.114 | 0.049 | 3.864 |
| 100 | 20.164 | 2972.884 | 0.132 | 16.432|

Note: the CPU decode performance can be improved by upto 50% (at 100% quality) if using 12-bit AC codes mode (internals use LUT).
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

## Future Work (in no order):
- [ ] Support other GPU manufacturers besides NVIDIA
- [ ] Allow sharing Huffman-Tables between images to allow GPUs to use 1 optimised table to decode many images
- [ ] Use a different MCU offset distribution for different compression formats, currently optimizes for YUV422
- [x] Add an optimized preview for previewing quality loss without waiting for full encoding
- [ ] Add YUV422 to the preview mode
- [ ] Support for 2 and 4 color channels
- [ ] More subsampling formats
- [ ] Add GPU encoder
- [ ] Add tools to create/select quantization tables
- [ ] Add multithread CPU encoder/decoder using Unity's Jobs System
- [ ] Cleanup the code into a distributable package
- [ ] Add thumbnails/previews to JpegAssets

## Special Thanks
- Inspired after reading: [Variable-Rate Texture Compression: Real-Time Rendering with JPEG](https://arxiv.org/abs/2510.08166)
- Great explaination of Huffman Tables: https://create.stephan-brumme.com/length-limited-prefix-codes/
