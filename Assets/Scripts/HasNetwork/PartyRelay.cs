using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

// Cloudflare 파티 중계 서버 연결 (Server/party-relay). 방 ID로 방에 WebSocket을 열고 봉투만 주고받는다.
// 내용은 PartySession이 암호화한 패킷이라 서버는 읽을 수 없다. 방장 PC에 포트를 열 필요가 없고, 파티원끼리 IP도 모른다.
//   보내기  : Broadcast(같은 방 모두, 참가 요청용) / SendTo(연결 번호 하나)
//   받기    : inbox에 (패킷, 보낸 연결 번호), 누가 나가면 Left
//   끊기면 자동으로 다시 연결한다 (연결 번호는 바뀐다. 상대는 서명이 맞는 패킷으로 새 번호를 따라간다)
// 모든 통신은 백그라운드 스레드에서 돈다. Unity API는 쓰지 않는다.
public sealed class PartyRelay
{
    // 배포한 Worker 주소 (Server/party-relay → npx wrangler deploy 결과)
    public const string ServerUrl = "wss://tkj-party.party-relay.workers.dev";

    public enum State { Connecting, Connected, Closed }

    public State Status { get; private set; } = State.Connecting;
    public uint MyConn { get; private set; }
    public int Failures { get; private set; }       // 연속 연결 실패 횟수 (화면 안내용)
    public string LastError { get; private set; } = "";

    const int MaxFrame = 1400;
    readonly Uri uri;
    readonly ConcurrentQueue<(byte[] data, uint from)> inbox;
    readonly ConcurrentQueue<uint> left = new ConcurrentQueue<uint>();
    readonly ConcurrentQueue<byte[]> outbox = new ConcurrentQueue<byte[]>();
    readonly SemaphoreSlim outboxSignal = new SemaphoreSlim(0);
    readonly CancellationTokenSource cts = new CancellationTokenSource();
    ClientWebSocket socket;

    public PartyRelay(byte[] roomId, ConcurrentQueue<(byte[] data, uint from)> inbox)
    {
        this.inbox = inbox;
        uri = new Uri($"{ServerUrl}/room/{BitConverter.ToString(roomId).Replace("-", "").ToLowerInvariant()}");
        _ = Task.Run(RunAsync);
    }

    public bool TryGetLeft(out uint conn) => left.TryDequeue(out conn);

    public void Broadcast(byte[] packet)
    {
        if (packet == null) return;
        var frame = new byte[1 + packet.Length];
        frame[0] = 1;
        Buffer.BlockCopy(packet, 0, frame, 1, packet.Length);
        Enqueue(frame);
    }

    public void SendTo(uint conn, byte[] packet)
    {
        if (packet == null || conn == 0) return;
        var frame = new byte[5 + packet.Length];
        frame[0] = 2;
        frame[1] = (byte)(conn >> 24);
        frame[2] = (byte)(conn >> 16);
        frame[3] = (byte)(conn >> 8);
        frame[4] = (byte)conn;
        Buffer.BlockCopy(packet, 0, frame, 5, packet.Length);
        Enqueue(frame);
    }

    void Enqueue(byte[] frame)
    {
        if (cts.IsCancellationRequested || outbox.Count > 200) return;   // 끊긴 동안 쌓이지 않게
        outbox.Enqueue(frame);
        outboxSignal.Release();
    }

    // 남은 메시지(나감 알림 등)를 잠깐 보낸 뒤 닫는다. wait > 0이면 그만큼 기다린다 (앱 종료 중)
    public void Close(int waitMs = 0)
    {
        if (Status == State.Closed) return;
        Task flush = Task.Run(async () =>
        {
            for (int i = 0; i < 10 && !outbox.IsEmpty; i++) await Task.Delay(50).ConfigureAwait(false);
            Status = State.Closed;
            cts.Cancel();
            try { socket?.Abort(); } catch (Exception) { }
        });
        if (waitMs > 0) flush.Wait(waitMs);
    }

    async Task RunAsync()
    {
        int delay = 1000;
        while (!cts.IsCancellationRequested)
        {
            var ws = new ClientWebSocket();
            socket = ws;
            try
            {
                await ws.ConnectAsync(uri, cts.Token).ConfigureAwait(false);
                Status = State.Connected;
                Failures = 0;
                delay = 1000;
                Task sending = SendLoop(ws);
                await ReceiveLoop(ws).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (!cts.IsCancellationRequested)
                {
                    Failures++;
                    LastError = e.GetBaseException().Message;
                }
            }
            finally
            {
                try { ws.Abort(); } catch (Exception) { }
                ws.Dispose();
            }
            if (cts.IsCancellationRequested) break;
            Status = State.Connecting;
            MyConn = 0;
            try { await Task.Delay(delay, cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            delay = Math.Min(delay * 2, 15000);
        }
        Status = State.Closed;
    }

    async Task SendLoop(ClientWebSocket ws)
    {
        try
        {
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                await outboxSignal.WaitAsync(cts.Token).ConfigureAwait(false);
                while (outbox.TryDequeue(out byte[] frame))
                    await ws.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // 끊김: 받기 쪽이 다시 연결한다
        }
    }

    async Task ReceiveLoop(ClientWebSocket ws)
    {
        var buffer = new byte[MaxFrame];
        while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
        {
            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, r.Count);
                    if (ms.Length > MaxFrame) return;   // 너무 큰 메시지: 연결을 끊고 다시 붙는다
                } while (!r.EndOfMessage);
                Handle(ms.ToArray());
            }
        }
    }

    void Handle(byte[] frame)
    {
        if (frame.Length < 5) return;
        uint id = ((uint)frame[1] << 24) | ((uint)frame[2] << 16) | ((uint)frame[3] << 8) | frame[4];
        switch (frame[0])
        {
            case 1:   // 받은 메시지
                if (frame.Length > 5 && inbox.Count < 500)
                {
                    var packet = new byte[frame.Length - 5];
                    Buffer.BlockCopy(frame, 5, packet, 0, packet.Length);
                    inbox.Enqueue((packet, id));
                }
                break;
            case 3:   // 내 연결 번호
                MyConn = id;
                break;
            case 4:   // 누가 나감
                left.Enqueue(id);
                break;
        }
    }
}
