using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp
{
    public class UdpClientHandler
    {
        private readonly UdpClient udpClient;
        private readonly string serverAddress;
        private readonly int serverPort;

        private TaskCompletionSource<byte[]> _receiveTcs;

        public UdpClientHandler(string address, int port)
        {
            serverAddress = address;
            serverPort = port;
            udpClient = new UdpClient(0);
        }

        public Task<byte[]> ReceiveImageAsync()
        {
            _receiveTcs = new TaskCompletionSource<byte[]>();
            return _receiveTcs.Task;
        }

        public async Task SendDataAsync(byte[] data)
        {
            const int maxPacketSize = 60000;
            int totalParts = (int)Math.Ceiling((double)data.Length / maxPacketSize);
            var endpoint = new IPEndPoint(IPAddress.Parse(serverAddress), serverPort);

            for (int i = 0; i < totalParts; i++)
            {
                int offset = i * maxPacketSize;
                int size = Math.Min(maxPacketSize, data.Length - offset);

                var packet = new byte[size + 8];
                Buffer.BlockCopy(BitConverter.GetBytes(i), 0, packet, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(totalParts), 0, packet, 4, 4);
                Buffer.BlockCopy(data, offset, packet, 8, size);

                await udpClient.SendAsync(packet, packet.Length, endpoint);
                await Task.Delay(1); // Небольшая пауза для предотвращения потери пакетов
            }
        }

        public void StartListening(Action<int> onProgress)
        {
            Task.Run(async () =>
            {
                var receivedParts = new ConcurrentDictionary<int, byte[]>();
                var totalPartsToReceive = -1;

                while (true)
                {
                    try
                    {
                        var result = await udpClient.ReceiveAsync();
                        var buffer = result.Buffer;

                        if (buffer.Length < 100 && Encoding.UTF8.GetString(buffer).StartsWith("PROGRESS"))
                        {
                            var parts = Encoding.UTF8.GetString(buffer).Split('|');
                            if (parts.Length == 2 && int.TryParse(parts[1], out int progress))
                            {
                                onProgress?.Invoke(progress);
                            }
                        }
                        else
                        {
                            int partNumber = BitConverter.ToInt32(buffer, 0);
                            totalPartsToReceive = BitConverter.ToInt32(buffer, 4);

                            byte[] partData = new byte[buffer.Length - 8];
                            Buffer.BlockCopy(buffer, 8, partData, 0, partData.Length);
                            receivedParts[partNumber] = partData;

                            if (receivedParts.Count == totalPartsToReceive)
                            {
                                var fullData = receivedParts.OrderBy(kvp => kvp.Key).SelectMany(kvp => kvp.Value).ToArray();
                                _receiveTcs?.TrySetResult(fullData);

                                receivedParts.Clear();
                                totalPartsToReceive = -1;
                            }
                        }
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Ошибка при получении данных: {ex.Message}");
                        _receiveTcs?.TrySetException(ex);
                        break;
                    }
                }
            });
        }

        public void Close()
        {
            udpClient.Close();
        }
    }
}