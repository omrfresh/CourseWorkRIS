using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;

public class ClientData
{
    public string ClientId { get; set; }
    public string Mode { get; set; }
    public List<string> FilePaths { get; set; }
    public string ReferenceImage { get; set; }
    public int ThreadCount { get; set; }
}
