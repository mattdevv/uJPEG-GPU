# uJPEG GPU

### What?
Highly optimised Unity package to compress textures into a JPEG like format with the ability to decompress on GPU. Compression follows standard JPEG path and supports 1-channel or 3-channel SDR images with optional chroma downsampling.
________________
### Why?
I needed a way to store a large amount of textures with only a few visible at a time but quickly switching. Keeping all the images in VRAM allowed quick switching but exceeded its capacity, instead I compressed the images into my own format and decompress them when needed directly on the GPU.
________________
### How?
Inspired by [Variable-Rate Texture Compression: Real-Time Rendering with JPEG](https://arxiv.org/abs/2510.08166), I store the bit offset to each JPEG MCU which allows them to be decoded individually. A compute shader implements decoding 1 MCU per wave using wave intrinsics. The CPU code is written to be almost entirely Burst compilable and achieves great speed as well, seemingly faster than Unity's own JPEG loader, but still only single threaded.
