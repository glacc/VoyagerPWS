using Glacc.UI;
using Glacc.UI.Components;
using Glacc.UI.Elements;
using SFML.Audio;
using SFML.Graphics;
using SFML.System;
using System.Numerics;
using System.Text;

namespace VoyagerPWS
{
    internal class Program
    {
        static string? initialPath = null;

        static AppWindow window = new AppWindow("Voyager PWS", 1024, 768, false);

        #region Elements

        const int elemWidth = 96;
        const int elemHeight = 24;
        const int elemSpacing = 8;

        static int GetElementX(int count, bool labelOffset = false)
            => elemSpacing + ((elemWidth + elemSpacing) * count) + (labelOffset ? (elemWidth / 2) : 0);
        static int GetElementY(int count, bool labelOffset = false)
            => elemSpacing + ((elemHeight + elemSpacing) * count) + (labelOffset ? (elemHeight / 2) : 0);
        static int GetElementYFromBottom(int count, bool labelOffset = false)
            => window.height - ((elemHeight + elemSpacing) * (count + 1)) + (labelOffset ? (elemHeight / 2) : 0);

        static List<Element?> elements = new List<Element?>();

        static Button? btnOpen;
        static Button? btnSlice;
        static Label? lblFileLoadInfo;

        static Label? lblDataInfo;

        static Button? btnPlay;
        static Button? btnPause;
        static Button? btnRewind;
        static Button? btnForward;

        static FileSelector? fileSelector;

        static void InitElements()
        {
            /* File Loading */

            btnOpen = new Button("Open...", GetElementX(0), GetElementY(0), elemWidth, elemHeight);
            btnOpen.onClick += delegate { if (fileSelector != null) fileSelector.visable = true; };
            elements.Add(btnOpen);

            btnSlice = new Button("Non-sliced", GetElementX(1), GetElementY(0), elemWidth, elemHeight);
            btnSlice.onClick += delegate
            {
                slicedMode = !slicedMode;
                btnSlice.text = slicedMode ? "Sliced" : "Non-sliced";
            };
            elements.Add(btnSlice);

            lblFileLoadInfo = new Label("", GetElementX(2), GetElementY(0, true), 16);
            lblFileLoadInfo.textAlign = TextAlign.Left;
            elements.Add(lblFileLoadInfo);

            /* Data Info */

            lblDataInfo = new Label("", GetElementX(1), GetElementY(3), 16);
            lblDataInfo.textAlign = TextAlign.TopLeft;
            elements.Add(lblDataInfo);

            /* Playback */

            btnPlay = new Button("Play", GetElementX(0), GetElementYFromBottom(0), elemWidth, elemHeight);
            btnPlay.onClick += delegate { if (dataPlayer.Status != SoundStatus.Playing) dataPlayer.Play(); };

            btnPause = new Button("Pause", GetElementX(1), GetElementYFromBottom(0), elemWidth, elemHeight);
            btnPause.onClick += delegate { dataPlayer.Pause(); };

            btnRewind = new Button("Rewind", GetElementX(2), GetElementYFromBottom(0), elemWidth, elemHeight);
            btnRewind.onClick += delegate { dataPlayer.Seek(-2.5f); };

            btnForward = new Button("Forward", GetElementX(3), GetElementYFromBottom(0), elemWidth, elemHeight);
            btnForward.onClick += delegate { dataPlayer.Seek(2.5f); };

            elements.Add(btnPlay);
            elements.Add(btnPause);
            elements.Add(btnRewind);
            elements.Add(btnForward);

            /* File Selector */

            fileSelector = new FileSelector(initialPath, window.width, window.height);
            fileSelector.onCancel += delegate { fileSelector.visable = false; };
            fileSelector.onFileSelect += OnFileSelect;
            fileSelector.visable = false;
            elements.Add(fileSelector);
        }

        #endregion

        #region FileSelector

