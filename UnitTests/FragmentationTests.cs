using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace UnitTests // Убедитесь, что namespace совпадает с вашим
{
    public class FragmentationTests
    {
        [Fact]
        public async Task SendAndReceive_LargeDataPacket_ShouldReassembleCorrectly()
        {
            // Arrange
            // Создаем "сервер", который слушает на случайном свободном порту
            var serverBindEndPoint = new IPEndPoint(IPAddress.Loopback, 0);
            using var server = new UdpClient(serverBindEndPoint);
            var serverEndPoint = (IPEndPoint)server.Client.LocalEndPoint;
            // -----------------------------

            // Создаем "клиент"
            using var client = new UdpClient();

            // Генерируем большой массив данных, имитирующий изображение
            var originalData = new byte[200_000]; // 200 KB
            new Random().NextBytes(originalData);

            // Act

            // --- 1. Логика отправки (взята из вашего кода) ---
            const int maxPacketSize = 60000;
            int totalParts = (int)Math.Ceiling((double)originalData.Length / maxPacketSize);

            var sendTask = Task.Run(async () =>
            {
                for (int i = 0; i < totalParts; i++)
                {
                    int offset = i * maxPacketSize;
                    int size = Math.Min(maxPacketSize, originalData.Length - offset);

                    var packet = new byte[size + 8];
                    Buffer.BlockCopy(BitConverter.GetBytes(i), 0, packet, 0, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(totalParts), 0, packet, 4, 4);
                    Buffer.BlockCopy(originalData, offset, packet, 8, size);

                    await client.SendAsync(packet, packet.Length, serverEndPoint);
                    await Task.Delay(1); // Имитируем задержку
                }
            });

            // --- 2. Логика получения (взята из вашего кода) ---
            var receivedParts = new Dictionary<int, byte[]>();
            byte[] reassembledData = null;

            var receiveTask = Task.Run(async () =>
            {
                var receiveTimeout = Task.Delay(5000); // Таймаут 5 секунд на случай ошибки

                while (receivedParts.Count < totalParts)
                {
                    var receiveResultTask = server.ReceiveAsync();
                    var completedTask = await Task.WhenAny(receiveResultTask, receiveTimeout);

                    if (completedTask == receiveTimeout)
                    {
                        throw new TimeoutException("Тест не получил все пакеты за 5 секунд.");
                    }

                    var result = await receiveResultTask;
                    var buffer = result.Buffer;

                    int partNumber = BitConverter.ToInt32(buffer, 0);
                    // int totalPartsReceived = BitConverter.ToInt32(buffer, 4); // Эта переменная не используется, можно убрать

                    var partData = new byte[buffer.Length - 8];
                    Buffer.BlockCopy(buffer, 8, partData, 0, partData.Length);

                    receivedParts[partNumber] = partData;
                }

                reassembledData = receivedParts.OrderBy(kvp => kvp.Key).SelectMany(kvp => kvp.Value).ToArray();
            });

            await Task.WhenAll(sendTask, receiveTask);

            // Assert
            Assert.NotNull(reassembledData);
            Assert.Equal(originalData.Length, reassembledData.Length); // Размеры должны совпадать
            Assert.Equal(originalData, reassembledData); // Содержимое должно быть идентичным
        }
    }
}