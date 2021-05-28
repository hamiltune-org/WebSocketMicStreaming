using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;

public abstract class WS_Server : MonoBehaviour
{
    private string _ip = "localhost";
    private int _port = 89;
    private int _timeoutMS = 5000;
    private TcpListener server;
    private bool _active = false;
    private int _numClients = 0;
    public int numClients { get { return _numClients; } }
    public bool active { get { return _active; } }
    private Queue<Action> actionQueue = new Queue<Action>();
    // Start is called before the first frame update
    void Start()
    {
 
    }

    public void StartServer(string ip, int port, int timeoutMS) {
        _ip = ip;
        _port = port;
        _timeoutMS = timeoutMS;
        StartServer();
    }

    public async void StartServer() {
        if (_ip.Equals("localhost")) _ip = "127.0.0.1";

        server = new TcpListener(IPAddress.Parse(_ip), _port);
        try {
            server.Start();
        }
        catch (Exception e) { e.ToString(); return; }

        _active = true;

        Thread clientThread = new Thread(findClient);
        clientThread.Start();
        Debug.Log("Started");

        // Main server loop
        while (active) {
            while (actionQueue.Count > 0) {
                actionQueue.Dequeue().Invoke();
            }
            await Task.Yield();
        }
        
    }

    private void findClient() {
        try {
            while (active) {
                TcpClient client = server.AcceptTcpClient();
                Thread clientThread = new Thread(new ParameterizedThreadStart(register));     
                clientThread.Start(client); 
            }
        }
        catch (Exception e) { e.ToString(); }
    }

    private void register(object clientObject) {
        TcpClient client = (TcpClient) clientObject;

        NetworkStream stream = client.GetStream();
        DateTime time = DateTime.Now;        

        while (client.Available < 3) {
            if ((DateTime.Now - time).TotalMilliseconds >= _timeoutMS) {
                return;
            }
        }
        
        byte[] bytes = new byte[client.Available];
        stream.Read(bytes, 0, client.Available);
        string s = Encoding.UTF8.GetString(bytes);
        
        if (Regex.IsMatch(s, "^GET", RegexOptions.IgnoreCase)) {
            Console.WriteLine("=====Handshaking from client=====\n{0}", s);
            // 1. Obtain the value of the "Sec-WebSocket-Key" request header without any leading or trailing whitespace
            // 2. Concatenate it with "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" (a special GUID specified by RFC 6455)
            // 3. Compute SHA-1 and Base64 hash of the new value
            // 4. Write the hash back as the value of "Sec-WebSocket-Accept" response header in an HTTP response
            string swk = Regex.Match(s, "Sec-WebSocket-Key: (.*)").Groups[1].Value.Trim();
            string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            byte[] swkaSha1 = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
            string swkaSha1Base64 = Convert.ToBase64String(swkaSha1);

            // HTTP/1.1 defines the sequence CR LF as the end-of-line marker
            byte[] response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Accept: " + swkaSha1Base64 + "\r\n\r\n");

            stream.Write(response, 0, response.Length);  

            client.SendTimeout = _timeoutMS; 
            // client.ReceiveTimeout = _timeoutMS;   
            
            handleClient(client);  
        }
    }

    public void Shutdown() {
        if (!active) return;
        
        server.Stop();
        _active = false;
        Debug.Log("Shutting down server");
    }

    private void handleClient(TcpClient client) {
        int clientId = ++_numClients;
        Debug.Log("Client " + clientId + " connected");  

        actionQueue.Enqueue(()=> {
            registerClient(client, clientId); 
        });         

        bool timedOut = false;
        NetworkStream stream;
        DateTime time;

        while (active) {
            time = DateTime.Now;
            stream = client.GetStream();

            while (!stream.DataAvailable) {
                if ((DateTime.Now - time).TotalMilliseconds >= _timeoutMS) {
                    timedOut = true;
                    break;
                }
                
            }
            if (timedOut) break;

            time = DateTime.Now;
            while (client.Available < 9) { // match against "get", changed from 3 bc firefox wouldnt work
                if ((DateTime.Now - time).TotalMilliseconds >= _timeoutMS) {
                    timedOut = true;
                    break;
                }
            }
            if (timedOut) break;
            
            byte[] bytes = new byte[client.Available];
            stream.Read(bytes, 0, client.Available);

            bool fin = (bytes[0] & 0b10000000) != 0,
                mask = (bytes[1] & 0b10000000) != 0; // must be true, "All messages from the client to the server have this bit set"

            int opcode = bytes[0] & 0b00001111, // expecting 1 - text message
                msglen = bytes[1] - 128, // & 0111 1111
                offset = 2;

            if (msglen == 126) {
                // was ToUInt16(bytes, offset) but the result is incorrect
                msglen = BitConverter.ToUInt16(new byte[] { bytes[3], bytes[2] }, 0);
                offset = 4;
            } else if (msglen == 127) {
                Console.WriteLine("TODO: msglen == 127, needs qword to store msglen");
                // i don't really know the byte order, please edit this
                // msglen = BitConverter.ToUInt64(new byte[] { bytes[5], bytes[4], bytes[3], bytes[2], bytes[9], bytes[8], bytes[7], bytes[6] }, 0);
                // offset = 10;
            }

            if (msglen == 0)
                Console.WriteLine("msglen == 0");
            else if (mask) {
                byte[] decoded = new byte[msglen];
                byte[] masks = new byte[4] { bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3] };
                offset += 4;
                for (int i = 0; i < msglen; ++i)
                    decoded[i] = (byte)(bytes[offset + i] ^ masks[i % 4]);
                
                if (opcode == 1) {
                    string text = Encoding.UTF8.GetString(decoded);
                    Debug.Log(text);
                }
                else {
                    actionQueue.Enqueue(()=> {
                        parse(decoded, client, clientId);
                    });                    
                }
            } else
                Console.WriteLine("mask bit not set");
            // 
        }

        if (timedOut) Debug.Log("Client " + clientId + " timed out");
        else Debug.Log("Client " + clientId + " disconnected");

        // User handle client close
        actionQueue.Enqueue(()=>{
            cleanUpClient(client, clientId);

            client.Close();
        });
    }

    abstract protected void registerClient(TcpClient client, int clientId);

    abstract protected void parse(byte[] bytes, TcpClient client, int clientId);    
    abstract protected void cleanUpClient(TcpClient client, int clientId);

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnApplicationQuit() {
        Shutdown();
    }

}