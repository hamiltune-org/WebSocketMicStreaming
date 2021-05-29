using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net.Sockets;

public class WS_MicStream : WS_Server
{
    public int protectedBuffers = 5;
    public const int bufferSize = 2*2048;
    // Start is called before the first frame update
    void Start()
    {
        StartServer();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    protected class MyClient {
        public int clientId;
        TcpClient client;
        public GameObject go;
        public AudioSource[] sources;
        public Queue<float[]> buffs = new Queue<float[]>();
        public int toggle = 0;
        public float[] mainBuffer = new float[2*bufferSize];
        public float[] currBuffer;
        public float volume = 5f;
        // For the second algorithm
        public bool active = false;
        public double nextTime = 0;
        public int toggle2 = 0;

        public MyClient(int id, TcpClient c, GameObject g) {
            clientId = id;
            client = c;
            go = g;
        }
    }
    List<MyClient> clients = new List<MyClient>();
    private MyClient findMyClient(int id) {
        foreach (MyClient c in clients) {
            if (c.clientId == id) {
                return c;
            }
        }
        return null;
    }

    protected override void registerClient(TcpClient client, int clientId)
    {
        // TODO: Move source from start from here but create new GO.
        GameObject sqr = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sqr.transform.position = Vector3.zero;
        sqr.transform.Translate(UnityEngine.Random.Range(-5, 5), UnityEngine.Random.Range(-5, 5), 0);

        MyClient newMyClient = new MyClient(clientId, client, sqr);

        newMyClient.sources = new AudioSource[] { 
            newMyClient.go.AddComponent(typeof(AudioSource)) as AudioSource, 
            newMyClient.go.AddComponent(typeof(AudioSource)) as AudioSource,
            // Original algorithm
            newMyClient.go.AddComponent(typeof(AudioSource)) as AudioSource, 
            newMyClient.go.AddComponent(typeof(AudioSource)) as AudioSource 
        };

        // Make 3D
        foreach (AudioSource s in newMyClient.sources) {
            s.spatialBlend = 1.0f;
            s.playOnAwake = false;
        }

        newMyClient.active = true;

        clients.Add(newMyClient);

        StartCoroutine(help(newMyClient));
    }

    private IEnumerator help(MyClient client) {
        while (client.buffs.Count < protectedBuffers) yield return null;
        client.nextTime = AudioSettings.dspTime + .2d;
        double t = (double)bufferSize / 44100d;
        while (client.active) {
            while (client.active && AudioSettings.dspTime < client.nextTime - 0.2d) yield return null;
            if (!client.active) yield break;
            // Debug.Log(AudioSettings.dspTime + ", " + client.nextTime +", " + client.buffs.Count);
            AudioClip c = AudioClip.Create("c", 2*bufferSize, 1, 44100, false);
            client.currBuffer = client.buffs.Dequeue();
            // Debug.Log(client.currBuffer[0]);
            Buffer.BlockCopy(client.mainBuffer, 4*bufferSize, client.mainBuffer, 0, 4*bufferSize);
            Buffer.BlockCopy(client.currBuffer, 0,client.mainBuffer, 4*bufferSize, 4*bufferSize);
            c.SetData(client.mainBuffer, 0);   
            // if (client.toggle == 0) {             
            client.sources[client.toggle + client.toggle2].clip = c;
            client.sources[client.toggle + client.toggle2].PlayScheduled(client.nextTime);
            client.sources[client.toggle + client.toggle2].Fade2(gameObject, client.nextTime);
            
            client.nextTime += t;
            //}
            client.toggle = 1 - client.toggle;
            if (client.toggle == 0) client.toggle2 = 2 - client.toggle2;
            //Debug.Log(myClient.buffs.Count);
        }
    }



    override protected void parse(byte[] bytes, TcpClient client, int clientId) {
        MyClient myClient = findMyClient(clientId);

        float[] data = new float[bufferSize];
        Buffer.BlockCopy(bytes, 0, data, 0, 4*bufferSize);
        for (int i = 0; i < bufferSize; i++) data[i] *= 3 *myClient.volume;
        myClient.buffs.Enqueue(data);
        while (myClient.buffs.Count < protectedBuffers) myClient.buffs.Enqueue(data);   
        // if (myClient.buffs.Count > protectedBuffers) {
        //     AudioClip c = AudioClip.Create("c", 2*bufferSize, 1, 44100, false);
        //     myClient.currBuffer = myClient.buffs.Dequeue();
        //     float t = myClient.mainBuffer[bufferSize];
        //     Buffer.BlockCopy(myClient.mainBuffer, 4*bufferSize, myClient.mainBuffer, 0, 4*bufferSize);
        //     Buffer.BlockCopy(myClient.currBuffer, 0,myClient. mainBuffer, 4*bufferSize, 4*bufferSize);
        //     c.SetData(myClient.mainBuffer, 0);   
        //     //if (toggle == 0) {             
        //     myClient.sources[myClient.toggle].PlayOneShot(c);
        //     myClient.sources[myClient.toggle].Fade(gameObject);
        //     //}
        //     myClient.toggle = 1 - myClient.toggle;
        //     //Debug.Log(myClient.buffs.Count);
        // }   
    }

    protected override void cleanUpClient(TcpClient client, int clientId)
    {
        MyClient myClient = findMyClient(clientId);

        myClient.active = false;

        StartCoroutine(delayedDestroy(myClient));
    }

    private IEnumerator delayedDestroy(MyClient myClient) {
        yield return new WaitForSeconds(1);
        Destroy(myClient.go);
    }
}

namespace UnityEngine
{
    public static class AudioSourceExtensions
    {
        public static void Fade(this AudioSource a, GameObject go)
        {
            try {
                go.GetComponent<MonoBehaviour>().StartCoroutine(FadeCore(a));
            } catch (Exception e) { e.ToString(); }
        }
 
        private static IEnumerator FadeCore(AudioSource a)
        {
            float duration = WS_MicStream.bufferSize/44100f;
            float startVolume = 1;
            if (a == null) yield break;
            a.volume = 0;
            while (a != null && a.volume < startVolume - 0.1f)
            {
                a.volume += startVolume * Time.deltaTime / duration;
                yield return new WaitForEndOfFrame();
            }
            if (a == null) yield break;
            a.volume = startVolume;
            while (a != null && a.volume > 0.01f)
            {
                a.volume -= startVolume * Time.deltaTime / duration;
                yield return new WaitForEndOfFrame();
            }
            if (a == null) yield break;
            a.volume = 0f;            
        }

        // Algorithm 2
        public static void Fade2(this AudioSource a, GameObject go, double t)
        {
            try {
                go.GetComponent<MonoBehaviour>().StartCoroutine(FadeCore2(a, t));
            } catch (Exception e) { e.ToString(); }
        }
 
        private static IEnumerator FadeCore2(AudioSource a, double t)
        {
            while (AudioSettings.dspTime < t) yield return null;
            float startVolume = 1;
            a.volume = 0;
            if (a == null) yield break;
            float duration = WS_MicStream.bufferSize/44100f;
            while (a != null && a.volume < startVolume - 0.1f)
            {
                a.volume += startVolume * Time.deltaTime / duration;
                yield return new WaitForEndOfFrame();
            }
            if (a == null) yield break;
            a.volume = startVolume;
            while (a != null && a.volume > 0.01f)
            {
                a.volume -= startVolume * Time.deltaTime / duration;
                yield return new WaitForEndOfFrame();
            }
            if (a == null) yield break;
            a.volume = 0f;            
        }
    }
}
