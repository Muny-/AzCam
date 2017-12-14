using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzCam
{
    public class VideoFrame
    {
        public Bitmap Image;

        public TimeSpan Time;

        public VideoFrame(Bitmap image, TimeSpan time)
        {
            this.Image = image;
            this.Time = time;
        }

        public void Dispose()
        {
            Image.Dispose();
        }
    }
}