        static void OnFileSelect(object? sender, EventArgs args)
        {
            FileSelector? fileSelector = sender as FileSelector;
            if (fileSelector == null)
                return;

            fileSelector.visable = false;

            if (lblFileLoadInfo != null)
                lblFileLoadInfo.text = $"Loading \"{fileSelector.lastSelectedFilePath}\" ...";

            TryLoadPWSData(fileSelector.lastSelectedFilePath);
        }

        #endregion

        #region VoyagerPWSData

        const int sampleRate = 28800;
        const int samplePerLine = 1600;

        static TimeSpan recordStartTime;

        static PWSSoundStream dataPlayer = new PWSSoundStream();

        static byte[] engineeringRecord = new byte[242];
        static string scIdSclkScet = string.Empty;

        static bool slicedMode = false;
        static List<byte> waveformData = new List<byte>();
        static List<int> linePositions = new List<int>();

        static List<float[]> fftData = new List<float[]>();

        static Texture? fftChartTexture = null;
        static Sprite? fftChart = null;
        static List<Text> fftChartFreqAxisText = new List<Text>();
        static List<Text> fftChartTimeAxisText = new List<Text>();

        static VertexArray timePosVertices = new VertexArray(PrimitiveType.Lines, 2);

        const int scopeWidthNonSliced = 800;
        const int scopeWidthSliced = 512;

        static int scopeScaledWidth = 800;
        static int scopeScaledHeight = 512;

        static void SetLinePosition(PWSSoundStream pwsSoundStream)
        {
            int maxPossibleSamples = fftData.Count * samplePerLine;

            int streamPos = pwsSoundStream.position;
            int lineIndex = streamPos / samplePerLine;
            if (lineIndex >= linePositions.Count)
                lineIndex = linePositions.Count - 1;

            int currLineStartPos = linePositions[lineIndex];
            int currPosition = currLineStartPos + (streamPos % samplePerLine);

            float startX = (window.width - scopeScaledWidth) / 2;
            float lineX = startX + (scopeScaledWidth * ((float)currPosition / maxPossibleSamples));

            float startY = (window.height - scopeScaledHeight) / 2;
            float endY = startY + scopeScaledHeight;

            timePosVertices[0] = new Vertex(new Vector2f(lineX, startY), Color.White);
            timePosVertices[1] = new Vertex(new Vector2f(lineX, endY), Color.White);
        }

        static bool ReadRow(int index, in FileStream fileStream, ref byte[] buffer)
        {
            if (index < 0 || buffer.Length < 1024)
                return false;

            const int rowSize = 1024;

            int startPosition = index * rowSize;
            if (fileStream.Length - rowSize < startPosition)
                return false;

            fileStream.Seek(startPosition, SeekOrigin.Begin);
            fileStream.Read(buffer, 0, rowSize);

            return true;
        }

        static void FFTRow(in byte[] buffer, out float[] output)
        {
            const int bufferLength = samplePerLine;
            const int outputSize = 800;

            const int fftSize = 2048;

            output = new float[outputSize];

            if (buffer.Length != bufferLength)
                return;
            Complex[] complexes = new Complex[fftSize];
            Complex[] complexesFft;

            const int skipCount = 0;
            const int samplesToCopy = bufferLength - skipCount;
            for (int i = 0; i < samplesToCopy; i++)
                complexes[i] = PWSSoundStream.pwsDataMapping[buffer[i + skipCount]] * Fourier.Hamming(samplesToCopy, i);

            Fourier.FFT(complexes, out complexesFft);

            const float scale = 1.0f / fftSize;
            for (int i = 0; i < output.Length; i++)
                output[i] = (float)complexesFft[i].Magnitude * scale;

            /*
            float maxMagnitude = 0.0f;
            for (int i = 0; i < output.Length; i++)
            {
                float magnitude = (float)complexesFft[i].Magnitude;
                if (magnitude > maxMagnitude)
                    maxMagnitude = magnitude;
                output[i] = magnitude;
            }

            for (int i = 0; i < output.Length; i++)
                output[i] /= maxMagnitude;
            */
        }

