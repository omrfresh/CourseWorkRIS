using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UdpServer
{
    public class Program
    {
        private static UdpClient server = new UdpClient(8080);
        private static ConcurrentDictionary<IPEndPoint, MessageAssembly> messageAssemblies = new ConcurrentDictionary<IPEndPoint, MessageAssembly>();

        private static async Task Main()
        {
            AppLogger.Initialize("Server");
            AppLogger.WriteToConsole = true;

            AppLogger.Log("[Server] Сервер запущен.");
            while (true)
            {
                var result = await server.ReceiveAsync();
                _ = Task.Run(() => HandlePacket(result.Buffer, result.RemoteEndPoint));
            }
        }

        private static async Task HandlePacket(byte[] data, IPEndPoint clientEP)
        {
            try
            {
                int partNumber = BitConverter.ToInt32(data, 0);
                int totalParts = BitConverter.ToInt32(data, 4);

                var assembly = messageAssemblies.GetOrAdd(clientEP, _ => new MessageAssembly { TotalParts = totalParts, Parts = new byte[totalParts][] });

                bool isLastPart = false;

                lock (assembly.SyncRoot)
                {
                    if (assembly.Parts[partNumber] == null)
                    {
                        int dataSize = data.Length - 8;
                        assembly.Parts[partNumber] = new byte[dataSize];
                        Buffer.BlockCopy(data, 8, assembly.Parts[partNumber], 0, dataSize);
                        assembly.ReceivedParts++;
                    }

                    if (assembly.ReceivedParts == totalParts)
                    {
                        isLastPart = true;
                    }
                }

                AppLogger.Log($"[Server] Получена часть {partNumber + 1}/{totalParts} от {clientEP}. Всего получено: {assembly.ReceivedParts}");

                if (isLastPart)
                {
                    AppLogger.Log($"[Server] Все {totalParts} частей получены от {clientEP}. Начинаю обработку.");
                    byte[] fullData = assembly.Parts.SelectMany(part => part).ToArray();
                    messageAssemblies.TryRemove(clientEP, out _);

                    await ProcessImageRequestAsync(fullData, clientEP);
                }
            }
            catch (Exception ex)
            {
                // Console.WriteLine($"Ошибка при обработке пакета: {ex.Message}"); // Заменено на логгер
                AppLogger.Log($"[Server] КРИТИЧЕСКАЯ ОШИБКА при обработке пакета: {ex.Message}");
            }
        }

        private static async Task ProcessImageRequestAsync(byte[] requestData, IPEndPoint clientEP)
        {
            using (var ms = new MemoryStream(requestData))
            using (var reader = new BinaryReader(ms))
            {
                int mode = reader.ReadInt32();
                int requestedThreadCount = reader.ReadInt32();
                int diameter = reader.ReadInt32();
                double sigmaColor = reader.ReadDouble();
                double sigmaSpace = reader.ReadDouble();
                int imageLength = reader.ReadInt32();
                byte[] imageData = reader.ReadBytes(imageLength);

                string modeStr = mode == 0 ? "Однопоточный" : "Многопоточный";
                AppLogger.Log($"[Server] Получен запрос. Режим: {modeStr}, Запрошено потоков: {requestedThreadCount}, Diameter: {diameter}");

                using (var imageStream = new MemoryStream(imageData))
                using (var originalBitmap = new Bitmap(imageStream))
                {
                    await ApplyFilterAndSendAsync(originalBitmap, clientEP, diameter, sigmaColor, sigmaSpace, mode, requestedThreadCount);
                }
            }
        }

        private static async Task ApplyFilterAndSendAsync(Bitmap original, IPEndPoint clientEP, int diameter, double sigmaColor, double sigmaSpace, int mode, int requestedThreadCount)
        {
            const PixelFormat processingFormat = PixelFormat.Format32bppArgb;

            BitmapData originalData = original.LockBits(new Rectangle(0, 0, original.Width, original.Height), ImageLockMode.ReadOnly, processingFormat);

            int bytes = Math.Abs(originalData.Stride) * original.Height;
            byte[] sourceBytes = new byte[bytes];
            byte[] destBytes = new byte[bytes];
            Marshal.Copy(originalData.Scan0, sourceBytes, 0, bytes);

            int width = original.Width;
            int height = original.Height;
            int stride = originalData.Stride;

            original.UnlockBits(originalData);
            original.Dispose();

            int threadCount;
            if (mode == 0)
            {
                threadCount = 1;
                AppLogger.Log("[Server] Выполнение в однопоточном режиме.");
            }
            else
            {
                threadCount = requestedThreadCount;
                AppLogger.Log($"[Server] Выполнение в многопоточном режиме с {threadCount} потоками.");
            }

            Thread[] threads = new Thread[threadCount];
            int rowsPerThread = height / threadCount;

            object progressLock = new object();
            int completedThreads = 0;
            int lastReportedProgress = 0;

            for (int i = 0; i < threadCount; i++)
            {
                int startY = i * rowsPerThread;
                int endY = (i == threadCount - 1) ? height : startY + rowsPerThread;

                threads[i] = new Thread(() =>
                {
                    BilateralFilter.Apply(sourceBytes, destBytes, width, height, stride, startY, endY, diameter, sigmaColor, sigmaSpace);

                    lock (progressLock)
                    {
                        completedThreads++;
                        int progress = (int)((double)completedThreads / threadCount * 100);
                        if (progress > lastReportedProgress)
                        {
                            lastReportedProgress = progress;
                            byte[] progressMsg = Encoding.UTF8.GetBytes($"PROGRESS|{progress}");
                            server.Send(progressMsg, progressMsg.Length, clientEP);
                            AppLogger.Log($"[Server] Отправлен прогресс: {progress}%");
                        }

                        if (completedThreads == threadCount)
                        {
                            Monitor.PulseAll(progressLock);
                        }
                    }
                });
                threads[i].Start();
            }

            lock (progressLock)
            {
                while (completedThreads < threadCount)
                {
                    Monitor.Wait(progressLock);
                }
            }

            AppLogger.Log("[Server] Обработка завершена. Отправка результата...");

            using (var processedBitmap = new Bitmap(width, height, processingFormat))
            {
                BitmapData processedData = processedBitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, processingFormat);
                Marshal.Copy(destBytes, 0, processedData.Scan0, bytes);
                processedBitmap.UnlockBits(processedData);

                using (var resultStream = new MemoryStream())
                {
                    processedBitmap.Save(resultStream, ImageFormat.Png);
                    byte[] resultImageData = resultStream.ToArray();
                    await SendDataAsync(resultImageData, clientEP);
                }
            }
        }

        private static async Task SendDataAsync(byte[] data, IPEndPoint clientEP)
        {
            const int maxPacketSize = 60000;
            int totalParts = (int)Math.Ceiling((double)data.Length / maxPacketSize);

            for (int i = 0; i < totalParts; i++)
            {
                int offset = i * maxPacketSize;
                int size = Math.Min(maxPacketSize, data.Length - offset);

                var packet = new byte[size + 8];
                Buffer.BlockCopy(BitConverter.GetBytes(i), 0, packet, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(totalParts), 0, packet, 4, 4);
                Buffer.BlockCopy(data, offset, packet, 8, size);

                await server.SendAsync(packet, packet.Length, clientEP);
                await Task.Delay(1);
            }
            AppLogger.Log($"[Server] Результат отправлен клиенту {clientEP} в {totalParts} частях.");
        }
    }

    public class MessageAssembly
    {
        public byte[][] Parts { get; set; }
        public int TotalParts { get; set; }
        public int ReceivedParts;
        public readonly object SyncRoot = new object();
    }
}