using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Packets;
using Serilog;

namespace Navislamia.AuthServer;

public class StubListener
{
    private static readonly int HeaderSize = Marshal.SizeOf<Header>();

    private readonly string _name;
    private readonly string _ip;
    private readonly int _port;
    private readonly IReadOnlyDictionary<ushort, Func<byte[]>> _handlers;
    private readonly ILogger _logger;

    private Socket? _listener;

    public StubListener(string name, string ip, int port,
        IReadOnlyDictionary<ushort, Func<byte[]>> handlers, ILogger logger)
    {
        _name = name;
        _ip = ip;
        _port = port;
        _handlers = handlers;
        _logger = logger;
    }

    public void Start()
    {
        var endPoint = new IPEndPoint(IPAddress.Parse(_ip), _port);
        _listener = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(endPoint);
        _listener.Listen(100);

        _logger.Information("{name} listening on {ip}:{port}", _name, _ip, _port);

        Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (true)
        {
            var socket = await _listener!.AcceptAsync();
            socket.NoDelay = true;
            _logger.Debug("{name}: client connected", _name);
            WireConnection(socket);
        }
    }

    private void WireConnection(Socket socket)
    {
        Connection connection = null!;
        connection = new Connection(socket)
        {
            OnDataSent = _ => { },
            OnDataReceived = bytesReceived => OnDataReceived(connection, bytesReceived),
            OnDisconnected = () => _logger.Debug("{name}: client disconnected", _name)
        };
        connection.Start();
    }

    private void OnDataReceived(Connection connection, int bytesReceived)
    {
        var remaining = bytesReceived;

        while (remaining > HeaderSize)
        {
            var header = new Header(connection.Peek(HeaderSize));
            var packetBytes = connection.Read((int)header.Length);
            remaining -= packetBytes.Length;

            if (_handlers.TryGetValue(header.ID, out var buildResponse))
            {
                var response = buildResponse();
                connection.Send(response);
                _logger.Debug("{name}: handled ID {id}, replied {len} bytes", _name, header.ID, response.Length);
            }
            else
            {
                _logger.Debug("{name}: unhandled ID {id} (length {len})", _name, header.ID, header.Length);
            }
        }
    }
}
