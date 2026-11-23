using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace NcBootstrap;

/// <summary>
/// nanoCAD ribbon BitmapDll ignores PNG-compressed / 16-only ICO files (blank icon).
/// Rewrite to classic multi-size DIB ICO (16x16 + 32x32, 32bpp).
/// </summary>
internal static class IconIco
{
	public static void EnsureClassicDibIco(string icoPath)
	{
		if (string.IsNullOrWhiteSpace(icoPath) || !File.Exists(icoPath))
		{
			return;
		}

		try
		{
			byte[] raw = File.ReadAllBytes(icoPath);
			if (LooksLikeClassicMultiDib(raw))
			{
				return;
			}

			using var ms = new MemoryStream(raw);
			using var src = new Icon(ms);
			using Bitmap bmp = src.ToBitmap();
			WriteDibIco(icoPath, new[]
			{
				ResizeRgba(bmp, 16),
				ResizeRgba(bmp, 32)
			});
			Log.Write("ICO нормализован в DIB 16+32: " + icoPath);
		}
		catch (Exception ex)
		{
			Log.Write("ICO normalize skip " + icoPath + ": " + ex.Message);
		}
	}

	private static bool LooksLikeClassicMultiDib(byte[] data)
	{
		if (data == null || data.Length < 22)
		{
			return false;
		}

		ushort type = BitConverter.ToUInt16(data, 2);
		ushort count = BitConverter.ToUInt16(data, 4);
		if (type != 1 || count < 2)
		{
			return false;
		}

		bool has16 = false, has32 = false, allDib = true;
		for (int i = 0; i < count; i++)
		{
			int off = 6 + i * 16;
			int w = data[off];
			int h = data[off + 1];
			int size = BitConverter.ToInt32(data, off + 8);
			int offset = BitConverter.ToInt32(data, off + 12);
			if (w == 16 && h == 16)
			{
				has16 = true;
			}

			if (w == 32 && h == 32)
			{
				has32 = true;
			}

			if (offset < 0 || offset + 8 > data.Length || size < 40)
			{
				allDib = false;
				continue;
			}

			// PNG signature inside ICO entry
			if (data[offset] == 0x89 && data[offset + 1] == 0x50)
			{
				allDib = false;
			}
			else if (!(data[offset] == 0x28 && data[offset + 1] == 0x00))
			{
				allDib = false;
			}
		}

		return allDib && has16 && has32;
	}

	private static Bitmap ResizeRgba(Bitmap src, int size)
	{
		var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
		using (var g = Graphics.FromImage(dst))
		{
			g.Clear(Color.Transparent);
			g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
			g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
			g.DrawImage(src, new Rectangle(0, 0, size, size));
		}

		return dst;
	}

	private static void WriteDibIco(string path, IList<Bitmap> images)
	{
		var payloads = new List<byte[]>();
		var entries = new List<(int w, int h, int size)>();
		foreach (Bitmap im in images)
		{
			byte[] payload = EncodeDib(im);
			payloads.Add(payload);
			entries.Add((im.Width >= 256 ? 0 : im.Width, im.Height >= 256 ? 0 : im.Height, payload.Length));
		}

		int count = entries.Count;
		int offset = 6 + 16 * count;
		using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
		using var bw = new BinaryWriter(fs);
		bw.Write((ushort)0);
		bw.Write((ushort)1);
		bw.Write((ushort)count);
		foreach (var e in entries)
		{
			bw.Write((byte)e.w);
			bw.Write((byte)e.h);
			bw.Write((byte)0);
			bw.Write((byte)0);
			bw.Write((ushort)1);
			bw.Write((ushort)32);
			bw.Write(e.size);
			bw.Write(offset);
			offset += e.size;
		}

		foreach (byte[] p in payloads)
		{
			bw.Write(p);
		}
	}

	private static byte[] EncodeDib(Bitmap bmp)
	{
		int w = bmp.Width;
		int h = bmp.Height;
		var rect = new Rectangle(0, 0, w, h);
		BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			int stride = w * 4;
			var xor = new byte[stride * h];
			// Bottom-up BGRA
			for (int y = 0; y < h; y++)
			{
				IntPtr src = data.Scan0 + (h - 1 - y) * data.Stride;
				Marshal.Copy(src, xor, y * stride, stride);
			}

			int andRow = ((w + 31) / 32) * 4;
			var andMask = new byte[andRow * h];
			using var ms = new MemoryStream();
			using var bw = new BinaryWriter(ms);
			bw.Write(40);
			bw.Write(w);
			bw.Write(h * 2);
			bw.Write((ushort)1);
			bw.Write((ushort)32);
			bw.Write(0);
			bw.Write(xor.Length);
			bw.Write(0);
			bw.Write(0);
			bw.Write(0);
			bw.Write(0);
			bw.Write(xor);
			bw.Write(andMask);
			return ms.ToArray();
		}
		finally
		{
			bmp.UnlockBits(data);
		}
	}
}
