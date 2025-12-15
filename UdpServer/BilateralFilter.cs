using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class BilateralFilter
{
    public static void Apply(byte[] sourceData, byte[] destinationData, int width, int height, int stride, int startY, int endY, int diameter, double sigmaColor, double sigmaSpace)
    {
        int radius = diameter / 2;
        double sigmaColor2 = 2 * sigmaColor * sigmaColor;
        double sigmaSpace2 = 2 * sigmaSpace * sigmaSpace;

        // Предварительно вычисляем веса для пространства (гауссово ядро)
        double[] spaceWeights = new double[diameter * diameter];
        int k = 0;
        for (int i = -radius; i <= radius; i++)
        {
            for (int j = -radius; j <= radius; j++)
            {
                spaceWeights[k++] = Math.Exp(-(i * i + j * j) / sigmaSpace2);
            }
        }

        for (int y = startY; y < endY; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double sumR = 0, sumG = 0, sumB = 0;
                double totalWeight = 0;

                int centerPixelIndex = y * stride + x * 4;
                byte centerB = sourceData[centerPixelIndex];
                byte centerG = sourceData[centerPixelIndex + 1];
                byte centerR = sourceData[centerPixelIndex + 2];

                k = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    for (int j = -radius; j <= radius; j++)
                    {
                        int currentX = x + j;
                        int currentY = y + i;

                        // Проверка границ
                        if (currentX >= 0 && currentX < width && currentY >= 0 && currentY < height)
                        {
                            int currentPixelIndex = currentY * stride + currentX * 4;
                            byte currentB = sourceData[currentPixelIndex];
                            byte currentG = sourceData[currentPixelIndex + 1];
                            byte currentR = sourceData[currentPixelIndex + 2];

                            // Вычисляем веса для цвета
                            double colorWeight = Math.Exp(-((currentR - centerR) * (currentR - centerR) +
                                                             (currentG - centerG) * (currentG - centerG) +
                                                             (currentB - centerB) * (currentB - centerB)) / sigmaColor2);

                            double weight = spaceWeights[k] * colorWeight;

                            sumB += currentB * weight;
                            sumG += currentG * weight;
                            sumR += currentR * weight;
                            totalWeight += weight;
                        }
                        k++;
                    }
                }

                int destPixelIndex = y * stride + x * 4;
                destinationData[destPixelIndex] = (byte)(sumB / totalWeight);
                destinationData[destPixelIndex + 1] = (byte)(sumG / totalWeight);
                destinationData[destPixelIndex + 2] = (byte)(sumR / totalWeight);
                destinationData[destPixelIndex + 3] = 255; 
            }
        }
    }
}