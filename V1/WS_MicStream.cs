using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class WS_MicStream : WS_Server
{
    public int protectedBuffers = 5;
    public const int bufferSize = 2*2048;
    Queue<float[]> buffs = new Queue<float[]>();
    AudioSource[] sources;
    int toggle = 0;
    float[] mainBuffer = new float[2*bufferSize];
    float[] currBuffer;
    // Start is called before the first frame update
    void Start()
    {
        sources = new AudioSource[] { 
            gameObject.AddComponent(typeof(AudioSource)) as AudioSource, 
            gameObject.AddComponent(typeof(AudioSource)) as AudioSource 
        };

        StartServer();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    override protected void parse(byte[] bytes) {
        float[] data = new float[bufferSize];
        Buffer.BlockCopy(bytes, 0, data, 0, 4*bufferSize);
        buffs.Enqueue(data);
        while (buffs.Count > protectedBuffers + 1) buffs.Dequeue();    
        if (buffs.Count > protectedBuffers) {
            AudioClip c = AudioClip.Create("c", 2*bufferSize, 1, 44100, false);
            currBuffer = buffs.Dequeue();
            float t = mainBuffer[currBuffer.Length];
            Buffer.BlockCopy(mainBuffer, 4*currBuffer.Length, mainBuffer, 0, 4*currBuffer.Length);
            Buffer.BlockCopy(currBuffer, 0, mainBuffer, 4*currBuffer.Length, 4*currBuffer.Length);
            c.SetData(mainBuffer, 0);   
            //if (toggle == 0) {             
            sources[toggle].PlayOneShot(c);
            sources[toggle].Fade();
            //}
            toggle = 1 - toggle;
            Debug.Log(buffs.Count);
        }   
    }
}

namespace UnityEngine
{
    public static class AudioSourceExtensions
    {
        public static void Fade(this AudioSource a)
        {
            a.GetComponent<MonoBehaviour>().StartCoroutine(FadeCore(a));
        }
 
        private static IEnumerator FadeCore(AudioSource a)
        {
            float duration = WS_MicStream.bufferSize/44100f;
            float startVolume = 1;
            a.volume = 0;
            while (a.volume < startVolume - 0.1f)
            {
                a.volume += startVolume * Time.deltaTime / duration;
                yield return new WaitForEndOfFrame();
            }
            a.volume = startVolume;
            while (a.volume > 0.01f)
            {
                a.volume -= startVolume * Time.deltaTime / duration;
                yield return new WaitForEndOfFrame();
            }
            a.volume = 0f;
            
        }
    }
}
