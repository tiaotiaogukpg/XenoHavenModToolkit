using System.Drawing;
using System.Drawing.Imaging;

var path = args.Length > 0
    ? args[0]
    : Path.GetFullPath("PartsSheetTemplate.png");

Directory.CreateDirectory(Path.GetDirectoryName(path)!);

using var bmp = new Bitmap(1026, 1026, PixelFormat.Format32bppArgb);
using var g = Graphics.FromImage(bmp);
g.Clear(Color.Transparent);
using var pen = new Pen(Color.FromArgb(180, 80, 140, 220), 2f);
foreach (var x in new[] { 0, 342, 684, 1026 })
{
    g.DrawLine(pen, x, 0, x, 342);
    g.DrawLine(pen, x, 342, x, 751);
}

g.DrawLine(pen, 0, 0, 1026, 0);
g.DrawLine(pen, 0, 342, 1026, 342);
g.DrawLine(pen, 0, 751, 1026, 751);
g.DrawLine(pen, 0, 1026, 1026, 1026);
g.DrawRectangle(pen, 309, 751, 202, 275);
g.DrawRectangle(pen, 513, 751, 202, 275);

using var font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Point);
using var brush = new SolidBrush(Color.FromArgb(220, 80, 140, 220));
g.DrawString("Head1", font, brush, 10, 10);
g.DrawString("Head2", font, brush, 352, 10);
g.DrawString("Head3", font, brush, 694, 10);
g.DrawString("HandR", font, brush, 10, 352);
g.DrawString("Body", font, brush, 352, 352);
g.DrawString("HandL", font, brush, 694, 352);
g.DrawString("FootR", font, brush, 319, 761);
g.DrawString("FootL", font, brush, 523, 761);

bmp.Save(path, ImageFormat.Png);
Console.WriteLine("ok " + path);
