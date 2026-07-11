using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Packets;
using Serilog;

namespace Navislamia.AuthServer.Net;

public delegate Task<byte[]?> PacketDispatch(byte[] packet);

public sealed class PacketServer
{
    private static readonly int HeaderSize = Marshal.SizeOf<Header>();

    private readonly string _name;
    private readonly string _ip;
    private readonly int _port;
    private readonly bool _useCipher;
    private readonly string _cipherKey;
    private readonly Func<PacketDispatch> _dispatcherFactory;
    private readonly ILogger _logger;

    private Socket? _listener;

    public PacketServer(string name, string ip, int port, bool useCipher, string cipherKey,
        Func<PacketDispatch> dispatcherFactory, ILogger logger)
    {
        _name = name;
        _ip = ip;
        _port = port;
        _useCipher = useCipher;
        _cipherKey = cipherKey;
        _dispatcherFactory = dispatcherFactory;
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
        var dispatch = _dispatcherFactory();

        Connection connection = _useCipher
            ? new CipherConnection(socket, _cipherKey)
            : new Connection(socket);

        connection.OnDataSent = _ => { };
        connection.OnDisconnected = () => _logger.Debug("{name}: client disconnected", _name);
        connection.OnDataReceived = bytesReceived => OnDataReceived(connection, dispatch, bytesReceived);
        connection.Start();
    }

    private void OnDataReceived(Connection connection, PacketDispatch dispatch, int bytesReceived)
    {
        var remaining = bytesReceived;

        while (remaining >= HeaderSize)
        {
            var header = new Header(connection.Peek(HeaderSize));

            if (header.Length > remaining)
            {
                return;
            }

            var packet = connection.Read((int)header.Length);
            remaining -= packet.Length;

            try
            {
                var response = dispatch(packet).GetAwaiter().GetResult();
                if (response is not null)
                {
                    connection.Send(response);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "{name}: error handling packet ID {id}", _name, header.ID);
            }
        }
    }
}