        static (byte r8, byte g8, byte b8) MagnitudeToColor(float value)
        {
            float logValue = MathF.Log10(value);

            float percent;
            float r, g, b;
            if (logValue >= -1f)
            {
                // Red //

                percent = logValue + 1.0f;

                r = 1.0f;
                g = 0.0f;
                b = 0.0f;
            }
            else if (logValue >= -2f)
            {
                // Red -> Yellow //

                percent = logValue + 2.0f;

                r = 1.0f;
                g = 1.0f - percent;
                b = 0.0f;
            }
            else if (logValue >= -3f)
            {
                // Yellow -> Green //

                percent = logValue + 3.0f;

                r = percent;
                g = 1.0f;
                b = 0.0f;
            }
            else if (logValue >= -4f)
            {
                // Green -> Cyan //

                percent = logValue + 4.0f;

                r = 0.0f;
                g = 1.0f;
                b = 1.0f - percent;
            }
            else if (logValue >= -4.5f)
            {
                // Cyan -> Blue //

                percent = logValue + 4.5f;
                percent += percent;

                r = 0.0f;
                g = percent;
                b = 1.0f;
            }
            else if (logValue >= -5f)
            {
                // Blue -> Black //

                percent = logValue + 5.0f;

                r = 0.0f;
                g = 0.0f;
                b = percent;
            }
            else
            {
                r = 0.0f;
                g = 0.0f;
                b = 0.0f;
            }    

            r = MathF.Max(0.0f, MathF.Min(r, 1.0f));
            g = MathF.Max(0.0f, MathF.Min(g, 1.0f));
            b = MathF.Max(0.0f, MathF.Min(b, 1.0f));

            r *= 255.0f;
            g *= 255.0f;
            b *= 255.0f;

            byte r8 = (byte)r;
            byte g8 = (byte)g;
            byte b8 = (byte)b;

            return (r8, g8, b8);
        }

        static void UpdateAxisText()
        {
            const uint textSize = 10;
            const int distToAxis = 8;

            int startX = (window.width - scopeScaledWidth) / 2;
            int startY = (window.height - scopeScaledHeight) / 2;

            /* X-Axis */

            foreach (Text text in fftChartTimeAxisText)
                text.Dispose();
            fftChartTimeAxisText.Clear();

            int maxPossibleSamples = fftData.Count * samplePerLine;

            int sampleInverval = maxPossibleSamples / 5;

            int slicedMultipler = slicedMode ? 5 : 1;

            for (int currPos = 0; currPos <= maxPossibleSamples; currPos += sampleInverval)
            {
                const long tickToSec = ((1000L / 100L) * 1000L * 1000L);

                float percent = (float)currPos / maxPossibleSamples;

                TimeSpan timeCurrPos = recordStartTime + new TimeSpan((long)(tickToSec * ((float)(currPos * slicedMultipler) / sampleRate)));
                string timeStr = timeCurrPos.ToString(@"hh\:mm\:ss");

                Text timeText = new Text(timeStr, Settings.font);
                timeText.FillColor = Color.Black;
                timeText.CharacterSize = textSize;
                Utils.UpdateTextOrigins(timeText, TextAlign.Top);

                timeText.Position = new Vector2f
                (
                    startX + (int)(scopeScaledWidth * percent),
                    startY + scopeScaledHeight + distToAxis
                );

                fftChartTimeAxisText.Add(timeText);
            }

            /* Y-Axis */

            fftChartFreqAxisText.Clear();

            const int fftSize = 2048;
            const int fftOutputSize = 800;

            const int freqSpacing = 2000;

            const int maxFrequency = (int)((sampleRate / 2) * ((float)fftOutputSize / (fftSize / 2.0f)));

            int freq = 0;
            int index = 0;
            while (freq <= maxFrequency)
            {
                Text freqText;
                if (index + 1 < fftChartFreqAxisText.Count)
                    freqText = fftChartFreqAxisText[index];
                else
                { 
                    freqText = new Text($"{MathF.Round(freq / 1000.0f, 2)} KHz", Settings.font);
                    fftChartFreqAxisText.Add(freqText);
                }
                freqText.CharacterSize = textSize;
                freqText.FillColor = Color.Black;
                Utils.UpdateTextOrigins(freqText, TextAlign.Right);

                float percent = 1.0f - ((float)freq / maxFrequency);
                freqText.Position = new Vector2f
                (
                    startX - distToAxis,
                    startY + (int)(scopeScaledHeight * percent)
                );

                freq += freqSpacing;
                index++;
            }
            int numOfTextNeeded = index;

            while (index < fftChartFreqAxisText.Count)
            {
                fftChartFreqAxisText[index].Dispose();
                index++;
            }

            fftChartFreqAxisText.RemoveRange(numOfTextNeeded, fftChartFreqAxisText.Count - numOfTextNeeded);
        }

