using System;
using System.Drawing;
using System.Threading;
using BiometricDevices;

namespace BiometricDevices
{
    public class FingerprintDevice : IDisposable
    {
        private const int FingerPresenceCheckIntervalMs = 50;
        private const int FingerDetectionThreshold = 800;
        private const int NDose = 4;

        private readonly IntPtr handle;
        private readonly Timer fingerDetectionTimer;
        private volatile bool isFingerDetected; // Reduz a concorrência de threads
        private readonly object ledLock = new(); // Lock compartilhado para LEDs

        public event EventHandler FingerDetected;
        public event EventHandler FingerReleased;

        public FingerprintDevice(IntPtr handle)
        {
            this.handle = handle;
            fingerDetectionTimer = new Timer(FingerDetectionCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool IsFingerPresent => isFingerDetected;

        public bool GreenLed
        {
            get => GetLedState().GreenIsOn;
            set => SetLedState(green: value, red: RedLed);
        }

        public bool RedLed
        {
            get => GetLedState().RedIsOn;
            set => SetLedState(green: GreenLed, red: value);
        }

        public void StartFingerDetection() => fingerDetectionTimer.Change(0, FingerPresenceCheckIntervalMs);

        public void StopFingerDetection() => fingerDetectionTimer.Change(Timeout.Infinite, Timeout.Infinite);

        public Bitmap ReadFingerprint()
        {
            if (LibScanApi.ftrScanGetImageSize(handle, out var imageSize))
            {
                var imageBuffer = new byte[imageSize.nImageSize];
                if (LibScanApi.ftrScanGetImage(handle, NDose, imageBuffer))
                {
                    return ConvertToBitmap(imageBuffer, imageSize.nWidth, imageSize.nHeight);
                }
            }
            throw new InvalidOperationException("Falha ao capturar a imagem da impressão digital.");
        }

        public void Dispose()
        {
            fingerDetectionTimer.Dispose();
            LibScanApi.ftrScanCloseDevice(handle);
        }

        private void FingerDetectionCallback(object state)
        {
            if (LibScanApi.ftrScanIsFingerPresent(handle, out var frameParams))
            {
                var fingerDetectedNow = frameParams.nContrastOnDose2 > FingerDetectionThreshold;

                if (fingerDetectedNow && !isFingerDetected)
                {
                    isFingerDetected = true;
                    OnFingerDetected();
                }
                else if (!fingerDetectedNow && isFingerDetected)
                {
                    isFingerDetected = false;
                    OnFingerReleased();
                }
            }
        }

        private Bitmap ConvertToBitmap(byte[] imageBuffer, int width, int height)
        {
            var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte pixelValue = (byte)(0xFF - imageBuffer[y * width + x]);
                    var color = Color.FromArgb(pixelValue, pixelValue, pixelValue);
                    bitmap.SetPixel(x, y, color);
                }
            }
            return bitmap;
        }

        private void SetLedState(bool green, bool red)
        {
            lock (ledLock)
            {
                LibScanApi.ftrScanSetDiodesStatus(handle, (byte)(green ? 255 : 0), (byte)(red ? 255 : 0));
            }
        }

        private LedState GetLedState()
        {
            LibScanApi.ftrScanGetDiodesStatus(handle, out bool green, out bool red);
            return new LedState { GreenIsOn = green, RedIsOn = red };
        }

        protected virtual void OnFingerDetected() => FingerDetected?.Invoke(this, EventArgs.Empty);

        protected virtual void OnFingerReleased() => FingerReleased?.Invoke(this, EventArgs.Empty);
    }

    public class LedState
    {
        public bool GreenIsOn { get; set; }
        public bool RedIsOn { get; set; }
    }
}
