using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Plant3D.IfcExporter.UI
{
    public static class IconHelper
    {
        public static BitmapSource CreateIfcIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Background cube / rounded rectangle
                var bgBrush = new LinearGradientBrush(
                    Color.FromRgb(37, 99, 235), // Blue 600
                    Color.FromRgb(30, 64, 175), // Blue 800
                    45.0
                );
                dc.DrawRoundedRectangle(bgBrush, null, new Rect(1, 1, size - 2, size - 2), size / 6.0, size / 6.0);

                // Pipe line (Orange)
                var pipePen = new Pen(new SolidColorBrush(Color.FromRgb(249, 115, 22)), size > 20 ? 3.5 : 2.0);
                dc.DrawLine(pipePen, new Point(3, size * 0.35), new Point(size * 0.55, size * 0.35));
                dc.DrawLine(pipePen, new Point(size * 0.55, size * 0.35), new Point(size * 0.55, size - 3));

                // Flange / joint indicator
                var flangeBrush = new SolidColorBrush(Color.FromRgb(254, 215, 170));
                dc.DrawEllipse(flangeBrush, null, new Point(size * 0.55, size * 0.35), size > 20 ? 3.0 : 1.8, size > 20 ? 3.0 : 1.8);

                // IFC Text
                if (size >= 24)
                {
                    var text = new FormattedText(
                        "IFC",
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                        size * 0.30,
                        Brushes.White,
                        1.0
                    );
                    dc.DrawText(text, new Point((size - text.Width) / 2.0, size * 0.55));
                }
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        public static BitmapSource CreateSettingsIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var brush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
                dc.DrawEllipse(null, new Pen(brush, 2.0), new Point(size / 2.0, size / 2.0), size * 0.35, size * 0.35);
                dc.DrawEllipse(brush, null, new Point(size / 2.0, size / 2.0), size * 0.15, size * 0.15);
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
