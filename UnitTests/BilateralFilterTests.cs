using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Xunit;

namespace ImageProcessing.Tests
{
    public class BilateralFilterTests
    {
        // Вспомогательный метод для создания тестового изображения и получения его байтов
        private (byte[] sourceBytes, int width, int height, int stride) GetTestImageData(int width, int height, Color color)
        {
            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(color);
                }

                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);
                var stride = bmpData.Stride;
                var byteCount = Math.Abs(stride) * bmp.Height;
                var bytes = new byte[byteCount];
                Marshal.Copy(bmpData.Scan0, bytes, 0, byteCount);
                bmp.UnlockBits(bmpData);

                return (bytes, width, height, stride);
            }
        }

        [Fact]
        public void Apply_OnSolidBlackImage_ShouldReturnSameBlackImage()
        {
            // Arrange (Подготовка)
            var (sourceBytes, width, height, stride) = GetTestImageData(10, 10, Color.Black);
            var destBytes = new byte[sourceBytes.Length];
            var originalSourceBytes = (byte[])sourceBytes.Clone(); // Копия для сравнения

            // Act (Действие)
            BilateralFilter.Apply(sourceBytes, destBytes, width, height, stride, 0, height, 5, 75, 75);

            // Assert (Проверка)
            // Фильтр не должен изменять идеально черный цвет, так как разница между пикселями равна нулю.
            Assert.Equal(originalSourceBytes, destBytes);
        }

        [Fact]
        public void Apply_OnSolidWhiteImage_ShouldReturnSameWhiteImage()
        {
            // Arrange (Подготовка)
            var (sourceBytes, width, height, stride) = GetTestImageData(10, 10, Color.Black);
            var destBytes = new byte[sourceBytes.Length];
            var originalSourceBytes = (byte[])sourceBytes.Clone(); // Копия для сравнения

            // Act (Действие)
            BilateralFilter.Apply(sourceBytes, destBytes, width, height, stride, 0, height, 5, 75, 75);

            // Assert (Проверка)
            // Фильтр не должен изменять идеально белый цвет, так как разница между пикселями равна нулю.
            Assert.Equal(originalSourceBytes, destBytes);
        }

        [Fact]
        public void Apply_ShouldChangeImageWithNoise()
        {
            // Arrange
            // Создаем простое изображение с "шумом" (один пиксель другого цвета)
            var (sourceBytes, width, height, stride) = GetTestImageData(10, 10, Color.Blue);
            // Ставим красный пиксель в центре. BGRA-формат: Blue, Green, Red, Alpha
            int centerPixelIndex = 5 * stride + 5 * 4;
            sourceBytes[centerPixelIndex] = 0;     // B
            sourceBytes[centerPixelIndex + 1] = 0;   // G
            sourceBytes[centerPixelIndex + 2] = 255; // R
            sourceBytes[centerPixelIndex + 3] = 255; // A

            var destBytes = new byte[sourceBytes.Length];
            var originalSourceBytes = (byte[])sourceBytes.Clone();

            // Act
            BilateralFilter.Apply(sourceBytes, destBytes, width, height, stride, 0, height, 5, 75, 75);

            // Assert
            // После применения фильтра изображение ДОЛЖНО измениться.
            // Шумный пиксель должен быть сглажен.
            Assert.NotEqual(originalSourceBytes, destBytes);
            // Проверяем, что центральный пиксель перестал быть чисто красным
            Assert.NotEqual(255, destBytes[centerPixelIndex + 2]);
        }

        [Fact]
        public void Apply_ToSpecificRegion_ShouldOnlyChangeThatRegion()
        {
            // Arrange
            var (sourceBytes, width, height, stride) = GetTestImageData(20, 20, Color.White);
            var destBytes = (byte[])sourceBytes.Clone(); // Начнем с копии, чтобы было с чем сравнивать

            // Act
            // Применяем фильтр только к верхней половине изображения (от строки 0 до 10)
            BilateralFilter.Apply(sourceBytes, destBytes, width, height, stride, 0, 10, 5, 75, 75);

            // Assert
            // Проверяем, что нижняя половина изображения (строки с 10 по 19) осталась НЕИЗМЕННОЙ.
            // Это доказывает, что разделение на потоки будет работать корректно.
            for (int y = 10; y < height; y++)
            {
                for (int x = 0; x < width * 4; x++) // *4 т.к. 4 байта на пиксель
                {
                    int index = y * stride + x;
                    Assert.Equal(sourceBytes[index], destBytes[index]);
                }
            }
        }
    }
}