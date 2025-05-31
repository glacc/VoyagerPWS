using System.Numerics;

namespace VoyagerPWS
{
    internal class Fourier
    {
        public static void DFT(in Complex[] input, out Complex[] output)
        {
            int count = input.Length;

            output = new Complex[count];

            for (int k = 0; k < count; k++)
            {
                for (int n = 0; n < count; n++)
                    output[k] += input[n] * Complex.Exp(new Complex(0.0, -((2.0 * Math.PI / count) * (k * n))));
            }
        }
        
        /*
        public static void IDFT(in Complex[] input, out Complex[] output)
        {
            int count = input.Length;
            
            output = new Complex[count];

            for (int k = 0; k < count; k++)
            {
                for (int n = 0; n < count; n++)
                    output[k] += (input[n] * Complex.Exp((2.0 * Math.PI / count) * (k * n))) / count;
            }
        }
        */

        static void Permutation(ref Complex[] complexes)
        {
            int length = complexes.Length;

            int nInv = 0;
            int initialBitMask = 0x01 << int.Log2(length - 1);
            for (int n = 0; n < length; n++)
            {
                if (n < nInv)
                {
                    Complex temp = complexes[n];
                    complexes[n] = complexes[nInv];
                    complexes[nInv] = temp;
                }

                int bitMask = initialBitMask;
                while (bitMask > 0)
                {
                    if ((nInv & bitMask) != 0)
                    {
                        nInv = nInv & ~bitMask;
                        bitMask >>= 1;
                    }
                    else
                    {
                        nInv |= bitMask;
                        break;
                    }
                }
            }
        }

        public static float Hamming(int length, int index)
            => 0.54f - (0.46f * MathF.Cos(2.0f * MathF.PI * index / (length - 1)));

        public static void FFT(in Complex[] input, out Complex[] output)
        {
            int count = input.Length;
            if (!int.IsPow2(input.Length))
            {
                DFT(in input, out output);
                return;
            }

            output = new Complex[count];
            Array.Copy(input, output, count);

            Permutation(ref output);

            int gap = 2;
            while (gap <= count)
            {
                int posBlock = 0;
                while (posBlock < count)
                {
                    int halfGap = gap / 2;

                    int indexUpper = posBlock;
                    int indexLower = indexUpper + halfGap;

                    int countInBlock = 0;
                    while (countInBlock < halfGap)
                    {
                        Complex upper = output[indexUpper];
                        Complex lower = output[indexLower];

                        int k = countInBlock;

                        Complex multiplier = Complex.Exp(new Complex(0.0, -((2.0 * Math.PI / gap) * k)));
                        Complex lowerMultiplied = lower * multiplier;

                        Complex destUpper = upper + lowerMultiplied;
                        Complex destLower = upper - lowerMultiplied;

                        output[indexUpper] = destUpper;
                        output[indexLower] = destLower;

                        indexUpper++;
                        indexLower++;

                        countInBlock++;
                    }

                    posBlock += gap;
                }

                gap *= 2;
            }
        }
    }
}
