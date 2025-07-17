using Stardrop.Models.Nexus.Web;
using Stardrop.ViewModels;
using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Stardrop.Utilities
{
    internal class NexusWebsocket
    {
        //#if DEBUG
        //        private readonly Uri ssoWebsocketURI = new("ws://127.0.0.1");
        //#else

        //#endif
        private readonly Uri ssoWebsocketURI = new("wss://sso.nexusmods.com");
        private readonly string connectionUUID = Guid.NewGuid().ToString();
        private readonly string connectionSlug = "stardrop";

        internal readonly string ssoUrl;

        private ClientWebSocket? _socket;
        private System.Timers.Timer? _pingTimer;
        private bool _hasResolved;

        public NexusWebsocket()
        {
            this.ssoUrl = $"https://www.nexusmods.com/sso?id={connectionUUID}&application={connectionSlug}";
        }

        public async Task<NexusConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
        {
            var result = new NexusConnectionResult();
            _socket = new ClientWebSocket();

            try
            {
                await _socket.ConnectAsync(ssoWebsocketURI, cancellationToken);

                var initialData = new
                {
                    id = connectionUUID,
                    token = (string?)null,
                    protocol = 2
                };
                string json = JsonSerializer.Serialize(initialData);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken
                );

                // ping every 30 seconds as requested by docs
                _pingTimer = new System.Timers.Timer(30_000);
                _pingTimer.Elapsed += async (_, __) =>
                {
                    if (_socket?.State == WebSocketState.Open)
                    {
                        try
                        {
                            await _socket.SendAsync(
                                new ArraySegment<byte>(Array.Empty<byte>()),
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None
                            );
                        }
                        catch
                        {
                            _pingTimer?.Stop();
                        }
                    }
                    else
                    {
                        _pingTimer?.Stop();
                    }
                };
                _pingTimer.AutoReset = true;
                _pingTimer.Start();

                // Receive data
                var buffer = new byte[4096];
                while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var recv = await _socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), cancellationToken);
                    if (recv.MessageType == WebSocketMessageType.Close) break;

                    var msg = Encoding.UTF8.GetString(buffer, 0, recv.Count);
                    Console.WriteLine($"[nexus websocket] received data {msg}");

                    var response = JsonSerializer.Deserialize<WebsocketResponse>(msg);
                    if (response != null && response.success && response.data != null)
                    {
                        // ignore connection_token
                        if (response.data.connection_token != null
                            && response.data.api_key == null)
                        {
                            continue;
                        }

                        result.Message = "successfully obtained api key";
                        result.ApiKey = response.data.api_key;
                        _hasResolved = true;
                        await _socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "got key",
                            CancellationToken.None
                        );
                        break;
                    }
                    else
                    {
                        result.Error = "received invalid message";
                        _hasResolved = true;
                        await _socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "invalid",
                            CancellationToken.None
                        );
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"[nexus websocket] exception: {ex}", Helper.Status.Debug);
                if (!_hasResolved)
                {
                    result.Error = ex.Message;
                    _hasResolved = true;
                }
            }
            finally
            {
                _pingTimer?.Stop();
                if (_socket?.State == WebSocketState.Open)
                {
                    await _socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "shutdown",
                        CancellationToken.None
                    );
                }
                _socket?.Dispose();
            }

            return result;
        }
    }
}
