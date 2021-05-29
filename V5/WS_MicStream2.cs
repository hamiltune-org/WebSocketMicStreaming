using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net.Sockets;

public class WS_MicStream2 : WS_Server
{
    public int numProtectedBuffers = 4;
    public double safeTime = 0.15d;
    // Start is called before the first frame update
    void Start()
    {
        StartServer();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    List<MicClient> clients = new List<MicClient>();
    private MicClient findMyClient(int id) {
        foreach (MicClient c in clients) {
            if (c.id == id) {
                return c;
            }
        }
        return null;
    }

    private class MicClient {
        public const int sampleRate = 44100;
        public const int bufferSize = 16384 / 2;
        public int offset = 0;
        public float[] buffer = new float[bufferSize];
        public int id;
        public AudioSource[] sources;
        public int toggle = 0;
        public GameObject obj;
        public Queue<AudioClip> clips = new Queue<AudioClip>();
        public bool active = false;
        public float volume = 5.0f;
        public double t = (double) MicClient.bufferSize / MicClient.sampleRate;
        public double nextTime;
    }

    protected override void registerClient(TcpClient client, int clientId)
    {
        GameObject sqr = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sqr.transform.position = Vector3.zero;
        sqr.transform.Translate(UnityEngine.Random.Range(-5, 5), UnityEngine.Random.Range(-5, 5), 0);

        MicClient micClient = new MicClient();
        micClient.id = clientId;
        micClient.obj = sqr;

        micClient.sources = new AudioSource[] { 
            sqr.AddComponent(typeof(AudioSource)) as AudioSource, 
            sqr.AddComponent(typeof(AudioSource)) as AudioSource 
        };
        foreach (AudioSource s in micClient.sources) {
            s.spatialBlend = 1.0f;
            s.playOnAwake = false;
        }

        clients.Add(micClient);

        StartCoroutine(help(micClient));
    }

    private IEnumerator help(MicClient micClient) {
        micClient.active = true;
        while (micClient.clips.Count <= numProtectedBuffers) yield return null;
        double t = (double) MicClient.bufferSize / MicClient.sampleRate;
        double nextTime = AudioSettings.dspTime + safeTime;
        while (micClient.active) {
            while (micClient.clips.Count > numProtectedBuffers) micClient.clips.Dequeue();
            micClient.sources[micClient.toggle].clip = micClient.clips.Dequeue();
            micClient.sources[micClient.toggle].PlayScheduled(nextTime);
            nextTime += t;
            micClient.toggle = 1 - micClient.toggle;
            while (AudioSettings.dspTime < nextTime - safeTime) yield return null;
        }


    }

    override protected void parse(byte[] bytes, TcpClient client, int clientId) 
    {
        MicClient micClient = findMyClient(clientId);

        if (micClient.offset == MicClient.bufferSize << 2) {
            micClient.offset = 0;
            AudioClip c = AudioClip.Create("c", MicClient.bufferSize, 1, MicClient.sampleRate, false);
            for (int i = 0; i < MicClient.bufferSize; i++) micClient.buffer[i] *= micClient.volume;
            c.SetData(micClient.buffer, 0);
            micClient.clips.Enqueue(c);
            if (micClient.clips.Count < numProtectedBuffers) micClient.clips.Enqueue(c);
        }
        Buffer.BlockCopy(bytes, 0, micClient.buffer, micClient.offset, bytes.Length);
        //for (int i = 0; i < MicClient.bufferSize / 4; i++) micClient.buffer[micClient.offset + i] *= micClient.volume;
        micClient.offset += bytes.Length;
    }

    protected override void cleanUpClient(TcpClient client, int clientId)
    {
        MicClient miClient = findMyClient(clientId);
        clients.Remove(miClient);

        StartCoroutine(delayedDestroy(miClient));
    }

    private IEnumerator delayedDestroy(MicClient myClient) {
        yield return new WaitForSeconds(1);
        Destroy(myClient.obj);
    }
}
