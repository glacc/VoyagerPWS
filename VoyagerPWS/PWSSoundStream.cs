using SFML.Audio;
using SFML.System;

namespace VoyagerPWS
{
    internal class PWSSoundStream : SoundStream
    {
        const int defaultBufferSize = 1024;

        public static readonly float[] pwsDataMapping =
            [
                -7.5f, -6.5f, -5.5f, -4.5f,
                -3.5f, -2.5f, -1.5f, -0.5f,
                 0.5f,  1.5f,  2.5f,  3.5f,
                 4.5f,  5.5f,  6.5f,  7.5f
            ];
        const float pwsDataMaxVal = 7.5f;

        public List<byte>? data = null;
        public int bufferSize;

        public volatile int position = 0;

        public Mutex mutex = new Mutex();

        protected override bool OnGetData(out short[] samples)
        {
            samples = new short[bufferSize];

            if (data == null)
                return false;

            mutex.WaitOne();

            if (position >= data.Count)
                position = 0;

            int samplesRemaining = data.Count - position;
            int samplesToRead = Math.Min(bufferSize, samplesRemaining);

            const float scale = 1.0f / pwsDataMaxVal;

            int offset = position;
            for (int i = 0; i < samplesToRead; i++)
            {
                samples[i] = (short)(pwsDataMapping[data[offset]] * scale * short.MaxValue);
                offset++;
            }

            position += samplesToRead;

            bool endReached = (position >= data.Count);

            mutex.ReleaseMutex();

            return !endReached;
        }

        protected override void OnSeek(Time timeOffset)
        {
            // position = (int)(timeOffset.AsSeconds() * this.SampleRate);
        }

        public void Seek(float seconds)
        {
            if (data == null)
                return;

            mutex.WaitOne();

            position += (int)(SampleRate * seconds);
            if (position >= data.Count)
            {
                if (this.Status == SoundStatus.Playing)
                    position = data.Count - 1;
                else
                    position = data.Count;
            }
            if (position < 0)
                position = 0;

            mutex.ReleaseMutex();
        }

        public void SetDataAndRewind(List<byte> data)
        {
            this.data = data;
            position = 0;
        }

        public PWSSoundStream(int bufferSize = defaultBufferSize)
        {
            this.bufferSize = bufferSize;

            this.Initialize(1, 28800);
        }
    }
}