        static void DrawFFTChart()
        {
            /* Create Texture */

            int maxLength = 0;
            foreach (float[] fftRowData in fftData)
            {
                if (fftRowData.Length > maxLength)
                    maxLength = fftRowData.Length;
            }

            int textureWidth = fftData.Count;
            int textureHeight = maxLength;
            int textureSize = textureWidth * textureHeight * 4;

            fftChartTexture?.Dispose();
            fftChartTexture = new Texture((uint)textureWidth, (uint)textureHeight);
            fftChart?.Dispose();
            fftChart = new Sprite(fftChartTexture);

            scopeScaledWidth = slicedMode ? scopeWidthSliced : scopeWidthNonSliced;

            fftChart.Scale = new Vector2f
            (
                (float)scopeScaledWidth / textureWidth,
                (float)scopeScaledHeight / textureHeight
            );
            fftChart.Position = new Vector2f
            (
                (window.width - scopeScaledWidth) / 2,
                (window.height - scopeScaledHeight) / 2
            );

            /* Draw Freq-Time Chart */

            byte[] pixels = new byte[textureSize];
            for (int x = 0; x < fftData.Count; x++)
            {
                float[] fftRowData = fftData[x];

                int offsetDecrement = (textureWidth + 1) * 4;
                int pixelOffset = ((textureWidth * (textureHeight - 1)) + x) * 4;

                int y = 0;
                while (y < fftRowData.Length)
                {
                    float currentValue = fftRowData[y];

                    /*
                    const float maxValue = 15.0f;
                    
                    float brightness = MathF.Pow(MathF.Max(0.0f, MathF.Min((currentValue / maxValue), 1.0f)), 1.5f) * 255.0f;
                    byte brightnessByte = (byte)brightness;

                    pixels[pixelOffset++] = brightnessByte;
                    pixels[pixelOffset++] = brightnessByte;
                    pixels[pixelOffset++] = brightnessByte;
                    pixels[pixelOffset++] = 0xFF;
                    */

                    byte r, g, b;
                    (r, g, b) = MagnitudeToColor(MathF.Pow(currentValue, 2.0f));

                    pixels[pixelOffset++] = r;
                    pixels[pixelOffset++] = g;
                    pixels[pixelOffset++] = b;
                    pixels[pixelOffset++] = 0xFF;

                    pixelOffset -= offsetDecrement;

                    y++;
                }

                while (y < textureHeight)
                {
                    pixelOffset += 3;
                    pixels[pixelOffset++] = 0xFF;

                    pixelOffset -= offsetDecrement;

                    y++;
                }
            }

            fftChartTexture.Update(pixels);

            /* Frequency Text */

            UpdateAxisText();
        }

        static bool TryLoadPWSData(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                if (lblFileLoadInfo != null)
                    lblFileLoadInfo.text = "Cannot load Voyager PWS data. Path is empty or null.";

                return false;
            }

            FileStream pwsDataFileStream;
            try
            {
                pwsDataFileStream = new FileStream(path, FileMode.Open);
            }
            catch (Exception ex)
            {
                if (lblFileLoadInfo != null)
                    lblFileLoadInfo.text = $"Failed to open data file: {ex}";

                return false;
            }

            if (pwsDataFileStream.Length > (4 << (10 * 2)))
            {
                if (lblFileLoadInfo != null)
                    lblFileLoadInfo.text = $"File too large. ({pwsDataFileStream.Length} bytes)";

                pwsDataFileStream.Close();

                return false;
            }

