using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public static class MCUBlock
{
    public static readonly byte[] ZigZagLUT =
    {
        0,   1,  5,  6, 14, 15, 27, 28,
        2,   4,  7, 13, 16, 26, 29, 42,
        3,   8, 12, 17, 25, 30, 41, 43,
        9,  11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63,
    };

    public static readonly byte[] InvZigZagLUT =
    {
        0,   1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    };

    public static readonly byte[] QuantizationTable =
    {
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68,109,103, 77,
        24, 35, 55, 64, 81,104,113, 92,
        49, 64, 78, 87,103,121,120,101,
        72, 92, 95, 98,112,100,103, 99
    };
    
    public static readonly byte[] highCompressionLumaQuant =
    {
        24, 18, 16, 24, 40, 64, 80, 96,
        18, 20, 22, 30, 48, 96, 96, 96,
        22, 22, 24, 40, 64, 96, 96, 96,
        24, 30, 40, 64, 96,128,128, 96,
        40, 48, 64, 96,128,160,160,128,
        64, 96, 96,128,160,192,192,160,
        80, 96, 96,128,160,192,192,160,
        96, 96, 96, 96,128,160,160,160
    };

    public static unsafe void ZigZagQuantize(float* input, short* output, float* quantRcp)
    {
        for (int i = 0; i < 64; i++)
        {
            int index = ZigZagLUT[i];
            float value = input[i] * quantRcp[i];
            output[index] = (short)math.round(value);
        }
    }
    
    public static unsafe void Encode(float* mcu, short* output, float* quant)
    {
        // Convert to cosine form
        FDCT8x8_AAN(mcu);
        
        ZigZagQuantize(mcu, output, quant);
    }
    
    public static unsafe void Decode(float* arrayf)
    {
        IDCT8x8(arrayf);
    }
    
    /* Below from: https://github.com/stbrumme/toojpeg/blob/master/toojpeg.cpp#L252
     * zlib License
     *
     * Copyright (c) 2011-2016 Stephan Brumme
     *
     * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
     * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
     * 1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software.
     *    If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
     * 2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
     * 3. This notice may not be removed or altered from any source distribution.
     */
    
    // forward DCT computation "in one dimension" (fast AAN algorithm by Arai, Agui and Nakajima: "A fast DCT-SQ scheme for images")
    private static unsafe void DCT(float* block, byte stride) // stride must be 1 (=horizontal) or 8 (=vertical)
    {
        const float SqrtHalfSqrt = 1.306562965f; //    sqrt((2 + sqrt(2)) / 2) = cos(pi * 1 / 8) * sqrt(2)
        const float InvSqrt      = 0.707106781f; // 1 / sqrt(2)                = cos(pi * 2 / 8)
        const float HalfSqrtSqrt = 0.382683432f; //     sqrt(2 - sqrt(2)) / 2  = cos(pi * 3 / 8)
        const float InvSqrtSqrt  = 0.541196100f; // 1 / sqrt(2 - sqrt(2))      = cos(pi * 3 / 8) * sqrt(2)

        // modify in-place
        ref float block0 = ref block[0         ];
        ref float block1 = ref block[1 * stride];
        ref float block2 = ref block[2 * stride];
        ref float block3 = ref block[3 * stride];
        ref float block4 = ref block[4 * stride];
        ref float block5 = ref block[5 * stride];
        ref float block6 = ref block[6 * stride];
        ref float block7 = ref block[7 * stride];

        // based on https://dev.w3.org/Amaya/libjpeg/jfdctflt.c , the original variable names can be found in my comments
        float add07 = block0 + block7; float sub07 = block0 - block7; // tmp0, tmp7
        float add16 = block1 + block6; float sub16 = block1 - block6; // tmp1, tmp6
        float add25 = block2 + block5; float sub25 = block2 - block5; // tmp2, tmp5
        float add34 = block3 + block4; float sub34 = block3 - block4; // tmp3, tmp4

        float add0347 = add07 + add34; float sub07_34 = add07 - add34; // tmp10, tmp13 ("even part" / "phase 2")
        float add1256 = add16 + add25; float sub16_25 = add16 - add25; // tmp11, tmp12

        block0 = add0347 + add1256; block4 = add0347 - add1256; // "phase 3"

        float z1 = (sub16_25 + sub07_34) * InvSqrt; // all temporary z-variables kept their original names
        block2 = sub07_34 + z1; block6 = sub07_34 - z1; // "phase 5"

        float sub23_45 = sub25 + sub34; // tmp10 ("odd part" / "phase 2")
        float sub12_56 = sub16 + sub25; // tmp11
        float sub01_67 = sub16 + sub07; // tmp12

        float z5 = (sub23_45 - sub01_67) * HalfSqrtSqrt;
        float z2 = sub23_45 * InvSqrtSqrt  + z5;
        float z3 = sub12_56 * InvSqrt;
        float z4 = sub01_67 * SqrtHalfSqrt + z5;
        float z6 = sub07 + z3; // z11 ("phase 5")
        float z7 = sub07 - z3; // z13
        block1 = z6 + z4; block7 = z6 - z4; // "phase 6"
        block5 = z7 + z2; block3 = z7 - z2;
    }
    
    public static unsafe void FDCT8x8_AAN(float* data)
    {
        // DCT: rows
        for (int offset = 0; offset < 8; offset++)
            DCT(data + offset*8, 1);
        // DCT: columns
        for (int offset = 0; offset < 8; offset++)
            DCT(data + offset*1, 8);
    }
    
    /* Copyright (c) 2022, NVIDIA CORPORATION. All rights reserved.
     *
     * Redistribution and use in source and binary forms, with or without
     * modification, are permitted provided that the following conditions
     * are met:
     *  * Redistributions of source code must retain the above copyright
     *    notice, this list of conditions and the following disclaimer.
     *  * Redistributions in binary form must reproduce the above copyright
     *    notice, this list of conditions and the following disclaimer in the
     *    documentation and/or other materials provided with the distribution.
     *  * Neither the name of NVIDIA CORPORATION nor the names of its
     *    contributors may be used to endorse or promote products derived
     *    from this software without specific prior written permission.
     *
     * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ``AS IS'' AND ANY
     * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
     * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
     * PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT OWNER OR
     * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
     * EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
     * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
     * PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY
     * OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
     * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
     * OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
     */
    private static unsafe void CUDAsubroutineInplaceIDCTvector(float* Vect0, int Step)
    {
        const float C_a = 1.387039845322148f; //!< a = (2^0.5) * cos(    pi / 16);  Used in forward and inverse DCT.
        const float C_b = 1.306562964876377f; //!< b = (2^0.5) * cos(    pi /  8);  Used in forward and inverse DCT.
        const float C_c = 1.175875602419359f; //!< c = (2^0.5) * cos(3 * pi / 16);  Used in forward and inverse DCT.
        const float C_d = 0.785694958387102f; //!< d = (2^0.5) * cos(5 * pi / 16);  Used in forward and inverse DCT.
        const float C_e = 0.541196100146197f; //!< e = (2^0.5) * cos(3 * pi /  8);  Used in forward and inverse DCT.
        const float C_f = 0.275899379282943f; //!< f = (2^0.5) * cos(7 * pi / 16);  Used in forward and inverse DCT.
        const float C_norm = 0.3535533905932737f; // 1 / (8^0.5)
        
	    float* Vect1 = Vect0 + Step;
	    float* Vect2 = Vect1 + Step;
	    float* Vect3 = Vect2 + Step;
	    float* Vect4 = Vect3 + Step;
	    float* Vect5 = Vect4 + Step;
	    float* Vect6 = Vect5 + Step;
	    float* Vect7 = Vect6 + Step;

	    float Y04P = (*Vect0) + (*Vect4);
	    float Y2b6eP = C_b * (*Vect2) + C_e * (*Vect6);

	    float Y04P2b6ePP = Y04P + Y2b6eP;
	    float Y04P2b6ePM = Y04P - Y2b6eP;
	    float Y7f1aP3c5dPP = C_f * (*Vect7) + C_a * (*Vect1) + C_c * (*Vect3) + C_d * (*Vect5);
	    float Y7a1fM3d5cMP = C_a * (*Vect7) - C_f * (*Vect1) + C_d * (*Vect3) - C_c * (*Vect5);

	    float Y04M = (*Vect0) - (*Vect4);
	    float Y2e6bM = C_e * (*Vect2) - C_b * (*Vect6);

	    float Y04M2e6bMP = Y04M + Y2e6bM;
	    float Y04M2e6bMM = Y04M - Y2e6bM;
	    float Y1c7dM3f5aPM = C_c * (*Vect1) - C_d * (*Vect7) - C_f * (*Vect3) - C_a * (*Vect5);
	    float Y1d7cP3a5fMM = C_d * (*Vect1) + C_c * (*Vect7) - C_a * (*Vect3) + C_f * (*Vect5);

	    (*Vect0) = C_norm * (Y04P2b6ePP + Y7f1aP3c5dPP);
	    (*Vect7) = C_norm * (Y04P2b6ePP - Y7f1aP3c5dPP);
	    (*Vect4) = C_norm * (Y04P2b6ePM + Y7a1fM3d5cMP);
	    (*Vect3) = C_norm * (Y04P2b6ePM - Y7a1fM3d5cMP);

	    (*Vect1) = C_norm * (Y04M2e6bMP + Y1c7dM3f5aPM);
	    (*Vect5) = C_norm * (Y04M2e6bMM - Y1d7cP3a5fMM);
	    (*Vect2) = C_norm * (Y04M2e6bMM + Y1d7cP3a5fMM);
	    (*Vect6) = C_norm * (Y04M2e6bMP - Y1c7dM3f5aPM);
    }
    
    private static unsafe void IDCT8x8(float* block) {
        
	    // IDCT: columns
	    for (int i = 0; i < 8; i++)
		    CUDAsubroutineInplaceIDCTvector(block + i, 8);
	    // IDCT: rows
	    for (int i = 0; i < 8; i++)
		    CUDAsubroutineInplaceIDCTvector(block + i * 8, 1);
    }
}
