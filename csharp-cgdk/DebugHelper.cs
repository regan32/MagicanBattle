using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Com.CodeGame.CodeWizards2016.DevKit.CSharpCgdk
{
    public static class DebugHelper
    {
        public static void VisualizeMatrix(bool[,] matrix, int width, int height, List<GridPos> path)
        {

            using (var writer = new FileStream("C:\\Matixa.bmp", FileMode.Create))
            {
                Bitmap map = new Bitmap(width, height);

                for (int i = 0; i < height; i++)
                {
                    for (int j = 0; j < width; j++)
                    {
                        map.SetPixel(j,i, matrix[j, i] ? Color.Black : Color.White);
                    }
                }

                foreach (var gridPose in path)
                {
                    map.SetPixel(gridPose.x, gridPose.y, Color.Blue);
                }

                map.Save(writer, ImageFormat.Bmp);
            }


        }
    }
}