            byte[] rowBuffer = new byte[1024];

            /* Read Eng rec and s/c ID SCLK SCET */

            Array.Clear(engineeringRecord, 0, engineeringRecord.Length);
            if (!ReadRow(0, pwsDataFileStream, ref rowBuffer))
            {
                if (lblFileLoadInfo != null)
                    lblFileLoadInfo.text = "Cannot read first row of data.";

                pwsDataFileStream.Close();

                return false;
            }

            Array.Copy(rowBuffer, 0, engineeringRecord, 0, 242);

            const int sizeOfScIdSclkScet = 300 - 249;
            byte[] scIdSclkScetBytes = new byte[sizeOfScIdSclkScet];
            Array.Copy(rowBuffer, 248, scIdSclkScetBytes, 0, sizeOfScIdSclkScet);
            scIdSclkScet = Encoding.ASCII.GetString(scIdSclkScetBytes);

            if (!TimeSpan.TryParse(scIdSclkScet.Substring(37, 12), out recordStartTime))
                recordStartTime = new TimeSpan(0, 0, 0);

            /* Waveform Data */

            waveformData.Clear();
            linePositions.Clear();
            fftData.Clear();

            int index = 1;
            int currLine = 1;
            int increment = slicedMode ? 5 : 1;
            while (ReadRow(index, pwsDataFileStream, ref rowBuffer))
            {
                short line = (short)(((short)rowBuffer[22] << 8) | rowBuffer[23]);
                linePositions.Add((line / increment) * samplePerLine);

                while (currLine < line)
                {
                    fftData.Add([]);
                    currLine += increment;
                }

                byte[] fftInputBuffer = new byte[samplePerLine];

                int offset = 0;
                for (int i = 220; i < 1020; i++)
                { 
                    byte currentByte = rowBuffer[i];

                    byte nibbleH = (byte)(currentByte >> 4);
                    byte nibbleL = (byte)(currentByte & 0x0F);

                    waveformData.Add(nibbleL);
                    waveformData.Add(nibbleH);

                    fftInputBuffer[offset++] = nibbleH;
                    fftInputBuffer[offset++] = nibbleL;
                }

                float[] fftOutputBuffer;
                FFTRow(fftInputBuffer, out fftOutputBuffer);
                fftData.Add(fftOutputBuffer);

                index += increment;
                currLine += increment;
            }
            linePositions.Add((currLine / increment) * samplePerLine);

            DrawFFTChart();

            dataPlayer.SetDataAndRewind(waveformData);
            if (dataPlayer.Status == SoundStatus.Playing)
                dataPlayer.Stop();
            dataPlayer.Play();

            pwsDataFileStream.Close();

            if (lblFileLoadInfo != null)
                lblFileLoadInfo.text = $"{path} loaded.";

            if (lblDataInfo != null)
                lblDataInfo.text = $"{scIdSclkScet}";

            return true;
        }

        #endregion

        static void UserInit(object? sender, EventArgs args)
        {
            InitElements();
        }

        static void UserUpdate(object? sender, EventArgs args)
        {
            Utils.UpdateElements(elements);
        }

        static void UserDraw(object? sender, EventArgs args)
        {
            if (fftChart != null)
            {
                SetLinePosition(dataPlayer);
                window.texture?.Draw(fftChart);
                window.texture?.Draw(timePosVertices);

                foreach(Text text in fftChartFreqAxisText)
                    window.texture?.Draw(text);

                foreach(Text text in fftChartTimeAxisText)
                    window.texture?.Draw(text);
            }

            Utils.DrawElements(elements, window.texture);
        }

        static void Main(string[] args)
        {
            Settings.defaultFontFileName = "MiSans-Regular.ttf";

            window.userInit += UserInit;
            window.userUpdate += UserUpdate;
            window.userDraw += UserDraw;

            if (args.Length >= 1)
                initialPath = args[0];

            window.Run();
        }
    }
}
